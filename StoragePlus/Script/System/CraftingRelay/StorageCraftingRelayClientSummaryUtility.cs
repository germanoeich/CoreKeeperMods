using System.Collections.Generic;
using Inventory;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

internal static class StorageCraftingRelayClientSummaryUtility
{
    private const float RelayCraftingRange = 10f;

    private readonly struct NearbyRelayInfo
    {
        public readonly Entity RelayEntity;
        public readonly StorageNetworkSnapshot LocalNetwork;

        public NearbyRelayInfo(Entity relayEntity, StorageNetworkSnapshot localNetwork)
        {
            RelayEntity = relayEntity;
            LocalNetwork = localNetwork;
        }
    }

    public static bool TryBuildAdditionalCounts(
        CraftingHandler craftingHandler,
        List<Entity> nearbyChests,
        out PlayerController player,
        out Dictionary<ObjectID, int> additionalCounts,
        out Dictionary<ObjectID, Entity> sourceRelays)
    {
        player = Manager.main?.player;
        additionalCounts = null;
        sourceRelays = null;

        if (player == null || player.querySystem == null)
        {
            return false;
        }

        World world = player.querySystem.World;
        if (world == null || !world.IsCreated)
        {
            return false;
        }

        EntityManager entityManager = world.EntityManager;
        Entity craftingEntity = craftingHandler.craftingEntity;
        if (craftingEntity == Entity.Null ||
            !entityManager.Exists(craftingEntity) ||
            !entityManager.HasComponent<LocalTransform>(craftingEntity))
        {
            return false;
        }

        PugDatabase.DatabaseBankCD databaseBank = player.querySystem.GetSingleton<PugDatabase.DatabaseBankCD>();
        StorageNetworkWorldCache cache = StorageNetworkWorldCacheRegistry.GetOrCreate(world);
        cache.EnsureBuilt(databaseBank);

        float3 craftingPosition = entityManager.GetComponentData<LocalTransform>(craftingEntity).Position;
        List<NearbyRelayInfo> nearbyRelays = GetNearbyRelayInfos(entityManager, cache, craftingPosition);
        if (nearbyRelays.Count == 0)
        {
            return false;
        }

        BufferLookup<ContainedObjectsBuffer> containedLookup = player.querySystem.GetBufferLookup<ContainedObjectsBuffer>(isReadOnly: true);
        BufferLookup<InventoryBuffer> inventoryLookup = player.querySystem.GetBufferLookup<InventoryBuffer>(isReadOnly: true);
        ComponentLookup<DurabilityCD> durabilityLookup = player.querySystem.GetComponentLookup<DurabilityCD>(isReadOnly: true);
        ComponentLookup<FullnessCD> fullnessLookup = player.querySystem.GetComponentLookup<FullnessCD>(isReadOnly: true);
        ComponentLookup<PetCD> petLookup = player.querySystem.GetComponentLookup<PetCD>(isReadOnly: true);

        additionalCounts = new Dictionary<ObjectID, int>();
        sourceRelays = new Dictionary<ObjectID, Entity>();

        for (int i = 0; i < nearbyRelays.Count; i++)
        {
            NearbyRelayInfo relayInfo = nearbyRelays[i];
            Dictionary<ObjectID, int> localNetworkChestCounts = BuildLocalNetworkChestCounts(
                cache,
                relayInfo.LocalNetwork,
                nearbyChests,
                inventoryLookup,
                containedLookup,
                databaseBank,
                durabilityLookup,
                fullnessLookup,
                petLookup);

            DynamicBuffer<StorageCraftingNetworkSummaryEntry> summaryEntries = entityManager.GetBuffer<StorageCraftingNetworkSummaryEntry>(relayInfo.RelayEntity);
            Dictionary<ObjectID, int> relayCounts = BuildRelayCounts(summaryEntries);
            foreach (KeyValuePair<ObjectID, int> relayCount in relayCounts)
            {
                int extraAmount = relayCount.Value;
                if (localNetworkChestCounts != null && localNetworkChestCounts.TryGetValue(relayCount.Key, out int localAmount))
                {
                    extraAmount -= localAmount;
                }

                if (extraAmount <= 0)
                {
                    continue;
                }

                if (additionalCounts.TryGetValue(relayCount.Key, out int currentAmount))
                {
                    additionalCounts[relayCount.Key] = currentAmount + extraAmount;
                }
                else
                {
                    additionalCounts.Add(relayCount.Key, extraAmount);
                }

                if (!sourceRelays.ContainsKey(relayCount.Key))
                {
                    sourceRelays.Add(relayCount.Key, relayInfo.RelayEntity);
                }
            }
        }

        return additionalCounts.Count > 0;
    }

    public static void ApplyAdditionalCounts(
        EntityManager entityManager,
        Dictionary<ObjectID, int> additionalCounts,
        Dictionary<ObjectID, Entity> sourceRelays,
        List<PugDatabase.MaterialInfo> materialInfos)
    {
        if (materialInfos == null)
        {
            return;
        }

        for (int i = 0; i < materialInfos.Count; i++)
        {
            PugDatabase.MaterialInfo materialInfo = materialInfos[i];
            if (!additionalCounts.TryGetValue(materialInfo.objectID, out int additionalAmount) || additionalAmount <= 0)
            {
                continue;
            }

            materialInfo.amountAvailable += additionalAmount;
            if (materialInfo.nearbyChestWithMaterial != Entity.Null ||
                !sourceRelays.TryGetValue(materialInfo.objectID, out Entity relayEntity))
            {
                continue;
            }

            materialInfo.nearbyChestWithMaterial = relayEntity;
            materialInfo.nearbyChestIcon = GetRelayIcon(entityManager, relayEntity);
        }
    }

