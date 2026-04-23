using System.Collections.Generic;
using Inventory;
using Unity.Entities;
using Unity.Mathematics;

internal sealed class StorageTerminalNetworkDepositSimulation
{
    private readonly Dictionary<Entity, ContainedObjectsBuffer[]> _contentsByInventory = new();

    public ContainedObjectsBuffer[] GetContents(Entity inventoryEntity, BufferLookup<ContainedObjectsBuffer> containedLookup)
    {
        if (_contentsByInventory.TryGetValue(inventoryEntity, out ContainedObjectsBuffer[] contents))
        {
            return contents;
        }

        if (!containedLookup.HasBuffer(inventoryEntity))
        {
            return null;
        }

        DynamicBuffer<ContainedObjectsBuffer> buffer = containedLookup[inventoryEntity];
        contents = new ContainedObjectsBuffer[buffer.Length];
        for (int i = 0; i < buffer.Length; i++)
        {
            contents[i] = buffer[i];
        }

        _contentsByInventory.Add(inventoryEntity, contents);
        return contents;
    }
}

internal static class StorageTerminalNetworkDepositPlanner
{
    private const int MaxStackAmount = 9999;

    public static bool QueueDepositFromPlayerSlot(
        Entity playerEntity,
        int sourceSlot,
        int requestedAmount,
        bool requireExistingMatch,
        IList<Entity> destinationInventories,
        StorageTerminalNetworkDepositSimulation simulation,
        BufferLookup<InventoryBuffer> inventoryLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup,
        BufferLookup<InventorySlotRequirementBuffer> inventorySlotRequirementLookup,
        PugDatabase.DatabaseBankCD databaseBank,
        ComponentLookup<ObjectCategoryTagsCD> objectCategoryTagsLookup,
        ComponentLookup<OverrideLegendaryForSlotRequirementsCD> overrideLegendaryLookup,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup,
        DynamicBuffer<InventoryChangeBuffer> inventoryChanges)
    {
        InventoryHandlerShared inventoryHandlerShared = default;
        return DepositFromPlayerSlot(
            playerEntity,
            sourceSlot,
            requestedAmount,
            requireExistingMatch,
            destinationInventories,
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
            inventoryChanges,
            useDropAnimation: false,
            pickupTarget: Entity.Null,
            in inventoryHandlerShared,
            default);
    }

    public static bool DropDepositFromPlayerSlot(
        Entity playerEntity,
        int sourceSlot,
        int requestedAmount,
        bool requireExistingMatch,
        Entity pickupTarget,
        IList<Entity> destinationInventories,
        StorageTerminalNetworkDepositSimulation simulation,
        BufferLookup<InventoryBuffer> inventoryLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup,
        BufferLookup<InventorySlotRequirementBuffer> inventorySlotRequirementLookup,
        PugDatabase.DatabaseBankCD databaseBank,
        ComponentLookup<ObjectCategoryTagsCD> objectCategoryTagsLookup,
        ComponentLookup<OverrideLegendaryForSlotRequirementsCD> overrideLegendaryLookup,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup,
        DynamicBuffer<InventoryChangeBuffer> inventoryChanges,
        in InventoryHandlerShared inventoryHandlerShared,
        float3 sourcePosition)
    {
        return DepositFromPlayerSlot(
            playerEntity,
            sourceSlot,
            requestedAmount,
            requireExistingMatch,
            destinationInventories,
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
            inventoryChanges,
            useDropAnimation: true,
            pickupTarget: pickupTarget,
            in inventoryHandlerShared,
            sourcePosition);
    }

