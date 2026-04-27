using System.Collections.Generic;
using UnityEngine;

public sealed class StorageTerminalGrid : ItemSlotsUIContainer, IScrollable, IStorageTerminalHotSyncAware
{
    [SerializeField]
    [Min(1)]
    private int configuredVisibleRows = 4;

    [SerializeField]
    [Min(0)]
    private int extraBufferedRows = 1;

    [SerializeField]
    [Min(1)]
    private int configuredColumns = 8;

    private readonly List<StorageTerminalItemEntry> _entries = new();

    [SerializeField]
    private StorageTerminalItemSlot slotTemplate;

    private StorageTerminalUI _owner;
    private float _currentScroll;
    private Vector3 _authoredLocalScale = Vector3.one;
    private Vector3 _authoredItemsRootScale = Vector3.one;

    private int VisibleRowCount => Mathf.Max(1, configuredVisibleRows);

    private int BufferedRowCount => VisibleRowCount + Mathf.Max(0, extraBufferedRows);

    private int ColumnCount => Mathf.Max(1, configuredColumns);

    internal StorageTerminalUI Owner => _owner;

    public override int MAX_ROWS => BufferedRowCount;

    public override int MAX_COLUMNS => ColumnCount;

    public override UIScrollWindow uiScrollWindow => scrollWindow;

    protected override void Awake()
    {
        _authoredLocalScale = transform.localScale;
        AutoAssignReferences();
        if (itemSlotsRoot != null)
        {
            _authoredItemsRootScale = itemSlotsRoot.transform.localScale;
        }
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();

        transform.localScale = _authoredLocalScale;
        if (itemSlotsRoot != null)
        {
            itemSlotsRoot.transform.localScale = _authoredItemsRootScale;
        }
    }

    private void Reset()
    {
        AutoAssignReferences();
    }

    private void OnValidate()
    {
        AutoAssignReferences();
        configuredVisibleRows = Mathf.Max(1, configuredVisibleRows);
        extraBufferedRows = Mathf.Max(0, extraBufferedRows);
        configuredColumns = Mathf.Max(1, configuredColumns);
    }

    public void Initialize(StorageTerminalUI owner)
    {
        _owner = owner;
        StorageTerminalUIUtility.EnsureUiElementLists(this);
        AutoAssignReferences();
        Init();

        if (scrollWindow != null)
        {
            scrollWindow.enabled = false;
        }
    }

    public override void Init()
    {
        if (initDone)
        {
            return;
        }

        AutoAssignReferences();
        if (itemSlotsRoot == null)
        {
            Debug.LogError("StorageTerminalGrid requires an ItemsRoot child.", this);
            return;
        }
        if (slotTemplate == null)
        {
            Debug.LogError("StorageTerminalGrid requires a SlotTemplate reference.", this);
            return;
        }

        visibleRows = VisibleRowCount;
        visibleColumns = ColumnCount;
        itemSlots = new List<SlotUIBase>();

        itemSlotsRoot.SetActive(keepSlotsEnabledAfterInit);
        initDone = true;
    }

    public override void ShowContainerUI()
    {
        base.ShowContainerUI();
        UpdateVisibleSlots();
    }

