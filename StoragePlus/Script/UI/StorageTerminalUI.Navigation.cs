using UnityEngine;

public sealed partial class StorageTerminalUI
{
    private bool _controllerFocusPending;
    private bool _wasUsingController;
    private ItemSlotsUIContainer _linkedPlayerInventory;

    internal UIelement EmptyGridFocusTarget => searchField;

    private void LinkNetworkFullnessBar()
    {
        if (networkFullnessBarHover == null || hintTextButton == null)
        {
            return;
        }

        StorageTerminalUIUtility.EnsureUiElementLists(networkFullnessBarHover);
        childElements.Add(networkFullnessBarHover);
        StorageTerminalUIUtility.ReplaceUiElementList(
            hintTextButton.bottomUIElements, networkFullnessBarHover, Manager.ui?.playerInventoryUI);
        StorageTerminalUIUtility.ReplaceUiElementList(networkFullnessBarHover.topUIElements, hintTextButton);
        StorageTerminalUIUtility.ReplaceUiElementList(networkFullnessBarHover.bottomUIElements, Manager.ui?.playerInventoryUI);
        StorageTerminalUIUtility.ReplaceUiElementList(networkFullnessBarHover.rightUIElements, grid);
    }

    private void LinkPlayerInventory()
    {
        ItemSlotsUIContainer inventory = Manager.ui?.playerInventoryUI;
        if (inventory == null || grid == null)
        {
            return;
        }

        StorageTerminalUIUtility.EnsureUiElementLists(inventory);
        StorageTerminalUIUtility.ReplaceUiElementList(grid.bottomUIElements, inventory);
        if (!inventory.topUIElements.Contains(grid))
        {
            inventory.topUIElements.Add(grid);
            _linkedPlayerInventory = inventory;
        }

        // Empty search results must still allow downward navigation into the inventory.
        foreach (UIelement element in GetComponentsInChildren<UIelement>(includeInactive: true))
        {
            if (element.bottomUIElements != null && element.bottomUIElements.Contains(grid) &&
                !element.bottomUIElements.Contains(inventory))
            {
                element.bottomUIElements.Add(inventory);
            }
        }
    }

    private void UnlinkPlayerInventory()
    {
        if (_linkedPlayerInventory != null)
        {
            _linkedPlayerInventory.topUIElements.Remove(grid);
            _linkedPlayerInventory = null;
        }
    }

    private void UpdateControllerFocus()
    {
        bool usingController = StorageTerminalUIUtility.IsUsingController();
        if (!usingController)
        {
            _wasUsingController = false;
            return;
        }

        if (Manager.ui == null || Manager.input.textInputIsActive ||
            (Manager.menu != null && Manager.menu.IsAnyMenuActive()))
        {
            return;
        }

        UIelement selected = Manager.ui.currentSelectedUIElement;
        bool needsFocus = _controllerFocusPending || selected == null || !selected.isShowing ||
            !selected.isVisibleOnScreen || (!_wasUsingController && !IsSelectionInsideRoot());
        _controllerFocusPending = false;
        _wasUsingController = true;
        if (!needsFocus)
        {
            return;
        }

        UIelement target = grid.GetClosestUIElement(searchField.transform.position) ?? EmptyGridFocusTarget;
        StorageTerminalUIUtility.SelectForController(target);
    }
}
