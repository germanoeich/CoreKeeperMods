using System.Collections.Generic;
using Inventory;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(InventorySystemGroup))]
[UpdateBefore(typeof(InventoryUpdateSystem))]
public partial class StorageTerminalQuickStackRelaySystem : PugSimulationSystemBase
{
    private const float QuickStackRange = 10f;

    private readonly List<NearbyRelayNetwork> _nearbyNetworks = new();
    private readonly HashSet<StorageNetworkSnapshot> _seenNetworks = new();

    protected override void OnCreate()
    {
        NeedDatabase();
        RequireForUpdate<StorageCraftingRelayTag>();
        RequireForUpdate<InventoryChangeBuffer>();
        base.OnCreate();
    }

    protected override void OnUpdate()
    {
        PugDatabase.DatabaseBankCD databaseBank = SystemAPI.GetSingleton<PugDatabase.DatabaseBankCD>();
        StorageNetworkWorldCache cache = StorageNetworkWorldCacheRegistry.GetOrCreate(World);
        cache.EnsureBuilt(databaseBank);

        Entity inventoryChangeBufferEntity = SystemAPI.GetSingletonEntity<InventoryChangeBuffer>();
        DynamicBuffer<InventoryChangeBuffer> inventoryChanges = SystemAPI.GetBuffer<InventoryChangeBuffer>(inventoryChangeBufferEntity);
        int initialLength = inventoryChanges.Length;
        if (initialLength == 0)
        {
            base.OnUpdate();
            return;
        }

        BufferLookup<InventoryBuffer> inventoryLookup = GetBufferLookup<InventoryBuffer>(isReadOnly: true);
        BufferLookup<ContainedObjectsBuffer> containedLookup = GetBufferLookup<ContainedObjectsBuffer>(isReadOnly: true);
        BufferLookup<InventorySlotRequirementBuffer> inventorySlotRequirementLookup = GetBufferLookup<InventorySlotRequirementBuffer>(isReadOnly: true);
        BufferLookup<LockedObjectsBuffer> lockedObjectsBufferLookup = GetBufferLookup<LockedObjectsBuffer>(isReadOnly: true);
        ComponentLookup<LocalTransform> localTransformLookup = GetComponentLookup<LocalTransform>(isReadOnly: true);
        ComponentLookup<ObjectCategoryTagsCD> objectCategoryTagsLookup = GetComponentLookup<ObjectCategoryTagsCD>(isReadOnly: true);
        ComponentLookup<OverrideLegendaryForSlotRequirementsCD> overrideLegendaryLookup = GetComponentLookup<OverrideLegendaryForSlotRequirementsCD>(isReadOnly: true);
        ComponentLookup<DurabilityCD> durabilityLookup = GetComponentLookup<DurabilityCD>(isReadOnly: true);
        ComponentLookup<FullnessCD> fullnessLookup = GetComponentLookup<FullnessCD>(isReadOnly: true);
        ComponentLookup<PetCD> petLookup = GetComponentLookup<PetCD>(isReadOnly: true);

        using NativeArray<Entity> relayEntities = cache.RelayQuery.ToEntityArray(Allocator.Temp);
        for (int changeIndex = 0; changeIndex < initialLength; changeIndex++)
        {
            InventoryChangeData change = inventoryChanges[changeIndex].inventoryChangeData;
            if (change.inventoryAction != InventoryAction.QuickStackToNearbyChests ||
                change.inventory1 == Entity.Null ||
                !inventoryLookup.HasBuffer(change.inventory1) ||
                !containedLookup.HasBuffer(change.inventory1) ||
                !localTransformLookup.TryGetComponent(change.inventory1, out LocalTransform playerTransform))
            {
                continue;
            }

            GatherNearbyNetworks(playerTransform.Position, relayEntities, localTransformLookup, cache);
            if (_nearbyNetworks.Count == 0)
            {
                continue;
            }

            StorageTerminalNetworkDepositSimulation simulation = new();
            ContainedObjectsBuffer[] simulatedPlayerContents = simulation.GetContents(change.inventory1, containedLookup);
            if (simulatedPlayerContents == null)
            {
                continue;
            }

            bool hasLockedObjects = lockedObjectsBufferLookup.TryGetBuffer(change.inventory1, out DynamicBuffer<LockedObjectsBuffer> lockedObjects);
            DynamicBuffer<InventoryBuffer> playerInventories = inventoryLookup[change.inventory1];
            for (int inventoryIndex = 0; inventoryIndex < playerInventories.Length; inventoryIndex++)
            {
                InventoryBuffer inventory = playerInventories[inventoryIndex];
                int endIndex = inventory.startIndex + inventory.size;
                for (int slot = inventory.startIndex; slot < endIndex && slot < simulatedPlayerContents.Length; slot++)
                {
                    if (hasLockedObjects && lockedObjects[slot].Value)
                    {
                        continue;
                    }

                    ContainedObjectsBuffer sourceObject = simulatedPlayerContents[slot];
                    if (sourceObject.objectID == ObjectID.None ||
                        sourceObject.amount <= 0 ||
                        !PugDatabase.GetEntityObjectInfo(sourceObject.objectID, databaseBank.databaseBankBlob, sourceObject.variation).isStackable ||
                        StorageTerminalSummaryUtility.ShouldCreateExactEntry(databaseBank, durabilityLookup, fullnessLookup, petLookup, sourceObject))
                    {
                        continue;
                    }

                    for (int networkIndex = 0; networkIndex < _nearbyNetworks.Count; networkIndex++)
                    {
                        if (simulatedPlayerContents[slot].objectID == ObjectID.None)
                        {
                            break;
                        }

                        StorageTerminalNetworkDepositPlanner.QueueDepositFromPlayerSlot(
                            change.inventory1,
                            slot,
                            int.MaxValue,
                            requireExistingMatch: true,
                            _nearbyNetworks[networkIndex].Network.CraftVisibleInventories,
                            simulation,
                            inventoryLookup,
                            containedLookup,
                            inventorySlotRequirementLookup,
                            databaseBank,
                            objectCategoryTagsLookup,
                            overrideLegendaryLookup,
                            durabilityLookup,
                            fullnessLookup,
                            petLookup,
                            inventoryChanges);
                    }
                }
            }
        }

        base.OnUpdate();
    }

