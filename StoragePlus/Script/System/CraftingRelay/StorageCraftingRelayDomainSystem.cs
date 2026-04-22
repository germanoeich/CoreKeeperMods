using System.Collections.Generic;
using Inventory;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(InventorySystemGroup))]
[UpdateBefore(typeof(InventoryUpdateSystem))]
public partial class StorageCraftingRelayDomainSystem : PugSimulationSystemBase
{
    private const float RelayCraftingRange = 10f;

    private readonly HashSet<Entity> _nearbyDeduplication = new();

    private InventoryHandlerShared _inventoryHandlerShared;
    private EntityQuery _relayQuery;

    protected override void OnCreate()
    {
        NeedDatabase();
        RequireForUpdate<StorageCraftingRelayTag>();
        RequireForUpdate<CraftBuffer>();
        RequireForUpdate<PhysicsWorldSingleton>();
        RequireForUpdate<NetworkTime>();
        RequireForUpdate<SkillTalentsTableCD>();
        RequireForUpdate<UpgradeCostsTableCD>();
        RequireForUpdate<InventoryAuxDataSystemDataCD>();
        _relayQuery = GetEntityQuery(
            ComponentType.ReadOnly<StorageCraftingRelayTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<StorageCraftingNetworkResolvedInventory>());
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
        Entity craftBufferEntity = SystemAPI.GetSingletonEntity<CraftBuffer>();
        DynamicBuffer<CraftBuffer> craftBuffer = SystemAPI.GetBuffer<CraftBuffer>(craftBufferEntity);
        if (craftBuffer.Length == 0)
        {
            base.OnUpdate();
            return;
        }

        NetworkTime networkTime = SystemAPI.GetSingleton<NetworkTime>();
        _inventoryHandlerShared.Update(ref base.CheckedStateRef, CreateCommandBuffer(), networkTime);

        ComponentLookup<LocalTransform> localTransformLookup = GetComponentLookup<LocalTransform>(isReadOnly: true);
        ComponentLookup<ObjectDataCD> objectDataLookup = GetComponentLookup<ObjectDataCD>(isReadOnly: true);
        ComponentLookup<InventoryAutoTransferEnabledCD> autoTransferLookup = GetComponentLookup<InventoryAutoTransferEnabledCD>(isReadOnly: true);
        BufferLookup<ContainedObjectsBuffer> containedLookup = GetBufferLookup<ContainedObjectsBuffer>(isReadOnly: true);
        BufferLookup<StorageCraftingNetworkResolvedInventory> resolvedInventoryLookup = GetBufferLookup<StorageCraftingNetworkResolvedInventory>(isReadOnly: true);
        CollisionWorld collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

        for (int i = 0; i < craftBuffer.Length; i++)
        {
            CraftBuffer craftRequest = craftBuffer[i];
            CraftActionData craftAction = craftRequest.craftActionData;

            if (!ShouldHandle(craftAction) ||
                craftAction.mainInventoryEntity == Entity.Null ||
                craftAction.craftingEntity == Entity.Null ||
                !localTransformLookup.TryGetComponent(craftAction.craftingEntity, out LocalTransform craftingTransform))
            {
                continue;
            }

            NativeList<Entity> inventories = new NativeList<Entity>(Allocator.Temp);
            inventories.Add(craftAction.mainInventoryEntity);

            InventoryUtility.GetNearbyChestsForCraftingByDistance(
                in craftingTransform.Position,
                in collisionWorld,
                in autoTransferLookup,
                in localTransformLookup,
                ref inventories);

            bool hasNearbyRelay = AddNearbyRelayInventories(
                craftingTransform.Position,
                localTransformLookup,
                containedLookup,
                resolvedInventoryLookup,
                ref inventories);

            if (!hasNearbyRelay)
            {
                inventories.Dispose();
                continue;
            }

            objectDataLookup.TryGetComponent(craftAction.mainInventoryEntity, out ObjectDataCD mainEntityObjectData);
            float3 outputPosition = default;
            if (localTransformLookup.TryGetComponent(craftAction.mainInventoryEntity, out LocalTransform mainTransform))
            {
                outputPosition = mainTransform.Position;
            }

            HandleCraftAction(craftAction, inventories, mainEntityObjectData, outputPosition);

            inventories.Dispose();
            craftBuffer.RemoveAt(i);
            i--;
        }

        base.OnUpdate();
    }