    private static List<NearbyRelayInfo> GetNearbyRelayInfos(
        EntityManager entityManager,
        StorageNetworkWorldCache cache,
        float3 craftingPosition)
    {
        List<NearbyRelayInfo> nearbyRelays = new();
        HashSet<ulong> seenNetworkHashes = new();
        float maxDistanceSq = RelayCraftingRange * RelayCraftingRange;

        using (NativeArray<Entity> relays = cache.RelayQuery.ToEntityArray(Allocator.Temp))
        {
            for (int i = 0; i < relays.Length; i++)
            {
                Entity relayEntity = relays[i];
                if (!entityManager.HasComponent<LocalTransform>(relayEntity) ||
                    !entityManager.HasComponent<StorageCraftingNetworkSummaryState>(relayEntity) ||
                    !entityManager.HasBuffer<StorageCraftingNetworkSummaryEntry>(relayEntity))
                {
                    continue;
                }

                StorageCraftingNetworkSummaryState summaryState = entityManager.GetComponentData<StorageCraftingNetworkSummaryState>(relayEntity);
                if (summaryState.networkHash == 0UL || summaryState.entryCount == 0)
                {
                    continue;
                }

                LocalTransform relayTransform = entityManager.GetComponentData<LocalTransform>(relayEntity);
                if (math.distancesq(craftingPosition, relayTransform.Position) > maxDistanceSq)
                {
                    continue;
                }

                if (!seenNetworkHashes.Add(summaryState.networkHash))
                {
                    continue;
                }

                cache.TryGetNetworkForRelay(relayEntity, out StorageNetworkSnapshot localNetwork);
                nearbyRelays.Add(new NearbyRelayInfo(relayEntity, localNetwork));
            }
        }

        return nearbyRelays;
    }

    private static Dictionary<ObjectID, int> BuildLocalNetworkChestCounts(
        StorageNetworkWorldCache cache,
        StorageNetworkSnapshot localNetwork,
        List<Entity> nearbyChests,
        BufferLookup<InventoryBuffer> inventoryLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup,
        PugDatabase.DatabaseBankCD databaseBank,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup)
    {
        if (localNetwork == null || nearbyChests == null || nearbyChests.Count == 0)
        {
            return null;
        }

        Dictionary<ObjectID, int> localCounts = null;
        for (int i = 0; i < nearbyChests.Count; i++)
        {
            Entity inventoryEntity = nearbyChests[i];
            if (!cache.TryGetCraftNetworkForInventory(inventoryEntity, out StorageNetworkSnapshot inventoryNetwork) ||
                !ReferenceEquals(inventoryNetwork, localNetwork) ||
                !inventoryLookup.HasBuffer(inventoryEntity) ||
                !containedLookup.HasBuffer(inventoryEntity))
            {
                continue;
            }

            localCounts ??= new Dictionary<ObjectID, int>();
            AccumulateInventoryCounts(
                localCounts,
                inventoryLookup[inventoryEntity],
                containedLookup[inventoryEntity],
                databaseBank,
                durabilityLookup,
                fullnessLookup,
                petLookup);
        }

        return localCounts;
    }

    private static Dictionary<ObjectID, int> BuildRelayCounts(DynamicBuffer<StorageCraftingNetworkSummaryEntry> summaryEntries)
    {
        Dictionary<ObjectID, int> relayCounts = new();
        for (int i = 0; i < summaryEntries.Length; i++)
        {
            StorageCraftingNetworkSummaryEntry summaryEntry = summaryEntries[i];
            if (summaryEntry.objectId == ObjectID.None || summaryEntry.totalAmount <= 0)
            {
                continue;
            }

            if (relayCounts.TryGetValue(summaryEntry.objectId, out int currentAmount))
            {
                relayCounts[summaryEntry.objectId] = currentAmount + summaryEntry.totalAmount;
            }
            else
            {
                relayCounts.Add(summaryEntry.objectId, summaryEntry.totalAmount);
            }
        }

        return relayCounts;
    }

    private static void AccumulateInventoryCounts(
        Dictionary<ObjectID, int> counts,
        DynamicBuffer<InventoryBuffer> inventorySlots,
        DynamicBuffer<ContainedObjectsBuffer> containedObjects,
        PugDatabase.DatabaseBankCD databaseBank,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup)
    {
        for (int inventoryIndex = 0; inventoryIndex < inventorySlots.Length; inventoryIndex++)
        {
            InventoryBuffer inventory = inventorySlots[inventoryIndex];
            int endIndex = inventory.startIndex + inventory.size;
            for (int slot = inventory.startIndex; slot < endIndex && slot < containedObjects.Length; slot++)
            {
                ContainedObjectsBuffer objectInSlot = containedObjects[slot];
                int countContribution = StorageTerminalSummaryUtility.GetCountContribution(
                    databaseBank,
                    durabilityLookup,
                    fullnessLookup,
                    petLookup,
                    objectInSlot);
                if (objectInSlot.objectID == ObjectID.None || countContribution <= 0)
                {
                    continue;
                }

                if (counts.TryGetValue(objectInSlot.objectID, out int amount))
                {
                    counts[objectInSlot.objectID] = amount + countContribution;
                }
                else
                {
                    counts.Add(objectInSlot.objectID, countContribution);
                }
            }
        }
    }

    private static Sprite GetRelayIcon(EntityManager entityManager, Entity relayEntity)
    {
        if (!entityManager.Exists(relayEntity) || !entityManager.HasComponent<ObjectDataCD>(relayEntity))
        {
            return null;
        }

        ObjectDataCD objectData = entityManager.GetComponentData<ObjectDataCD>(relayEntity);
        return PugDatabase.GetObjectInfo(objectData.objectID, objectData.variation)?.smallIcon;
    }
}
