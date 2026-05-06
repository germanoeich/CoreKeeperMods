using System.Collections.Generic;
using UnityEngine;

public sealed partial class StorageTerminalUI
{
    [SerializeField]
    private Transform networkFullnessBarRoot;

    [SerializeField]
    private Transform networkFullnessBarFillScaleRoot;

    [SerializeField]
    private StorageTerminalNetworkFullnessHover networkFullnessBarHover;

    [SerializeField]
    private SpriteRenderer networkFullnessBarBackground;

    [SerializeField]
    private SpriteRenderer networkFullnessBarFill;

    [SerializeField]
    private SpriteRenderer networkFullnessBarFrame;

    private int _lastUsedSlotCount = -1;
    private int _lastTotalSlotCount = -1;
    private int _lastTotalItemCount = -1;
    private Vector3 _networkFullnessBarFillScaleRootAuthoredScale = Vector3.one;
    private bool _hasNetworkFullnessBarFillScaleRootAuthoredScale;
    private readonly TextAndFormatFields _networkFullnessHoverTitle = new()
    {
        text = "Network usage",
        dontLocalize = true
    };
    private readonly TextAndFormatFields _networkFullnessSlotsLine = new()
    {
        dontLocalize = true,
        color = Color.white * 0.99f,
        paddingBeneath = 0.125f
    };
    private readonly TextAndFormatFields _networkFullnessItemsLine = new()
    {
        dontLocalize = true,
        color = Color.white * 0.95f
    };
    private readonly List<TextAndFormatFields> _networkFullnessHoverDescription = new(2);
    private int _lastRenderedNetworkFullnessSlots = -1;
    private int _lastRenderedNetworkFullnessTotalSlots = -1;
    private int _lastRenderedNetworkFullnessItems = -1;

    private void ResetNetworkFullnessCache()
    {
        _lastUsedSlotCount = -1;
        _lastTotalSlotCount = -1;
        _lastTotalItemCount = -1;
    }

    private void RefreshNetworkFullnessBar(int usedSlotCount, int totalSlotCount, bool available)
    {
        EnsureNetworkFullnessBarBuilt();
        if (networkFullnessBarRoot == null ||
            networkFullnessBarFillScaleRoot == null ||
            networkFullnessBarBackground == null ||
            networkFullnessBarFill == null ||
            networkFullnessBarFrame == null)
        {
            return;
        }

        bool shouldShow = available && totalSlotCount > 0;
        if (networkFullnessBarRoot.gameObject.activeSelf != shouldShow)
        {
            networkFullnessBarRoot.gameObject.SetActive(shouldShow);
        }

        if (!shouldShow)
        {
            return;
        }

        float ratio = Mathf.Clamp01((float)usedSlotCount / totalSlotCount);
        if (!_hasNetworkFullnessBarFillScaleRootAuthoredScale)
        {
            _networkFullnessBarFillScaleRootAuthoredScale = networkFullnessBarFillScaleRoot.localScale;
            _hasNetworkFullnessBarFillScaleRootAuthoredScale = true;
        }

        Vector3 fillScale = _networkFullnessBarFillScaleRootAuthoredScale;
        fillScale.y *= ratio;
        networkFullnessBarFillScaleRoot.localScale = fillScale;
        networkFullnessBarHover?.SetCapacityPercent(ratio * 100f);
        RefreshNetworkFullnessHoverText();
    }

    private void EnsureNetworkFullnessBarBuilt()
    {
        if (networkFullnessBarRoot != null &&
            networkFullnessBarFillScaleRoot != null &&
            networkFullnessBarBackground != null &&
            networkFullnessBarFill != null &&
            networkFullnessBarFrame != null)
        {
            return;
        }

        Transform buttonsAnchor = (layoutRoot != null ? layoutRoot : transform).Find("ButtonsAnchor");
        if (buttonsAnchor == null)
        {
            return;
        }

        networkFullnessBarRoot ??= buttonsAnchor.Find("NetworkFullnessBar");
        networkFullnessBarFillScaleRoot ??= networkFullnessBarRoot?.Find("FillScaleRoot");
        networkFullnessBarHover ??= networkFullnessBarRoot?.GetComponent<StorageTerminalNetworkFullnessHover>();
        networkFullnessBarBackground ??= networkFullnessBarRoot?.Find("Background")?.GetComponent<SpriteRenderer>();
        networkFullnessBarFill ??= networkFullnessBarFillScaleRoot?.Find("Fill")?.GetComponent<SpriteRenderer>();
        networkFullnessBarFrame ??= networkFullnessBarRoot?.Find("Frame")?.GetComponent<SpriteRenderer>();

        if (networkFullnessBarRoot == null ||
            networkFullnessBarFillScaleRoot == null ||
            networkFullnessBarHover == null ||
            networkFullnessBarBackground == null ||
            networkFullnessBarFill == null ||
            networkFullnessBarFrame == null)
        {
            networkFullnessBarRoot = null;
            networkFullnessBarFillScaleRoot = null;
            networkFullnessBarHover = null;
            networkFullnessBarBackground = null;
            networkFullnessBarFill = null;
            networkFullnessBarFrame = null;
        }
        else if (!_hasNetworkFullnessBarFillScaleRootAuthoredScale)
        {
            _networkFullnessBarFillScaleRootAuthoredScale = networkFullnessBarFillScaleRoot.localScale;
            _hasNetworkFullnessBarFillScaleRootAuthoredScale = true;
        }
    }

    internal TextAndFormatFields GetNetworkFullnessHoverTitle()
    {
        return _networkFullnessHoverTitle;
    }

    internal List<TextAndFormatFields> GetNetworkFullnessHoverDescription()
    {
        RefreshNetworkFullnessHoverText();
        return _networkFullnessHoverDescription;
    }

    private void RefreshNetworkFullnessHoverText()
    {
        if (_networkFullnessHoverDescription.Count == 0)
        {
            _networkFullnessHoverDescription.Add(_networkFullnessSlotsLine);
            _networkFullnessHoverDescription.Add(_networkFullnessItemsLine);
        }

        int occupiedSlots = Mathf.Max(0, _lastUsedSlotCount);
        int totalSlots = Mathf.Max(0, _lastTotalSlotCount);
        int totalItems = Mathf.Max(0, _lastTotalItemCount);
        if (occupiedSlots == _lastRenderedNetworkFullnessSlots &&
            totalSlots == _lastRenderedNetworkFullnessTotalSlots &&
            totalItems == _lastRenderedNetworkFullnessItems)
        {
            return;
        }

        _networkFullnessSlotsLine.text = $"{occupiedSlots}/{totalSlots} slots";
        _networkFullnessItemsLine.text = $"Total items: {totalItems}";
        _lastRenderedNetworkFullnessSlots = occupiedSlots;
        _lastRenderedNetworkFullnessTotalSlots = totalSlots;
        _lastRenderedNetworkFullnessItems = totalItems;
    }
}
