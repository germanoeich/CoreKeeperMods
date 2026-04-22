using System.Collections.Generic;
using UnityEngine;

public sealed class StorageTerminalShowFiltersButton : ButtonUIElement, IStorageTerminalHotSyncAware
{
    [SerializeField]
    private GameObject filtersPanel;

    private StorageTerminalUI owner;

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
        base.Awake();
        HideLabel();
    }

    public override void OnLeftClicked(bool mod1, bool mod2)
    {
        if (filtersPanel == null)
        {
            return;
        }

        base.OnLeftClicked(mod1, mod2);

        bool shouldShow = !filtersPanel.activeSelf;
        filtersPanel.SetActive(shouldShow);
        if (!shouldShow &&
            Manager.ui != null &&
            Manager.ui.currentSelectedUIElement != null &&
            Manager.ui.currentSelectedUIElement.transform.IsChildOf(filtersPanel.transform))
        {
            Manager.ui.DeselectAnySelectedUIElement();
        }

        owner?.OnFiltersPanelVisibilityChanged();
    }

    public override TextAndFormatFields GetHoverTitle()
    {
        return new TextAndFormatFields
        {
            text = filtersPanel != null && filtersPanel.activeSelf ? "Hide filters" : "Show filters",
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
        owner.AppendInteractionHints(lines, owner.CreateInteractionHintLine("Toggle filters", "UIInteract"));
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
        HideLabel();
    }

    private void HideLabel()
    {
        if (text != null)
        {
            text.gameObject.SetActive(false);
        }
    }

    private void AssignSerializedReferences()
    {
        owner ??= GetComponentInParent<StorageTerminalUI>(includeInactive: true);
        filtersPanel ??= transform.parent?.Find("FiltersPanel")?.gameObject;
        optionalSelectedMarker ??= transform.Find("selectedMarker")?.gameObject;
        text ??= transform.Find("text")?.GetComponent<PugText>();

        spritesShownUnpressed ??= new List<SpriteRenderer>();
        spritesShownPressed ??= new List<SpriteRenderer>();

        AssignSpriteList(
            spritesShownUnpressed,
            transform.Find("Background")?.GetComponent<SpriteRenderer>(),
            transform.Find("Border")?.GetComponent<SpriteRenderer>());
        AssignSpriteList(
            spritesShownPressed,
            transform.Find("pressedMarker")?.GetComponent<SpriteRenderer>());
    }

    private static void AssignSpriteList(List<SpriteRenderer> target, params SpriteRenderer[] values)
    {
        target.Clear();
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] != null)
            {
                target.Add(values[i]);
            }
        }
    }
}
