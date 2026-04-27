using System;
using Unity.Entities;

public struct StorageCraftingNetworkSummaryState : IComponentData
{
    public ulong networkHash;

    public ulong contentsHash;

    public int entryCount;

    public int usedSlotCount;

    public int totalSlotCount;
}

[Flags]
public enum StorageTerminalSummaryEntryFlags : byte
{
    None = 0,
    ExactMatch = 1 << 0,
    VirtualStack = 1 << 1
}

[InternalBufferCapacity(0)]
public struct StorageCraftingNetworkSummaryEntry : IBufferElementData
{
    public ObjectID objectId;

    public int totalAmount;

    public int itemAmount;

    public int variation;

    public int auxDataIndex;

    public ulong entryId;

    public byte flags;

    public readonly bool IsExactMatch => (flags & (byte)StorageTerminalSummaryEntryFlags.ExactMatch) != 0;
}

[InternalBufferCapacity(0)]
public struct StorageCraftingNetworkResolvedInventory : IBufferElementData
{
    public Entity inventoryEntity;
}

public static class StorageTerminalSummaryUtility
{
    public const int MaxStackAmount = global::Constants.inventoryMaxAmountPerSlot;

    public static bool IsStackable(
        PugDatabase.DatabaseBankCD databaseBank,
        ObjectID objectId,
        int variation)
    {
        if (objectId == ObjectID.None)
        {
            return false;
        }

        return PugDatabase.GetEntityObjectInfo(objectId, databaseBank.databaseBankBlob, variation).isStackable;
    }

    public static bool IsStackable(
        PugDatabase.DatabaseBankCD databaseBank,
        in ContainedObjectsBuffer containedObject)
    {
        return IsStackable(databaseBank, containedObject.objectID, containedObject.variation);
    }

    public static bool UsesAmountAsState(
        PugDatabase.DatabaseBankCD databaseBank,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup,
        ObjectID objectId,
        int variation)
    {
        return PugDatabase.AmountIsDurabilityOrFullnessOrXp(databaseBank, durabilityLookup, fullnessLookup, petLookup, objectId, variation);
    }

    public static bool UsesAmountAsState(
        PugDatabase.DatabaseBankCD databaseBank,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup,
        in ContainedObjectsBuffer containedObject)
    {
        return UsesAmountAsState(databaseBank, durabilityLookup, fullnessLookup, petLookup, containedObject.objectID, containedObject.variation);
    }

    public static int GetCountContribution(
        PugDatabase.DatabaseBankCD databaseBank,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup,
        in ContainedObjectsBuffer containedObject)
    {
        if (containedObject.objectID == ObjectID.None)
        {
            return 0;
        }

        if (UsesAmountAsState(databaseBank, durabilityLookup, fullnessLookup, petLookup, containedObject) ||
            !IsStackable(databaseBank, containedObject))
        {
            return 1;
        }

        return containedObject.amount > 0 ? containedObject.amount : 0;
    }

    public static bool ShouldCreateExactEntry(
        PugDatabase.DatabaseBankCD databaseBank,
        ComponentLookup<DurabilityCD> durabilityLookup,
        ComponentLookup<FullnessCD> fullnessLookup,
        ComponentLookup<PetCD> petLookup,
        in ContainedObjectsBuffer containedObject)
    {
        return containedObject.auxDataIndex != 0 ||
               UsesAmountAsState(databaseBank, durabilityLookup, fullnessLookup, petLookup, containedObject) ||
               !IsStackable(databaseBank, containedObject);
    }

    public static ulong BuildExactEntryId(Entity inventoryEntity, int slotIndex)
    {
        unchecked
        {
            ulong hash = 1469598103934665603UL;
            hash = (hash ^ (uint)inventoryEntity.Index) * 1099511628211UL;
            hash = (hash ^ (uint)inventoryEntity.Version) * 1099511628211UL;
            hash = (hash ^ (uint)slotIndex) * 1099511628211UL;
            return hash;
        }
    }

    public static ulong BuildVirtualStackEntryId(ObjectID objectId, int variation, int stackIndex)
    {
        unchecked
        {
            ulong hash = 1469598103934665603UL;
            hash = (hash ^ (uint)objectId) * 1099511628211UL;
            hash = (hash ^ (uint)variation) * 1099511628211UL;
            hash = (hash ^ (uint)stackIndex) * 1099511628211UL;
            hash = (hash ^ (uint)StorageTerminalSummaryEntryFlags.VirtualStack) * 1099511628211UL;
            return hash;
        }
    }
}
