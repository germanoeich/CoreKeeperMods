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

    /// <summary>
    /// Picks a destination slot across every player inventory buffer: the main inventory
    /// (index 0) and the four pouch inventories (indices 1-4, sized by the equipped pouch).
    /// Mirrors the priority of <see cref="InventoryUtility.TryFindSlotToAddTo"/> for a
    /// chest-to-player transfer: a partial stack anywhere wins, then an empty slot whose
    /// slot requirement the object fulfils (a matching pouch), then any other empty slot.
    /// </summary>
    private static bool TryFindDestinationSlot(
        ObjectDataCD objectData,
        DynamicBuffer<InventoryBuffer> playerInventories,
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

        int firstEmptySlot = -1;
        int firstRequirementFulfillingEmptySlot = -1;
        for (int inventoryIndex = 0; inventoryIndex < playerInventories.Length; inventoryIndex++)
        {
            InventoryBuffer inventory = playerInventories[inventoryIndex];
            if (inventory.cantAddObjectsToInventory)
            {
                continue;
            }

            int startIndex = inventory.startIndex;
            int endIndex = math.min(startIndex + inventory.size, simulatedPlayerContents.Length);
            for (int slot = startIndex; slot < endIndex; slot++)
            {
                int indexFulfillingRequirements = -1;
                if (hasPlayerSlotRequirements &&
                    !InventoryUtility.ObjectIsValidToPutInInventory(
                        playerSlotRequirements,
                        objectTagCD,
                        objectData.objectID,
                        playerInventories,
                        overrideLegendaryLookup,
                        out indexFulfillingRequirements,
                        databaseBank,
                        slot))
                {
                    continue;
                }

                ContainedObjectsBuffer destinationObject = simulatedPlayerContents[slot];
                bool canStackInSlot = isStackable && !inventory.canOnlyContainOneItemPerSlot;
                if (canStackInSlot &&
                    destinationObject.objectID == objectData.objectID &&
                    destinationObject.variation == objectData.variation &&
                    destinationObject.amount < MaxStackAmount)
                {
                    destinationSlot = slot;
                    capacity = MaxStackAmount - destinationObject.amount;
                    return true;
                }

                if (destinationObject.objectID != ObjectID.None)
                {
                    continue;
                }

                if (firstRequirementFulfillingEmptySlot == -1 && indexFulfillingRequirements != -1)
                {
                    firstRequirementFulfillingEmptySlot = slot;
                }

                if (firstEmptySlot == -1)
                {
                    firstEmptySlot = slot;
                }
            }
        }

        int emptySlot = firstRequirementFulfillingEmptySlot != -1 ? firstRequirementFulfillingEmptySlot : firstEmptySlot;
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
