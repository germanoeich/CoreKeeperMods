using System.Collections.Generic;
using Inventory;
using PugTilemap;
using Unity.Entities;

internal enum StorageTerminalItemCategory
{
    All,
    Food,
    Ingredients,
    Crops,
    Fish,
    Materials,
    Blocks,
    Ground,
    Floor,
    Bridges,
    Decoration,
    Placeables,
    Weapons,
    Tools,
    Armor,
    Accessories,
    Valuables,
    Pets,
    Critters,
    Misc
}

internal static class StorageTerminalItemCategoryUtility
{
    private static BlobAssetReference<PugDatabase.PugDatabaseBank> _recipeIngredientSource;
    private static HashSet<ObjectID> _recipeIngredientObjectIds;

    private static readonly StorageTerminalItemCategory[] OrderedCategories =
    {
        StorageTerminalItemCategory.All,
        StorageTerminalItemCategory.Food,
        StorageTerminalItemCategory.Ingredients,
        StorageTerminalItemCategory.Crops,
        StorageTerminalItemCategory.Fish,
        StorageTerminalItemCategory.Materials,
        StorageTerminalItemCategory.Blocks,
        StorageTerminalItemCategory.Ground,
        StorageTerminalItemCategory.Floor,
        StorageTerminalItemCategory.Bridges,
        StorageTerminalItemCategory.Decoration,
        StorageTerminalItemCategory.Placeables,
        StorageTerminalItemCategory.Weapons,
        StorageTerminalItemCategory.Tools,
        StorageTerminalItemCategory.Armor,
        StorageTerminalItemCategory.Accessories,
        StorageTerminalItemCategory.Valuables,
        StorageTerminalItemCategory.Pets,
        StorageTerminalItemCategory.Critters,
        StorageTerminalItemCategory.Misc
    };

    public static IReadOnlyList<StorageTerminalItemCategory> GetOrderedCategories()
    {
        return OrderedCategories;
    }

    public static string GetDisplayName(StorageTerminalItemCategory category)
    {
        return category switch
        {
            StorageTerminalItemCategory.All => "All",
            StorageTerminalItemCategory.Food => "Food",
            StorageTerminalItemCategory.Ingredients => "Ingredients",
            StorageTerminalItemCategory.Crops => "Crops",
            StorageTerminalItemCategory.Fish => "Fish",
            StorageTerminalItemCategory.Materials => "Materials",
            StorageTerminalItemCategory.Blocks => "Blocks",
            StorageTerminalItemCategory.Ground => "Ground",
            StorageTerminalItemCategory.Floor => "Floor",
            StorageTerminalItemCategory.Bridges => "Bridges",
            StorageTerminalItemCategory.Decoration => "Decoration",
            StorageTerminalItemCategory.Placeables => "Placeables",
            StorageTerminalItemCategory.Weapons => "Weapons",
            StorageTerminalItemCategory.Tools => "Tools",
            StorageTerminalItemCategory.Armor => "Armor",
            StorageTerminalItemCategory.Accessories => "Accessories",
            StorageTerminalItemCategory.Valuables => "Valuables",
            StorageTerminalItemCategory.Pets => "Pets",
            StorageTerminalItemCategory.Critters => "Critters",
            _ => "Misc"
        };
    }

    public static string GetShortDisplayName(StorageTerminalItemCategory category)
    {
        return category switch
        {
            StorageTerminalItemCategory.Food => "Food",
            StorageTerminalItemCategory.Ingredients => "Ing",
            StorageTerminalItemCategory.Crops => "Crop",
            StorageTerminalItemCategory.Fish => "Fish",
            StorageTerminalItemCategory.Materials => "Mat",
            StorageTerminalItemCategory.Blocks => "Blk",
            StorageTerminalItemCategory.Ground => "Gnd",
            StorageTerminalItemCategory.Floor => "Flr",
            StorageTerminalItemCategory.Bridges => "Brg",
            StorageTerminalItemCategory.Decoration => "Dec",
            StorageTerminalItemCategory.Placeables => "Plc",
            StorageTerminalItemCategory.Weapons => "Wpn",
            StorageTerminalItemCategory.Tools => "Tool",
            StorageTerminalItemCategory.Armor => "Arm",
            StorageTerminalItemCategory.Accessories => "Acc",
            StorageTerminalItemCategory.Valuables => "Val",
            StorageTerminalItemCategory.Pets => "Pet",
            StorageTerminalItemCategory.Critters => "Crit",
            StorageTerminalItemCategory.All => "All",
            _ => "Misc"
        };
    }

