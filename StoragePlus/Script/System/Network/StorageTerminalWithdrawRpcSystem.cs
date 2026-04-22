using Inventory;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class StorageTerminalWithdrawRpcSystem : PugSimulationSystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<StorageTerminalWithdrawRpc>();
        RequireForUpdate<InventoryChangeBuffer>();
        base.OnCreate();
    }

    protected override void OnUpdate()
    {
        PugDatabase.DatabaseBankCD databaseBank = SystemAPI.GetSingleton<PugDatabase.DatabaseBankCD>();
        Entity inventoryChangeBufferEntity = SystemAPI.GetSingletonEntity<InventoryChangeBuffer>();
        DynamicBuffer<InventoryChangeBuffer> inventoryChanges = SystemAPI.GetBuffer<InventoryChangeBuffer>(inventoryChangeBufferEntity);

        BufferLookup<StorageCraftingNetworkResolvedInventory> resolvedInventoryLookup = GetBufferLookup<StorageCraftingNetworkResolvedInventory>(isReadOnly: true);
        BufferLookup<InventoryBuffer> inventoryLookup = GetBufferLookup<InventoryBuffer>(isReadOnly: true);
        BufferLookup<ContainedObjectsBuffer> containedLookup = GetBufferLookup<ContainedObjectsBuffer>(isReadOnly: true);
        BufferLookup<InventorySlotRequirementBuffer> inventorySlotRequirementLookup = GetBufferLookup<InventorySlotRequirementBuffer>(isReadOnly: true);
        ComponentLookup<CommandTarget> commandTargetLookup = GetComponentLookup<CommandTarget>(isReadOnly: true);
        ComponentLookup<ObjectCategoryTagsCD> objectCategoryTagsLookup = GetComponentLookup<ObjectCategoryTagsCD>(isReadOnly: true);
        ComponentLookup<OverrideLegendaryForSlotRequirementsCD> overrideLegendaryLookup = GetComponentLookup<OverrideLegendaryForSlotRequirementsCD>(isReadOnly: true);
        ComponentLookup<DurabilityCD> durabilityLookup = GetComponentLookup<DurabilityCD>(isReadOnly: true);
        ComponentLookup<FullnessCD> fullnessLookup = GetComponentLookup<FullnessCD>(isReadOnly: true);
        ComponentLookup<PetCD> petLookup = GetComponentLookup<PetCD>(isReadOnly: true);

        EntityCommandBuffer ecb = new(Allocator.Temp);

        foreach (var (rpc, receiveRpc, rpcEntity) in SystemAPI
                     .Query<RefRO<StorageTerminalWithdrawRpc>, RefRO<ReceiveRpcCommandRequest>>()
                     .WithEntityAccess())
        {
            ecb.DestroyEntity(rpcEntity);

            StorageTerminalWithdrawRpc request = rpc.ValueRO;
            if (request.amount <= 0 || request.objectId == ObjectID.None)
            {
                continue;
            }

            Entity sourceConnection = receiveRpc.ValueRO.SourceConnection;
            if (!commandTargetLookup.TryGetComponent(sourceConnection, out CommandTarget commandTarget))
            {
                continue;
            }

            Entity playerEntity = commandTarget.targetEntity;
            if (playerEntity == Entity.Null ||
                !EntityManager.Exists(playerEntity) ||
                !inventoryLookup.HasBuffer(playerEntity) ||
                !containedLookup.HasBuffer(playerEntity))
            {
                continue;
            }

            DynamicBuffer<InventoryBuffer> playerInventories = inventoryLookup[playerEntity];
            if (playerInventories.Length == 0)
            {
                continue;
            }

            InventoryBuffer playerMainInventory = playerInventories[0];
            DynamicBuffer<ContainedObjectsBuffer> playerContents = containedLookup[playerEntity];
            bool hasPlayerSlotRequirements = inventorySlotRequirementLookup.TryGetBuffer(playerEntity, out DynamicBuffer<InventorySlotRequirementBuffer> playerSlotRequirements);
            ContainedObjectsBuffer[] simulatedPlayerContents = new ContainedObjectsBuffer[playerContents.Length];
            for (int i = 0; i < playerContents.Length; i++)
            {
                simulatedPlayerContents[i] = playerContents[i];
            }

            if (request.relayEntity == Entity.Null ||
                !EntityManager.Exists(request.relayEntity) ||
                !resolvedInventoryLookup.HasBuffer(request.relayEntity))
            {
                continue;
            }

            bool takeAll = request.amount == int.MaxValue;
            int remaining = request.amount;
            DynamicBuffer<StorageCraftingNetworkResolvedInventory> resolvedInventories = resolvedInventoryLookup[request.relayEntity];
            StorageTerminalSummaryEntryFlags entryFlags = (StorageTerminalSummaryEntryFlags)request.flags;

            if ((entryFlags & StorageTerminalSummaryEntryFlags.ExactMatch) != 0)
            {
                StorageTerminalWithdrawRequestProcessor.TryQueueExactObject(
                    playerEntity,
                    request,
                    ref remaining,
                    takeAll,
                    resolvedInventories,
                    playerInventories,
                    playerMainInventory,
                    simulatedPlayerContents,
                    hasPlayerSlotRequirements,
                    playerSlotRequirements,
                    inventoryLookup,
                    containedLookup,
                    databaseBank,
                    objectCategoryTagsLookup,
                    overrideLegendaryLookup,
                    durabilityLookup,
                    fullnessLookup,
                    petLookup,
                    inventoryChanges);
                continue;
            }

            for (int i = 0; i < resolvedInventories.Length && (takeAll || remaining > 0); i++)
            {
                Entity sourceInventory = resolvedInventories[i].inventoryEntity;
                if (!inventoryLookup.HasBuffer(sourceInventory) || !containedLookup.HasBuffer(sourceInventory))
                {
                    continue;
                }

                StorageTerminalWithdrawRequestProcessor.QueueMatchingObjects(
                    playerEntity,
                    sourceInventory,
                    request.objectId,
                    request.variation,
                    ref remaining,
                    takeAll,
                    playerInventories,
                    playerMainInventory,
                    simulatedPlayerContents,
                    hasPlayerSlotRequirements,
                    playerSlotRequirements,
                    inventoryLookup[sourceInventory],
                    containedLookup[sourceInventory],
                    databaseBank,
                    objectCategoryTagsLookup,
                    overrideLegendaryLookup,
                    durabilityLookup,
                    fullnessLookup,
                    petLookup,
                    inventoryChanges);
            }
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();
        base.OnUpdate();
    }

}
