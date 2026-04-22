using System.Collections.Generic;
using Inventory;
using Unity.Entities;
using Unity.Mathematics;

internal sealed class StorageCraftingRelaySummaryBuildResult
{
    public ulong ContentsHash;
    public int UsedSlotCount;
    public int TotalSlotCount;
    public readonly List<StorageCraftingNetworkSummaryEntry> Entries = new();
}

internal static class StorageCraftingRelaySummaryBuilder
{
    public static StorageCraftingRelaySummaryBuildResult BuildSummary(
        StorageNetworkSnapshot network,
        PugDatabase.DatabaseBankCD databaseBank,
        BufferLookup<InventoryBuffer> inventoryLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup)
    {
        Dictionary<long, StorageCraftingNetworkSummaryEntry> groupedEntries = new();
        List<StorageCraftingNetworkSummaryEntry> exactEntries = new();
        StorageCraftingRelaySummaryBuildResult result = new();

        for (int i = 0; i < network.CraftVisibleInventories.Count; i++)
        {
            Entity inventoryEntity = network.CraftVisibleInventories[i];
            if (!inventoryLookup.HasBuffer(inventoryEntity) || !containedLookup.HasBuffer(inventoryEntity))
            {
                continue;
            }

            DynamicBuffer<InventoryBuffer> inventorySlots = inventoryLookup[inventoryEntity];
            DynamicBuffer<ContainedObjectsBuffer> containedObjects = containedLookup[inventoryEntity];
            AccumulateInventoryCounts(
                groupedEntries,
                exactEntries,
                inventoryEntity,
                inventorySlots,
                containedObjects,
                ref result.UsedSlotCount,
                ref result.TotalSlotCount,
                databaseBank,
                durabilityLookup,
                fullnessLookup,
                petLookup);
        }

        List<StorageCraftingNetworkSummaryEntry> entries = new(groupedEntries.Count + exactEntries.Count);
        foreach (StorageCraftingNetworkSummaryEntry entry in groupedEntries.Values)
        {
            AddVirtualStackEntries(entries, entry.objectId, entry.variation, entry.totalAmount);
        }

        for (int i = 0; i < exactEntries.Count; i++)
        {
            entries.Add(exactEntries[i]);
        }

        entries.Sort(CompareEntries);

        result.ContentsHash = ComputeContentsHash(entries);

        for (int i = 0; i < entries.Count; i++)
        {
            result.Entries.Add(entries[i]);
        }

        return result;
    }

    private static void AccumulateInventoryCounts(
        Dictionary<long, StorageCraftingNetworkSummaryEntry> groupedEntries,
        List<StorageCraftingNetworkSummaryEntry> exactEntries,
        Entity inventoryEntity,
        DynamicBuffer<InventoryBuffer> inventorySlots,
        DynamicBuffer<ContainedObjectsBuffer> containedObjects,
        ref int usedSlotCount,
        ref int totalSlotCount,
        PugDatabase.DatabaseBankCD databaseBank,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup)
    {
        for (int inventoryIndex = 0; inventoryIndex < inventorySlots.Length; inventoryIndex++)
        {
            InventoryBuffer inventory = inventorySlots[inventoryIndex];
            int endIndex = inventory.startIndex + inventory.size;
            totalSlotCount += inventory.size;
            for (int slot = inventory.startIndex; slot < endIndex && slot < containedObjects.Length; slot++)
            {
                ContainedObjectsBuffer objectInSlot = containedObjects[slot];
                if (objectInSlot.objectID != ObjectID.None)
                {
                    usedSlotCount++;
                }

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

                if (StorageTerminalSummaryUtility.ShouldCreateExactEntry(
                        databaseBank,
                        durabilityLookup,
                        fullnessLookup,
                        petLookup,
                        objectInSlot))
                {
                    exactEntries.Add(new StorageCraftingNetworkSummaryEntry
                    {
                        objectId = objectInSlot.objectID,
                        totalAmount = countContribution,
                        itemAmount = objectInSlot.amount,
                        variation = objectInSlot.variation,
                        auxDataIndex = objectInSlot.auxDataIndex,
                        entryId = StorageTerminalSummaryUtility.BuildExactEntryId(inventoryEntity, slot),
                        flags = (byte)StorageTerminalSummaryEntryFlags.ExactMatch
                    });
                    continue;
                }

                long groupedKey = BuildGroupedKey(objectInSlot.objectID, objectInSlot.variation);
                if (groupedEntries.TryGetValue(groupedKey, out StorageCraftingNetworkSummaryEntry entry))
                {
                    entry.totalAmount += countContribution;
                    entry.itemAmount += countContribution;
                    groupedEntries[groupedKey] = entry;
                    continue;
                }

                groupedEntries.Add(groupedKey, new StorageCraftingNetworkSummaryEntry
                {
                    objectId = objectInSlot.objectID,
                    totalAmount = countContribution,
                    itemAmount = countContribution,
                    variation = objectInSlot.variation
                });
            }
        }
    }

