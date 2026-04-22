using Inventory;
using Unity.Entities;

internal static class StorageTerminalWithdrawRequestProcessor
{
    public static void QueueMatchingObjects(
        Entity playerEntity,
        Entity sourceInventory,
        ObjectID objectId,
        int variation,
        ref int remaining,
        bool takeAll,
        DynamicBuffer<InventoryBuffer> playerInventories,
        InventoryBuffer playerMainInventory,
        ContainedObjectsBuffer[] simulatedPlayerContents,
        bool hasPlayerSlotRequirements,
        DynamicBuffer<InventorySlotRequirementBuffer> playerSlotRequirements,
        DynamicBuffer<InventoryBuffer> inventories,
        DynamicBuffer<ContainedObjectsBuffer> containedObjects,
        PugDatabase.DatabaseBankCD databaseBank,
        ComponentLookup<ObjectCategoryTagsCD> objectCategoryTagsLookup,
        ComponentLookup<OverrideLegendaryForSlotRequirementsCD> overrideLegendaryLookup,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup,
        DynamicBuffer<InventoryChangeBuffer> inventoryChanges)
    {
        for (int inventoryIndex = 0; inventoryIndex < inventories.Length && (takeAll || remaining > 0); inventoryIndex++)
        {
            InventoryBuffer inventory = inventories[inventoryIndex];
            int endIndex = inventory.startIndex + inventory.size;

            for (int slot = inventory.startIndex; slot < endIndex && (takeAll || remaining > 0); slot++)
            {
                if (slot >= containedObjects.Length)
                {
                    continue;
                }

                ContainedObjectsBuffer objectInSlot = containedObjects[slot];
                int countContribution = StorageTerminalSummaryUtility.GetCountContribution(
                    databaseBank,
                    durabilityLookup,
                    fullnessLookup,
                    petLookup,
                    objectInSlot);
                if (objectInSlot.objectID != objectId ||
                    objectInSlot.variation != variation ||
                    countContribution <= 0)
                {
                    continue;
                }

                StorageTerminalWithdrawTransferPlanner.QueueObjectTransfer(
                    playerEntity,
                    sourceInventory,
                    slot,
                    objectInSlot,
                    countContribution,
                    ref remaining,
                    takeAll,
                    playerInventories,
                    playerMainInventory,
                    simulatedPlayerContents,
                    hasPlayerSlotRequirements,
                    playerSlotRequirements,
                    databaseBank,
                    objectCategoryTagsLookup,
                    overrideLegendaryLookup,
                    inventoryChanges);
            }
        }
    }

    public static void TryQueueExactObject(
        Entity playerEntity,
        StorageTerminalWithdrawRpc request,
        ref int remaining,
        bool takeAll,
        DynamicBuffer<StorageCraftingNetworkResolvedInventory> resolvedInventories,
        DynamicBuffer<InventoryBuffer> playerInventories,
        InventoryBuffer playerMainInventory,
        ContainedObjectsBuffer[] simulatedPlayerContents,
        bool hasPlayerSlotRequirements,
        DynamicBuffer<InventorySlotRequirementBuffer> playerSlotRequirements,
        BufferLookup<InventoryBuffer> inventoryLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup,
        PugDatabase.DatabaseBankCD databaseBank,
        ComponentLookup<ObjectCategoryTagsCD> objectCategoryTagsLookup,
        ComponentLookup<OverrideLegendaryForSlotRequirementsCD> overrideLegendaryLookup,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup,
        DynamicBuffer<InventoryChangeBuffer> inventoryChanges)
    {
        for (int i = 0; i < resolvedInventories.Length && (takeAll || remaining > 0); i++)
        {
            Entity sourceInventory = resolvedInventories[i].inventoryEntity;
            if (!inventoryLookup.HasBuffer(sourceInventory) || !containedLookup.HasBuffer(sourceInventory))
            {
                continue;
            }

            DynamicBuffer<InventoryBuffer> inventories = inventoryLookup[sourceInventory];
            DynamicBuffer<ContainedObjectsBuffer> containedObjects = containedLookup[sourceInventory];
            for (int inventoryIndex = 0; inventoryIndex < inventories.Length && (takeAll || remaining > 0); inventoryIndex++)
            {
                InventoryBuffer inventory = inventories[inventoryIndex];
                int endIndex = inventory.startIndex + inventory.size;
                for (int slot = inventory.startIndex; slot < endIndex && (takeAll || remaining > 0); slot++)
                {
                    if (slot >= containedObjects.Length)
                    {
                        continue;
                    }

                    ContainedObjectsBuffer objectInSlot = containedObjects[slot];
                    if (!MatchesExactRequest(
                            request,
                            sourceInventory,
                            slot,
                            objectInSlot,
                            databaseBank,
                            durabilityLookup,
                            fullnessLookup,
                            petLookup))
                    {
                        continue;
                    }

                    int countContribution = StorageTerminalSummaryUtility.GetCountContribution(
                        databaseBank,
                        durabilityLookup,
                        fullnessLookup,
                        petLookup,
                        objectInSlot);
                    if (countContribution <= 0)
                    {
                        return;
                    }

                    StorageTerminalWithdrawTransferPlanner.QueueObjectTransfer(
                        playerEntity,
                        sourceInventory,
                        slot,
                        objectInSlot,
                        countContribution,
                        ref remaining,
                        takeAll,
                        playerInventories,
                        playerMainInventory,
                        simulatedPlayerContents,
                        hasPlayerSlotRequirements,
                        playerSlotRequirements,
                        databaseBank,
                        objectCategoryTagsLookup,
                        overrideLegendaryLookup,
                        inventoryChanges);
                    return;
                }
            }
        }
    }

    private static bool MatchesExactRequest(
        StorageTerminalWithdrawRpc request,
        Entity sourceInventory,
        int slot,
        ContainedObjectsBuffer objectInSlot,
        PugDatabase.DatabaseBankCD databaseBank,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup)
    {
        if (objectInSlot.objectID != request.objectId ||
            objectInSlot.variation != request.variation ||
            objectInSlot.auxDataIndex != request.auxDataIndex)
        {
            return false;
        }

        ulong currentEntryId = StorageTerminalSummaryUtility.BuildExactEntryId(sourceInventory, slot);
        if (request.entryId == currentEntryId)
        {
            return true;
        }

        return StorageTerminalSummaryUtility.ShouldCreateExactEntry(
                   databaseBank,
                   durabilityLookup,
                   fullnessLookup,
                   petLookup,
                   objectInSlot) &&
               objectInSlot.amount == request.itemAmount;
    }
}
