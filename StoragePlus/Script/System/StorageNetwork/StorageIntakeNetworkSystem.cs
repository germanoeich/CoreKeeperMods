using System.Collections.Generic;
using Inventory;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class StorageIntakeNetworkSystem : PugSimulationSystemBase
{
    private readonly StorageIntakeRouteBuilder _routeBuilder = new();
    private readonly List<StorageIntakeNetworkRoutes> _cachedNetworks = new();
    private readonly Dictionary<long, StorageIntakeHopperRoute> _hopperRoutesByTile = new();

    private ulong _cachedTopologyHash;
    private ulong _cachedMemberHash;
    private EntityQuery _droppedItemQuery;

    protected override void OnCreate()
    {
        NeedDatabase();
        RequireForUpdate<StorageConnectorTag>();
        RequireForUpdate<InventoryChangeBuffer>();
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
        StorageNetworkWorldCache cache = StorageNetworkWorldCacheRegistry.GetOrCreate(World);
        cache.EnsureBuilt(databaseBank);

        if (cache.TopologyHash != _cachedTopologyHash || cache.IntakeMembershipHash != _cachedMemberHash)
        {
            _cachedTopologyHash = cache.TopologyHash;
            _cachedMemberHash = cache.IntakeMembershipHash;
            _routeBuilder.Rebuild(cache.Networks, _cachedNetworks);
            RebuildHopperRouteLookup();
        }

        if (_cachedNetworks.Count > 0)
        {
            DrainConnectorOutputs(databaseBank);
            CollectDroppedItems(databaseBank);
        }

        base.OnUpdate();
    }

    private void RebuildHopperRouteLookup()
    {
        _hopperRoutesByTile.Clear();

        for (int networkIndex = 0; networkIndex < _cachedNetworks.Count; networkIndex++)
        {
            List<StorageIntakeHopperRoute> hopperRoutes = _cachedNetworks[networkIndex].HopperRoutes;
            for (int routeIndex = 0; routeIndex < hopperRoutes.Count; routeIndex++)
            {
                StorageIntakeHopperRoute route = hopperRoutes[routeIndex];
                _hopperRoutesByTile[route.HopperTileKey] = route;
            }
        }
    }

    private void DrainConnectorOutputs(PugDatabase.DatabaseBankCD databaseBank)
    {
        Entity inventoryChangeBufferEntity = SystemAPI.GetSingletonEntity<InventoryChangeBuffer>();
        DynamicBuffer<InventoryChangeBuffer> inventoryChangeBuffer = SystemAPI.GetBuffer<InventoryChangeBuffer>(inventoryChangeBufferEntity);

        BufferLookup<ContainedObjectsBuffer> containedLookup = GetBufferLookup<ContainedObjectsBuffer>(isReadOnly: true);
        BufferLookup<InventoryBuffer> inventoryLookup = GetBufferLookup<InventoryBuffer>(isReadOnly: true);
        ComponentLookup<DurabilityCD> durabilityLookup = GetComponentLookup<DurabilityCD>(isReadOnly: true);
        ComponentLookup<FullnessCD> fullnessLookup = GetComponentLookup<FullnessCD>(isReadOnly: true);
        ComponentLookup<PetCD> petLookup = GetComponentLookup<PetCD>(isReadOnly: true);

        for (int networkIndex = 0; networkIndex < _cachedNetworks.Count; networkIndex++)
        {
            StorageIntakeNetworkRoutes network = _cachedNetworks[networkIndex];
            for (int routeIndex = 0; routeIndex < network.OutputRoutes.Count; routeIndex++)
            {
                StorageIntakeOutputRoute route = network.OutputRoutes[routeIndex];
                if (!StorageIntakeTransferPlanner.TryPlanNextTransfer(
                        route.OutputInventory,
                        route.OrderedInputInventories,
                        databaseBank,
                        inventoryLookup,
                        containedLookup,
                        durabilityLookup,
                        fullnessLookup,
                        petLookup,
                        out StorageIntakeTransferOperation transfer))
                {
                    continue;
                }

                inventoryChangeBuffer.Add(new InventoryChangeBuffer
                {
                    inventoryChangeData = Create.MoveAmount(
                        transfer.SourceInventory,
                        transfer.SourceSlot,
                        transfer.DestinationInventory,
                        transfer.DestinationSlot,
                        transfer.DestinationSlot + 1,
                        1,
                        destroyExisting: false)
                });
            }
        }
    }

    private void CollectDroppedItems(PugDatabase.DatabaseBankCD databaseBank)
    {
        if (_hopperRoutesByTile.Count == 0)
        {
            return;
        }

        using NativeArray<Entity> droppedItems = _droppedItemQuery.ToEntityArray(Allocator.Temp);
        if (droppedItems.Length == 0)
        {
            return;
        }

        Entity inventoryChangeBufferEntity = SystemAPI.GetSingletonEntity<InventoryChangeBuffer>();
        DynamicBuffer<InventoryChangeBuffer> inventoryChangeBuffer = SystemAPI.GetBuffer<InventoryChangeBuffer>(inventoryChangeBufferEntity);

        ComponentLookup<ObjectDataCD> objectDataLookup = GetComponentLookup<ObjectDataCD>(isReadOnly: true);
        ComponentLookup<LocalTransform> localTransformLookup = GetComponentLookup<LocalTransform>(isReadOnly: true);
        ComponentLookup<PickUpItemCD> pickUpLookup = GetComponentLookup<PickUpItemCD>(isReadOnly: true);
        ComponentLookup<EntityDestroyedCD> entityDestroyedLookup = GetComponentLookup<EntityDestroyedCD>(isReadOnly: true);
        ComponentLookup<ObjectCategoryTagsCD> objectCategoryTagsLookup = GetComponentLookup<ObjectCategoryTagsCD>(isReadOnly: true);
        ComponentLookup<OverrideLegendaryForSlotRequirementsCD> overrideLegendaryLookup = GetComponentLookup<OverrideLegendaryForSlotRequirementsCD>(isReadOnly: true);
        BufferLookup<ContainedObjectsBuffer> containedLookup = GetBufferLookup<ContainedObjectsBuffer>(isReadOnly: true);
        BufferLookup<InventoryBuffer> inventoryLookup = GetBufferLookup<InventoryBuffer>(isReadOnly: true);
        BufferLookup<InventorySlotRequirementBuffer> slotRequirementLookup = GetBufferLookup<InventorySlotRequirementBuffer>(isReadOnly: true);

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

            DynamicBuffer<ContainedObjectsBuffer> droppedContents = containedLookup[droppedItemEntity];
            if (!TryGetDroppedItem(droppedContents, out ContainedObjectsBuffer droppedObject))
            {
                continue;
            }

            LocalTransform droppedTransform = localTransformLookup[droppedItemEntity];
            long tileKey = ToTileKey(droppedTransform.Position);
            if (!_hopperRoutesByTile.TryGetValue(tileKey, out StorageIntakeHopperRoute route))
            {
                continue;
            }

            if (!TryFindHopperDestinationInventory(
                    droppedObject,
                    route.OrderedInputInventories,
                    databaseBank,
                    inventoryLookup,
                    containedLookup,
                    slotRequirementLookup,
                    overrideLegendaryLookup,
                    objectCategoryTagsLookup,
                    out Entity destinationInventory))
            {
                continue;
            }

            inventoryChangeBuffer.Add(new InventoryChangeBuffer
            {
                inventoryChangeData = Create.MoveOrDropAllItems(
                    droppedItemEntity,
                    destinationInventory,
                    -1,
                    -1,
                    droppedTransform.Position)
            });
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

    private static bool TryFindHopperDestinationInventory(
        ContainedObjectsBuffer droppedObject,
        List<Entity> orderedInputInventories,
        PugDatabase.DatabaseBankCD databaseBank,
        BufferLookup<InventoryBuffer> inventoryLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup,
        BufferLookup<InventorySlotRequirementBuffer> slotRequirementLookup,
        ComponentLookup<OverrideLegendaryForSlotRequirementsCD> overrideLegendaryLookup,
        ComponentLookup<ObjectCategoryTagsCD> objectCategoryTagsLookup,
        out Entity destinationInventory)
    {
        destinationInventory = Entity.Null;
        Entity primaryPrefabEntity = PugDatabase.GetPrimaryPrefabEntity(
            droppedObject.objectID,
            databaseBank.databaseBankBlob,
            droppedObject.variation);
        objectCategoryTagsLookup.TryGetComponent(primaryPrefabEntity, out ObjectCategoryTagsCD objectTags);

        for (int i = 0; i < orderedInputInventories.Count; i++)
        {
            Entity inventoryEntity = orderedInputInventories[i];
            if (!inventoryLookup.HasBuffer(inventoryEntity) ||
                !containedLookup.HasBuffer(inventoryEntity) ||
                !slotRequirementLookup.HasBuffer(inventoryEntity))
            {
                continue;
            }

            if (!InventoryUtility.HasRoomForObject(
                    databaseBank,
                    containedLookup[inventoryEntity],
                    inventoryLookup[inventoryEntity],
                    slotRequirementLookup[inventoryEntity],
                    overrideLegendaryLookup,
                    objectTags,
                    droppedObject.objectID,
                    droppedObject.variation))
            {
                continue;
            }

            destinationInventory = inventoryEntity;
            return true;
        }

        return false;
    }

    private static long ToTileKey(float3 worldPosition)
    {
        int2 tile = new((int)math.round(worldPosition.x), (int)math.round(worldPosition.z));
        return ((long)tile.x << 32) ^ (uint)tile.y;
    }
}
