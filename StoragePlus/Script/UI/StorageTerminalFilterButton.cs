using System.Collections.Generic;
using UnityEngine;

public sealed class StorageTerminalFilterButton : ButtonUIElement, IStorageTerminalHotSyncAware
{
    private enum ButtonMode
    {
        Category,
        Reset
    }

    private static readonly Color ActiveBackgroundColor = new(0.8f, 0.9f, 1f, 1f);
    private static readonly Color ActiveBorderColor = new(0.92f, 0.97f, 1f, 1f);

    private StorageTerminalUI owner;
    private StorageTerminalItemCategory category;
    private ButtonMode mode;
    private Sprite resetIcon;

    [SerializeField]
    private SpriteRenderer background;

    [SerializeField]
    private SpriteRenderer border;

    [SerializeField]
    private GameObject hoverMarker;

    [SerializeField]
    private SpriteRenderer hoverMarkerRenderer;

    [SerializeField]
    private GameObject activeBorder;

    [SerializeField]
    private SpriteRenderer activeBorderRenderer;

    private Color _backgroundDefaultColor;
    private Color _borderDefaultColor;
    private bool _hasVisualCache;

    private void Reset()
    {
        AssignSerializedReferences();
    }

    private void OnValidate()
    {
        AssignSerializedReferences();
    }

    protected override void Awake()
    {
        StorageTerminalUIUtility.EnsureUiElementLists(this);
        CacheDefaultColors();
        base.Awake();
        RefreshVisuals(force: true);
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        RefreshVisuals(force: false);
    }

    internal void Initialize(StorageTerminalUI storageTerminalUi, StorageTerminalItemCategory filterCategory)
    {
        owner = storageTerminalUi;
        category = filterCategory;
        mode = ButtonMode.Category;
        resetIcon = null;

        adjustSpritesToFitTextSize = false;
        showHoverTitle = false;
        showHoverDesc = false;
        playClickSoundEffect = false;
        gameObject.name = $"{StorageTerminalItemCategoryUtility.GetDisplayName(category)}Filter";

        if (text != null)
        {
            text.gameObject.SetActive(false);
        }

        GetComponent<StorageTerminalFilterIconAuthoring>()?.Apply(category);

        RefreshVisuals(force: true);
    }

    internal void InitializeReset(StorageTerminalUI storageTerminalUi, Sprite manualIcon)
    {
        owner = storageTerminalUi;
        category = StorageTerminalItemCategory.All;
        mode = ButtonMode.Reset;
        resetIcon = manualIcon;

        adjustSpritesToFitTextSize = false;
        showHoverTitle = false;
        showHoverDesc = false;
        playClickSoundEffect = false;
        gameObject.name = "ResetFiltersButton";

        if (text != null)
        {
            text.gameObject.SetActive(false);
        }

        GetComponent<StorageTerminalFilterIconAuthoring>()?.ApplyManualSprite(resetIcon);
        RefreshVisuals(force: true);
    }

    internal StorageTerminalItemCategory Category => category;

    internal void RefreshIcon(StorageTerminalFilterIconAuthoring templateIconAuthoring)
    {
        StorageTerminalFilterIconAuthoring iconAuthoring = GetComponent<StorageTerminalFilterIconAuthoring>();
        if (iconAuthoring == null)
        {
            return;
        }

        if (mode == ButtonMode.Reset)
        {
            iconAuthoring.ApplyManualSprite(resetIcon);
            return;
        }

        iconAuthoring.CopyConfigurationFrom(templateIconAuthoring);
        iconAuthoring.Apply(category);
    }

    public override void OnLeftClicked(bool mod1, bool mod2)
    {
        base.OnLeftClicked(mod1, mod2);
        if (owner == null)
        {
            return;
        }

        if (mode == ButtonMode.Reset)
        {
            owner.ResetCategoryFilters();
        }
        else
        {
            owner.ToggleCategoryFilter(category);
        }
    }

    public override TextAndFormatFields GetHoverTitle()
    {
        return new TextAndFormatFields
        {
            text = mode == ButtonMode.Reset ? "Reset filters" : StorageTerminalItemCategoryUtility.GetDisplayName(category),
            dontLocalize = true
        };
    }

