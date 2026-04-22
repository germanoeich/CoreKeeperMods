using System;
using CoreLib.Submodule.UserInterface;
using Inventory;
using Unity.Entities;

public sealed partial class StorageTerminalUI
{
    private void RefreshEntries(bool force)
    {
        Entity relayEntity = GetRelayEntity();
        if (relayEntity == Entity.Null)
        {
            RefreshNetworkFullnessBar(0, 0, available: false);
            _filteredEntries.Clear();
            grid.SetEntries(_filteredEntries, resetScroll: true);
            SetEmptyState("Storage network unavailable.");
            _lastRelayEntity = Entity.Null;
            _lastContentsHash = 0;
            _lastEntryCount = -1;
            ResetNetworkFullnessCache();
            return;
        }

        EntityManager entityManager = world.EntityManager;
        if (!TryGetRelaySummary(entityManager, relayEntity, out StorageCraftingNetworkSummaryState summaryState, out DynamicBuffer<StorageCraftingNetworkSummaryEntry> summaryEntries))
        {
            RefreshNetworkFullnessBar(0, 0, available: false);
            _filteredEntries.Clear();
            grid.SetEntries(_filteredEntries, resetScroll: true);
            SetEmptyState("Storage network unavailable.");
            _lastRelayEntity = Entity.Null;
            _lastContentsHash = 0;
            _lastEntryCount = -1;
            ResetNetworkFullnessCache();
            return;
        }

        string filter = searchField != null ? searchField.GetInputText().Trim() : string.Empty;
        int categoryMask = GetSelectedCategoryMask();
        int totalItemCount = ComputeSummaryTotalItemCount(summaryEntries);
        bool relayChanged = relayEntity != _lastRelayEntity;
        bool filterChanged = !string.Equals(filter, _lastFilter, StringComparison.Ordinal);
        bool categoryChanged = categoryMask != _lastCategoryMask;
        int sortSignature = GetSortSignature();
        bool sortChanged = sortSignature != _lastSortSignature;
        ulong summaryEntriesHash = ComputeClientSummaryEntriesHash(summaryEntries);
        int summaryEntriesCount = summaryEntries.Length;
        bool fullnessChanged = summaryState.usedSlotCount != _lastUsedSlotCount ||
                               summaryState.totalSlotCount != _lastTotalSlotCount;
        bool needsRefresh = force ||
                            relayChanged ||
                            filterChanged ||
                            categoryChanged ||
                            sortChanged ||
                            fullnessChanged ||
                            summaryEntriesHash != _lastContentsHash ||
                            summaryEntriesCount != _lastEntryCount;
        if (!needsRefresh)
        {
            return;
        }

        _filteredEntries.Clear();
        for (int i = 0; i < summaryEntries.Length; i++)
        {
            StorageCraftingNetworkSummaryEntry summaryEntry = summaryEntries[i];
            if (summaryEntry.objectId == ObjectID.None || summaryEntry.totalAmount <= 0)
            {
                continue;
            }

            ContainedObjectsBuffer containedObject = CreateContainedObject(summaryEntry);
            string displayName = GetDisplayName(containedObject);
            if (!string.IsNullOrEmpty(filter) && displayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            StorageTerminalItemEntry entry = new StorageTerminalItemEntry(
                containedObject,
                summaryEntry.totalAmount,
                summaryEntry.entryId,
                (StorageTerminalSummaryEntryFlags)summaryEntry.flags,
                displayName);
            if (!MatchesSelectedCategories(entry))
            {
                continue;
            }

            _filteredEntries.Add(entry);
        }

        SortEntries(_filteredEntries);

        grid.SetEntries(_filteredEntries, resetScroll: force || filterChanged || categoryChanged || relayChanged || sortChanged);
        RefreshNetworkFullnessBar(summaryState.usedSlotCount, summaryState.totalSlotCount, available: true);
        SetEmptyState(GetEmptyStateText(summaryEntriesCount, _filteredEntries.Count, filter, HasSelectedCategories()));

        _lastRelayEntity = relayEntity;
        _lastContentsHash = summaryEntriesHash;
        _lastEntryCount = summaryEntriesCount;
        _lastFilter = filter;
        _lastCategoryMask = categoryMask;
        _lastSortSignature = sortSignature;
        _lastUsedSlotCount = summaryState.usedSlotCount;
        _lastTotalSlotCount = summaryState.totalSlotCount;
        _lastTotalItemCount = totalItemCount;
    }

    private static bool TryGetRelaySummary(
        EntityManager entityManager,
        Entity relayEntity,
        out StorageCraftingNetworkSummaryState summaryState,
        out DynamicBuffer<StorageCraftingNetworkSummaryEntry> summaryEntries)
    {
        if (!entityManager.Exists(relayEntity) ||
            !entityManager.HasComponent<StorageCraftingNetworkSummaryState>(relayEntity) ||
            !entityManager.HasBuffer<StorageCraftingNetworkSummaryEntry>(relayEntity))
        {
            summaryState = default;
            summaryEntries = default;
            return false;
        }

        summaryState = entityManager.GetComponentData<StorageCraftingNetworkSummaryState>(relayEntity);
        summaryEntries = entityManager.GetBuffer<StorageCraftingNetworkSummaryEntry>(relayEntity);
        return true;
    }

    private static ContainedObjectsBuffer CreateContainedObject(StorageCraftingNetworkSummaryEntry summaryEntry)
    {
        return new ContainedObjectsBuffer
        {
            objectData = new ObjectDataCD
            {
                objectID = summaryEntry.objectId,
                amount = summaryEntry.itemAmount,
                variation = summaryEntry.variation
            },
            auxDataIndex = summaryEntry.auxDataIndex
        };
    }

    private static string GetDisplayName(ContainedObjectsBuffer containedObject)
    {
        TextAndFormatFields objectName = PlayerController.GetObjectName(containedObject, localize: true);
        if (objectName != null && !string.IsNullOrWhiteSpace(objectName.text))
        {
            return objectName.text;
        }

        return containedObject.objectID.ToString();
    }

    private static ulong ComputeClientSummaryEntriesHash(DynamicBuffer<StorageCraftingNetworkSummaryEntry> summaryEntries)
    {
        unchecked
        {
            ulong hash = 1469598103934665603UL;
            for (int i = 0; i < summaryEntries.Length; i++)
            {
                StorageCraftingNetworkSummaryEntry entry = summaryEntries[i];
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

    private static int ComputeSummaryTotalItemCount(DynamicBuffer<StorageCraftingNetworkSummaryEntry> summaryEntries)
    {
        int totalItemCount = 0;
        for (int i = 0; i < summaryEntries.Length; i++)
        {
            StorageCraftingNetworkSummaryEntry entry = summaryEntries[i];
            if (entry.objectId == ObjectID.None || entry.totalAmount <= 0)
            {
                continue;
            }

            totalItemCount += entry.totalAmount;
        }

        return totalItemCount;
    }

    private string GetEmptyStateText(int unfilteredCount, int filteredCount, string filter, bool hasCategoryFilters)
    {
        if (unfilteredCount == 0)
        {
            return "Storage network is empty.";
        }

        if (filteredCount == 0 && (!string.IsNullOrEmpty(filter) || hasCategoryFilters))
        {
            return "No matching items.";
        }

        return string.Empty;
    }

    private void SetEmptyState(string message)
    {
        if (emptyStateText == null)
        {
            return;
        }

        bool hasMessage = !string.IsNullOrEmpty(message);
        emptyStateText.gameObject.SetActive(hasMessage);
        if (hasMessage)
        {
            emptyStateText.Render(message, force: true);
        }
    }
}
