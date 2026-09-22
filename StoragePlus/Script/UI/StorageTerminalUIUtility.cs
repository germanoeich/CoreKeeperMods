using System.Collections.Generic;
using Rewired;
using CoreLib.Submodule.UserInterface.Component;
using UnityEngine;

internal static class StorageTerminalUIUtility
{
    public static void ConfigureNavigationLinks(
        StorageTerminalUI owner,
        StorageTerminalSearchField searchField,
        StorageTerminalSortButton sortButton,
        StorageTerminalSortButton sortOrderButton,
        StorageTerminalShowFiltersButton showFiltersButton,
        StorageTerminalHintTextButton hintTextButton,
        StorageTerminalGrid grid)
    {
        if (owner == null)
        {
            return;
        }

        EnsureUiElementLists(owner);
        if (searchField != null)
        {
            EnsureUiElementLists(searchField);
        }

        if (sortButton != null)
        {
            EnsureUiElementLists(sortButton);
        }

        if (sortOrderButton != null)
        {
            EnsureUiElementLists(sortOrderButton);
        }

        if (showFiltersButton != null)
        {
            EnsureUiElementLists(showFiltersButton);
        }

        if (hintTextButton != null)
        {
            EnsureUiElementLists(hintTextButton);
        }

        if (grid != null)
        {
            EnsureUiElementLists(grid);
        }

        ReplaceUiElementList(owner.childElements, searchField, sortButton, sortOrderButton, showFiltersButton, hintTextButton, grid);

        if (searchField != null)
        {
            ReplaceUiElementList(searchField.bottomUIElements, grid);
            ClearUiElementList(searchField.topUIElements);
            ReplaceUiElementList(searchField.leftUIElements, GetFirstAvailable(sortButton, sortOrderButton, hintTextButton));
            ReplaceUiElementList(searchField.rightUIElements, showFiltersButton);
        }

        if (sortButton != null)
        {
            ReplaceUiElementList(sortButton.topUIElements, searchField);
            ReplaceUiElementList(sortButton.bottomUIElements, GetFirstAvailable(sortOrderButton, hintTextButton, grid));
            ClearUiElementList(sortButton.leftUIElements);
            ReplaceUiElementList(sortButton.rightUIElements, grid);
        }

        if (sortOrderButton != null)
        {
            ReplaceUiElementList(sortOrderButton.topUIElements, GetFirstAvailable(sortButton, searchField));
            ReplaceUiElementList(sortOrderButton.bottomUIElements, GetFirstAvailable(hintTextButton, grid));
            ClearUiElementList(sortOrderButton.leftUIElements);
            ReplaceUiElementList(sortOrderButton.rightUIElements, grid);
        }

        if (showFiltersButton != null)
        {
            ClearUiElementList(showFiltersButton.topUIElements);
            ReplaceUiElementList(showFiltersButton.bottomUIElements, grid);
            ReplaceUiElementList(showFiltersButton.leftUIElements, searchField);
            ClearUiElementList(showFiltersButton.rightUIElements);
        }

        if (hintTextButton != null)
        {
            ReplaceUiElementList(hintTextButton.topUIElements, GetFirstAvailable(sortOrderButton, sortButton, searchField));
            ReplaceUiElementList(hintTextButton.bottomUIElements, Manager.ui?.playerInventoryUI);
            ClearUiElementList(hintTextButton.leftUIElements);
            ReplaceUiElementList(hintTextButton.rightUIElements, grid);
        }

        if (grid != null)
        {
            ReplaceUiElementList(grid.topUIElements, searchField, showFiltersButton);
            ReplaceUiElementList(grid.bottomUIElements, Manager.ui?.playerInventoryUI);
            ReplaceUiElementList(grid.leftUIElements, sortButton, sortOrderButton, hintTextButton);
            ReplaceUiElementList(grid.rightUIElements, Manager.ui?.playerInventoryUI);
        }
    }

    public static void EnsureUiElementLists(UIelement element)
    {
        element.topUIElements ??= new List<UIelement>();
        element.bottomUIElements ??= new List<UIelement>();
        element.leftUIElements ??= new List<UIelement>();
        element.rightUIElements ??= new List<UIelement>();
        element.childElements ??= new List<UIelement>();
    }

