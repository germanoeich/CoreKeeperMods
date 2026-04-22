using System.Collections.Generic;
using UnityEngine;

public sealed class StorageTerminalFilterGrid : MonoBehaviour, IStorageTerminalHotSyncAware
{
    private readonly List<StorageTerminalFilterButton> _buttons = new();

    [SerializeField]
    private Transform filterTemplate;

    [Min(1)]
    [SerializeField]
    private int filterColumnCount = 3;

    [Min(0.01f)]
    [SerializeField]
    private float filterCellWidth = 1f;

    [Min(0.01f)]
    [SerializeField]
    private float filterCellHeight = 1f;

    [Min(0f)]
    [SerializeField]
    private float filterCellGapX;

    [Min(0f)]
    [SerializeField]
    private float filterCellGapY;

    [SerializeField]
    private Vector2 filterGridOffset;

    [SerializeField]
    private Sprite resetFiltersIcon;

    private StorageTerminalUI _owner;

    internal IReadOnlyList<StorageTerminalFilterButton> Buttons => _buttons;

    internal bool IsVisible => gameObject.activeSelf;

    internal int ColumnCount => GetColumnCount();

    private void Reset()
    {
        AssignSerializedReferences();
    }

    private void OnValidate()
    {
        AssignSerializedReferences();
    }

    public void OnHotSyncApplied()
    {
        AssignSerializedReferences();
        if (_owner != null)
        {
            RefreshRuntimeButtons();
        }
    }

    internal void EnsureBuilt(StorageTerminalUI owner)
    {
        _owner = owner;
        AssignSerializedReferences();

        if (filterTemplate == null)
        {
            Debug.LogError("StorageTerminalFilterGrid is missing FilterTemplate.", this);
            return;
        }

        if (_buttons.Count > 0)
        {
            RefreshRuntimeButtons();
            return;
        }

        BuildButtons();
    }

    internal Vector2 GetCellSize()
    {
        return new Vector2(GetCellWidth(), GetCellHeight());
    }

    private void BuildButtons()
    {
        Vector3 origin = GetGridOrigin();
        int columnCount = GetColumnCount();
        Vector2 cellStep = GetCellStep();
        StorageTerminalFilterIconAuthoring templateIconAuthoring = filterTemplate.GetComponent<StorageTerminalFilterIconAuthoring>();
        Debug.Log(
            $"StorageTerminal filter template state before build: hasIconAuthoring={templateIconAuthoring != null}, " +
            $"iconEntries={templateIconAuthoring?.IconEntryCount ?? -1}, " +
            $"entrySummary={templateIconAuthoring?.GetDebugEntrySummary() ?? "<none>"}.",
            filterTemplate);

        IReadOnlyList<StorageTerminalItemCategory> categories = StorageTerminalItemCategoryUtility.GetOrderedCategories();
        for (int i = 0, visibleIndex = 0; i < categories.Count; i++)
        {
            StorageTerminalItemCategory category = categories[i];
            if (category == StorageTerminalItemCategory.All)
            {
                continue;
            }

            Transform filterInstance = Instantiate(filterTemplate, transform, false);
            int column = visibleIndex % columnCount;
            int row = visibleIndex / columnCount;
            filterInstance.localPosition = origin + new Vector3(column * cellStep.x, -row * cellStep.y, 0f);

            StorageTerminalFilterButton button = filterInstance.GetComponent<StorageTerminalFilterButton>();
            if (button == null)
            {
                Debug.LogError("StorageTerminalFilterGrid FilterTemplate is missing StorageTerminalFilterButton.", filterInstance);
                Destroy(filterInstance.gameObject);
                continue;
            }

            button.Initialize(_owner, category);
            filterInstance.gameObject.SetActive(true);
            _buttons.Add(button);
            visibleIndex++;
        }

        Transform resetInstance = Instantiate(filterTemplate, transform, false);
        int resetIndex = _buttons.Count;
        int resetColumn = resetIndex % columnCount;
        int resetRow = resetIndex / columnCount;
        resetInstance.localPosition = origin + new Vector3(resetColumn * cellStep.x, -resetRow * cellStep.y, 0f);

        StorageTerminalFilterButton resetButton = resetInstance.GetComponent<StorageTerminalFilterButton>();
        if (resetButton == null)
        {
            Debug.LogError("StorageTerminalFilterGrid FilterTemplate is missing StorageTerminalFilterButton.", resetInstance);
            Destroy(resetInstance.gameObject);
        }
        else
        {
            resetButton.InitializeReset(_owner, resetFiltersIcon);
            resetInstance.gameObject.SetActive(true);
            _buttons.Add(resetButton);
        }

        filterTemplate.gameObject.SetActive(false);
        RefreshRuntimeButtons();
    }

    private void RefreshRuntimeButtons()
    {
        RefreshButtonLayout();
        RefreshButtonSizing();
        RefreshButtonIcons();
    }

    private void RefreshButtonLayout()
    {
        if (filterTemplate == null)
        {
            return;
        }

        Vector3 origin = GetGridOrigin();
        int columnCount = GetColumnCount();
        Vector2 cellStep = GetCellStep();
        for (int i = 0; i < _buttons.Count; i++)
        {
            StorageTerminalFilterButton button = _buttons[i];
            if (button == null)
            {
                continue;
            }

            int column = i % columnCount;
            int row = i / columnCount;
            button.transform.localPosition = origin + new Vector3(column * cellStep.x, -row * cellStep.y, 0f);
        }
    }

    private void RefreshButtonSizing()
    {
        Vector2 cellSize = GetCellSize();
        for (int i = 0; i < _buttons.Count; i++)
        {
            StorageTerminalFilterButton button = _buttons[i];
            if (button == null)
            {
                continue;
            }

            button.ApplyCellSize(cellSize);
        }
    }

    private void RefreshButtonIcons()
    {
        if (filterTemplate == null)
        {
            return;
        }

        StorageTerminalFilterIconAuthoring templateIconAuthoring = filterTemplate.GetComponent<StorageTerminalFilterIconAuthoring>();
        for (int i = 0; i < _buttons.Count; i++)
        {
            StorageTerminalFilterButton button = _buttons[i];
            if (button == null)
            {
                continue;
            }

            button.RefreshIcon(templateIconAuthoring);
        }
    }

    private void AssignSerializedReferences()
    {
        filterTemplate ??= transform.Find("FilterTemplate");
    }

    private Vector3 GetGridOrigin()
    {
        if (filterTemplate == null)
        {
            return new Vector3(filterGridOffset.x, filterGridOffset.y, 0f);
        }

        return filterTemplate.localPosition
            + new Vector3(GetCellGapX(), -GetCellGapY(), 0f)
            + new Vector3(filterGridOffset.x, filterGridOffset.y, 0f);
    }

    private int GetColumnCount()
    {
        return Mathf.Max(1, filterColumnCount);
    }

    private Vector2 GetCellStep()
    {
        return new Vector2(
            GetCellWidth() + GetCellGapX(),
            GetCellHeight() + GetCellGapY());
    }

    private float GetCellWidth()
    {
        return Mathf.Max(0.01f, filterCellWidth);
    }

    private float GetCellHeight()
    {
        return Mathf.Max(0.01f, filterCellHeight);
    }

    private float GetCellGapX()
    {
        return Mathf.Max(0f, filterCellGapX);
    }

    private float GetCellGapY()
    {
        return Mathf.Max(0f, filterCellGapY);
    }
}