    internal void RefreshScrollPresentation()
    {
        if (scrollWindow == null)
        {
            return;
        }

        float scrollHeight = Mathf.Max(0f, GetCurrentWindowHeight() - scrollWindow.windowHeight);
        ScrollBar scrollBar = scrollWindow.scrollBar;
        if (scrollBar == null)
        {
            return;
        }

        bool hasScrollableContent = scrollHeight > 0f;
        if (scrollWindow.autoHideScrollbar && !hasScrollableContent)
        {
            scrollBar.gameObject.SetActive(false);
            return;
        }

        scrollBar.gameObject.SetActive(true);
        if (scrollBar.root != null)
        {
            scrollBar.root.SetActive(hasScrollableContent);
        }

        if (!hasScrollableContent || scrollBar.background == null || scrollBar.handle == null || scrollBar.handle.handleSpriteRenderer == null)
        {
            return;
        }

        float backgroundHeight = scrollBar.background.size.y;
        float visibleRatio = scrollWindow.windowHeight / (scrollWindow.windowHeight + scrollHeight);
        float handleHeight = Mathf.Max(backgroundHeight * visibleRatio, 0.625f);
        Vector2 handleSize = new(scrollBar.handle.handleSpriteRenderer.size.x, handleHeight);

        if (scrollBar.handle.handleSpritesToResize != null)
        {
            for (int i = 0; i < scrollBar.handle.handleSpritesToResize.Count; i++)
            {
                SpriteRenderer spriteRenderer = scrollBar.handle.handleSpritesToResize[i];
                if (spriteRenderer != null)
                {
                    spriteRenderer.size = handleSize;
                }
            }
        }

        if (scrollBar.handle.handleCollider != null)
        {
            Vector3 colliderSize = scrollBar.handle.handleCollider.size;
            colliderSize.y = handleHeight;
            scrollBar.handle.handleCollider.size = colliderSize;
        }

        float scrollRange = scrollHeight - scrollWindow.minScrollPos;
        float normalizedPosition = scrollRange <= 0.001f
            ? 1f
            : Mathf.Clamp01(1f - (scrollWindow.scrollingContent.localPosition.y - scrollWindow.minScrollPos) / scrollRange);
        scrollBar.UpdateScrollBarPosition(normalizedPosition);
    }

    internal void SetEntries(List<StorageTerminalItemEntry> entries, bool resetScroll)
    {
        StorageTerminalItemSlot selectedSlotBeforeRefresh = Manager.ui != null ? Manager.ui.currentSelectedUIElement as StorageTerminalItemSlot : null;
        StorageTerminalItemSlot.SelectionIdentity selectedIdentityBeforeRefresh = selectedSlotBeforeRefresh != null
            ? selectedSlotBeforeRefresh.Identity
            : default;

        _entries.Clear();
        _entries.AddRange(entries);
        EnsureSlotPoolSize(GetRequiredSlotCount());

        if (resetScroll)
        {
            _currentScroll = 0f;
            Vector3 localPosition = itemSlotsRoot.transform.localPosition;
            localPosition.y = 0f;
            itemSlotsRoot.transform.localPosition = localPosition;
        }
        else
        {
            ClampScrollToContent();
        }

        UpdateVisibleSlots();
        if (Manager.ui.currentSelectedUIElement is StorageTerminalItemSlot selectedSlot &&
            selectedSlot.transform.IsChildOf(itemSlotsRoot.transform) &&
            (!selectedSlot.gameObject.activeInHierarchy ||
             (selectedSlot == selectedSlotBeforeRefresh && !selectedSlot.Identity.Equals(selectedIdentityBeforeRefresh))))
        {
            Manager.ui.DeselectAnySelectedUIElement();
        }

        RefreshScrollPresentation();
    }

    public void UpdateContainingElements(float scroll)
    {
        if (Mathf.Abs(_currentScroll - scroll) <= 0.001f)
        {
            return;
        }

        _currentScroll = scroll;
        UpdateVisibleSlots();
    }

    public bool IsBottomElementSelected()
    {
        return Manager.ui.currentSelectedUIElement is StorageTerminalItemSlot slot &&
               slot.DataIndex >= _entries.Count - ColumnCount;
    }

    public bool IsTopElementSelected()
    {
        return Manager.ui.currentSelectedUIElement is StorageTerminalItemSlot slot &&
               slot.DataIndex >= 0 &&
               slot.DataIndex < ColumnCount;
    }

    public float GetCurrentWindowHeight()
    {
        if (_entries.Count == 0)
        {
            return 0f;
        }

        return Mathf.CeilToInt(_entries.Count / (float)ColumnCount) * spread;
    }

    internal void RequestWithdraw(StorageTerminalItemEntry entry, int amount)
    {
        _owner.RequestWithdraw(entry, amount);
    }

    private void AutoAssignReferences()
    {
        scrollWindow ??= GetComponent<UIScrollWindow>();

        if (itemSlotsRoot == null)
        {
            Transform itemsRootTransform = transform.Find("ItemsRoot");
            if (itemsRootTransform != null)
            {
                itemSlotsRoot = itemsRootTransform.gameObject;
            }
        }

        if (slotTemplate == null)
        {
            slotTemplate = transform.Find("SlotTemplate")?.GetComponent<StorageTerminalItemSlot>();
        }
    }

