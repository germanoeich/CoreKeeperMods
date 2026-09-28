using System.Collections.Generic;
using Inventory;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(InventorySystemGroup))]
[UpdateBefore(typeof(InventoryUpdateSystem))]
public partial class ItemDropperRequestSystem : PugSimulationSystemBase
{
    private readonly List<CandidateInventory> _candidateInventories = new();
    private EntityQuery _droppedItemQuery;

    // Fair share: droppers with the same filter on the same network take turns, instead of the
    // first one in query order taking every item. Keyed by dropper entity index.
    private readonly Dictionary<int, int> _dropCountByDropper = new();
    private readonly Dictionary<int, long> _lastGroupKeyByDropper = new();
    private readonly Dictionary<long, HashSet<int>> _droppersByGroup = new();

    // A peer that has not reached the gate for this long (output jammed, e.g. its machine is full;
    // filter cleared; cut off from the network) no longer holds the rest of its group back. A normal
    // drop cycle (pickup wait + pickup cooldown), even with slow or manual pickup, stays well under this.
    private const double InactivePeerSeconds = 10.0;
    private readonly Dictionary<int, double> _lastGateTimeByDropper = new();

    // A removed dropper would otherwise stay in its group with a frozen count; once every live peer
    // passed that count the whole group would stop dropping. Swept at most once per frame.
    private EntityQuery _dropperStateQuery;
    private int _lastStalePruneFrame = -1;
    private readonly HashSet<int> _liveDropperIndices = new();
    private readonly List<int> _deadDropperScratch = new();
    private readonly List<long> _deadGroupScratch = new();

    private struct CandidateInventory
    {
        public Entity Inventory;
        public float DistanceSq;
    }

