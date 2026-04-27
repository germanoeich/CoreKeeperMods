using System.Collections.Generic;
using CoreLib.Submodule.UserInterface;
using Unity.Entities;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation, WorldSystemFilterFlags.Default)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class StorageCraftingRelaySummarySystem : PugSimulationSystemBase
{
    private readonly Dictionary<ulong, StorageCraftingRelaySummaryBuildResult> _summaryByNetworkHash = new();
    private readonly HashSet<ulong> _activeNetworkHashes = new();
    private readonly HashSet<ulong> _rebuiltNetworkHashesThisUpdate = new();
    private readonly List<ulong> _staleNetworkHashes = new();

    protected override void OnCreate()
    {
        NeedDatabase();
        RequireForUpdate<StorageCraftingRelayTag>();
        base.OnCreate();
    }

    protected override void OnUpdate()
    {
        if (!ShouldUpdateInThisWorld())
        {
            base.OnUpdate();
            return;
        }

        PugDatabase.DatabaseBankCD databaseBank = SystemAPI.GetSingleton<PugDatabase.DatabaseBankCD>();
        StorageNetworkWorldCache cache = StorageNetworkWorldCacheRegistry.GetOrCreate(World);
        cache.EnsureBuilt(databaseBank);

        BufferLookup<InventoryBuffer> inventoryLookup = GetBufferLookup<InventoryBuffer>(isReadOnly: true);
        BufferLookup<ContainedObjectsBuffer> containedLookup = GetBufferLookup<ContainedObjectsBuffer>(isReadOnly: true);
        ComponentLookup<DurabilityCD> durabilityLookup = GetComponentLookup<DurabilityCD>(isReadOnly: true);
        ComponentLookup<FullnessCD> fullnessLookup = GetComponentLookup<FullnessCD>(isReadOnly: true);
        ComponentLookup<PetCD> petLookup = GetComponentLookup<PetCD>(isReadOnly: true);
        uint lastSystemVersion = LastSystemVersion;

        _activeNetworkHashes.Clear();
        _rebuiltNetworkHashesThisUpdate.Clear();

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

            _activeNetworkHashes.Add(network.NetworkHash);
            bool hasBuildResult = _summaryByNetworkHash.TryGetValue(network.NetworkHash, out StorageCraftingRelaySummaryBuildResult buildResult);
            bool shouldBuild = !hasBuildResult;
            ulong inventoryFingerprint = 0UL;
            bool inventoryFingerprintComputed = false;

            if (hasBuildResult &&
                !_rebuiltNetworkHashesThisUpdate.Contains(network.NetworkHash) &&
                NetworkInventoriesChanged(network, inventoryLookup, containedLookup, lastSystemVersion))
            {
                inventoryFingerprint = ComputeInventoryFingerprint(network, inventoryLookup, containedLookup);
                inventoryFingerprintComputed = true;
                shouldBuild = inventoryFingerprint != buildResult.InventoryFingerprint;
            }

            if (shouldBuild)
            {
                if (!inventoryFingerprintComputed)
                {
                    inventoryFingerprint = ComputeInventoryFingerprint(network, inventoryLookup, containedLookup);
                }

                buildResult = StorageCraftingRelaySummaryBuilder.BuildSummary(
                    network,
                    databaseBank,
                    inventoryLookup,
                    containedLookup,
                    durabilityLookup,
                    fullnessLookup,
                    petLookup);
                buildResult.InventoryFingerprint = inventoryFingerprint;
                _summaryByNetworkHash[network.NetworkHash] = buildResult;
                _rebuiltNetworkHashesThisUpdate.Add(network.NetworkHash);
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

        PruneStaleSummaryCacheEntries();
        base.OnUpdate();
    }

    private bool ShouldUpdateInThisWorld()
    {
        if (!World.IsClient())
        {
            return true;
        }

        if (UserInterfaceModule.GetCurrentInterface<StorageTerminalUI>() != null)
        {
            return true;
        }

        PlayerController player = Manager.main?.player;
        return player?.activeCraftingHandler != null &&
               player.activeCraftingHandler != player.playerCraftingHandler;
    }

    private static bool NetworkInventoriesChanged(
        StorageNetworkSnapshot network,
        BufferLookup<InventoryBuffer> inventoryLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup,
        uint lastSystemVersion)
    {
        for (int i = 0; i < network.CraftVisibleInventories.Count; i++)
        {
            Entity inventoryEntity = network.CraftVisibleInventories[i];
            if (!inventoryLookup.HasBuffer(inventoryEntity) || !containedLookup.HasBuffer(inventoryEntity))
            {
                return true;
            }

            if (inventoryLookup.DidChange(inventoryEntity, lastSystemVersion) ||
                containedLookup.DidChange(inventoryEntity, lastSystemVersion))
            {
                return true;
            }
        }

        return false;
    }

    private static ulong ComputeInventoryFingerprint(
        StorageNetworkSnapshot network,
        BufferLookup<InventoryBuffer> inventoryLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup)
    {
        unchecked
        {
            ulong hash = 1469598103934665603UL;
            for (int i = 0; i < network.CraftVisibleInventories.Count; i++)
            {
                Entity inventoryEntity = network.CraftVisibleInventories[i];
                hash = (hash ^ (ulong)(uint)inventoryEntity.Index) * 1099511628211UL;
                hash = (hash ^ (ulong)(uint)inventoryEntity.Version) * 1099511628211UL;

                if (!inventoryLookup.HasBuffer(inventoryEntity) || !containedLookup.HasBuffer(inventoryEntity))
                {
                    hash = (hash ^ 0xD1B54A32D192ED03UL) * 1099511628211UL;
                    continue;
                }

                DynamicBuffer<InventoryBuffer> inventorySlots = inventoryLookup[inventoryEntity];
                DynamicBuffer<ContainedObjectsBuffer> containedObjects = containedLookup[inventoryEntity];
                hash = (hash ^ (ulong)(uint)inventorySlots.Length) * 1099511628211UL;
                hash = (hash ^ (ulong)(uint)containedObjects.Length) * 1099511628211UL;

                for (int inventoryIndex = 0; inventoryIndex < inventorySlots.Length; inventoryIndex++)
                {
                    InventoryBuffer inventory = inventorySlots[inventoryIndex];
                    hash = (hash ^ (ulong)(uint)inventory.startIndex) * 1099511628211UL;
                    hash = (hash ^ (ulong)(uint)inventory.size) * 1099511628211UL;

                    int endIndex = inventory.startIndex + inventory.size;
                    for (int slot = inventory.startIndex; slot < endIndex && slot < containedObjects.Length; slot++)
                    {
                        ContainedObjectsBuffer containedObject = containedObjects[slot];
                        hash = (hash ^ (ulong)(uint)containedObject.objectID) * 1099511628211UL;
                        hash = (hash ^ (ulong)(uint)containedObject.amount) * 1099511628211UL;
                        hash = (hash ^ (ulong)(uint)containedObject.variation) * 1099511628211UL;
                        hash = (hash ^ (ulong)(uint)containedObject.auxDataIndex) * 1099511628211UL;
                    }
                }
            }

            return hash;
        }
    }

    private void PruneStaleSummaryCacheEntries()
    {
        _staleNetworkHashes.Clear();
        foreach (ulong networkHash in _summaryByNetworkHash.Keys)
        {
            if (!_activeNetworkHashes.Contains(networkHash))
            {
                _staleNetworkHashes.Add(networkHash);
            }
        }

        for (int i = 0; i < _staleNetworkHashes.Count; i++)
        {
            _summaryByNetworkHash.Remove(_staleNetworkHashes[i]);
        }
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
