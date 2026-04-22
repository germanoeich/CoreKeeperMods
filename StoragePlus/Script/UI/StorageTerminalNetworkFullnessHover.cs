using System.Collections.Generic;
using UnityEngine;

public sealed class StorageTerminalNetworkFullnessHover : UIelement, IStorageTerminalHotSyncAware
{
    private const float DefaultWarningPercentage = 90f;

    [SerializeField]
    private StorageTerminalUI owner;

    [SerializeField]
    private SpriteRenderer background;

    [SerializeField]
    private BoxCollider boxCollider;

    [SerializeField]
    private SpriteRenderer pixelSpriteRenderer;

    [SerializeField]
    private Color defaultStateColor = new(0.42f, 0.82f, 1f, 0.95f);

    [SerializeField]
    private Color warningStateColor = new(1f, 0.76f, 0.34f, 0.97f);

    [SerializeField]
    [Range(0f, 100f)]
    private float warningPercentage = DefaultWarningPercentage;

    private float _lastCapacityPercent = -1f;

    private void Reset()
    {
        AssignSerializedReferences();
    }

    private void OnValidate()
    {
        AssignSerializedReferences();
    }

    private void Awake()
    {
        StorageTerminalUIUtility.EnsureUiElementLists(this);
        AssignSerializedReferences();
    }

    public override TextAndFormatFields GetHoverTitle()
    {
        return owner?.GetNetworkFullnessHoverTitle();
    }

    public override List<TextAndFormatFields> GetHoverDescription()
    {
        return owner?.GetNetworkFullnessHoverDescription();
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
        RefreshPixelColor();
    }

    internal void SetCapacityPercent(float capacityPercent)
    {
        _lastCapacityPercent = Mathf.Clamp(capacityPercent, 0f, 100f);
        RefreshPixelColor();
    }

    private void AssignSerializedReferences()
    {
        owner ??= GetComponentInParent<StorageTerminalUI>(includeInactive: true);
        background ??= transform.Find("Background")?.GetComponent<SpriteRenderer>();
        boxCollider ??= GetComponent<BoxCollider>();
        pixelSpriteRenderer ??= transform.Find("FillScaleRoot/Fill")?.GetComponent<SpriteRenderer>();

        if (background == null || boxCollider == null)
        {
            return;
        }

        Vector3 backgroundPosition = background.transform.localPosition;
        Vector2 backgroundSize = background.size;
        boxCollider.center = new Vector3(backgroundPosition.x, backgroundPosition.y, -0.2f);
        boxCollider.size = new Vector3(backgroundSize.x, backgroundSize.y, 4f);
        RefreshPixelColor();
    }

    private void RefreshPixelColor()
    {
        if (pixelSpriteRenderer == null)
        {
            return;
        }

        float clampedWarningPercentage = Mathf.Clamp(warningPercentage, 0f, 100f);
        pixelSpriteRenderer.color = _lastCapacityPercent >= clampedWarningPercentage
            ? warningStateColor
            : defaultStateColor;
    }
}