    public static int GetOrderedIndex(StorageTerminalItemCategory category)
    {
        for (int i = 0; i < OrderedCategories.Length; i++)
        {
            if (OrderedCategories[i] == category)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    public static StorageTerminalItemCategory GetPrimaryCategory(ContainedObjectsBuffer containedObject)
    {
        ClassificationData data = CreateClassificationData(containedObject);
        if (Matches(StorageTerminalItemCategory.Pets, data))
        {
            return StorageTerminalItemCategory.Pets;
        }

        if (Matches(StorageTerminalItemCategory.Fish, data))
        {
            return StorageTerminalItemCategory.Fish;
        }

        if (Matches(StorageTerminalItemCategory.Critters, data))
        {
            return StorageTerminalItemCategory.Critters;
        }

        if (Matches(StorageTerminalItemCategory.Crops, data))
        {
            return StorageTerminalItemCategory.Crops;
        }

        if (Matches(StorageTerminalItemCategory.Food, data))
        {
            return StorageTerminalItemCategory.Food;
        }

        if (Matches(StorageTerminalItemCategory.Ingredients, data))
        {
            return StorageTerminalItemCategory.Ingredients;
        }

        if (Matches(StorageTerminalItemCategory.Valuables, data))
        {
            return StorageTerminalItemCategory.Valuables;
        }

        if (Matches(StorageTerminalItemCategory.Weapons, data))
        {
            return StorageTerminalItemCategory.Weapons;
        }

        if (Matches(StorageTerminalItemCategory.Tools, data))
        {
            return StorageTerminalItemCategory.Tools;
        }

        if (Matches(StorageTerminalItemCategory.Armor, data))
        {
            return StorageTerminalItemCategory.Armor;
        }

        if (Matches(StorageTerminalItemCategory.Accessories, data))
        {
            return StorageTerminalItemCategory.Accessories;
        }

        if (Matches(StorageTerminalItemCategory.Blocks, data))
        {
            return StorageTerminalItemCategory.Blocks;
        }

        if (Matches(StorageTerminalItemCategory.Ground, data))
        {
            return StorageTerminalItemCategory.Ground;
        }

        if (Matches(StorageTerminalItemCategory.Floor, data))
        {
            return StorageTerminalItemCategory.Floor;
        }

        if (Matches(StorageTerminalItemCategory.Bridges, data))
        {
            return StorageTerminalItemCategory.Bridges;
        }

        if (Matches(StorageTerminalItemCategory.Decoration, data))
        {
            return StorageTerminalItemCategory.Decoration;
        }

        if (Matches(StorageTerminalItemCategory.Materials, data))
        {
            return StorageTerminalItemCategory.Materials;
        }

        if (Matches(StorageTerminalItemCategory.Placeables, data))
        {
            return StorageTerminalItemCategory.Placeables;
        }

        return StorageTerminalItemCategory.Misc;
    }

    public static bool Matches(StorageTerminalItemCategory category, ContainedObjectsBuffer containedObject)
    {
        return Matches(category, CreateClassificationData(containedObject));
    }

    private static bool Matches(StorageTerminalItemCategory category, ClassificationData data)
    {
        if (category == StorageTerminalItemCategory.All)
        {
            return data.HasValidObject;
        }

        return category switch
        {
            StorageTerminalItemCategory.Food => data.ObjectType == ObjectType.Eatable ||
                                                data.HasCookedFood ||
                                                HasAnyTag(data.TagsMask, ObjectCategoryTag.UncommonOrLowerCookedFood, ObjectCategoryTag.RareOrHigherCookedFood),
            StorageTerminalItemCategory.Ingredients => data.HasCookingIngredient ||
                                                       HasAnyTag(data.TagsMask, ObjectCategoryTag.CookingIngredient),
            StorageTerminalItemCategory.Crops => HasAnyTag(data.TagsMask, ObjectCategoryTag.SeedExtractable, ObjectCategoryTag.Seed),
            StorageTerminalItemCategory.Fish => data.HasFishComponent ||
                                                data.IngredientType == IngredientType.Fish ||
                                                HasAnyTag(data.TagsMask, ObjectCategoryTag.Fish),
            StorageTerminalItemCategory.Materials => data.ObjectType == ObjectType.UniqueCraftingComponent ||
                                                     data.IsRecipeIngredient,
            StorageTerminalItemCategory.Blocks => IsBlockTile(data.TileType),
            StorageTerminalItemCategory.Ground => IsGroundTile(data.TileType),
            StorageTerminalItemCategory.Floor => IsFloorTile(data.TileType),
            StorageTerminalItemCategory.Bridges => data.TileType == TileType.bridge,
            StorageTerminalItemCategory.Decoration => IsDecorativePlaceable(data),
            StorageTerminalItemCategory.Placeables => IsPlaceable(data),
            StorageTerminalItemCategory.Weapons => IsWeaponType(data.ObjectType),
            StorageTerminalItemCategory.Tools => IsToolType(data.ObjectType),
            StorageTerminalItemCategory.Armor => IsArmorType(data.ObjectType),
            StorageTerminalItemCategory.Accessories => IsAccessoryType(data.ObjectType),
            StorageTerminalItemCategory.Valuables => data.ObjectType == ObjectType.Valuable ||
                                                     HasAnyTag(data.TagsMask, ObjectCategoryTag.Valuable),
            StorageTerminalItemCategory.Pets => data.ObjectType == ObjectType.Pet ||
                                                data.HasPetComponent ||
                                                HasAnyTag(data.TagsMask, ObjectCategoryTag.Pet, ObjectCategoryTag.PetEgg),
            StorageTerminalItemCategory.Critters => data.ObjectType == ObjectType.Critter ||
                                                    HasAnyTag(data.TagsMask, ObjectCategoryTag.Critter),
            _ => !data.HasValidObject ||
                 (!Matches(StorageTerminalItemCategory.Food, data) &&
                  !Matches(StorageTerminalItemCategory.Ingredients, data) &&
                  !Matches(StorageTerminalItemCategory.Crops, data) &&
                  !Matches(StorageTerminalItemCategory.Fish, data) &&
                  !Matches(StorageTerminalItemCategory.Materials, data) &&
                  !Matches(StorageTerminalItemCategory.Blocks, data) &&
                  !Matches(StorageTerminalItemCategory.Ground, data) &&
                  !Matches(StorageTerminalItemCategory.Floor, data) &&
                  !Matches(StorageTerminalItemCategory.Bridges, data) &&
                  !Matches(StorageTerminalItemCategory.Decoration, data) &&
                  !Matches(StorageTerminalItemCategory.Placeables, data) &&
                  !Matches(StorageTerminalItemCategory.Weapons, data) &&
                  !Matches(StorageTerminalItemCategory.Tools, data) &&
                  !Matches(StorageTerminalItemCategory.Armor, data) &&
                  !Matches(StorageTerminalItemCategory.Accessories, data) &&
                  !Matches(StorageTerminalItemCategory.Valuables, data) &&
                  !Matches(StorageTerminalItemCategory.Pets, data) &&
                  !Matches(StorageTerminalItemCategory.Critters, data))
        };
    }

    private static bool IsWeaponType(ObjectType objectType)
    {
        return objectType == ObjectType.MeleeWeapon ||
               objectType == ObjectType.RangeWeapon ||
               objectType == ObjectType.SummoningWeapon ||
               objectType == ObjectType.ThrowingWeapon ||
               objectType == ObjectType.BeamWeapon;
    }

    private static bool IsToolType(ObjectType objectType)
    {
        return objectType == ObjectType.Shovel ||
               objectType == ObjectType.Hoe ||
               objectType == ObjectType.CastingItem ||
               objectType == ObjectType.MiningPick ||
               objectType == ObjectType.PaintTool ||
               objectType == ObjectType.FishingRod ||
               objectType == ObjectType.BugNet ||
               objectType == ObjectType.Sledge ||
               objectType == ObjectType.RoofingTool ||
               objectType == ObjectType.DrillTool ||
               objectType == ObjectType.WaterCan ||
               objectType == ObjectType.Bucket ||
               objectType == ObjectType.Seeder ||
               objectType == ObjectType.Instrument;
    }

    private static bool IsArmorType(ObjectType objectType)
    {
        return objectType == ObjectType.Helm ||
               objectType == ObjectType.BreastArmor ||
               objectType == ObjectType.PantsArmor;
    }

    private static bool IsAccessoryType(ObjectType objectType)
    {
        return objectType == ObjectType.Necklace ||
               objectType == ObjectType.Ring ||
               objectType == ObjectType.Offhand ||
               objectType == ObjectType.Bag ||
               objectType == ObjectType.Lantern ||
               objectType == ObjectType.Pouch;
    }

    private static bool IsBlockTile(TileType tileType)
    {
        return tileType == TileType.wall ||
               tileType == TileType.thinWall ||
               tileType == TileType.greatWall ||
               tileType == TileType.wallGrass;
    }

    private static bool IsFloorTile(TileType tileType)
    {
        return tileType == TileType.floor ||
               tileType == TileType.rug ||
               tileType == TileType.litFloor ||
               tileType == TileType.looseFlooring;
    }

    private static bool IsGroundTile(TileType tileType)
    {
        return tileType == TileType.ground ||
               tileType == TileType.dugUpGround ||
               tileType == TileType.wateredGround ||
               tileType == TileType.groundSlime ||
               tileType == TileType.chrysalis ||
               tileType == TileType.circuitPlate ||
               tileType == TileType.ancientCircuitPlate;
    }

    private static bool IsStructuralTile(TileType tileType)
    {
        return IsBlockTile(tileType) ||
               IsGroundTile(tileType) ||
               IsFloorTile(tileType) ||
               tileType == TileType.bridge;
    }

    private static bool IsPlaceable(ClassificationData data)
    {
        return data.ObjectType == ObjectType.PlaceablePrefab ||
               data.TileType != TileType.none ||
               data.HasPlacement ||
               data.HasGroundDecoration;
    }

    private static bool IsFunctionalPlaceable(ClassificationData data)
    {
        return data.HasExtraInventory ||
               data.HasInventoryAutoTransfer ||
               data.HasCrafting ||
               data.HasAnvil ||
               data.HasMerchant ||
               data.HasPortal ||
               data.HasEnemySpawnerPlatform ||
               data.HasObjectFiltering ||
               data.HasCategoryFiltering;
    }

    private static bool IsDecorativePlaceable(ClassificationData data)
    {
        if (!IsPlaceable(data) || IsStructuralTile(data.TileType) || IsFunctionalPlaceable(data))
        {
            return false;
        }

        if (data.ObjectType == ObjectType.Eatable ||
            data.ObjectType == ObjectType.Critter ||
            data.ObjectType == ObjectType.Pet ||
            data.ObjectType == ObjectType.Valuable ||
            data.ObjectType == ObjectType.UniqueCraftingComponent ||
            IsWeaponType(data.ObjectType) ||
            IsToolType(data.ObjectType) ||
            IsArmorType(data.ObjectType) ||
            IsAccessoryType(data.ObjectType))
        {
            return false;
        }

        if (data.HasCookingIngredient ||
            data.HasCookedFood ||
            data.HasFishComponent ||
            data.HasPetComponent ||
            HasAnyTag(data.TagsMask, ObjectCategoryTag.CookingIngredient) ||
            HasAnyTag(data.TagsMask, ObjectCategoryTag.SeedExtractable, ObjectCategoryTag.Seed) ||
            HasAnyTag(data.TagsMask, ObjectCategoryTag.Fish) ||
            HasAnyTag(data.TagsMask, ObjectCategoryTag.Pet, ObjectCategoryTag.PetEgg) ||
            HasAnyTag(data.TagsMask, ObjectCategoryTag.Critter) ||
            HasAnyTag(data.TagsMask, ObjectCategoryTag.Valuable))
        {
            return false;
        }

        return true;
    }

    private static bool IsRecipeIngredient(ObjectID objectId)
    {
        if (objectId == ObjectID.None)
        {
            return false;
        }

        EnsureRecipeIngredientCache();
        return _recipeIngredientObjectIds != null && _recipeIngredientObjectIds.Contains(objectId);
    }

    private static void EnsureRecipeIngredientCache()
    {
        if (Manager.main?.player?.querySystem == null ||
            !Manager.main.player.querySystem.TryGetSingleton<PugDatabase.DatabaseBankCD>(out PugDatabase.DatabaseBankCD databaseBankCD) ||
            !databaseBankCD.databaseBankBlob.IsCreated)
        {
            _recipeIngredientSource = default;
            _recipeIngredientObjectIds = null;
            return;
        }

        if (_recipeIngredientObjectIds != null && _recipeIngredientSource.Equals(databaseBankCD.databaseBankBlob))
        {
            return;
        }

        HashSet<ObjectID> recipeIngredientObjectIds = new HashSet<ObjectID>();
        ref BlobArray<PugDatabase.EntityObjectInfo> objectInfos = ref databaseBankCD.databaseBankBlob.Value.objectInfos;
        for (int i = 0; i < objectInfos.Length; i++)
        {
            ref BlobArray<ObjectWithAmount> requiredObjectsToCraft = ref objectInfos[i].requiredObjectsToCraft;
            for (int j = 0; j < requiredObjectsToCraft.Length; j++)
            {
                ObjectID requiredObjectId = requiredObjectsToCraft[j].objectID;
                if (requiredObjectId != ObjectID.None)
                {
                    recipeIngredientObjectIds.Add(requiredObjectId);
                }
            }
        }

        _recipeIngredientSource = databaseBankCD.databaseBankBlob;
        _recipeIngredientObjectIds = recipeIngredientObjectIds;
    }

    private static bool HasAnyTag(ulong tagsMask, ObjectCategoryTag firstTag)
    {
        return ObjectCategoryTagsCD.HasTag(tagsMask, firstTag);
    }

    private static bool HasAnyTag(ulong tagsMask, ObjectCategoryTag firstTag, ObjectCategoryTag secondTag)
    {
        return ObjectCategoryTagsCD.HasTag(tagsMask, firstTag) ||
               ObjectCategoryTagsCD.HasTag(tagsMask, secondTag);
    }

    private static bool HasAnyTag(
        ulong tagsMask,
        ObjectCategoryTag firstTag,
        ObjectCategoryTag secondTag,
        ObjectCategoryTag thirdTag,
        ObjectCategoryTag fourthTag,
        ObjectCategoryTag fifthTag,
        ObjectCategoryTag sixthTag,
        ObjectCategoryTag seventhTag,
        ObjectCategoryTag eighthTag)
    {
        return ObjectCategoryTagsCD.HasTag(tagsMask, firstTag) ||
               ObjectCategoryTagsCD.HasTag(tagsMask, secondTag) ||
               ObjectCategoryTagsCD.HasTag(tagsMask, thirdTag) ||
               ObjectCategoryTagsCD.HasTag(tagsMask, fourthTag) ||
               ObjectCategoryTagsCD.HasTag(tagsMask, fifthTag) ||
               ObjectCategoryTagsCD.HasTag(tagsMask, sixthTag) ||
               ObjectCategoryTagsCD.HasTag(tagsMask, seventhTag) ||
               ObjectCategoryTagsCD.HasTag(tagsMask, eighthTag);
    }

    private static ClassificationData CreateClassificationData(ContainedObjectsBuffer containedObject)
    {
        ObjectDataCD objectData = containedObject.objectData;
        if (objectData.objectID == ObjectID.None)
        {
            return default;
        }

        ObjectInfo objectInfo = PugDatabase.GetObjectInfo(objectData.objectID, objectData.variation);
        ulong tagsMask = 0;
        if (PugDatabase.TryGetComponent<ObjectCategoryTagsCD>(objectData, out ObjectCategoryTagsCD categoryTags))
        {
            tagsMask = categoryTags.tagsBitMask;
        }

        bool hasCookingIngredient = PugDatabase.TryGetComponent<CookingIngredientCD>(objectData, out CookingIngredientCD ingredient);

        return new ClassificationData(
            hasValidObject: true,
            objectType: objectInfo != null ? objectInfo.objectType : ObjectType.NonUsable,
            tileType: objectInfo != null ? objectInfo.tileType : TileType.none,
            tagsMask: tagsMask,
            isRecipeIngredient: IsRecipeIngredient(objectData.objectID),
            hasCookingIngredient: hasCookingIngredient,
            ingredientType: hasCookingIngredient ? ingredient.ingredientType : IngredientType.None,
            hasPlacement: PugDatabase.HasComponent<PlacementCD>(objectData),
            hasCookedFood: PugDatabase.HasComponent<CookedFoodCD>(objectData),
            hasGroundDecoration: PugDatabase.HasComponent<GroundDecorationCD>(objectData),
            hasPetComponent: PugDatabase.HasComponent<PetCD>(objectData),
            hasFishComponent: PugDatabase.HasComponent<FishCD>(objectData),
            hasExtraInventory: PugDatabase.HasComponent<ExtraInventoryCD>(objectData),
            hasInventoryAutoTransfer: PugDatabase.HasComponent<InventoryAutoTransferEnabledCD>(objectData),
            hasCrafting: PugDatabase.HasComponent<CraftingCD>(objectData),
            hasAnvil: PugDatabase.HasComponent<AnvilCD>(objectData),
            hasMerchant: PugDatabase.HasComponent<MerchantCD>(objectData),
            hasPortal: PugDatabase.HasComponent<PortalCD>(objectData),
            hasEnemySpawnerPlatform: PugDatabase.HasComponent<EnemySpawnerPlatformCD>(objectData),
            hasObjectFiltering: PugDatabase.HasComponent<ObjectFilteringCD>(objectData),
            hasCategoryFiltering: PugDatabase.HasComponent<CategoryFilteringCD>(objectData));
    }

    private readonly struct ClassificationData
    {
        public readonly bool HasValidObject;
        public readonly ObjectType ObjectType;
        public readonly TileType TileType;
        public readonly ulong TagsMask;
        public readonly bool IsRecipeIngredient;
        public readonly bool HasCookingIngredient;
        public readonly IngredientType IngredientType;
        public readonly bool HasPlacement;
        public readonly bool HasCookedFood;
        public readonly bool HasGroundDecoration;
        public readonly bool HasPetComponent;
        public readonly bool HasFishComponent;
        public readonly bool HasExtraInventory;
        public readonly bool HasInventoryAutoTransfer;
        public readonly bool HasCrafting;
        public readonly bool HasAnvil;
        public readonly bool HasMerchant;
        public readonly bool HasPortal;
        public readonly bool HasEnemySpawnerPlatform;
        public readonly bool HasObjectFiltering;
        public readonly bool HasCategoryFiltering;

        public ClassificationData(
            bool hasValidObject,
            ObjectType objectType,
            TileType tileType,
            ulong tagsMask,
            bool isRecipeIngredient,
            bool hasCookingIngredient,
            IngredientType ingredientType,
            bool hasPlacement,
            bool hasCookedFood,
            bool hasGroundDecoration,
            bool hasPetComponent,
            bool hasFishComponent,
            bool hasExtraInventory,
            bool hasInventoryAutoTransfer,
            bool hasCrafting,
            bool hasAnvil,
            bool hasMerchant,
            bool hasPortal,
            bool hasEnemySpawnerPlatform,
            bool hasObjectFiltering,
            bool hasCategoryFiltering)
        {
            HasValidObject = hasValidObject;
            ObjectType = objectType;
            TileType = tileType;
            TagsMask = tagsMask;
            IsRecipeIngredient = isRecipeIngredient;
            HasCookingIngredient = hasCookingIngredient;
            IngredientType = ingredientType;
            HasPlacement = hasPlacement;
            HasCookedFood = hasCookedFood;
            HasGroundDecoration = hasGroundDecoration;
            HasPetComponent = hasPetComponent;
            HasFishComponent = hasFishComponent;
            HasExtraInventory = hasExtraInventory;
            HasInventoryAutoTransfer = hasInventoryAutoTransfer;
            HasCrafting = hasCrafting;
            HasAnvil = hasAnvil;
            HasMerchant = hasMerchant;
            HasPortal = hasPortal;
            HasEnemySpawnerPlatform = hasEnemySpawnerPlatform;
            HasObjectFiltering = hasObjectFiltering;
            HasCategoryFiltering = hasCategoryFiltering;
        }
    }
}

internal readonly struct StorageTerminalItemEntry
{
    public readonly ContainedObjectsBuffer ContainedObject;
    public readonly int TotalAmount;
    public readonly ulong EntryId;
    public readonly StorageTerminalSummaryEntryFlags Flags;
    public readonly string DisplayName;
    public readonly StorageTerminalItemCategory Category;

    public StorageTerminalItemEntry(
        ContainedObjectsBuffer containedObject,
        int totalAmount,
        ulong entryId,
        StorageTerminalSummaryEntryFlags flags,
        string displayName)
    {
        ContainedObject = containedObject;
        TotalAmount = totalAmount;
        EntryId = entryId;
        Flags = flags;
        DisplayName = displayName;
        Category = StorageTerminalItemCategoryUtility.GetPrimaryCategory(containedObject);
    }

    public ObjectID ObjectId => ContainedObject.objectID;

    public int Variation => ContainedObject.variation;

    public bool IsExactMatch => (Flags & StorageTerminalSummaryEntryFlags.ExactMatch) != 0;

    public bool ShouldShowAmountNumber => TotalAmount > 1;

    public bool MatchesCategory(StorageTerminalItemCategory category)
    {
        return StorageTerminalItemCategoryUtility.Matches(category, ContainedObject);
    }

    public ContainedObjectsBuffer CreateContainedObject()
    {
        return ContainedObject;
    }
}
