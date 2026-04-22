using System.Collections.Generic;
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
            ClearUiElementList(searchField.leftUIElements);
            ReplaceUiElementList(searchField.rightUIElements, GetFirstAvailable(sortButton, sortOrderButton, showFiltersButton, hintTextButton));
        }

        if (sortButton != null)
        {
            ClearUiElementList(sortButton.topUIElements);
            ReplaceUiElementList(sortButton.bottomUIElements, grid);
            ReplaceUiElementList(sortButton.leftUIElements, searchField);
            ReplaceUiElementList(sortButton.rightUIElements, GetFirstAvailable(sortOrderButton, showFiltersButton, hintTextButton));
        }

        if (sortOrderButton != null)
        {
            ClearUiElementList(sortOrderButton.topUIElements);
            ReplaceUiElementList(sortOrderButton.bottomUIElements, grid);
            if (sortButton != null)
            {
                ReplaceUiElementList(sortOrderButton.leftUIElements, sortButton);
            }
            else
            {
                ReplaceUiElementList(sortOrderButton.leftUIElements, searchField);
            }
            ReplaceUiElementList(sortOrderButton.rightUIElements, GetFirstAvailable(showFiltersButton, hintTextButton));
        }

        if (showFiltersButton != null)
        {
            ClearUiElementList(showFiltersButton.topUIElements);
            ReplaceUiElementList(showFiltersButton.bottomUIElements, GetFirstAvailable(hintTextButton, grid));
            ReplaceUiElementList(showFiltersButton.leftUIElements, GetFirstAvailable(sortOrderButton, sortButton, searchField));
            ReplaceUiElementList(showFiltersButton.rightUIElements, hintTextButton);
        }

        if (hintTextButton != null)
        {
            ClearUiElementList(hintTextButton.topUIElements);
            ReplaceUiElementList(hintTextButton.bottomUIElements, grid);
            ReplaceUiElementList(hintTextButton.leftUIElements, GetFirstAvailable(showFiltersButton, sortOrderButton, sortButton, searchField));
            ClearUiElementList(hintTextButton.rightUIElements);
        }

        if (grid != null)
        {
            ReplaceUiElementList(grid.topUIElements, searchField, sortButton, sortOrderButton, showFiltersButton, hintTextButton);
            ClearUiElementList(grid.bottomUIElements);
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
        return Manager.input != null &&
               Manager.input.IsAnyGamepadConnected() &&
               Manager.input.singleplayerInputModule != null &&
               !Manager.input.singleplayerInputModule.PrefersKeyboardAndMouse();
    }

    public static TextAndFormatFields CreateInteractionHintLine(
        string description,
        bool prefersJoystick,
        params string[] bindingActionNames)
    {
        if (string.IsNullOrWhiteSpace(description) || Manager.ui == null || bindingActionNames == null || bindingActionNames.Length == 0)
        {
            return null;
        }

        List<string> bindingParts = new(bindingActionNames.Length);
        for (int i = 0; i < bindingActionNames.Length; i++)
        {
            string bindingActionName = bindingActionNames[i];
            if (string.IsNullOrWhiteSpace(bindingActionName))
            {
                continue;
            }

            string shortCutString = Manager.ui.GetShortCutString(bindingActionName, prefersJoystick);
            if (string.IsNullOrWhiteSpace(shortCutString))
            {
                continue;
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
