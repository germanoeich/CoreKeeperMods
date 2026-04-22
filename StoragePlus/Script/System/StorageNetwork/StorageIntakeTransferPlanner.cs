using System.Collections.Generic;
using Inventory;
using Unity.Entities;

internal readonly struct StorageIntakeTransferOperation
{
    public readonly Entity SourceInventory;
    public readonly int SourceSlot;
    public readonly Entity DestinationInventory;
    public readonly int DestinationSlot;

    public StorageIntakeTransferOperation(Entity sourceInventory, int sourceSlot, Entity destinationInventory, int destinationSlot)
    {
        SourceInventory = sourceInventory;
        SourceSlot = sourceSlot;
        DestinationInventory = destinationInventory;
        DestinationSlot = destinationSlot;
    }
}

internal static class StorageIntakeTransferPlanner
{
    private const int MaxStackAmount = 9999;

    public static bool TryPlanNextTransfer(
        Entity outputInventory,
        List<Entity> orderedInputInventories,
        PugDatabase.DatabaseBankCD databaseBank,
        BufferLookup<InventoryBuffer> inventoryLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup,
        out StorageIntakeTransferOperation transfer)
    {
        transfer = default;

        if (!containedLookup.HasBuffer(outputInventory) || !inventoryLookup.HasBuffer(outputInventory))
        {
            return false;
        }

        DynamicBuffer<ContainedObjectsBuffer> outputContents = containedLookup[outputInventory];
        DynamicBuffer<InventoryBuffer> outputSlots = inventoryLookup[outputInventory];
        return TryFindTransfer(
            outputInventory,
            outputContents,
            outputSlots,
            orderedInputInventories,
            databaseBank,
            inventoryLookup,
            containedLookup,
            durabilityLookup,
            fullnessLookup,
            petLookup,
            out transfer);
    }

    private static bool TryFindTransfer(
        Entity outputInventory,
        DynamicBuffer<ContainedObjectsBuffer> containedObjects,
        DynamicBuffer<InventoryBuffer> inventorySlots,
        List<Entity> orderedInputInventories,
        PugDatabase.DatabaseBankCD databaseBank,
        BufferLookup<InventoryBuffer> inventoryLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup,
        out StorageIntakeTransferOperation transfer)
    {
        transfer = default;

        for (int i = 0; i < inventorySlots.Length; i++)
        {
            InventoryBuffer inventory = inventorySlots[i];
            int endIndex = inventory.startIndex + inventory.size;

            for (int slot = inventory.startIndex; slot < endIndex; slot++)
            {
                if (slot >= containedObjects.Length)
                {
                    continue;
                }

                ContainedObjectsBuffer objectInSlot = containedObjects[slot];
                if (objectInSlot.objectID == ObjectID.None ||
                    StorageTerminalSummaryUtility.GetCountContribution(
                        databaseBank,
                        durabilityLookup,
                        fullnessLookup,
                        petLookup,
                        objectInSlot) <= 0)
                {
                    continue;
                }

                bool sourceCanStack = PugDatabase.GetEntityObjectInfo(objectInSlot.objectID, databaseBank.databaseBankBlob, objectInSlot.variation).isStackable &&
                                      !StorageTerminalSummaryUtility.ShouldCreateExactEntry(
                                          databaseBank,
                                          durabilityLookup,
                                          fullnessLookup,
                                          petLookup,
                                          objectInSlot);
                if (!TryFindDestinationSlot(
                        orderedInputInventories,
                        objectInSlot,
                        sourceCanStack,
                        inventoryLookup,
                        containedLookup,
                        out Entity destinationInventory,
                        out int destinationSlot))
                {
                    continue;
                }

                transfer = new StorageIntakeTransferOperation(outputInventory, slot, destinationInventory, destinationSlot);
                return true;
            }
        }

        transfer = default;
        return false;
    }

    private static bool TryFindDestinationSlot(
        List<Entity> storageInventories,
        ContainedObjectsBuffer sourceObject,
        bool sourceIsStackable,
        BufferLookup<InventoryBuffer> inventoryLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup,
        out Entity destinationInventory,
        out int destinationSlot)
    {
        destinationInventory = Entity.Null;
        destinationSlot = -1;

        if (sourceObject.objectID == ObjectID.None)
        {
            return false;
        }

        Entity emptyInventory = Entity.Null;
        int emptySlot = -1;

        for (int i = 0; i < storageInventories.Count; i++)
        {
            Entity inventoryEntity = storageInventories[i];
            if (!inventoryLookup.HasBuffer(inventoryEntity) || !containedLookup.HasBuffer(inventoryEntity))
            {
                continue;
            }

            DynamicBuffer<InventoryBuffer> inventorySlots = inventoryLookup[inventoryEntity];
            DynamicBuffer<ContainedObjectsBuffer> containedObjects = containedLookup[inventoryEntity];

            if (TryFindStackSlot(sourceObject, sourceIsStackable, inventorySlots, containedObjects, out int stackSlot))
            {
                destinationInventory = inventoryEntity;
                destinationSlot = stackSlot;
                return true;
            }

            if (emptyInventory == Entity.Null && TryFindEmptySlot(inventorySlots, containedObjects, out int candidateEmptySlot))
            {
                emptyInventory = inventoryEntity;
                emptySlot = candidateEmptySlot;
            }
        }

        if (emptyInventory == Entity.Null)
        {
            return false;
        }

        destinationInventory = emptyInventory;
        destinationSlot = emptySlot;
        return true;
    }

    private static bool TryFindStackSlot(
        ContainedObjectsBuffer sourceObject,
        bool sourceIsStackable,
        DynamicBuffer<InventoryBuffer> inventorySlots,
        DynamicBuffer<ContainedObjectsBuffer> containedObjects,
        out int slotIndex)
    {
        if (!sourceIsStackable)
        {
            slotIndex = -1;
            return false;
        }

        for (int i = 0; i < inventorySlots.Length; i++)
        {
            InventoryBuffer inventory = inventorySlots[i];
            if (inventory.cantAddObjectsToInventory || inventory.canOnlyContainOneItemPerSlot)
            {
                continue;
            }

            int endIndex = inventory.startIndex + inventory.size;
            for (int slot = inventory.startIndex; slot < endIndex; slot++)
            {
                if (slot >= containedObjects.Length)
                {
                    continue;
                }

                ContainedObjectsBuffer objectInSlot = containedObjects[slot];
                if (objectInSlot.objectID != sourceObject.objectID ||
                    objectInSlot.variation != sourceObject.variation ||
                    objectInSlot.amount >= MaxStackAmount)
                {
                    continue;
                }

                slotIndex = slot;
                return true;
            }
        }

        slotIndex = -1;
        return false;
    }

    private static bool TryFindEmptySlot(
        DynamicBuffer<InventoryBuffer> inventorySlots,
        DynamicBuffer<ContainedObjectsBuffer> containedObjects,
        out int slotIndex)
    {
        for (int i = 0; i < inventorySlots.Length; i++)
        {
            InventoryBuffer inventory = inventorySlots[i];
            if (inventory.cantAddObjectsToInventory)
            {
                continue;
            }

            int endIndex = inventory.startIndex + inventory.size;
            for (int slot = inventory.startIndex; slot < endIndex; slot++)
            {
                if (slot >= containedObjects.Length)
                {
                    continue;
                }

                if (containedObjects[slot].objectID != ObjectID.None)
                {
                    continue;
                }

                slotIndex = slot;
                return true;
            }
        }

        slotIndex = -1;
        return false;
    }
}
