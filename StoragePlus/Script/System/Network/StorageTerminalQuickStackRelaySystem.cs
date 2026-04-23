using System.Collections.Generic;
using Inventory;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(InventorySystemGroup))]
[UpdateBefore(typeof(InventoryUpdateSystem))]
public partial class StorageTerminalQuickStackRelaySystem : PugSimulationSystemBase
{
    private const float QuickStackRange = 10f;

    private readonly List<NearbyRelayNetwork> _nearbyNetworks = new();
    private readonly HashSet<StorageNetworkSnapshot> _seenNetworks = new();

    private InventoryHandlerShared _inventoryHandlerShared;

    protected override void OnCreate()
    {
        NeedDatabase();
        RequireForUpdate<StorageCraftingRelayTag>();
        RequireForUpdate<InventoryChangeBuffer>();
        RequireForUpdate<NetworkTime>();
        RequireForUpdate<SkillTalentsTableCD>();
        RequireForUpdate<UpgradeCostsTableCD>();
        RequireForUpdate<InventoryAuxDataSystemDataCD>();
        RequireForUpdate<WorldInfoCD>();
        base.OnCreate();
    }

    protected override void OnStartRunning()
    {
        _inventoryHandlerShared = new InventoryHandlerShared(
            ref base.CheckedStateRef,
            SystemAPI.GetSingleton<PugDatabase.DatabaseBankCD>(),
            SystemAPI.GetSingleton<SkillTalentsTableCD>(),
            SystemAPI.GetSingleton<UpgradeCostsTableCD>(),
            SystemAPI.GetSingleton<InventoryAuxDataSystemDataCD>());

        base.OnStartRunning();
    }

    protected override void OnUpdate()
    {
        NetworkTime networkTime = SystemAPI.GetSingleton<NetworkTime>();
        if (networkTime.IsPartialTick)
        {
            base.OnUpdate();
            return;
        }

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

        _inventoryHandlerShared.Update(ref base.CheckedStateRef, CreateCommandBuffer(), networkTime);

        bool worldIsReadOnly = SystemAPI.GetSingleton<WorldInfoCD>().guestMode;
        ComponentLookup<PlayerGhost> playerGhostLookup = _inventoryHandlerShared.playerGhostLookup;
        BufferLookup<InventoryBuffer> inventoryLookup = _inventoryHandlerShared.inventoryLookup;
        BufferLookup<ContainedObjectsBuffer> containedLookup = _inventoryHandlerShared.containedObjectsBufferLookup;
        BufferLookup<InventorySlotRequirementBuffer> inventorySlotRequirementLookup = _inventoryHandlerShared.inventorySlotRequirementBufferLookup;
        BufferLookup<LockedObjectsBuffer> lockedObjectsBufferLookup = _inventoryHandlerShared.lockedObjectsBufferLookup;
        ComponentLookup<LocalTransform> localTransformLookup = _inventoryHandlerShared.localTransformLookup;
        ComponentLookup<ObjectCategoryTagsCD> objectCategoryTagsLookup = GetComponentLookup<ObjectCategoryTagsCD>(isReadOnly: true);
        ComponentLookup<OverrideLegendaryForSlotRequirementsCD> overrideLegendaryLookup = GetComponentLookup<OverrideLegendaryForSlotRequirementsCD>(isReadOnly: true);
        ComponentLookup<DurabilityCD> durabilityLookup = GetComponentLookup<DurabilityCD>(isReadOnly: true);
        ComponentLookup<FullnessCD> fullnessLookup = GetComponentLookup<FullnessCD>(isReadOnly: true);
        ComponentLookup<PetCD> petLookup = GetComponentLookup<PetCD>(isReadOnly: true);

        using NativeArray<Entity> relayEntities = cache.RelayQuery.ToEntityArray(Allocator.Temp);
        for (int changeIndex = 0; changeIndex < initialLength; changeIndex++)
        {
            InventoryChangeBuffer inventoryChange = inventoryChanges[changeIndex];
            InventoryChangeData change = inventoryChange.inventoryChangeData;
            if (change.inventoryAction != InventoryAction.QuickStackToNearbyChests ||
                ShouldSkipForGuestMode(worldIsReadOnly, inventoryChange.playerEntity, playerGhostLookup) ||
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

                        StorageTerminalNetworkDepositPlanner.DropDepositFromPlayerSlot(
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
                            inventoryChanges,
                            in _inventoryHandlerShared,
                            playerTransform.Position);
                    }
                }
            }
        }

        base.OnUpdate();
    }

    private static bool ShouldSkipForGuestMode(
        bool worldIsReadOnly,
        Entity playerEntity,
        ComponentLookup<PlayerGhost> playerGhostLookup)
    {
        return worldIsReadOnly &&
               playerGhostLookup.TryGetComponent(playerEntity, out PlayerGhost playerGhost) &&
               playerGhost.adminPrivileges <= 0;
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
