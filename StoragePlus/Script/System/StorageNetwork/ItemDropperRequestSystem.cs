using System.Collections.Generic;
using Inventory;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(InventorySystemGroup))]
[UpdateBefore(typeof(InventoryUpdateSystem))]
public partial class ItemDropperRequestSystem : PugSimulationSystemBase
{
    private readonly List<CandidateInventory> _candidateInventories = new();
    private EntityQuery _droppedItemQuery;

    private struct CandidateInventory
    {
        public Entity Inventory;
        public float DistanceSq;
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
        base.OnCreate();
    }

    protected override void OnUpdate()
    {
        PugDatabase.DatabaseBankCD databaseBank = SystemAPI.GetSingleton<PugDatabase.DatabaseBankCD>();
        Entity inventoryChangeBufferEntity = SystemAPI.GetSingletonEntity<InventoryChangeBuffer>();
        DynamicBuffer<InventoryChangeBuffer> inventoryChanges = SystemAPI.GetBuffer<InventoryChangeBuffer>(inventoryChangeBufferEntity);

        StorageNetworkWorldCache cache = StorageNetworkWorldCacheRegistry.GetOrCreate(World);
        cache.EnsureBuilt(databaseBank);

        using NativeArray<Entity> droppedItems = _droppedItemQuery.ToEntityArray(Allocator.Temp);

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

            float3 dropperPosition = transformRef.ValueRO.Position;
            bool hasMatchingDroppedItem = HasMatchingDroppedItem(
                    filtering,
                    dropperPosition,
                    droppedItems,
                    objectDataLookup,
                    localTransformLookup,
                    pickUpLookup,
                    entityDestroyedLookup,
                    containedLookup);
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

            BuildCandidateInventories(network, dropperPosition, localTransformLookup);
            TryQueueDropRequest(
                filtering,
                databaseBank,
                inventoryLookup,
                containedLookup,
                durabilityLookup,
                fullnessLookup,
                petLookup,
                inventoryChanges,
                GetDropPosition(dropperPosition));
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

    private static bool HasMatchingDroppedItem(
        ObjectFilteringCD filtering,
        float3 dropperPosition,
        NativeArray<Entity> droppedItems,
        ComponentLookup<ObjectDataCD> objectDataLookup,
        ComponentLookup<LocalTransform> localTransformLookup,
        ComponentLookup<PickUpItemCD> pickUpLookup,
        ComponentLookup<EntityDestroyedCD> entityDestroyedLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup)
    {
        long dropperTileKey = ToTileKey(dropperPosition);

        for (int i = 0; i < droppedItems.Length; i++)
        {
            Entity droppedItemEntity = droppedItems[i];
            if (entityDestroyedLookup.HasComponent(droppedItemEntity) &&
                entityDestroyedLookup.IsComponentEnabled(droppedItemEntity))
            {
                continue;
            }

            if (objectDataLookup[droppedItemEntity].objectID != ObjectID.DroppedItem)
            {
                continue;
            }

            PickUpItemCD pickUpItem = pickUpLookup[droppedItemEntity];
            if (pickUpItem.state == PickUpItemState.IsBeingPickedUp ||
                pickUpItem.state == PickUpItemState.ForcePickUp ||
                pickUpItem.state == PickUpItemState.HasBeenPickedUp)
            {
                continue;
            }

            if (ToTileKey(localTransformLookup[droppedItemEntity].Position) != dropperTileKey ||
                !TryGetDroppedItem(containedLookup[droppedItemEntity], out ContainedObjectsBuffer droppedObject))
            {
                continue;
            }

            if (droppedObject.objectID == filtering.filterObject &&
                droppedObject.variation == filtering.filterVariation)
            {
                return true;
            }
        }

        return false;
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