    public static void MoveSourceSlotToDestinationsOrDrop(
        Entity sourceInventory,
        int sourceSlot,
        IList<Entity> destinationInventories,
        StorageTerminalNetworkDepositSimulation simulation,
        BufferLookup<InventoryBuffer> inventoryLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup,
        BufferLookup<InventorySlotRequirementBuffer> inventorySlotRequirementLookup,
        PugDatabase.DatabaseBankCD databaseBank,
        ComponentLookup<ObjectCategoryTagsCD> objectCategoryTagsLookup,
        ComponentLookup<OverrideLegendaryForSlotRequirementsCD> overrideLegendaryLookup,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup,
        in InventoryHandlerShared inventoryHandlerShared,
        float3 dropPosition)
    {
        if (destinationInventories == null || destinationInventories.Count == 0)
        {
            DropSourceSlot(in inventoryHandlerShared, sourceInventory, sourceSlot, dropPosition);
            return;
        }

        if (!inventoryLookup.HasBuffer(sourceInventory) || !SlotBelongsToInventory(inventoryLookup[sourceInventory], sourceSlot))
        {
            return;
        }

        ContainedObjectsBuffer[] simulatedSourceContents = simulation.GetContents(sourceInventory, containedLookup);
        if (simulatedSourceContents == null || sourceSlot < 0 || sourceSlot >= simulatedSourceContents.Length)
        {
            return;
        }

        while (sourceSlot < simulatedSourceContents.Length)
        {
            ContainedObjectsBuffer sourceObject = simulatedSourceContents[sourceSlot];
            bool usesExactState = StorageTerminalSummaryUtility.ShouldCreateExactEntry(
                databaseBank,
                durabilityLookup,
                fullnessLookup,
                petLookup,
                sourceObject);
            int sourceItemCount = StorageTerminalSummaryUtility.GetCountContribution(
                databaseBank,
                durabilityLookup,
                fullnessLookup,
                petLookup,
                sourceObject);
            if (sourceObject.objectID == ObjectID.None || sourceItemCount <= 0)
            {
                return;
            }

            bool isStackable = PugDatabase.GetEntityObjectInfo(sourceObject.objectID, databaseBank.databaseBankBlob, sourceObject.variation).isStackable;
            bool canStackIntoExistingSlots = isStackable && !usesExactState;
            ObjectDataCD sourceObjectData = sourceObject.objectData;
            Entity primaryPrefabEntity = PugDatabase.GetPrimaryPrefabEntity(sourceObjectData.objectID, databaseBank.databaseBankBlob, sourceObjectData.variation);
            ObjectCategoryTagsCD objectTagCD = objectCategoryTagsLookup.HasComponent(primaryPrefabEntity)
                ? objectCategoryTagsLookup[primaryPrefabEntity]
                : default;

            if (!TryFindDestinationSlot(
                    sourceObjectData,
                    canStackIntoExistingSlots,
                    destinationInventories,
                    simulation,
                    inventoryLookup,
                    containedLookup,
                    inventorySlotRequirementLookup,
                    databaseBank,
                    objectTagCD,
                    overrideLegendaryLookup,
                    out Entity destinationInventory,
                    out int destinationSlot,
                    out int capacity))
            {
                DropSourceSlot(in inventoryHandlerShared, sourceInventory, sourceSlot, dropPosition);
                return;
            }

            int moveAmount = canStackIntoExistingSlots
                ? math.min(sourceObject.amount, capacity)
                : sourceItemCount;
            if (moveAmount <= 0 ||
                !InventoryUtility.TryMove(
                    in inventoryHandlerShared,
                    sourceInventory,
                    sourceSlot,
                    destinationInventory,
                    destinationSlot,
                    destinationSlot + 1,
                    moveAmount,
                    destroyExisting: false))
            {
                DropSourceSlot(in inventoryHandlerShared, sourceInventory, sourceSlot, dropPosition);
                return;
            }

            ReserveDestinationSlot(
                simulation.GetContents(destinationInventory, containedLookup),
                destinationSlot,
                sourceObject,
                moveAmount,
                canStackIntoExistingSlots);
            ReserveSourceSlot(simulatedSourceContents, sourceSlot, moveAmount, canStackIntoExistingSlots);
        }
    }

