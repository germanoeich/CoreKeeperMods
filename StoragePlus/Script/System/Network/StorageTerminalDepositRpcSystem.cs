using System.Collections.Generic;
using Inventory;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class StorageTerminalDepositRpcSystem : PugSimulationSystemBase
{
    protected override void OnCreate()
    {
        NeedDatabase();
        RequireForUpdate<StorageTerminalDepositRpc>();
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
                     .Query<RefRO<StorageTerminalDepositRpc>, RefRO<ReceiveRpcCommandRequest>>()
                     .WithEntityAccess())
        {
            ecb.DestroyEntity(rpcEntity);

            StorageTerminalDepositRpc request = rpc.ValueRO;
            if (request.sourceSlot < 0 || request.relayEntity == Entity.Null)
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
                !containedLookup.HasBuffer(playerEntity) ||
                !cache.TryGetNetworkForRelay(request.relayEntity, out StorageNetworkSnapshot network) ||
                network.CraftVisibleInventories.Count == 0)
            {
                continue;
            }

            DynamicBuffer<ContainedObjectsBuffer> playerContents = containedLookup[playerEntity];
            if (request.sourceSlot >= playerContents.Length)
            {
                continue;
            }

            StorageTerminalNetworkDepositSimulation simulation = new();
            StorageTerminalNetworkDepositPlanner.QueueDepositFromPlayerSlot(
                playerEntity,
                request.sourceSlot,
                request.amount <= 0 ? int.MaxValue : request.amount,
                requireExistingMatch: false,
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
                inventoryChanges);
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();
        base.OnUpdate();
    }
}