    private void UpdateVisibleSlots()
    {
        if (itemSlots == null)
        {
            return;
        }

        float sideStart = (0f - (ColumnCount - 1) / 2f) * spread;
        int startRow = Mathf.Max(0, Mathf.FloorToInt(_currentScroll / spread));
        int startIndex = startRow * ColumnCount;
        float virtualOffset = startRow * spread;
        bool hasPartialRowVisible = _currentScroll - virtualOffset > 0.001f;
        int visibleRowBudget = VisibleRowCount + (hasPartialRowVisible ? 1 : 0);
        int activeSlotCount = Mathf.Min(Mathf.Max(0, _entries.Count - startIndex), visibleRowBudget * ColumnCount);

        for (int i = 0; i < itemSlots.Count; i++)
        {
            StorageTerminalItemSlot slot = itemSlots[i] as StorageTerminalItemSlot;
            if (slot == null)
            {
                continue;
            }

            if (i >= activeSlotCount)
            {
                slot.gameObject.SetActive(false);
                continue;
            }

            int dataIndex = startIndex + i;
            if (dataIndex >= _entries.Count)
            {
                slot.gameObject.SetActive(false);
                continue;
            }

            int column = i % ColumnCount;
            int row = i / ColumnCount;

            slot.transform.localPosition = new Vector3(
                sideStart + column * spread,
                -row * spread - virtualOffset,
                0f);

            StorageTerminalItemEntry entry = _entries[dataIndex];
            if (!slot.IsBoundTo(entry, i, dataIndex))
            {
                slot.Bind(entry, this, i, dataIndex);
            }

            slot.gameObject.SetActive(true);
        }
    }

    private void EnsureSlotPoolSize(int requiredSlotCount)
    {
        if (slotTemplate == null || itemSlots == null)
        {
            return;
        }

        while (itemSlots.Count < requiredSlotCount)
        {
            int slotIndex = itemSlots.Count;
            StorageTerminalItemSlot slot = Instantiate(slotTemplate, itemSlotsRoot.transform);
            slot.name = $"Slot{slotIndex:00}";
            slot.visibleSlotIndex = slotIndex;
            slot.Init(this);
            slot.gameObject.SetActive(keepSlotsEnabledAfterInit);
            itemSlots.Add(slot);
        }

        RefreshSlotGridMetadata();
        firstSlot = itemSlots.Count > 0 ? itemSlots[0] : null;
    }

    private int GetRequiredSlotCount()
    {
        return Mathf.Min(_entries.Count, BufferedRowCount * ColumnCount);
    }

    private void ClampScrollToContent()
    {
        float maxScroll = Mathf.Max(0f, GetCurrentWindowHeight() - VisibleRowCount * spread);
        if (_currentScroll <= maxScroll)
        {
            return;
        }

        _currentScroll = maxScroll;
        Vector3 localPosition = itemSlotsRoot.transform.localPosition;
        localPosition.y = maxScroll;
        itemSlotsRoot.transform.localPosition = localPosition;
    }

    public void OnHotSyncApplied()
    {
        AutoAssignReferences();
        _authoredLocalScale = transform.localScale;
        if (itemSlotsRoot != null)
        {
            _authoredItemsRootScale = itemSlotsRoot.transform.localScale;
        }

        if (initDone)
        {
            visibleRows = VisibleRowCount;
            visibleColumns = ColumnCount;
            EnsureSlotPoolSize(GetRequiredSlotCount());
            RefreshSlotGridMetadata();
            ClampScrollToContent();
            UpdateVisibleSlots();
        }
    }

    private void RefreshSlotGridMetadata()
    {
        if (itemSlots == null)
        {
            return;
        }

        for (int i = 0; i < itemSlots.Count; i++)
        {
            if (itemSlots[i] == null)
            {
                continue;
            }

            itemSlots[i].uiSlotXPosition = i % MAX_COLUMNS;
            itemSlots[i].uiSlotYPosition = i / MAX_COLUMNS;
        }
    }
}
