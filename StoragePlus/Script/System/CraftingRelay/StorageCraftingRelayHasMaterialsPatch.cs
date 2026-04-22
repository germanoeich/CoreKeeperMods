using System.Collections.Generic;
using HarmonyLib;
using Inventory;
using Unity.Entities;
using Unity.Mathematics;

[HarmonyPatch(typeof(CraftingHandler), nameof(CraftingHandler.HasMaterialsInCraftingInventoryToCraftRecipe), new[] { typeof(CraftingHandler.RecipeInfo), typeof(bool), typeof(List<Entity>), typeof(bool), typeof(int) })]
public static class StorageCraftingRelayHasMaterialsPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        CraftingHandler __instance,
        CraftingHandler.RecipeInfo recipeInfo,
        bool checkPlayerInventoryToo,
        List<Entity> nearbyChestsToTakeMaterialsFrom,
        bool useRequiredObjectsSetInRecipeInfo,
        int multiplier,
        ref bool __result)
    {
        if (!recipeInfo.isValid ||
            !StorageCraftingRelayClientSummaryUtility.TryBuildAdditionalCounts(
                __instance,
                nearbyChestsToTakeMaterialsFrom,
                out PlayerController player,
                out Dictionary<ObjectID, int> additionalCounts,
                out _))
        {
            return;
        }

        BufferLookup<ContainedObjectsBuffer> containedLookup = player.querySystem.GetBufferLookup<ContainedObjectsBuffer>(isReadOnly: true);
        BufferLookup<InventoryBuffer> inventoryLookup = player.querySystem.GetBufferLookup<InventoryBuffer>(isReadOnly: true);
        ComponentLookup<AnvilCD> anvilLookup = player.querySystem.GetComponentLookup<AnvilCD>(isReadOnly: true);
        ComponentLookup<ObjectDataCD> objectDataLookup = player.querySystem.GetComponentLookup<ObjectDataCD>(isReadOnly: true);
        BufferLookup<SummarizedConditionsBuffer> summarizedConditionsLookup = player.querySystem.GetBufferLookup<SummarizedConditionsBuffer>(isReadOnly: true);
        PugDatabase.DatabaseBankCD databaseBank = player.querySystem.GetSingleton<PugDatabase.DatabaseBankCD>();

        Dictionary<ObjectID, int> requiredAmounts = new();
        List<CraftingObject> requiredObjects = useRequiredObjectsSetInRecipeInfo
            ? recipeInfo.requiredObjectsToCraft
            : PugDatabase.GetObjectInfo(recipeInfo.objectID).requiredObjectsToCraft;

        float costMultiplier = InventoryUtility.GetAnyMaterialCostMultiplier(
            anvilLookup,
            objectDataLookup,
            summarizedConditionsLookup,
            __instance.craftingEntity,
            player.entity);

        for (int i = 0; i < requiredObjects.Count; i++)
        {
            CraftingObject requiredObject = requiredObjects[i];
            int amountNeeded = (int)math.max(1f, math.round(requiredObject.amount * costMultiplier)) * multiplier;
            if (requiredAmounts.TryGetValue(requiredObject.objectID, out int currentAmount))
            {
                requiredAmounts[requiredObject.objectID] = currentAmount + amountNeeded;
            }
            else
            {
                requiredAmounts.Add(requiredObject.objectID, amountNeeded);
            }
        }

        foreach (KeyValuePair<ObjectID, int> pair in requiredAmounts)
        {
            int amountAvailable = 0;
            if (additionalCounts.TryGetValue(pair.Key, out int relayAmount))
            {
                amountAvailable += relayAmount;
            }

            if (checkPlayerInventoryToo)
            {
                amountAvailable += InventoryUtility.GetTotalAmount(containedLookup, inventoryLookup, databaseBank, player.entity, pair.Key);
            }

            if (nearbyChestsToTakeMaterialsFrom != null)
            {
                for (int i = 0; i < nearbyChestsToTakeMaterialsFrom.Count; i++)
                {
                    Entity inventoryEntity = nearbyChestsToTakeMaterialsFrom[i];
                    if (containedLookup.HasBuffer(inventoryEntity))
                    {
                        amountAvailable += InventoryUtility.GetTotalAmount(containedLookup, inventoryLookup, databaseBank, inventoryEntity, pair.Key);
                    }
                }
            }

            if (amountAvailable < pair.Value)
            {
                __result = false;
                return;
            }
        }

        __result = true;
    }
}
