using CoreLib.Submodule.UserInterface;
using HarmonyLib;

[HarmonyPatch(typeof(InventorySlotUI), nameof(InventorySlotUI.TryToSendItemToOtherInventoryOrEquip))]
internal static class StorageTerminalPlayerInventoryTransferPatch
{
    [HarmonyPrefix]
    private static bool TryDepositIntoOpenTerminal(InventorySlotUI __instance, ref bool __result)
    {
        if (__instance == null || (!__instance.isPlayerInventorySlot && !__instance.isPlayerPouchSlot))
        {
            return true;
        }

        StorageTerminalUI terminalUI = UserInterfaceModule.GetCurrentInterface<StorageTerminalUI>();
        if (terminalUI == null || !terminalUI.Root.activeInHierarchy)
        {
            return true;
        }

        if (__instance.GetObjectData().objectID == ObjectID.None)
        {
            __result = false;
            return false;
        }

        if (!terminalUI.TryRequestDeposit(__instance))
        {
            __instance.PlayErrorEffect();
            __result = false;
            return false;
        }

        AudioManager.SfxUI(SfxID.uiPickup, 0.4f, reuse: true, 1f, 0.1f);
        __result = false;
        return false;
    }
}
