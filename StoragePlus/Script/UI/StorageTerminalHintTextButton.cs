using System.Collections.Generic;
using UnityEngine;

public sealed class StorageTerminalHintTextButton : ButtonUIElement, IStorageTerminalHotSyncAware
{
    [SerializeField]
    private GameObject stateMarker;

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
        AssignSerializedReferences();
        base.Awake();
        HideLabel();
        RefreshVisuals(force: true);
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        RefreshVisuals(force: false);
    }

    internal void Initialize(StorageTerminalUI storageTerminalUi)
    {
        owner = storageTerminalUi;
        adjustSpritesToFitTextSize = false;
        showHoverTitle = false;
        showHoverDesc = false;
        playClickSoundEffect = false;
        HideLabel();
        RefreshVisuals(force: true);
    }

    public override void OnLeftClicked(bool mod1, bool mod2)
    {
        base.OnLeftClicked(mod1, mod2);
        owner?.ToggleInteractionHints();
    }

    public override TextAndFormatFields GetHoverTitle()
    {
        return new TextAndFormatFields
        {
            text = owner != null && owner.ShowInteractionHints ? "Hide hints" : "Show hints",
            dontLocalize = true
        };
    }

    public override List<TextAndFormatFields> GetHoverDescription()
    {
        return owner?.GetHintTextButtonHoverDescription();
    }

    public override HoverWindowAlignment GetHoverWindowAlignment()
    {
        return Manager.input.SystemPrefersKeyboardAndMouse()
            ? HoverWindowAlignment.BOTTOM_RIGHT_OF_CURSOR
            : HoverWindowAlignment.BOTTOM_RIGHT_OF_SCREEN;
    }

    public void OnHotSyncApplied()
    {
        AssignSerializedReferences();
        HideLabel();
        RefreshVisuals(force: true);
    }

    internal void RefreshVisuals(bool force)
    {
        if (stateMarker == null || owner == null)
        {
            return;
        }

        bool hasKeyboardFocus = Manager.ui != null && Manager.ui.currentSelectedUIElement == this;
        bool shouldShowMarker = owner.ShowInteractionHints || hasKeyboardFocus;
        if (force || stateMarker.activeSelf != shouldShowMarker)
        {
            stateMarker.SetActive(shouldShowMarker);
        }
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
        stateMarker ??= transform.Find("selectedMarker")?.gameObject;
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