    private static bool DepositFromPlayerSlot(
        Entity playerEntity,
        int sourceSlot,
        int requestedAmount,
        bool requireExistingMatch,
        IList<Entity> destinationInventories,
        StorageTerminalNetworkDepositSimulation simulation,
        BufferLookup<InventoryBuffer> inventoryLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup,
        BufferLookup<InventorySlotRequirementBuffer> inventorySlotRequirementLookup,
        PugDatabase.DatabaseBankCD databaseBank,
        ComponentLookup<ObjectCategoryTagsCD> objectCategoryTagsLookup,
        ComponentLookup<OverrideLegendaryForSlotRequirementsCD> overrideLegendaryLookup,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup,
        DynamicBuffer<InventoryChangeBuffer> inventoryChanges,
        bool useDropAnimation,
        Entity pickupTarget,
        in InventoryHandlerShared inventoryHandlerShared,
        float3 sourcePosition)
    {
        if (destinationInventories == null || destinationInventories.Count == 0)
        {
            return false;
        }

        if (!inventoryLookup.HasBuffer(playerEntity) || !SlotBelongsToInventory(inventoryLookup[playerEntity], sourceSlot))
        {
            return false;
        }

        ContainedObjectsBuffer[] simulatedPlayerContents = simulation.GetContents(playerEntity, containedLookup);
        if (simulatedPlayerContents == null || sourceSlot < 0 || sourceSlot >= simulatedPlayerContents.Length)
        {
            return false;
        }

        ContainedObjectsBuffer sourceObject = simulatedPlayerContents[sourceSlot];
        bool usesExactState = StorageTerminalSummaryUtility.ShouldCreateExactEntry(
            databaseBank,
            durabilityLookup,
            fullnessLookup,
            petLookup,
            sourceObject);
        int sourceItemCount = StorageTerminalSummaryUtility.GetCountContribution(
            databaseBank,
            durabilityLookup,
            fullnessLookup,
            petLookup,
            sourceObject);
        if (sourceObject.objectID == ObjectID.None || sourceItemCount <= 0)
        {
            return false;
        }

        bool isStackable = PugDatabase.GetEntityObjectInfo(sourceObject.objectID, databaseBank.databaseBankBlob, sourceObject.variation).isStackable;
        bool canStackIntoExistingSlots = isStackable && !usesExactState;

        if (requireExistingMatch &&
            !NetworkHasExistingMatch(destinationInventories, sourceObject.objectID, sourceObject.variation, simulation, inventoryLookup, containedLookup))
        {
            return false;
        }

        int remainingToMove = GetRequestedMoveAmount(sourceObject, requestedAmount, canStackIntoExistingSlots, sourceItemCount);
        if (remainingToMove <= 0)
        {
            return false;
        }

        ObjectDataCD sourceObjectData = sourceObject.objectData;
        Entity primaryPrefabEntity = PugDatabase.GetPrimaryPrefabEntity(sourceObjectData.objectID, databaseBank.databaseBankBlob, sourceObjectData.variation);
        ObjectCategoryTagsCD objectTagCD = objectCategoryTagsLookup.HasComponent(primaryPrefabEntity)
            ? objectCategoryTagsLookup[primaryPrefabEntity]
            : default;

        bool movedAny = false;
        while (remainingToMove > 0)
        {
            if (!TryFindDestinationSlot(
                    sourceObjectData,
                    canStackIntoExistingSlots,
                    destinationInventories,
                    simulation,
                    inventoryLookup,
                    containedLookup,
                    inventorySlotRequirementLookup,
                    databaseBank,
                    objectTagCD,
                    overrideLegendaryLookup,
                    out Entity destinationInventory,
                    out int destinationSlot,
                    out int capacity))
            {
                break;
            }

            int moveAmount = canStackIntoExistingSlots
                ? math.min(remainingToMove, capacity)
                : remainingToMove;
            if (moveAmount <= 0)
            {
                break;
            }

            if (useDropAnimation)
            {
                InventoryUtility.DropItem(
                    in inventoryHandlerShared,
                    playerEntity,
                    sourceSlot,
                    moveAmount,
                    sourcePosition,
                    default,
                    pickupTarget == Entity.Null ? destinationInventory : pickupTarget,
                    ignoreRayChecksForPickup: true);
            }
            else
            {
                inventoryChanges.Add(new InventoryChangeBuffer
                {
                    playerEntity = playerEntity,
                    inventoryChangeData = Create.MoveAmount(
                        playerEntity,
                        sourceSlot,
                        destinationInventory,
                        destinationSlot,
                        destinationSlot + 1,
                        moveAmount,
                        destroyExisting: false)
                });
            }

            ReserveDestinationSlot(
                simulation.GetContents(destinationInventory, containedLookup),
                destinationSlot,
                sourceObject,
                moveAmount,
                canStackIntoExistingSlots);
            ReserveSourceSlot(simulatedPlayerContents, sourceSlot, moveAmount, canStackIntoExistingSlots);

            movedAny = true;
            if (!canStackIntoExistingSlots)
            {
                break;
            }

            remainingToMove -= moveAmount;
            sourceObject = simulatedPlayerContents[sourceSlot];
            if (sourceObject.objectID == ObjectID.None ||
                StorageTerminalSummaryUtility.GetCountContribution(
                    databaseBank,
                    durabilityLookup,
                    fullnessLookup,
                    petLookup,
                    sourceObject) <= 0)
            {
                break;
            }
        }

        return movedAny;
    }

