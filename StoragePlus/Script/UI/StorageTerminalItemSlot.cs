using System.Collections.Generic;
using PugMod;
using UnityEngine;

public sealed class StorageTerminalItemSlot : SlotUIBase, IStorageTerminalHotSyncAware
{
    internal readonly struct SelectionIdentity
    {
        public readonly ObjectID ObjectId;
        public readonly int Variation;
        public readonly ulong EntryId;
        public readonly StorageTerminalSummaryEntryFlags Flags;

        public SelectionIdentity(ObjectID objectId, int variation, ulong entryId, StorageTerminalSummaryEntryFlags flags)
        {
            ObjectId = objectId;
            Variation = variation;
            EntryId = entryId;
            Flags = flags;
        }

        public readonly bool Equals(in SelectionIdentity other)
        {
            return ObjectId == other.ObjectId &&
                   Variation == other.Variation &&
                   EntryId == other.EntryId &&
                   Flags == other.Flags;
        }
    }

    public ColorReplacer colorReplacer;

    [SerializeField]
    private Sprite commonHighlightBorderSprite;

    [SerializeField]
    private Sprite uncommonHighlightBorderSprite;

    [SerializeField]
    private Sprite rareHighlightBorderSprite;

    [SerializeField]
    private Sprite epicHighlightBorderSprite;

    [SerializeField]
    private Sprite legendaryHighlightBorderSprite;

    private StorageTerminalItemEntry _entry;
    private Vector3 _authoredLocalScale = Vector3.one;
    private Color _authoredBackgroundColor = Color.white;
    private Color _authoredHighlightBorderColor = Color.white;
    private Sprite _authoredHighlightBorderSprite;

    public int DataIndex { get; private set; } = -1;

    internal SelectionIdentity Identity => new(_entry.ObjectId, _entry.Variation, _entry.EntryId, _entry.Flags);

    public override UIScrollWindow uiScrollWindow => slotsUIContainer?.scrollWindow;

    public override float localScrollPosition => transform.localPosition.y;

