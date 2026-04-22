using Inventory;
using Unity.Entities;
using Unity.Mathematics;

internal static class StorageTerminalWithdrawTransferPlanner
{
    private const int MaxStackAmount = 9999;

    public static void QueueObjectTransfer(
        Entity playerEntity,
        Entity sourceInventory,
        int sourceSlot,
        ContainedObjectsBuffer objectInSlot,
        int sourceRemaining,
        ref int remaining,
        bool takeAll,
        DynamicBuffer<InventoryBuffer> playerInventories,
        InventoryBuffer playerMainInventory,
        ContainedObjectsBuffer[] simulatedPlayerContents,
        bool hasPlayerSlotRequirements,
        DynamicBuffer<InventorySlotRequirementBuffer> playerSlotRequirements,
        PugDatabase.DatabaseBankCD databaseBank,
        ComponentLookup<ObjectCategoryTagsCD> objectCategoryTagsLookup,
        ComponentLookup<OverrideLegendaryForSlotRequirementsCD> overrideLegendaryLookup,
        DynamicBuffer<InventoryChangeBuffer> inventoryChanges)
    {
        bool isStackable = PugDatabase.GetEntityObjectInfo(objectInSlot.objectID, databaseBank.databaseBankBlob, objectInSlot.variation).isStackable;

        while (sourceRemaining > 0 && (takeAll || remaining > 0))
        {
            if (!TryFindDestinationSlot(
                    objectInSlot.objectData,
                    playerInventories,
                    playerMainInventory,
                    simulatedPlayerContents,
                    hasPlayerSlotRequirements,
                    playerSlotRequirements,
                    databaseBank,
                    objectCategoryTagsLookup,
                    overrideLegendaryLookup,
                    out int destinationSlot,
                    out int capacity))
            {
                break;
            }

            int requestedAmount = takeAll ? sourceRemaining : math.min(sourceRemaining, remaining);
            int amountToMove = isStackable ? math.min(requestedAmount, capacity) : 1;
            if (amountToMove <= 0)
            {
                break;
            }

            inventoryChanges.Add(new InventoryChangeBuffer
            {
                playerEntity = playerEntity,
                inventoryChangeData = Create.MoveAmount(
                    sourceInventory,
                    sourceSlot,
                    playerEntity,
                    destinationSlot,
                    destinationSlot + 1,
                    amountToMove,
                    destroyExisting: false)
            });

            ReserveDestinationSlot(simulatedPlayerContents, destinationSlot, objectInSlot, amountToMove, isStackable);
            sourceRemaining -= amountToMove;

            if (!takeAll)
            {
                remaining -= amountToMove;
            }

            if (!isStackable)
            {
                break;
            }
        }
    }

    private static bool TryFindDestinationSlot(
        ObjectDataCD objectData,
        DynamicBuffer<InventoryBuffer> playerInventories,
        InventoryBuffer playerMainInventory,
        ContainedObjectsBuffer[] simulatedPlayerContents,
        bool hasPlayerSlotRequirements,
        DynamicBuffer<InventorySlotRequirementBuffer> playerSlotRequirements,
        PugDatabase.DatabaseBankCD databaseBank,
        ComponentLookup<ObjectCategoryTagsCD> objectCategoryTagsLookup,
        ComponentLookup<OverrideLegendaryForSlotRequirementsCD> overrideLegendaryLookup,
        out int destinationSlot,
        out int capacity)
    {
        destinationSlot = -1;
        capacity = 0;

        Entity primaryPrefabEntity = PugDatabase.GetPrimaryPrefabEntity(objectData.objectID, databaseBank.databaseBankBlob, objectData.variation);
        ObjectCategoryTagsCD objectTagCD = objectCategoryTagsLookup.HasComponent(primaryPrefabEntity)
            ? objectCategoryTagsLookup[primaryPrefabEntity]
            : default;
        bool isStackable = PugDatabase.GetEntityObjectInfo(objectData.objectID, databaseBank.databaseBankBlob).isStackable;

        int emptySlot = -1;
        int startIndex = playerMainInventory.startIndex;
        int endIndex = playerMainInventory.startIndex + playerMainInventory.size;
        for (int slot = startIndex; slot < endIndex; slot++)
        {
            if (slot >= simulatedPlayerContents.Length)
            {
                continue;
            }

            if (hasPlayerSlotRequirements &&
                !InventoryUtility.ObjectIsValidToPutInInventory(
                    playerSlotRequirements,
                    objectTagCD,
                    objectData.objectID,
                    playerInventories,
                    overrideLegendaryLookup,
                    out _,
                    databaseBank,
                    slot))
            {
                continue;
            }

            bool canStackInSlot = isStackable && !InventoryUtility.CheckIfCanOnlyContainOneItemPerSlot(playerInventories, slot);
            ContainedObjectsBuffer destinationObject = simulatedPlayerContents[slot];
            if (canStackInSlot &&
                destinationObject.objectID == objectData.objectID &&
                destinationObject.variation == objectData.variation &&
                destinationObject.amount < MaxStackAmount)
            {
                destinationSlot = slot;
                capacity = MaxStackAmount - destinationObject.amount;
                return capacity > 0;
            }

            if (emptySlot == -1 && destinationObject.objectID == ObjectID.None)
            {
                emptySlot = slot;
            }
        }

        if (emptySlot == -1)
        {
            return false;
        }

        destinationSlot = emptySlot;
        capacity = isStackable && !InventoryUtility.CheckIfCanOnlyContainOneItemPerSlot(playerInventories, emptySlot) ? MaxStackAmount : 1;
        return true;
    }

    private static void ReserveDestinationSlot(
        ContainedObjectsBuffer[] simulatedPlayerContents,
        int destinationSlot,
        ContainedObjectsBuffer sourceObject,
        int amountToMove,
        bool isStackable)
    {
        ContainedObjectsBuffer destinationObject = simulatedPlayerContents[destinationSlot];
        if (destinationObject.objectID == ObjectID.None)
        {
            destinationObject = sourceObject;
            if (isStackable)
            {
                destinationObject.objectData.amount = amountToMove;
            }
        }
        else if (isStackable)
        {
            destinationObject.objectData.amount += amountToMove;
        }

        simulatedPlayerContents[destinationSlot] = destinationObject;
    }
}