    private static int GetRequestedMoveAmount(
        ContainedObjectsBuffer sourceObject,
        int requestedAmount,
        bool canStackIntoExistingSlots,
        int sourceItemCount)
    {
        if (!canStackIntoExistingSlots)
        {
            return sourceItemCount;
        }

        if (requestedAmount == int.MaxValue)
        {
            return sourceObject.amount;
        }

        return math.min(sourceObject.amount, requestedAmount);
    }

    private static bool NetworkHasExistingMatch(
        IList<Entity> destinationInventories,
        ObjectID objectId,
        int variation,
        StorageTerminalNetworkDepositSimulation simulation,
        BufferLookup<InventoryBuffer> inventoryLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup)
    {
        for (int i = 0; i < destinationInventories.Count; i++)
        {
            if (!inventoryLookup.HasBuffer(destinationInventories[i]))
            {
                continue;
            }

            ContainedObjectsBuffer[] contents = simulation.GetContents(destinationInventories[i], containedLookup);
            if (contents == null)
            {
                continue;
            }

            DynamicBuffer<InventoryBuffer> inventories = inventoryLookup[destinationInventories[i]];
            for (int inventoryIndex = 0; inventoryIndex < inventories.Length; inventoryIndex++)
            {
                InventoryBuffer inventory = inventories[inventoryIndex];
                int endIndex = inventory.startIndex + inventory.size;
                for (int slot = inventory.startIndex; slot < endIndex && slot < contents.Length; slot++)
                {
                    ContainedObjectsBuffer objectInSlot = contents[slot];
                    if (objectInSlot.objectID == objectId && objectInSlot.variation == variation)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static void DropSourceSlot(
        in InventoryHandlerShared inventoryHandlerShared,
        Entity sourceInventory,
        int sourceSlot,
        float3 dropPosition)
    {
        if (!inventoryHandlerShared.containedObjectsBufferLookup.HasBuffer(sourceInventory))
        {
            return;
        }

        DynamicBuffer<ContainedObjectsBuffer> contents = inventoryHandlerShared.containedObjectsBufferLookup[sourceInventory];
        if (sourceSlot < 0 || sourceSlot >= contents.Length || contents[sourceSlot].objectID == ObjectID.None)
        {
            return;
        }

        InventoryUtility.DropItem(
            in inventoryHandlerShared,
            sourceInventory,
            sourceSlot,
            int.MaxValue,
            dropPosition);
    }

    private static bool SlotBelongsToInventory(DynamicBuffer<InventoryBuffer> inventories, int slot)
    {
        for (int i = 0; i < inventories.Length; i++)
        {
            InventoryBuffer inventory = inventories[i];
            if (slot >= inventory.startIndex && slot < inventory.startIndex + inventory.size)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindDestinationSlot(
        ObjectDataCD sourceObjectData,
        bool canStackIntoExistingSlots,
        IList<Entity> destinationInventories,
        StorageTerminalNetworkDepositSimulation simulation,
        BufferLookup<InventoryBuffer> inventoryLookup,
        BufferLookup<ContainedObjectsBuffer> containedLookup,
        BufferLookup<InventorySlotRequirementBuffer> inventorySlotRequirementLookup,
        PugDatabase.DatabaseBankCD databaseBank,
        ObjectCategoryTagsCD objectTagCD,
        ComponentLookup<OverrideLegendaryForSlotRequirementsCD> overrideLegendaryLookup,
        out Entity destinationInventory,
        out int destinationSlot,
        out int capacity)
    {
        destinationInventory = Entity.Null;
        destinationSlot = -1;
        capacity = 0;

        Entity emptyInventory = Entity.Null;
        int emptySlot = -1;
        int emptyCapacity = 0;

        for (int inventoryIndex = 0; inventoryIndex < destinationInventories.Count; inventoryIndex++)
        {
            Entity inventoryEntity = destinationInventories[inventoryIndex];
            if (!inventoryLookup.HasBuffer(inventoryEntity))
            {
                continue;
            }

            ContainedObjectsBuffer[] simulatedDestinationContents = simulation.GetContents(inventoryEntity, containedLookup);
            if (simulatedDestinationContents == null)
            {
                continue;
            }

            DynamicBuffer<InventoryBuffer> inventoryBuffers = inventoryLookup[inventoryEntity];
            bool hasSlotRequirements = inventorySlotRequirementLookup.TryGetBuffer(inventoryEntity, out DynamicBuffer<InventorySlotRequirementBuffer> slotRequirements);
            for (int bufferIndex = 0; bufferIndex < inventoryBuffers.Length; bufferIndex++)
            {
                InventoryBuffer inventory = inventoryBuffers[bufferIndex];
                if (inventory.cantAddObjectsToInventory)
                {
                    continue;
                }

                int endIndex = inventory.startIndex + inventory.size;
                for (int slot = inventory.startIndex; slot < endIndex; slot++)
                {
                    if (slot >= simulatedDestinationContents.Length)
                    {
                        continue;
                    }

                    if (hasSlotRequirements &&
                        !InventoryUtility.ObjectIsValidToPutInInventory(
                            slotRequirements,
                            objectTagCD,
                            sourceObjectData.objectID,
                            inventoryBuffers,
                            overrideLegendaryLookup,
                            out _,
                            databaseBank,
                            slot))
                    {
                        continue;
                    }

                    ContainedObjectsBuffer destinationObject = simulatedDestinationContents[slot];
                    bool canStackInSlot = canStackIntoExistingSlots &&
                                          !InventoryUtility.CheckIfCanOnlyContainOneItemPerSlot(inventoryBuffers, slot);
                    if (canStackInSlot &&
                        destinationObject.objectID == sourceObjectData.objectID &&
                        destinationObject.variation == sourceObjectData.variation &&
                        destinationObject.amount < MaxStackAmount)
                    {
                        destinationInventory = inventoryEntity;
                        destinationSlot = slot;
                        capacity = MaxStackAmount - destinationObject.amount;
                        return capacity > 0;
                    }

                    if (emptyInventory == Entity.Null && destinationObject.objectID == ObjectID.None)
                    {
                        emptyInventory = inventoryEntity;
                        emptySlot = slot;
                        emptyCapacity = canStackInSlot ? MaxStackAmount : 1;
                    }
                }
            }
        }

        if (emptyInventory == Entity.Null)
        {
            return false;
        }

        destinationInventory = emptyInventory;
        destinationSlot = emptySlot;
        capacity = emptyCapacity;
        return true;
    }

    private static void ReserveDestinationSlot(
        ContainedObjectsBuffer[] simulatedDestinationContents,
        int destinationSlot,
        ContainedObjectsBuffer sourceObject,
        int moveAmount,
        bool canStackIntoExistingSlots)
    {
        ContainedObjectsBuffer destinationObject = simulatedDestinationContents[destinationSlot];
        if (destinationObject.objectID == ObjectID.None)
        {
            destinationObject = sourceObject;
            if (canStackIntoExistingSlots)
            {
                destinationObject.objectData.amount = moveAmount;
            }
        }
        else if (canStackIntoExistingSlots)
        {
            destinationObject.objectData.amount += moveAmount;
        }

        simulatedDestinationContents[destinationSlot] = destinationObject;
    }

    private static void ReserveSourceSlot(
        ContainedObjectsBuffer[] simulatedPlayerContents,
        int sourceSlot,
        int moveAmount,
        bool canStackIntoExistingSlots)
    {
        ContainedObjectsBuffer sourceObject = simulatedPlayerContents[sourceSlot];
        if (!canStackIntoExistingSlots || sourceObject.amount <= moveAmount)
        {
            simulatedPlayerContents[sourceSlot] = default;
            return;
        }

        sourceObject.objectData.amount -= moveAmount;
        simulatedPlayerContents[sourceSlot] = sourceObject;
    }
}