    public override bool isVisibleOnScreen
    {
        get
        {
            if (uiScrollWindow != null && !uiScrollWindow.IsShowingPosition(localScrollPosition, background.size.y * 0.5f))
            {
                return false;
            }

            return base.isVisibleOnScreen;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        _authoredLocalScale = transform.localScale;
        if (background != null)
        {
            _authoredBackgroundColor = background.color;
        }

        if (highlightBorder != null)
        {
            _authoredHighlightBorderColor = highlightBorder.color;
            _authoredHighlightBorderSprite = highlightBorder.sprite;
        }
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        transform.localScale = _authoredLocalScale;
    }

    private void Reset()
    {
        StorageTerminalUIUtility.EnsureUiElementLists(this);
    }

    internal void Bind(StorageTerminalItemEntry entry, StorageTerminalGrid grid, int visibleSlotIndex, int dataIndex)
    {
        StorageTerminalUIUtility.EnsureUiElementLists(this);
        bool shouldShowHoverBorder = Manager.ui != null && Manager.ui.currentSelectedUIElement == this;

        _entry = entry;
        slotsUIContainer = grid;
        DataIndex = dataIndex;
        this.visibleSlotIndex = visibleSlotIndex;
        StorageTerminalUIUtility.ApplyVanillaItemIconMaterial(icon);

        if (background != null)
        {
            background.color = _authoredBackgroundColor;
        }

        if (highlightBorder != null)
        {
            highlightBorder.color = _authoredHighlightBorderColor;
            highlightBorder.sprite = _authoredHighlightBorderSprite;
        }

        ContainedObjectsBuffer containedObject = entry.CreateContainedObject();
        if (!PugDatabase.TryGetObjectInfo(entry.ObjectId, out ObjectInfo objectInfo, entry.Variation) || objectInfo.icon == null)
        {
            SetMissingIcon();
            if (Manager.ui != null && icon != null)
            {
                Manager.ui.ApplyAnyIconGradientMap(default, icon);
            }
        }
        else
        {
            Sprite iconOverride = Manager.ui.itemOverridesTable.GetIconOverride(containedObject.objectData, getSmallIcon: false);
            icon.sprite = iconOverride != null ? iconOverride : objectInfo.icon;
            icon.transform.localPosition = objectInfo.iconOffset;
            icon.transform.localScale = Vector3.one;
            icon.color = Color.white;
            if (colorReplacer != null)
            {
                colorReplacer.UpdateColorReplacerFromObjectData(containedObject);
            }

            Manager.ui.ApplyAnyIconGradientMap(containedObject, icon);
            if (background != null)
            {
                background.color = _authoredBackgroundColor;
            }

            if (highlightBorder != null)
            {
                highlightBorder.sprite = GetHighlightBorderSprite(objectInfo.rarity);
                highlightBorder.color = new Color(1f, 1f, 1f, _authoredHighlightBorderColor.a);
            }
        }

        if (amountNumber != null)
        {
            amountNumber.gameObject.SetActive(entry.ShouldShowAmountNumber);
            if (entry.ShouldShowAmountNumber)
            {
                amountNumber.localize = false;
                amountNumber.Render(entry.TotalAmount.ToString(), force: true);
            }
        }

        if (amountNumberShadow != null)
        {
            amountNumberShadow.gameObject.SetActive(entry.ShouldShowAmountNumber);
            if (entry.ShouldShowAmountNumber)
            {
                amountNumberShadow.localize = false;
                amountNumberShadow.Render(entry.TotalAmount.ToString(), force: true);
            }
        }

        if (underlayIcon != null)
        {
            underlayIcon.gameObject.SetActive(false);
        }

        if (overlayIcon != null)
        {
            overlayIcon.gameObject.SetActive(false);
        }

        if (tiledDecorationBackground != null)
        {
            tiledDecorationBackground.gameObject.SetActive(false);
        }

        if (upgradeIcon != null)
        {
            upgradeIcon.gameObject.SetActive(false);
        }

        if (hoverBorder != null)
        {
            hoverBorder.gameObject.SetActive(shouldShowHoverBorder);
        }

        if (highlightBorder != null)
        {
            highlightBorder.gameObject.SetActive(true);
        }

        if (highlight != null)
        {
            highlight.SetActive(false);
        }
    }

    public override void OnSelected()
    {
        uiScrollWindow?.MoveScrollToIncludePosition(localScrollPosition, background.size.y * 0.5f);
        OnSelectSlot();
    }

    public override void OnDeselected(bool playEffect = true)
    {
        OnDeselectSlot();
    }

    public override void OnSetSlotActive()
    {
    }

    public override void OnSetSlotInactivate()
    {
    }

    public override void OnLeftClicked(bool mod1, bool mod2)
    {
        int amount = mod1 ? 10 : 1;
        RequestWithdraw(Mathf.Min(amount, _entry.TotalAmount));
    }

    public override void OnRightClicked(bool mod1, bool mod2)
    {
        RequestWithdraw(int.MaxValue);
    }

    private Sprite GetHighlightBorderSprite(Rarity rarity)
    {
        return rarity switch
        {
            Rarity.Poor => commonHighlightBorderSprite ?? _authoredHighlightBorderSprite,
            Rarity.Common => commonHighlightBorderSprite ?? _authoredHighlightBorderSprite,
            Rarity.Uncommon => uncommonHighlightBorderSprite ?? commonHighlightBorderSprite ?? _authoredHighlightBorderSprite,
            Rarity.Rare => rareHighlightBorderSprite ?? uncommonHighlightBorderSprite ?? commonHighlightBorderSprite ?? _authoredHighlightBorderSprite,
            Rarity.Epic => epicHighlightBorderSprite ?? rareHighlightBorderSprite ?? uncommonHighlightBorderSprite ?? commonHighlightBorderSprite ?? _authoredHighlightBorderSprite,
            Rarity.Legendary => legendaryHighlightBorderSprite ?? epicHighlightBorderSprite ?? rareHighlightBorderSprite ?? uncommonHighlightBorderSprite ?? commonHighlightBorderSprite ?? _authoredHighlightBorderSprite,
            _ => commonHighlightBorderSprite ?? _authoredHighlightBorderSprite
        };
    }

    public override TextAndFormatFields GetHoverTitle()
    {
        TextAndFormatFields objectName = PlayerController.GetObjectName(GetSlotObject(), localize: false) ?? new TextAndFormatFields
        {
            text = _entry.DisplayName,
            dontLocalize = true
        };

        if (PugDatabase.TryGetObjectInfo(_entry.ObjectId, out ObjectInfo objectInfo, _entry.Variation))
        {
            objectName.color = Manager.text.GetRarityColor(objectInfo.rarity);
        }

        return objectName;
    }

    public override List<TextAndFormatFields> GetHoverDescription()
    {
        if (!API.Authoring.ObjectProperties.TryGetPropertyString(_entry.ObjectId, "name", out string objectName))
        {
            objectName = _entry.ObjectId.ToString();
        }

        string nameTermOverride = Manager.ui.itemOverridesTable.GetNameTermOverride(new ObjectDataCD
        {
            objectID = _entry.ObjectId,
            variation = _entry.Variation
        });

        if (nameTermOverride != null)
        {
            objectName = nameTermOverride;
        }

        List<TextAndFormatFields> lines = new()
        {
            new()
            {
                text = "Items/" + objectName + "Desc"
            }
        };

        StorageTerminalUI owner = (slotsUIContainer as StorageTerminalGrid)?.Owner;
        owner?.AppendInteractionHints(
            lines,
            owner.CreateInteractionHintLine("Fetch 1", "UIInteract"),
            owner.CreateInteractionHintLine("Fetch stack", "UISecondInteract"),
            owner.CreateInteractionHintLine("Fetch 10", "HotbarSwapModifier", "UIInteract"));

        return lines;
    }

    public override List<TextAndFormatFields> GetHoverStats(bool previewReinforced)
    {
        return GetHoverStats(GetSlotObject(), previewReinforced, previewUpgraded: false);
    }

    public override HoverWindowAlignment GetHoverWindowAlignment()
    {
        return Manager.input.SystemPrefersKeyboardAndMouse()
            ? HoverWindowAlignment.BOTTOM_RIGHT_OF_CURSOR
            : HoverWindowAlignment.BOTTOM_RIGHT_OF_SCREEN;
    }

    public override bool GetDurabilityOrFullnessOrXp(out int durability, out int maxDurability, out AmountType amountType)
    {
        return base.GetDurabilityOrFullnessOrXp(out durability, out maxDurability, out amountType);
    }

    public override bool GetLevel(out int level, out bool isMaxLevel)
    {
        return base.GetLevel(out level, out isMaxLevel);
    }

    protected override ContainedObjectsBuffer GetSlotObject()
    {
        return _entry.CreateContainedObject();
    }

    private void RequestWithdraw(int amount)
    {
        if (_entry.ObjectId == ObjectID.None || amount <= 0 || slotsUIContainer is not StorageTerminalGrid grid)
        {
            return;
        }

        grid.RequestWithdraw(_entry, amount);
        AudioManager.Sfx(SfxID.twitch, transform.position, 0.1f, 0.55f, 0.1f, reuse: true);
        SetAnimationTrigger(-1023692900);
        if (Manager.ui != null && Manager.input.SystemPrefersKeyboardAndMouse() && Manager.ui.currentSelectedUIElement == this)
        {
            Manager.ui.DeselectAnySelectedUIElement();
        }
    }

    public void OnHotSyncApplied()
    {
        _authoredLocalScale = transform.localScale;
        StorageTerminalUIUtility.ApplyVanillaItemIconMaterial(icon);
        if (background != null)
        {
            _authoredBackgroundColor = background.color;
        }

        if (highlightBorder != null)
        {
            _authoredHighlightBorderColor = highlightBorder.color;
            _authoredHighlightBorderSprite = highlightBorder.sprite;
        }
    }
}