    private void HandleCraftAction(
        CraftActionData craftAction,
        NativeList<Entity> inventories,
        ObjectDataCD mainEntityObjectData,
        float3 outputPosition)
    {
        switch (craftAction.craftAction)
        {
            case CraftAction.Craft:
                InventoryUtility.Craft(
                    in _inventoryHandlerShared,
                    inventories[0],
                    mainEntityObjectData,
                    inventories,
                    new CanCraftObjectsBuffer
                    {
                        objectID = craftAction.objectId,
                        amount = craftAction.amount
                    },
                    craftAction.additionalFreeAmount,
                    outputPosition,
                    useCraftingCostMultiplier: true,
                    craftAction.playerEntity,
                    craftAction.craftingEntity);
                break;
            case CraftAction.SetupCookBookRecipe:
                InventoryUtility.SetupCookBookRecipe(
                    in _inventoryHandlerShared,
                    craftAction.craftingEntity,
                    craftAction.playerEntity,
                    craftAction.targetInventoryEntity,
                    craftAction.bool0,
                    inventories,
                    craftAction.objectId,
                    craftAction.int0);
                break;
            case CraftAction.ActivateRecipeSlot:
                InventoryUtility.ActivateRecipeSlot(
                    in _inventoryHandlerShared,
                    craftAction.craftingEntity,
                    craftAction.playerEntity,
                    inventories,
                    craftAction.int0,
                    craftAction.bool0,
                    craftAction.int1);
                break;
            case CraftAction.Upgrade:
                InventoryUtility.Upgrade(
                    in _inventoryHandlerShared,
                    craftAction.targetInventoryEntity,
                    craftAction.int0,
                    craftAction.objectId,
                    inventories,
                    craftAction.int1,
                    craftAction.bool0);
                break;
            case CraftAction.RepairOrReinforce:
                InventoryUtility.RepairOrReinforce(
                    in _inventoryHandlerShared,
                    craftAction.targetInventoryEntity,
                    craftAction.int0,
                    craftAction.objectId,
                    inventories,
                    craftAction.int1,
                    craftAction.craftingEntity,
                    craftAction.playerEntity,
                    craftAction.bool0,
                    craftAction.bool1);
                break;
            case CraftAction.RepairOrReinforceAllItems:
                InventoryUtility.RepairAllItems(
                    in _inventoryHandlerShared,
                    craftAction.targetInventoryEntity,
                    inventories,
                    craftAction.craftingEntity,
                    craftAction.playerEntity,
                    craftAction.bool0);
                break;
        }
    }

    private bool AddNearbyRelayInventories(
        float3 craftingPosition,
        ComponentLookup<LocalTransform> localTransformLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup,
        BufferLookup<StorageCraftingNetworkResolvedInventory> resolvedInventoryLookup,
        ref NativeList<Entity> inventories)
    {
        _nearbyDeduplication.Clear();
        for (int i = 0; i < inventories.Length; i++)
        {
            _nearbyDeduplication.Add(inventories[i]);
        }

        bool hasNearbyRelay = false;
        float maxDistanceSq = RelayCraftingRange * RelayCraftingRange;

        using (NativeArray<Entity> relays = _relayQuery.ToEntityArray(Allocator.Temp))
        {
            for (int relayIndex = 0; relayIndex < relays.Length; relayIndex++)
            {
                Entity relayEntity = relays[relayIndex];
                if (!localTransformLookup.TryGetComponent(relayEntity, out LocalTransform relayTransform) ||
                    math.distancesq(craftingPosition, relayTransform.Position) > maxDistanceSq)
                {
                    continue;
                }

                hasNearbyRelay = true;

                if (containedLookup.HasBuffer(relayEntity) && _nearbyDeduplication.Add(relayEntity))
                {
                    inventories.Add(relayEntity);
                }

                if (!resolvedInventoryLookup.HasBuffer(relayEntity))
                {
                    continue;
                }

                DynamicBuffer<StorageCraftingNetworkResolvedInventory> resolvedInventories = resolvedInventoryLookup[relayEntity];
                for (int i = 0; i < resolvedInventories.Length; i++)
                {
                    Entity inventoryEntity = resolvedInventories[i].inventoryEntity;
                    if (_nearbyDeduplication.Add(inventoryEntity))
                    {
                        inventories.Add(inventoryEntity);
                    }
                }
            }
        }

        return hasNearbyRelay;
    }

    private static bool ShouldHandle(CraftActionData craftAction)
    {
        return craftAction.craftAction == CraftAction.Craft && craftAction.objectId != ObjectID.None && craftAction.amount > 0 ||
               craftAction.craftAction == CraftAction.SetupCookBookRecipe && craftAction.objectId != ObjectID.None ||
               craftAction.craftAction == CraftAction.ActivateRecipeSlot && craftAction.int1 >= 0 ||
               craftAction.craftAction == CraftAction.Upgrade ||
               craftAction.craftAction == CraftAction.RepairOrReinforce ||
               craftAction.craftAction == CraftAction.RepairOrReinforceAllItems;
    }
}
