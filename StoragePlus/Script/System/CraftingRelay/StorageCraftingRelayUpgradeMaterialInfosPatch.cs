using System.Collections.Generic;
using HarmonyLib;
using Unity.Entities;

[HarmonyPatch(typeof(CraftingHandler), nameof(CraftingHandler.GetCraftingMaterialInfosForUpgrade), new[] { typeof(int), typeof(List<Entity>) })]
public static class StorageCraftingRelayUpgradeMaterialInfosPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        CraftingHandler __instance,
        List<Entity> nearbyChestsToTakeMaterialsFrom,
        ref List<PugDatabase.MaterialInfo> __result)
    {
        if (!StorageCraftingRelayClientSummaryUtility.TryBuildAdditionalCounts(
                __instance,
                nearbyChestsToTakeMaterialsFrom,
                out PlayerController player,
                out Dictionary<ObjectID, int> additionalCounts,
                out Dictionary<ObjectID, Entity> sourceRelays))
        {
            return;
        }

        StorageCraftingRelayClientSummaryUtility.ApplyAdditionalCounts(
            player.querySystem.World.EntityManager,
            additionalCounts,
            sourceRelays,
            __result);
    }
}
