using System.Collections.Generic;
using UnityEngine;

public sealed partial class StorageTerminalUI
{
    private readonly HashSet<StorageTerminalItemCategory> _selectedCategories = new();
    private int _lastCategoryMask = int.MinValue;

    internal void ToggleCategoryFilter(StorageTerminalItemCategory category)
    {
        if (category == StorageTerminalItemCategory.All)
        {
            return;
        }

        if (!_selectedCategories.Add(category))
        {
            _selectedCategories.Remove(category);
        }

        RefreshFilterButtonVisuals();
        RefreshEntries(force: true);
    }

    internal void ResetCategoryFilters()
    {
        if (_selectedCategories.Count == 0)
        {
            return;
        }

        _selectedCategories.Clear();
        RefreshFilterButtonVisuals();
        RefreshEntries(force: true);
    }

    internal bool IsCategorySelected(StorageTerminalItemCategory category)
    {
        return _selectedCategories.Contains(category);
    }

    internal bool HasAnyCategoryFiltersSelected()
    {
        return _selectedCategories.Count > 0;
    }

    internal void OnFiltersPanelVisibilityChanged()
    {
        RefreshNavigationLinks();
        RefreshFilterButtonVisuals();
    }

    private void EnsureFilterButtonsBuilt()
    {
        if (filtersPanel != null && filterGrid == null)
        {
            Debug.LogError("StorageTerminalUI FiltersPanel is missing StorageTerminalFilterGrid.", filtersPanel);
            return;
        }

        filterGrid?.EnsureBuilt(this);
    }

    private void RefreshFilterButtonVisuals()
    {
        IReadOnlyList<StorageTerminalFilterButton> buttons = filterGrid?.Buttons;
        if (buttons == null)
        {
            return;
        }

        for (int i = 0; i < buttons.Count; i++)
        {
            if (buttons[i] != null)
            {
                buttons[i].RefreshVisuals(force: true);
            }
        }
    }

    private void RefreshNavigationLinks()
    {
        StorageTerminalUIUtility.ConfigureNavigationLinks(this, searchField, sortButton, sortOrderButton, showFiltersButton, hintTextButton, grid);
        RefreshFilterNavigation();
    }

    private void RefreshFilterNavigation()
    {
        IReadOnlyList<StorageTerminalFilterButton> buttons = filterGrid?.Buttons;
        bool filtersVisible = filtersPanel != null && filtersPanel.gameObject.activeSelf && buttons != null && buttons.Count > 0;
        if (showFiltersButton != null)
        {
            UIelement bottomTarget = filtersVisible
                ? buttons[0]
                : (hintTextButton != null ? hintTextButton : grid);
            StorageTerminalUIUtility.ReplaceUiElementList(showFiltersButton.bottomUIElements, bottomTarget);
        }

        if (buttons == null || buttons.Count == 0 || filterGrid == null)
        {
            return;
        }

        int columnCount = Mathf.Max(1, filterGrid.ColumnCount);
        for (int i = 0; i < buttons.Count; i++)
        {
            StorageTerminalFilterButton button = buttons[i];
            if (button == null)
            {
                continue;
            }

            StorageTerminalUIUtility.EnsureUiElementLists(button);

            int column = i % columnCount;
            int row = i / columnCount;
            int topIndex = i - columnCount;
            int bottomIndex = i + columnCount;

            StorageTerminalUIUtility.ReplaceUiElementList(button.topUIElements, topIndex >= 0 ? buttons[topIndex] : showFiltersButton);
            StorageTerminalUIUtility.ReplaceUiElementList(button.bottomUIElements, bottomIndex < buttons.Count ? buttons[bottomIndex] : grid);
            StorageTerminalUIUtility.ReplaceUiElementList(button.leftUIElements, column > 0 ? buttons[i - 1] : showFiltersButton);

            if (column < columnCount - 1 && i + 1 < buttons.Count)
            {
                StorageTerminalUIUtility.ReplaceUiElementList(button.rightUIElements, buttons[i + 1]);
            }
            else if (row == 0)
            {
                StorageTerminalUIUtility.ReplaceUiElementList(button.rightUIElements, searchField);
            }
            else
            {
                StorageTerminalUIUtility.ClearUiElementList(button.rightUIElements);
            }
        }
    }

    private bool MatchesSelectedCategories(StorageTerminalItemEntry entry)
    {
        if (_selectedCategories.Count == 0)
        {
            return true;
        }

        foreach (StorageTerminalItemCategory category in _selectedCategories)
        {
            if (entry.MatchesCategory(category))
            {
                return true;
            }
        }

        return false;
    }

    private int GetSelectedCategoryMask()
    {
        int mask = 0;
        foreach (StorageTerminalItemCategory category in _selectedCategories)
        {
            if (category == StorageTerminalItemCategory.All)
            {
                continue;
            }

            int bit = 1 << ((int)category - 1);
            mask |= bit;
        }

        return mask;
    }

    private bool HasSelectedCategories()
    {
        return _selectedCategories.Count > 0;
    }
}
