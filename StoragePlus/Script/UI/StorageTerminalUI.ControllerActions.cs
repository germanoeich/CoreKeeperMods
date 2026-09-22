using InputType = PlayerInput.InputType;

public sealed partial class StorageTerminalUI
{
    internal const InputType ControllerSecondaryAction = InputType.PICK_UP_ITEMS;
    internal const InputType ControllerFetchTenAction = InputType.PICK_UP_HALF;

    private void UpdateControllerActions()
    {
        if (!StorageTerminalUIUtility.IsUsingController() || Manager.ui == null ||
            Manager.input.textInputIsActive || (Manager.menu != null && Manager.menu.IsAnyMenuActive()))
        {
            return;
        }

        PlayerInput input = Manager.input.singleplayerInputModule;
        UIelement selected = Manager.ui.currentSelectedUIElement;
        if (input == null || selected == null || !selected.isShowing || !selected.isVisibleOnScreen ||
            !selected.transform.IsChildOf(transform))
        {
            return;
        }

        // Native UIMouse already dispatches these; remapped buttons must not fire twice.
        if (input.WasButtonPressedDownThisFrame(InputType.UI_INTERACT) ||
            input.WasButtonPressedDownThisFrame(InputType.UI_SECOND_INTERACT))
        {
            return;
        }

        if (selected is StorageTerminalItemSlot && input.WasButtonPressedDownThisFrame(ControllerFetchTenAction))
        {
            selected.LeftClick(mod1: true, mod2: false);
        }
        else if ((selected is StorageTerminalItemSlot || selected is StorageTerminalSortButton) &&
                 input.WasButtonPressedDownThisFrame(ControllerSecondaryAction))
        {
            selected.RightClick();
        }
    }
}
