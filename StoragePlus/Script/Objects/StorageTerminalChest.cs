using CoreLib.Submodule.UserInterface;
using Pug.UnityExtensions;

public class StorageTerminalChest : Chest
{
    private bool _usesFallbackHiddenTile;

    protected override void OnShow()
    {
        _usesFallbackHiddenTile = StorageNetworkHiddenTileUtility.TryEnable(base.entity, base.world, base.WorldPosition.RoundToInt2());
        base.OnShow();
    }

    protected override void OnHide()
    {
        if (_usesFallbackHiddenTile)
        {
            StorageNetworkHiddenTileUtility.Disable(base.WorldPosition.RoundToInt2());
            _usesFallbackHiddenTile = false;
        }

        base.OnHide();
    }

    public override void Use()
    {
        if (Manager.ui != null)
        {
            Manager.ui.HideAllInventoryAndCraftingUI();
        }

        UserInterfaceModule.OpenModUI(this, StorageTerminalUI.InterfaceId);
    }

    public override void OnFree()
    {
        if (Manager.ui != null && UserInterfaceModule.GetInteractionEntity() == base.entity)
        {
            Manager.ui.HideAllInventoryAndCraftingUI();
        }

        base.OnFree();
    }
}
