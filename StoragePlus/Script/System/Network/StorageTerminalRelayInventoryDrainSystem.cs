using Inventory;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(InventorySystemGroup))]
[UpdateAfter(typeof(InventoryUpdateSystem))]
public partial class StorageTerminalRelayInventoryDrainSystem : PugSimulationSystemBase
{
    private InventoryHandlerShared _inventoryHandlerShared;

    protected override void OnCreate()
    {
        NeedDatabase();
        RequireForUpdate<StorageCraftingRelayTag>();
        RequireForUpdate<NetworkTime>();
        RequireForUpdate<SkillTalentsTableCD>();
        RequireForUpdate<UpgradeCostsTableCD>();
        RequireForUpdate<InventoryAuxDataSystemDataCD>();
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

        _inventoryHandlerShared.Update(ref base.CheckedStateRef, CreateCommandBuffer(), networkTime);

        BufferLookup<InventoryBuffer> inventoryLookup = _inventoryHandlerShared.inventoryLookup;
        BufferLookup<ContainedObjectsBuffer> containedLookup = _inventoryHandlerShared.containedObjectsBufferLookup;
        BufferLookup<InventorySlotRequirementBuffer> inventorySlotRequirementLookup = _inventoryHandlerShared.inventorySlotRequirementBufferLookup;
        ComponentLookup<ObjectCategoryTagsCD> objectCategoryTagsLookup = GetComponentLookup<ObjectCategoryTagsCD>(isReadOnly: true);
        ComponentLookup<OverrideLegendaryForSlotRequirementsCD> overrideLegendaryLookup = GetComponentLookup<OverrideLegendaryForSlotRequirementsCD>(isReadOnly: true);
        ComponentLookup<DurabilityCD> durabilityLookup = GetComponentLookup<DurabilityCD>(isReadOnly: true);
        ComponentLookup<FullnessCD> fullnessLookup = GetComponentLookup<FullnessCD>(isReadOnly: true);
        ComponentLookup<PetCD> petLookup = GetComponentLookup<PetCD>(isReadOnly: true);

        foreach (var (relayTransform, relayEntity) in SystemAPI
                     .Query<RefRO<LocalTransform>>()
                     .WithAll<StorageCraftingRelayTag>()
                     .WithEntityAccess())
        {
            if (!inventoryLookup.HasBuffer(relayEntity) || !containedLookup.HasBuffer(relayEntity))
            {
                continue;
            }

            DynamicBuffer<InventoryBuffer> relayInventories = inventoryLookup[relayEntity];
            DynamicBuffer<ContainedObjectsBuffer> relayContents = containedLookup[relayEntity];
            if (!HasAnyStagedItem(relayInventories, relayContents))
            {
                continue;
            }

            float3 dropPosition = relayTransform.ValueRO.Position;
            if (!cache.TryGetNetworkForRelay(relayEntity, out StorageNetworkSnapshot network) ||
                network.CraftVisibleInventories.Count == 0)
            {
                DropAllStagedItems(relayEntity, relayInventories, relayContents, in _inventoryHandlerShared, dropPosition);
                continue;
            }

            StorageTerminalNetworkDepositSimulation simulation = new();
            for (int inventoryIndex = 0; inventoryIndex < relayInventories.Length; inventoryIndex++)
            {
                InventoryBuffer inventory = relayInventories[inventoryIndex];
                int endIndex = inventory.startIndex + inventory.size;
                for (int slot = inventory.startIndex; slot < endIndex && slot < relayContents.Length; slot++)
                {
                    if (relayContents[slot].objectID == ObjectID.None)
                    {
                        continue;
                    }

                    StorageTerminalNetworkDepositPlanner.MoveSourceSlotToDestinationsOrDrop(
                        relayEntity,
                        slot,
                        network.CraftVisibleInventories,
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
                        in _inventoryHandlerShared,
                        dropPosition);
                }
            }
        }

        base.OnUpdate();
    }

    private static bool HasAnyStagedItem(
        DynamicBuffer<InventoryBuffer> inventories,
        DynamicBuffer<ContainedObjectsBuffer> contents)
    {
        for (int inventoryIndex = 0; inventoryIndex < inventories.Length; inventoryIndex++)
        {
            InventoryBuffer inventory = inventories[inventoryIndex];
            int endIndex = inventory.startIndex + inventory.size;
            for (int slot = inventory.startIndex; slot < endIndex && slot < contents.Length; slot++)
            {
                if (contents[slot].objectID != ObjectID.None)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void DropAllStagedItems(
        Entity relayEntity,
        DynamicBuffer<InventoryBuffer> inventories,
        DynamicBuffer<ContainedObjectsBuffer> contents,
        in InventoryHandlerShared inventoryHandlerShared,
        float3 dropPosition)
    {
        for (int inventoryIndex = 0; inventoryIndex < inventories.Length; inventoryIndex++)
        {
            InventoryBuffer inventory = inventories[inventoryIndex];
            int endIndex = inventory.startIndex + inventory.size;
            for (int slot = inventory.startIndex; slot < endIndex && slot < contents.Length; slot++)
            {
                if (contents[slot].objectID == ObjectID.None)
                {
                    continue;
                }

                InventoryUtility.DropItem(
                    in inventoryHandlerShared,
                    relayEntity,
                    slot,
                    int.MaxValue,
                    dropPosition);
            }
        }
    }
}