    private static long BuildGroupedKey(ObjectID objectId, int variation)
    {
        return ((long)(uint)objectId << 32) | (uint)variation;
    }

    private static void AddVirtualStackEntries(
        List<StorageCraftingNetworkSummaryEntry> entries,
        ObjectID objectId,
        int variation,
        int totalAmount)
    {
        if (objectId == ObjectID.None || totalAmount <= 0)
        {
            return;
        }

        int stackIndex = 0;
        int remaining = totalAmount;
        while (remaining > 0)
        {
            int stackAmount = math.min(remaining, StorageTerminalSummaryUtility.MaxStackAmount);
            entries.Add(new StorageCraftingNetworkSummaryEntry
            {
                objectId = objectId,
                totalAmount = stackAmount,
                itemAmount = stackAmount,
                variation = variation,
                entryId = StorageTerminalSummaryUtility.BuildVirtualStackEntryId(objectId, variation, stackIndex),
                flags = (byte)StorageTerminalSummaryEntryFlags.VirtualStack
            });

            remaining -= stackAmount;
            stackIndex++;
        }
    }

    private static int CompareEntries(StorageCraftingNetworkSummaryEntry left, StorageCraftingNetworkSummaryEntry right)
    {
        int objectCompare = ((int)left.objectId).CompareTo((int)right.objectId);
        if (objectCompare != 0)
        {
            return objectCompare;
        }

        int variationCompare = left.variation.CompareTo(right.variation);
        if (variationCompare != 0)
        {
            return variationCompare;
        }

        int flagsCompare = left.flags.CompareTo(right.flags);
        if (flagsCompare != 0)
        {
            return flagsCompare;
        }

        int amountCompare = left.itemAmount.CompareTo(right.itemAmount);
        if (amountCompare != 0)
        {
            return amountCompare;
        }

        return left.entryId.CompareTo(right.entryId);
    }

    private static ulong ComputeContentsHash(List<StorageCraftingNetworkSummaryEntry> entries)
    {
        unchecked
        {
            ulong hash = 1469598103934665603UL;
            for (int i = 0; i < entries.Count; i++)
            {
                StorageCraftingNetworkSummaryEntry entry = entries[i];
                hash = (hash ^ (ulong)(uint)entry.objectId) * 1099511628211UL;
                hash = (hash ^ (ulong)(uint)entry.variation) * 1099511628211UL;
                hash = (hash ^ (ulong)(uint)entry.totalAmount) * 1099511628211UL;
                hash = (hash ^ (ulong)(uint)entry.itemAmount) * 1099511628211UL;
                hash = (hash ^ (ulong)(uint)entry.auxDataIndex) * 1099511628211UL;
                hash = (hash ^ entry.entryId) * 1099511628211UL;
                hash = (hash ^ entry.flags) * 1099511628211UL;
            }

            return hash;
        }
    }
}