    public static bool IsUsingController()
    {
        return Manager.input != null && !Manager.input.SystemPrefersKeyboardAndMouse();
    }

    public static void SelectForController(UIelement element)
    {
        if (element == null || Manager.ui == null)
        {
            return;
        }

        element.Select();
        Manager.ui.mouse?.PlaceMousePositionOnSelectedUIElementWhenControlledByJoystick();
    }

    public static void ReplaceUiElementList(List<UIelement> target, params UIelement[] elements)
    {
        target.Clear();
        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i] != null)
            {
                target.Add(elements[i]);
            }
        }
    }

    public static void ClearUiElementList(List<UIelement> target)
    {
        target.Clear();
    }

    public static void ApplyVanillaItemIconMaterial(SpriteRenderer targetRenderer)
    {
        if (targetRenderer == null)
        {
            return;
        }

        SpriteRenderer sourceRenderer = GetVanillaItemIconSourceRenderer();
        if (sourceRenderer?.sharedMaterial == null)
        {
            return;
        }

        if (targetRenderer.sharedMaterial != sourceRenderer.sharedMaterial)
        {
            targetRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
        }
    }

    private static SpriteRenderer GetVanillaItemIconSourceRenderer()
    {
        if (Manager.ui?.playerInventoryUI?.itemSlotPrefab?.icon != null)
        {
            return Manager.ui.playerInventoryUI.itemSlotPrefab.icon;
        }

        if (Manager.ui?.chestInventoryUI?.itemSlotPrefab?.icon != null)
        {
            return Manager.ui.chestInventoryUI.itemSlotPrefab.icon;
        }

        if (Manager.ui?.itemSlotsBar?.itemSlotPrefab?.icon != null)
        {
            return Manager.ui.itemSlotsBar.itemSlotPrefab.icon;
        }

        return null;
    }

    public static bool ShouldPreferJoystickHints()
    {
        return IsUsingController();
    }

    public static TextAndFormatFields CreateInteractionHintLine(
        string description,
        bool prefersJoystick,
        params PlayerInput.InputType[] bindingActions)
    {
        if (string.IsNullOrWhiteSpace(description) || Manager.ui == null || bindingActions == null || bindingActions.Length == 0)
        {
            return null;
        }

        List<string> bindingParts = new(bindingActions.Length);
        for (int i = 0; i < bindingActions.Length; i++)
        {
            string shortCutString = GetInteractionShortcut(bindingActions[i], prefersJoystick);
            if (string.IsNullOrWhiteSpace(shortCutString))
            {
                return null;
            }

            bindingParts.Add(PugText.GetButtonStringForThai(shortCutString));
        }

        if (bindingParts.Count == 0)
        {
            return null;
        }

        return new TextAndFormatFields
        {
            text = description + ": " + string.Join(" + ", bindingParts),
            dontLocalize = true,
            dontLocalizeFormatFields = true,
            color = Color.white * 0.95f
        };
    }

    private static string GetInteractionShortcut(PlayerInput.InputType action, bool prefersJoystick)
    {
        if (!prefersJoystick)
        {
            return Manager.ui.GetShortCutString((int)action, prefersJoystick: false);
        }

        Player player = Manager.input?.singleplayerInputModule?.rewiredPlayer;
        if (player == null)
        {
            return null;
        }

        Controller activeController = player.controllers.GetLastActiveController();
        List<ActionElementMap> maps = new();
        player.controllers.maps.GetElementMapsWithAction((int)action, skipDisabledMaps: true, maps);
        ActionElementMap binding = null;
        foreach (ActionElementMap map in maps)
        {
            if (map.controllerMap.controllerType != ControllerType.Joystick)
            {
                continue;
            }

            binding ??= map;
            if (map.controllerMap.controller == activeController)
            {
                binding = map;
                break;
            }
        }

        return binding == null ? null : Manager.ui.controllerButtonToCharTable.GetControllerButtonCharacter(
            ControllerType.Joystick, binding.controllerMap.controller.name, binding.elementIdentifierName);
    }

    private static UIelement GetFirstAvailable(params UIelement[] elements)
    {
        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i] != null)
            {
                return elements[i];
            }
        }

        return null;
    }
}