    private void GatherNearbyNetworks(
        float3 origin,
        NativeArray<Entity> relayEntities,
        ComponentLookup<LocalTransform> localTransformLookup,
        StorageNetworkWorldCache cache)
    {
        _nearbyNetworks.Clear();
        _seenNetworks.Clear();

        float maxDistanceSq = QuickStackRange * QuickStackRange;
        for (int i = 0; i < relayEntities.Length; i++)
        {
            Entity relayEntity = relayEntities[i];
            if (!localTransformLookup.TryGetComponent(relayEntity, out LocalTransform relayTransform) ||
                math.distancesq(origin, relayTransform.Position) > maxDistanceSq ||
                !cache.TryGetNetworkForRelay(relayEntity, out StorageNetworkSnapshot network) ||
                network.CraftVisibleInventories.Count == 0 ||
                !_seenNetworks.Add(network))
            {
                continue;
            }

            _nearbyNetworks.Add(new NearbyRelayNetwork(network, math.distancesq(origin, relayTransform.Position)));
        }

        _nearbyNetworks.Sort(static (left, right) => left.DistanceSq.CompareTo(right.DistanceSq));
    }

    private readonly struct NearbyRelayNetwork
    {
        public readonly StorageNetworkSnapshot Network;
        public readonly float DistanceSq;

        public NearbyRelayNetwork(StorageNetworkSnapshot network, float distanceSq)
        {
            Network = network;
            DistanceSq = distanceSq;
        }
    }
}
