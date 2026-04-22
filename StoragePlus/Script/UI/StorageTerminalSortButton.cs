using System.Collections.Generic;
using CoreLib.Submodule.UserInterface.Component;
using UnityEngine;

internal enum StorageTerminalSortButtonMode
{
    Sorter,
    Order
}

public sealed class StorageTerminalSortButton : ButtonUIElement, IStorageTerminalHotSyncAware
{
    [SerializeField]
    private Sprite sortSprite;

    [SerializeField]
    private Sprite ascendingSprite;

    [SerializeField]
    private Sprite descendingSprite;

    [SerializeField]
    private List<SpriteRenderer> spritesToUpdate = new();

    private StorageTerminalUI owner;

    private StorageTerminalSortButtonMode mode;

    private Sprite _lastRenderedSprite;

    protected override void Awake()
    {
        StorageTerminalUIUtility.EnsureUiElementLists(this);
        base.Awake();
        if (text != null)
        {
            text.gameObject.SetActive(false);
        }

        RefreshVisuals(force: true);
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        RefreshVisuals(force: false);
    }

    internal void Initialize(StorageTerminalUI storageTerminalUi, StorageTerminalSortButtonMode buttonMode)
    {
        owner = storageTerminalUi;
        mode = buttonMode;
        adjustSpritesToFitTextSize = false;
        RefreshVisuals(force: true);
    }

    public override void OnLeftClicked(bool mod1, bool mod2)
    {
        if (owner == null)
        {
            return;
        }

        switch (mode)
        {
            case StorageTerminalSortButtonMode.Sorter:
                owner.NextSort();
                break;
            case StorageTerminalSortButtonMode.Order:
                owner.ToggleSortOrder();
                break;
        }
    }

    public override void OnRightClicked(bool mod1, bool mod2)
    {
        if (owner == null)
        {
            return;
        }

        switch (mode)
        {
            case StorageTerminalSortButtonMode.Sorter:
                owner.PrevSort();
                break;
            case StorageTerminalSortButtonMode.Order:
                owner.ToggleSortOrder();
                break;
        }
    }

    public override TextAndFormatFields GetHoverTitle()
    {
        return new TextAndFormatFields
        {
            text = mode == StorageTerminalSortButtonMode.Sorter ? "Sort" : "Sort order",
            dontLocalize = true
        };
    }

    public override List<TextAndFormatFields> GetHoverDescription()
    {
        if (owner == null)
        {
            return null;
        }

        List<TextAndFormatFields> lines = owner.GetSortButtonHoverDescription(mode) ?? new List<TextAndFormatFields>();
        owner.AppendInteractionHints(
            lines,
            mode == StorageTerminalSortButtonMode.Sorter
                ? owner.CreateInteractionHintLine("Next sort", "UIInteract")
                : owner.CreateInteractionHintLine("Toggle sort order", "UIInteract"),
            mode == StorageTerminalSortButtonMode.Sorter
                ? owner.CreateInteractionHintLine("Previous sort", "UISecondInteract")
                : owner.CreateInteractionHintLine("Toggle sort order", "UISecondInteract"));
        return lines;
    }

    public override HoverWindowAlignment GetHoverWindowAlignment()
    {
        return Manager.input.SystemPrefersKeyboardAndMouse()
            ? HoverWindowAlignment.BOTTOM_RIGHT_OF_CURSOR
            : HoverWindowAlignment.BOTTOM_RIGHT_OF_SCREEN;
    }

    public void OnHotSyncApplied()
    {
        RefreshVisuals(force: true);
    }

    private void RefreshVisuals(bool force)
    {
        if (owner == null)
        {
            return;
        }

        Sprite nextSprite = mode == StorageTerminalSortButtonMode.Sorter
            ? sortSprite
            : (owner.UseReverseSorting ? descendingSprite : ascendingSprite);

        if (!force && ReferenceEquals(_lastRenderedSprite, nextSprite))
        {
            return;
        }

        _lastRenderedSprite = nextSprite;
        for (int i = 0; i < spritesToUpdate.Count; i++)
        {
            if (spritesToUpdate[i] != null)
            {
                spritesToUpdate[i].sprite = nextSprite;
            }
        }
    }
}