    private readonly struct DroppedItemKey : System.IEquatable<DroppedItemKey>
    {
        public readonly long TileKey;
        public readonly int ObjectId;
        public readonly int Variation;

        public DroppedItemKey(long tileKey, ObjectID objectId, int variation)
        {
            TileKey = tileKey;
            ObjectId = (int)objectId;
            Variation = variation;
        }

        public bool Equals(DroppedItemKey other)
        {
            return TileKey == other.TileKey &&
                   ObjectId == other.ObjectId &&
                   Variation == other.Variation;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)(TileKey ^ (TileKey >> 32));
                hash = (hash * 397) ^ ObjectId;
                hash = (hash * 397) ^ Variation;
                return hash;
            }
        }
    }

    protected override void OnCreate()
    {
        NeedDatabase();
        RequireForUpdate<InventoryChangeBuffer>();
        RequireForUpdate<ObjectFilteringCD>();
        RequireForUpdate<StorageConnectorTag>();
        _droppedItemQuery = GetEntityQuery(
            ComponentType.ReadOnly<ObjectDataCD>(),
            ComponentType.ReadOnly<ContainedObjectsBuffer>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<PickUpItemCD>());
        // Same shape as the dropper query in OnUpdate; only used to prune stale per-dropper state.
        _dropperStateQuery = GetEntityQuery(
            ComponentType.ReadOnly<ObjectFilteringCD>(),
            ComponentType.ReadOnly<ItemDropperTimingCD>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<StorageConnectorTag>(),
            ComponentType.ReadOnly<OutputConnectorTag>());
        base.OnCreate();
    }

    protected override void OnUpdate()
    {
        PugDatabase.DatabaseBankCD databaseBank = SystemAPI.GetSingleton<PugDatabase.DatabaseBankCD>();
        Entity inventoryChangeBufferEntity = SystemAPI.GetSingletonEntity<InventoryChangeBuffer>();
        DynamicBuffer<InventoryChangeBuffer> inventoryChanges = SystemAPI.GetBuffer<InventoryChangeBuffer>(inventoryChangeBufferEntity);

        StorageNetworkWorldCache cache = StorageNetworkWorldCacheRegistry.GetOrCreate(World);
        cache.EnsureBuilt(databaseBank);

        BufferLookup<InventoryBuffer> inventoryLookup = GetBufferLookup<InventoryBuffer>(isReadOnly: true);
        BufferLookup<ContainedObjectsBuffer> containedLookup = GetBufferLookup<ContainedObjectsBuffer>(isReadOnly: true);
        ComponentLookup<ObjectDataCD> objectDataLookup = GetComponentLookup<ObjectDataCD>(isReadOnly: true);
        ComponentLookup<LocalTransform> localTransformLookup = GetComponentLookup<LocalTransform>(isReadOnly: true);
        ComponentLookup<PickUpItemCD> pickUpLookup = GetComponentLookup<PickUpItemCD>(isReadOnly: true);
        ComponentLookup<EntityDestroyedCD> entityDestroyedLookup = GetComponentLookup<EntityDestroyedCD>(isReadOnly: true);
        ComponentLookup<DurabilityCD> durabilityLookup = GetComponentLookup<DurabilityCD>(isReadOnly: true);
        ComponentLookup<FullnessCD> fullnessLookup = GetComponentLookup<FullnessCD>(isReadOnly: true);
        ComponentLookup<PetCD> petLookup = GetComponentLookup<PetCD>(isReadOnly: true);
        double elapsedTime = SystemAPI.Time.ElapsedTime;
        NativeArray<Entity> droppedItems = default;
        NativeParallelHashSet<DroppedItemKey> droppedItemLookup = default;

        try
        {
            foreach ((RefRO<ObjectFilteringCD> filteringRef, RefRW<ItemDropperTimingCD> timingRef, RefRO<LocalTransform> transformRef, Entity entity) in SystemAPI
                         .Query<RefRO<ObjectFilteringCD>, RefRW<ItemDropperTimingCD>, RefRO<LocalTransform>>()
                         .WithAll<StorageConnectorTag, OutputConnectorTag>()
                         .WithEntityAccess())
            {
                ObjectFilteringCD filtering = filteringRef.ValueRO;
                ref ItemDropperTimingCD timing = ref timingRef.ValueRW;
                if (!HasActiveWhitelist(filtering) ||
                    !cache.TryGetNetworkForConnector(entity, out StorageNetworkSnapshot network))
                {
                    timing.hadMatchingDroppedItemLastTick = false;
                    continue;
                }

                if (!droppedItems.IsCreated)
                {
                    droppedItems = _droppedItemQuery.ToEntityArray(Allocator.TempJob);
                    droppedItemLookup = BuildDroppedItemLookup(
                        droppedItems,
                        objectDataLookup,
                        localTransformLookup,
                        pickUpLookup,
                        entityDestroyedLookup,
                        containedLookup,
                        Dependency);
                }

                float3 dropperPosition = transformRef.ValueRO.Position;
                bool hasMatchingDroppedItem = droppedItemLookup.IsCreated &&
                                              droppedItemLookup.Contains(new DroppedItemKey(
                                                  ToTileKey(dropperPosition),
                                                  filtering.filterObject,
                                                  filtering.filterVariation));
                if (hasMatchingDroppedItem)
                {
                    timing.hadMatchingDroppedItemLastTick = true;
                    continue;
                }

                if (timing.hadMatchingDroppedItemLastTick)
                {
                    timing.hadMatchingDroppedItemLastTick = false;
                    timing.nextAllowedDropTime = elapsedTime + math.max(0d, timing.pickupCooldownSeconds);
                    continue;
                }

                if (elapsedTime < timing.nextAllowedDropTime)
                {
                    continue;
                }

                long groupKey = ComputeGroupKey(network, filtering);
                if (!PassesFairShareGate(entity.Index, groupKey, elapsedTime))
                {
                    continue;
                }

                BuildCandidateInventories(network, dropperPosition, localTransformLookup);
                bool dropped = TryQueueDropRequest(
                    filtering,
                    databaseBank,
                    inventoryLookup,
                    containedLookup,
                    durabilityLookup,
                    fullnessLookup,
                    petLookup,
                    inventoryChanges,
                    GetDropPosition(dropperPosition));
                if (dropped)
                {
                    NotifyDropSucceeded(entity.Index);
                }
            }
        }
        finally
        {
            if (droppedItemLookup.IsCreated)
            {
                droppedItemLookup.Dispose();
            }

            if (droppedItems.IsCreated)
            {
                droppedItems.Dispose();
            }
        }
    }

    private void BuildCandidateInventories(
        StorageNetworkSnapshot network,
        float3 origin,
        ComponentLookup<LocalTransform> localTransformLookup)
    {
        _candidateInventories.Clear();

        for (int i = 0; i < network.CraftVisibleInventories.Count; i++)
        {
            Entity inventoryEntity = network.CraftVisibleInventories[i];
            if (!localTransformLookup.HasComponent(inventoryEntity))
            {
                continue;
            }

            float distanceSq = math.distancesq(origin, localTransformLookup[inventoryEntity].Position);
            _candidateInventories.Add(new CandidateInventory
            {
                Inventory = inventoryEntity,
                DistanceSq = distanceSq
            });
        }

        _candidateInventories.Sort(static (left, right) =>
        {
            int distanceCompare = left.DistanceSq.CompareTo(right.DistanceSq);
            if (distanceCompare != 0)
            {
                return distanceCompare;
            }

            int indexCompare = left.Inventory.Index.CompareTo(right.Inventory.Index);
            if (indexCompare != 0)
            {
                return indexCompare;
            }

            return left.Inventory.Version.CompareTo(right.Inventory.Version);
        });
    }

    private bool TryQueueDropRequest(
        ObjectFilteringCD filtering,
        PugDatabase.DatabaseBankCD databaseBank,
        BufferLookup<InventoryBuffer> inventoryLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup,
        DynamicBuffer<InventoryChangeBuffer> inventoryChanges,
        float3 dropPosition)
    {
        HashSet<long> reservedSourceSlots = StorageNetworkWorldCacheRegistry.GetOrCreate(World).ReservedSourceSlotsThisTick;
        for (int inventoryIndex = 0; inventoryIndex < _candidateInventories.Count; inventoryIndex++)
        {
            Entity sourceInventory = _candidateInventories[inventoryIndex].Inventory;
            if (!inventoryLookup.HasBuffer(sourceInventory) || !containedLookup.HasBuffer(sourceInventory))
            {
                continue;
            }

            DynamicBuffer<InventoryBuffer> inventories = inventoryLookup[sourceInventory];
            DynamicBuffer<ContainedObjectsBuffer> containedObjects = containedLookup[sourceInventory];
            for (int bufferIndex = 0; bufferIndex < inventories.Length; bufferIndex++)
            {
                InventoryBuffer inventory = inventories[bufferIndex];
                int endIndex = inventory.startIndex + inventory.size;

                for (int slot = inventory.startIndex; slot < endIndex && slot < containedObjects.Length; slot++)
                {
                    long slotKey = ((long)sourceInventory.Index << 32) | (uint)slot;
                    if (reservedSourceSlots.Contains(slotKey))
                    {
                        continue;
                    }

                    ContainedObjectsBuffer objectInSlot = containedObjects[slot];
                    if (objectInSlot.objectID != filtering.filterObject ||
                        objectInSlot.variation != filtering.filterVariation ||
                        StorageTerminalSummaryUtility.GetCountContribution(
                            databaseBank,
                            durabilityLookup,
                            fullnessLookup,
                            petLookup,
                            objectInSlot) <= 0)
                    {
                        continue;
                    }

                    inventoryChanges.Add(new InventoryChangeBuffer
                    {
                        inventoryChangeData = Create.DropItem(
                            sourceInventory,
                            slot,
                            1,
                            dropPosition)
                    });
                    reservedSourceSlots.Add(slotKey);
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasActiveWhitelist(ObjectFilteringCD filtering)
    {
        return filtering.filterType == FilterType.Whitelist &&
               filtering.filterObject != ObjectID.None;
    }

    // Groups are per (filter, network). Without the network hash, same-filter droppers in two
    // unrelated bases would share one counter and the busier base would starve the other.
    private static long ComputeGroupKey(StorageNetworkSnapshot network, ObjectFilteringCD filtering)
    {
        long filterKey = ((long)(int)filtering.filterObject << 32) | (uint)filtering.filterVariation;
        return filterKey ^ unchecked((long)network.NetworkHash);
    }

    private bool PassesFairShareGate(int entityIndex, long groupKey, double now)
    {
        PruneStaleDropperState();

        // Back from being inactive: its old count is stale, so it re-enters like a newcomer instead
        // of monopolising the group until it has caught up.
        bool returning = _lastGateTimeByDropper.TryGetValue(entityIndex, out double lastGate) &&
                         now - lastGate > InactivePeerSeconds;
        _lastGateTimeByDropper[entityIndex] = now;

        if (_lastGroupKeyByDropper.TryGetValue(entityIndex, out long oldKey) && oldKey != groupKey)
        {
            if (_droppersByGroup.TryGetValue(oldKey, out HashSet<int> oldSet))
            {
                oldSet.Remove(entityIndex);
            }
        }

        _lastGroupKeyByDropper[entityIndex] = groupKey;

        if (!_droppersByGroup.TryGetValue(groupKey, out HashSet<int> peers))
        {
            peers = new HashSet<int>();
            _droppersByGroup[groupKey] = peers;
        }

        peers.Add(entityIndex);

        bool known = _dropCountByDropper.TryGetValue(entityIndex, out int myCount);
        if (!known || returning)
        {
            // Start at the highest active peer count so a newcomer (or returnee) doesn't get a burst
            // of catch-up drops.
            foreach (int peer in peers)
            {
                if (IsActivePeer(peer, entityIndex, now) &&
                    _dropCountByDropper.TryGetValue(peer, out int peerCount) &&
                    peerCount > myCount)
                {
                    myCount = peerCount;
                }
            }

            _dropCountByDropper[entityIndex] = myCount;
        }

        // Compare against the lowest peer count, not the highest: with a tie, a max comparison lets
        // every tied dropper through, and the one that keeps losing the same-slot race in
        // TryQueueDropRequest never advances its count and never drops again. Only active peers
        // count, so one jammed dropper can't stall the rest of its group.
        int minOther = int.MaxValue;
        foreach (int peer in peers)
        {
            if (IsActivePeer(peer, entityIndex, now) &&
                _dropCountByDropper.TryGetValue(peer, out int peerCount) &&
                peerCount < minOther)
            {
                minOther = peerCount;
            }
        }

        return myCount <= minOther;
    }

    private bool IsActivePeer(int peer, int self, double now)
    {
        return peer != self &&
               _lastGateTimeByDropper.TryGetValue(peer, out double lastGate) &&
               now - lastGate <= InactivePeerSeconds;
    }

    private void NotifyDropSucceeded(int entityIndex)
    {
        if (_dropCountByDropper.TryGetValue(entityIndex, out int count))
        {
            _dropCountByDropper[entityIndex] = count + 1;
        }
        else
        {
            _dropCountByDropper[entityIndex] = 1;
        }
    }

    private void PruneStaleDropperState()
    {
        int frame = UnityEngine.Time.frameCount;
        if (frame == _lastStalePruneFrame)
        {
            return;
        }

        _lastStalePruneFrame = frame;

        _liveDropperIndices.Clear();
        using (NativeArray<Entity> droppers = _dropperStateQuery.ToEntityArray(Allocator.Temp))
        {
            for (int i = 0; i < droppers.Length; i++)
            {
                _liveDropperIndices.Add(droppers[i].Index);
            }
        }

        PruneDeadDropperKeys(_dropCountByDropper);
        PruneDeadDropperKeys(_lastGroupKeyByDropper);
        PruneDeadDropperKeys(_lastGateTimeByDropper);

        _deadGroupScratch.Clear();
        foreach (KeyValuePair<long, HashSet<int>> group in _droppersByGroup)
        {
            _deadDropperScratch.Clear();
            foreach (int index in group.Value)
            {
                if (!_liveDropperIndices.Contains(index))
                {
                    _deadDropperScratch.Add(index);
                }
            }

            for (int i = 0; i < _deadDropperScratch.Count; i++)
            {
                group.Value.Remove(_deadDropperScratch[i]);
            }

            if (group.Value.Count == 0)
            {
                _deadGroupScratch.Add(group.Key);
            }
        }

        for (int i = 0; i < _deadGroupScratch.Count; i++)
        {
            _droppersByGroup.Remove(_deadGroupScratch[i]);
        }
    }

    private void PruneDeadDropperKeys<T>(Dictionary<int, T> perDropperState)
    {
        _deadDropperScratch.Clear();
        foreach (int index in perDropperState.Keys)
        {
            if (!_liveDropperIndices.Contains(index))
            {
                _deadDropperScratch.Add(index);
            }
        }

        for (int i = 0; i < _deadDropperScratch.Count; i++)
        {
            perDropperState.Remove(_deadDropperScratch[i]);
        }
    }

    private static NativeParallelHashSet<DroppedItemKey> BuildDroppedItemLookup(
        NativeArray<Entity> droppedItems,
        ComponentLookup<ObjectDataCD> objectDataLookup,
        ComponentLookup<LocalTransform> localTransformLookup,
        ComponentLookup<PickUpItemCD> pickUpLookup,
        ComponentLookup<EntityDestroyedCD> entityDestroyedLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup,
        JobHandle inputDeps)
    {
        if (droppedItems.Length == 0)
        {
            return default;
        }

        NativeParallelHashSet<DroppedItemKey> lookup = new(droppedItems.Length, Allocator.TempJob);
        new BuildDroppedItemLookupJob
        {
            DroppedItems = droppedItems,
            ObjectDataLookup = objectDataLookup,
            LocalTransformLookup = localTransformLookup,
            PickUpLookup = pickUpLookup,
            EntityDestroyedLookup = entityDestroyedLookup,
            ContainedLookup = containedLookup,
            DroppedItemLookup = lookup.AsParallelWriter()
        }.Schedule(droppedItems.Length, 64, inputDeps).Complete();

        return lookup;
    }

    [BurstCompile]
    private struct BuildDroppedItemLookupJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<Entity> DroppedItems;

        [ReadOnly]
        public ComponentLookup<ObjectDataCD> ObjectDataLookup;

        [ReadOnly]
        public ComponentLookup<LocalTransform> LocalTransformLookup;

        [ReadOnly]
        public ComponentLookup<PickUpItemCD> PickUpLookup;

        [ReadOnly]
        public ComponentLookup<EntityDestroyedCD> EntityDestroyedLookup;

        [ReadOnly]
        public BufferLookup<ContainedObjectsBuffer> ContainedLookup;

        public NativeParallelHashSet<DroppedItemKey>.ParallelWriter DroppedItemLookup;

        public void Execute(int index)
        {
            Entity droppedItemEntity = DroppedItems[index];
            if (EntityDestroyedLookup.HasComponent(droppedItemEntity) &&
                EntityDestroyedLookup.IsComponentEnabled(droppedItemEntity))
            {
                return;
            }

            if (ObjectDataLookup[droppedItemEntity].objectID != ObjectID.DroppedItem)
            {
                return;
            }

            PickUpItemCD pickUpItem = PickUpLookup[droppedItemEntity];
            if (pickUpItem.state == PickUpItemState.IsBeingPickedUp ||
                pickUpItem.state == PickUpItemState.ForcePickUp ||
                pickUpItem.state == PickUpItemState.HasBeenPickedUp)
            {
                return;
            }

            if (!TryGetDroppedItem(ContainedLookup[droppedItemEntity], out ContainedObjectsBuffer droppedObject))
            {
                return;
            }

            DroppedItemLookup.Add(new DroppedItemKey(
                ToTileKey(LocalTransformLookup[droppedItemEntity].Position),
                droppedObject.objectID,
                droppedObject.variation));
        }
    }

    private static bool TryGetDroppedItem(
        DynamicBuffer<ContainedObjectsBuffer> droppedContents,
        out ContainedObjectsBuffer droppedObject)
    {
        for (int i = 0; i < droppedContents.Length; i++)
        {
            if (droppedContents[i].objectID == ObjectID.None)
            {
                continue;
            }

            droppedObject = droppedContents[i];
            return true;
        }

        droppedObject = default;
        return false;
    }

    private static float3 GetDropPosition(float3 dropperPosition)
    {
        int tileX = (int)math.round(dropperPosition.x);
        int tileY = (int)math.round(dropperPosition.z);
        return new float3(tileX, 0f, tileY);
    }

    private static long ToTileKey(float3 worldPosition)
    {
        return ((long)(int)math.round(worldPosition.x) << 32) ^ (uint)(int)math.round(worldPosition.z);
    }
}