    public override List<TextAndFormatFields> GetHoverDescription()
    {
        if (owner == null)
        {
            return null;
        }

        List<TextAndFormatFields> lines = new();
        if (mode == ButtonMode.Reset)
        {
            if (owner.HasAnyCategoryFiltersSelected())
            {
                lines.Add(new TextAndFormatFields
                {
                    text = "Clear all active filters.",
                    dontLocalize = true,
                    color = Color.white * 0.99f
                });
            }
            else
            {
                lines.Add(new TextAndFormatFields
                {
                    text = "No filters are active.",
                    dontLocalize = true,
                    color = Color.white * 0.99f
                });
            }

            owner.AppendInteractionHints(lines, owner.CreateInteractionHintLine("Reset all filters", "UIInteract"));
            return lines;
        }

        if (owner.IsCategorySelected(category))
        {
            lines.Add(new TextAndFormatFields
            {
                text = "Filter active",
                dontLocalize = true,
                color = Color.white * 0.99f
            });
        }

        owner.AppendInteractionHints(
            lines,
            owner.CreateInteractionHintLine(owner.IsCategorySelected(category) ? "Remove filter" : "Add filter", "UIInteract"));
        return lines.Count > 0 ? lines : null;
    }

    public override HoverWindowAlignment GetHoverWindowAlignment()
    {
        return Manager.input.SystemPrefersKeyboardAndMouse()
            ? HoverWindowAlignment.BOTTOM_RIGHT_OF_CURSOR
            : HoverWindowAlignment.BOTTOM_RIGHT_OF_SCREEN;
    }

    public void OnHotSyncApplied()
    {
        CacheDefaultColors();
        StorageTerminalFilterIconAuthoring iconAuthoring = GetComponent<StorageTerminalFilterIconAuthoring>();
        if (mode == ButtonMode.Reset)
        {
            iconAuthoring?.ApplyManualSprite(resetIcon);
        }
        else
        {
            iconAuthoring?.Apply(category);
        }

        RefreshVisuals(force: true);
    }

    internal void ApplyCellSize(Vector2 cellSize)
    {
        BoxCollider collider = GetComponent<BoxCollider>();
        if (collider != null)
        {
            collider.size = new Vector3(cellSize.x, cellSize.y, 1f);
            collider.center = Vector3.zero;
        }

        ApplySpriteSize(background, cellSize);
        ApplySpriteSize(border, cellSize);
        ApplySpriteSize(hoverMarkerRenderer, cellSize);
        ApplySpriteSize(activeBorderRenderer, cellSize);
    }

    internal void RefreshVisuals(bool force)
    {
        if (!_hasVisualCache || owner == null)
        {
            return;
        }

        bool isSelected = mode == ButtonMode.Reset
            ? owner.HasAnyCategoryFiltersSelected()
            : owner.IsCategorySelected(category);
        if (background != null)
        {
            background.color = isSelected ? ActiveBackgroundColor : _backgroundDefaultColor;
        }

        if (border != null)
        {
            border.color = isSelected ? ActiveBorderColor : _borderDefaultColor;
        }

        if (activeBorder != null)
        {
            if (force || activeBorder.activeSelf != isSelected)
            {
                activeBorder.SetActive(isSelected);
            }
        }

        if (hoverMarker != null)
        {
            bool hasKeyboardFocus = Manager.ui != null && Manager.ui.currentSelectedUIElement == this;
            if (force || hoverMarker.activeSelf != hasKeyboardFocus)
            {
                hoverMarker.SetActive(hasKeyboardFocus);
            }
        }
    }

    private void AssignSerializedReferences()
    {
        background ??= transform.Find("Background")?.GetComponent<SpriteRenderer>();
        border ??= transform.Find("border")?.GetComponent<SpriteRenderer>();
        hoverMarker ??= transform.Find("hoverBorder")?.gameObject;
        hoverMarkerRenderer ??= hoverMarker != null ? hoverMarker.GetComponent<SpriteRenderer>() : null;
        activeBorder ??= transform.Find("activeBorder")?.gameObject;
        activeBorderRenderer ??= activeBorder != null ? activeBorder.GetComponent<SpriteRenderer>() : null;
        text ??= transform.Find("text")?.GetComponent<PugText>();
        optionalSelectedMarker ??= hoverMarker;
        spritesShownUnpressed ??= new List<SpriteRenderer>();
        spritesShownPressed ??= new List<SpriteRenderer>();
    }

    private static void ApplySpriteSize(SpriteRenderer spriteRenderer, Vector2 cellSize)
    {
        if (spriteRenderer == null)
        {
            return;
        }

        spriteRenderer.size = cellSize;
    }

    private void CacheDefaultColors()
    {
        if (!_hasVisualCache && background != null && border != null)
        {
            _backgroundDefaultColor = background.color;
            _borderDefaultColor = border.color;
            _hasVisualCache = true;
        }
    }
}
