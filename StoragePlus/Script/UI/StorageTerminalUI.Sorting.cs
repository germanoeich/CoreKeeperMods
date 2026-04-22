using System.Collections.Generic;
using System.Globalization;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public sealed partial class StorageTerminalUI
{
    private enum SortMode
    {
        Type,
        Alphabetical,
        Armor,
        Damage,
        Level,
        Value
    }

    private static readonly SortMode[] SortModes =
    {
        SortMode.Type,
        SortMode.Alphabetical,
        SortMode.Armor,
        SortMode.Damage,
        SortMode.Level,
        SortMode.Value
    };

    private SortMode _currentSortMode = SortMode.Type;
    private bool _useReverseSorting = true;
    private int _lastSortSignature = int.MinValue;

    internal void NextSort()
    {
        int currentIndex = GetCurrentSortModeIndex();
        currentIndex++;
        if (currentIndex >= SortModes.Length)
        {
            currentIndex = 0;
        }

        _currentSortMode = SortModes[currentIndex];
        RequestSortRefresh();
    }

    internal void PrevSort()
    {
        int currentIndex = GetCurrentSortModeIndex();
        currentIndex--;
        if (currentIndex < 0)
        {
            currentIndex = SortModes.Length - 1;
        }

        _currentSortMode = SortModes[currentIndex];
        RequestSortRefresh();
    }

    internal void ToggleSortOrder()
    {
        _useReverseSorting = !_useReverseSorting;
        RequestSortRefresh();
    }

    internal bool UseReverseSorting => _useReverseSorting;

    internal List<TextAndFormatFields> GetSortButtonHoverDescription(StorageTerminalSortButtonMode mode)
    {
        List<TextAndFormatFields> lines = new();
        switch (mode)
        {
            case StorageTerminalSortButtonMode.Sorter:
                lines.Add(new TextAndFormatFields
                {
                    text = GetCurrentSorterDisplayName(),
                    dontLocalize = true,
                    color = Color.white * 0.99f,
                    paddingBeneath = 0.125f
                });
                break;

            case StorageTerminalSortButtonMode.Order:
                lines.Add(new TextAndFormatFields
                {
                    text = GetCurrentSortOrderDisplayName(),
                    dontLocalize = true,
                    color = Color.white * 0.99f,
                    paddingBeneath = 0.125f
                });
                break;
        }

        return lines;
    }

    private void EnsureSortControlsBuilt()
    {
        if (sortButton == null || sortOrderButton == null)
        {
            return;
        }

        sortButton.Initialize(this, StorageTerminalSortButtonMode.Sorter);
        sortOrderButton.Initialize(this, StorageTerminalSortButtonMode.Order);
        RefreshNavigationLinks();
    }

    private int GetCurrentSortModeIndex()
    {
        for (int i = 0; i < SortModes.Length; i++)
        {
            if (SortModes[i] == _currentSortMode)
            {
                return i;
            }
        }

        return 0;
    }

    private int GetSortSignature()
    {
        return ((int)_currentSortMode * 397) ^ (_useReverseSorting ? 1 : 0);
    }

    private void RequestSortRefresh()
    {
        _lastSortSignature = int.MinValue;
    }

    private string GetCurrentSorterDisplayName()
    {
        return _currentSortMode switch
        {
            SortMode.Type => "Type",
            SortMode.Alphabetical => "Alphabetical",
            SortMode.Armor => "Armor",
            SortMode.Damage => "Damage",
            SortMode.Level => "Level",
            SortMode.Value => "Value",
            _ => "Alphabetical"
        };
    }

    private string GetCurrentSortOrderDisplayName()
    {
        return _useReverseSorting ? "Descending" : "Ascending";
    }

    private void SortEntries(List<StorageTerminalItemEntry> entries)
    {
        entries.Sort(CompareEntries);
    }

    private int CompareEntries(StorageTerminalItemEntry left, StorageTerminalItemEntry right)
    {
        int compare = CompareByMode(_currentSortMode, left, right);
        if (compare != 0)
        {
            return compare;
        }

        if (_currentSortMode != SortMode.Alphabetical)
        {
            compare = CompareByMode(SortMode.Alphabetical, left, right);
            if (compare != 0)
            {
                return compare;
            }
        }

        compare = CompareInternalIndex(left, right);
        if (compare != 0)
        {
            return ApplySortDirection(_currentSortMode, compare);
        }

        if (left.IsExactMatch != right.IsExactMatch)
        {
            return left.IsExactMatch ? 1 : -1;
        }

        return left.EntryId.CompareTo(right.EntryId);
    }

    private int CompareByMode(SortMode mode, StorageTerminalItemEntry left, StorageTerminalItemEntry right)
    {
        int compare = mode switch
        {
            SortMode.Type => CompareType(left, right),
            SortMode.Alphabetical => CompareAlphabetical(left, right),
            SortMode.Armor => CompareNumeric(GetArmor(left), GetArmor(right)),
            SortMode.Damage => CompareNumeric(GetDamage(left), GetDamage(right)),
            SortMode.Level => CompareNumeric(GetLevel(left), GetLevel(right)),
            SortMode.Value => CompareNumeric(GetValue(left), GetValue(right)),
            _ => 0
        };

        return ApplySortDirection(mode, compare);
    }

    private static int CompareAlphabetical(StorageTerminalItemEntry left, StorageTerminalItemEntry right)
    {
        return CultureInfo.CurrentCulture.CompareInfo.Compare(
            left.DisplayName,
            right.DisplayName,
            CompareOptions.IgnoreCase | CompareOptions.StringSort);
    }

    private static int GetTypeSortValue(StorageTerminalItemEntry entry)
    {
        ObjectInfo objectInfo = PugDatabase.GetObjectInfo(entry.ObjectId, entry.Variation);
        return objectInfo != null
            ? (int)objectInfo.objectType
            : int.MaxValue;
    }

    private static int GetCategorySortValue(StorageTerminalItemEntry entry)
    {
        return StorageTerminalItemCategoryUtility.GetOrderedIndex(entry.Category);
    }

    private static int CompareType(StorageTerminalItemEntry left, StorageTerminalItemEntry right)
    {
        int compare = CompareNumeric(GetTypeSortValue(left), GetTypeSortValue(right));
        if (compare != 0)
        {
            return compare;
        }

        return CompareNumeric(GetCategorySortValue(left), GetCategorySortValue(right));
    }

    private static int CompareInternalIndex(StorageTerminalItemEntry left, StorageTerminalItemEntry right)
    {
        int leftValue = ((int)left.ObjectId * 10000) + left.Variation;
        int rightValue = ((int)right.ObjectId * 10000) + right.Variation;
        return CompareNumeric(leftValue, rightValue);
    }

    private int ApplySortDirection(SortMode mode, int compare)
    {
        bool descending = mode == SortMode.Alphabetical || mode == SortMode.Type
            ? !_useReverseSorting
            : _useReverseSorting;
        return descending ? -compare : compare;
    }

    private static int CompareNumeric(int left, int right)
    {
        return left.CompareTo(right);
    }

    private static int GetLevel(StorageTerminalItemEntry entry)
    {
        ObjectDataCD objectData = entry.ContainedObject.objectData;
        if (objectData.variation > 0)
        {
            return objectData.variation;
        }

        return PugDatabase.TryGetComponent<LevelCD>(objectData, out LevelCD levelCd)
            ? levelCd.level
            : 0;
    }

    private int GetDamage(StorageTerminalItemEntry entry)
    {
        ObjectDataCD objectData = entry.ContainedObject.objectData;
        if (PugDatabase.TryGetComponent<HasWeaponDamageCD>(objectData, out HasWeaponDamageCD hasWeaponDamageCd))
        {
            if (hasWeaponDamageCd.isMagic)
            {
                return GetDamage(objectData, DamageCategory.Magic);
            }

            if (hasWeaponDamageCd.isRange)
            {
                return GetDamage(objectData, DamageCategory.PhysicalRange);
            }

            return math.max(GetDamage(objectData, DamageCategory.PhysicalMelee), GetDamage(objectData, DamageCategory.Explosive));
        }

        return math.max(GetDamage(objectData, DamageCategory.Summon), GetDamage(objectData, DamageCategory.Explosive));
    }

    private int GetArmor(StorageTerminalItemEntry entry)
    {
        UIManager uiManager = Manager.ui;
        if (uiManager == null || uiManager.conditionsIconsTable == null)
        {
            return 0;
        }

        List<ConditionData> conditions = GetConditionsOnEquip(entry.ContainedObject.objectData);
        int armor = 0;
        for (int i = 0; i < conditions.Count; i++)
        {
            ConditionData condition = conditions[i];
            if (uiManager.conditionsIconsTable.GetConditionInfo(condition.conditionID).effect == ConditionEffect.Armor)
            {
                armor += condition.value;
            }
        }

        return armor;
    }

    private List<ConditionData> GetConditionsOnEquip(ObjectDataCD objectData)
    {
        List<ConditionData> conditions = new();
        if (!PugDatabase.HasComponent<GivesConditionsWhenEquippedBuffer>(objectData))
        {
            return conditions;
        }

        Entity levelEntity = EntityUtility.GetLevelEntity(objectData);
        DynamicBuffer<GivesConditionsWhenEquippedBuffer> buffer = levelEntity != Entity.Null &&
                                                                  world != null &&
                                                                  world.IsCreated &&
                                                                  EntityUtility.TryGetBuffer(levelEntity, world, out DynamicBuffer<GivesConditionsWhenEquippedBuffer> levelBuffer)
            ? levelBuffer
            : PugDatabase.GetBuffer<GivesConditionsWhenEquippedBuffer>(objectData);

        for (int i = 0; i < buffer.Length; i++)
        {
            EquipmentCondition equipmentCondition = buffer[i].equipmentCondition;
            if (equipmentCondition.id == ConditionID.None)
            {
                continue;
            }

            conditions.Add(new ConditionData
            {
                conditionID = equipmentCondition.id,
                value = equipmentCondition.value
            });
        }

        return conditions;
    }

    private enum DamageCategory
    {
        PhysicalMelee,
        PhysicalRange,
        Magic,
        Summon,
        Explosive
    }

    private int GetDamage(ObjectDataCD objectData, DamageCategory category)
    {
        switch (category)
        {
            case DamageCategory.PhysicalMelee:
            case DamageCategory.PhysicalRange:
            case DamageCategory.Magic:
                if (!PugDatabase.TryGetComponent<HasWeaponDamageCD>(objectData, out HasWeaponDamageCD hasWeaponDamageCd))
                {
                    return 0;
                }

                if (category == DamageCategory.PhysicalMelee && (hasWeaponDamageCd.isRange || hasWeaponDamageCd.isMagic))
                {
                    return 0;
                }

                if (category == DamageCategory.PhysicalRange && (!hasWeaponDamageCd.isRange || hasWeaponDamageCd.isMagic))
                {
                    return 0;
                }

                if (category == DamageCategory.Magic && !hasWeaponDamageCd.isMagic)
                {
                    return 0;
                }

                return GetLevelEntityDamage(objectData);

            case DamageCategory.Summon:
                if (!PugDatabase.TryGetComponent<SecondaryUseCD>(objectData, out SecondaryUseCD secondaryUseCd) || !secondaryUseCd.summonsMinion)
                {
                    return 0;
                }

                if (!PugDatabase.TryGetComponent<LevelCD>(objectData, out LevelCD levelCd) ||
                    !PugDatabase.TryGetComponent<MinionCD>(secondaryUseCd.minionToSpawn, out MinionCD minionCd))
                {
                    return 0;
                }

                return MinionExtensions.GetMinionBaseDamage(minionCd, levelCd.level);

            case DamageCategory.Explosive:
                return world != null && world.IsCreated &&
                       StatsUIUtility.HasExplosiveWeapon(objectData, out IsExplosiveCD isExplosiveCd, world)
                    ? isExplosiveCd.damage
                    : 0;
        }

        return 0;
    }

    private int GetLevelEntityDamage(ObjectDataCD objectData)
    {
        if (world == null || !world.IsCreated)
        {
            return 0;
        }

        Entity levelEntity = EntityUtility.GetLevelEntity(objectData);
        return levelEntity != Entity.Null &&
               EntityUtility.TryGetComponentData<WeaponDamageCD>(levelEntity, world, out WeaponDamageCD weaponDamageCd)
            ? weaponDamageCd.GetDamage(false)
            : 0;
    }

    private static int GetValue(StorageTerminalItemEntry entry)
    {
        ObjectDataCD objectData = entry.ContainedObject.objectData;
        ObjectInfo objectInfo = PugDatabase.GetObjectInfo(objectData.objectID, objectData.variation);
        if (objectData.objectID == ObjectID.None || objectInfo == null || PugDatabase.HasComponent<CantBeSoldAuthoring>(objectData) || objectInfo.rarity == Rarity.Legendary)
        {
            return 0;
        }

        int sellValue = objectInfo.sellValue;
        if (sellValue < 0)
        {
            sellValue = GetRaritySellValue(objectInfo.rarity);

            if (PugDatabase.HasComponent<CookedFoodAuthoring>(objectData))
            {
                ObjectID primaryIngredient = CookedFoodCD.GetPrimaryIngredientFromVariation(objectData.variation);
                ObjectID secondaryIngredient = CookedFoodCD.GetSecondaryIngredientFromVariation(objectData.variation);
                sellValue = GetValue(primaryIngredient, 0) + GetValue(secondaryIngredient, 0);
            }
            else
            {
                int extraSellFromIngredients = 0;
                List<CraftingObject> requiredObjectsToCraft = objectInfo.requiredObjectsToCraft;
                for (int i = 0; i < requiredObjectsToCraft.Count; i++)
                {
                    CraftingObject craftingObject = requiredObjectsToCraft[i];
                    ObjectInfo ingredientObjectInfo = PugDatabase.GetObjectInfo(craftingObject.objectID);
                    if (ingredientObjectInfo != null && ingredientObjectInfo.sellValue != 0)
                    {
                        extraSellFromIngredients += GetRaritySellValue(ingredientObjectInfo.rarity) * craftingObject.amount;
                    }
                }

                if (extraSellFromIngredients > 0)
                {
                    sellValue = (int)math.round(math.max(1f, sellValue * 0.3f) + extraSellFromIngredients);
                }
            }

            float randomization = Unity.Mathematics.Random.CreateFromIndex((uint)objectData.objectID).NextFloat(-0.1f, 0.1f);
            sellValue = math.max(1, sellValue + (int)math.round(sellValue * randomization));
        }

        return sellValue;
    }

    private static int GetValue(ObjectID objectId, int variation)
    {
        return GetValue(new StorageTerminalItemEntry(
            new ContainedObjectsBuffer
            {
                objectData = new ObjectDataCD
                {
                    objectID = objectId,
                    variation = variation
                }
            },
            0,
            0,
            StorageTerminalSummaryEntryFlags.None,
            objectId.ToString()));
    }

    private static int GetRaritySellValue(Rarity rarity)
    {
        return 1 + math.max(0, (int)rarity) * 5;
    }
}
