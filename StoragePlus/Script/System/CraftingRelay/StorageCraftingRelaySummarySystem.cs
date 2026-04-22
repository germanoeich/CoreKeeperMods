using System.Collections.Generic;
using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class StorageCraftingRelaySummarySystem : PugSimulationSystemBase
{
    protected override void OnCreate()
    {
        NeedDatabase();
        RequireForUpdate<StorageCraftingRelayTag>();
        base.OnCreate();
    }

    protected override void OnUpdate()
    {
        PugDatabase.DatabaseBankCD databaseBank = SystemAPI.GetSingleton<PugDatabase.DatabaseBankCD>();
        StorageNetworkWorldCache cache = StorageNetworkWorldCacheRegistry.GetOrCreate(World);
        cache.EnsureBuilt(databaseBank);

        BufferLookup<InventoryBuffer> inventoryLookup = GetBufferLookup<InventoryBuffer>(isReadOnly: true);
        BufferLookup<ContainedObjectsBuffer> containedLookup = GetBufferLookup<ContainedObjectsBuffer>(isReadOnly: true);
        ComponentLookup<DurabilityCD> durabilityLookup = GetComponentLookup<DurabilityCD>(isReadOnly: true);
        ComponentLookup<FullnessCD> fullnessLookup = GetComponentLookup<FullnessCD>(isReadOnly: true);
        ComponentLookup<PetCD> petLookup = GetComponentLookup<PetCD>(isReadOnly: true);
        Dictionary<StorageNetworkSnapshot, StorageCraftingRelaySummaryBuildResult> summaryCache = new();

        foreach (var (state, summaryBuffer, resolvedBuffer, relayEntity) in SystemAPI
                     .Query<RefRW<StorageCraftingNetworkSummaryState>, DynamicBuffer<StorageCraftingNetworkSummaryEntry>, DynamicBuffer<StorageCraftingNetworkResolvedInventory>>()
                     .WithAll<StorageCraftingRelayTag>()
                     .WithEntityAccess())
        {
            if (!cache.TryGetNetworkForRelay(relayEntity, out StorageNetworkSnapshot network))
            {
                ClearRelay(state, summaryBuffer, resolvedBuffer);
                continue;
            }

            if (!summaryCache.TryGetValue(network, out StorageCraftingRelaySummaryBuildResult buildResult))
            {
                buildResult = StorageCraftingRelaySummaryBuilder.BuildSummary(
                    network,
                    databaseBank,
                    inventoryLookup,
                    containedLookup,
                    durabilityLookup,
                    fullnessLookup,
                    petLookup);
                summaryCache.Add(network, buildResult);
            }

            StorageCraftingNetworkSummaryState currentState = state.ValueRO;
            if (currentState.networkHash == network.NetworkHash &&
                currentState.contentsHash == buildResult.ContentsHash &&
                currentState.entryCount == buildResult.Entries.Count &&
                currentState.usedSlotCount == buildResult.UsedSlotCount &&
                currentState.totalSlotCount == buildResult.TotalSlotCount)
            {
                continue;
            }

            state.ValueRW = new StorageCraftingNetworkSummaryState
            {
                networkHash = network.NetworkHash,
                contentsHash = buildResult.ContentsHash,
                entryCount = buildResult.Entries.Count,
                usedSlotCount = buildResult.UsedSlotCount,
                totalSlotCount = buildResult.TotalSlotCount
            };

            summaryBuffer.Clear();
            for (int i = 0; i < buildResult.Entries.Count; i++)
            {
                summaryBuffer.Add(buildResult.Entries[i]);
            }

            resolvedBuffer.Clear();
            for (int i = 0; i < network.CraftVisibleInventories.Count; i++)
            {
                resolvedBuffer.Add(new StorageCraftingNetworkResolvedInventory
                {
                    inventoryEntity = network.CraftVisibleInventories[i]
                });
            }
        }

        base.OnUpdate();
    }

    private static void ClearRelay(
        RefRW<StorageCraftingNetworkSummaryState> state,
        DynamicBuffer<StorageCraftingNetworkSummaryEntry> summaryBuffer,
        DynamicBuffer<StorageCraftingNetworkResolvedInventory> resolvedBuffer)
    {
        if (state.ValueRO.networkHash == 0UL &&
            state.ValueRO.contentsHash == 0UL &&
            state.ValueRO.entryCount == 0 &&
            summaryBuffer.Length == 0 &&
            resolvedBuffer.Length == 0)
        {
            return;
        }

        state.ValueRW = default;
        summaryBuffer.Clear();
        resolvedBuffer.Clear();
    }
}
