using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class StorageTerminalUI
{
    [NonSerialized]
    private bool _showInteractionHints = true;

    internal bool ShowInteractionHints => _showInteractionHints;

    internal void ToggleInteractionHints()
    {
        _showInteractionHints = !_showInteractionHints;
        RefreshHintButtonVisuals();
    }

    internal TextAndFormatFields CreateInteractionHintLine(string description, params PlayerInput.InputType[] bindingActions)
    {
        return !_showInteractionHints
            ? null
            : StorageTerminalUIUtility.CreateInteractionHintLine(description, StorageTerminalUIUtility.ShouldPreferJoystickHints(), bindingActions);
    }

    internal TextAndFormatFields CreateSecondaryInteractionHintLine(string description)
    {
        return CreateInteractionHintLine(description, StorageTerminalUIUtility.IsUsingController()
            ? ControllerSecondaryAction
            : PlayerInput.InputType.UI_SECOND_INTERACT);
    }

    internal TextAndFormatFields CreateFetchTenInteractionHintLine()
    {
        return StorageTerminalUIUtility.IsUsingController()
            ? CreateInteractionHintLine("Fetch 10", ControllerFetchTenAction)
            : CreateInteractionHintLine("Fetch 10", PlayerInput.InputType.PICK_UP_10, PlayerInput.InputType.UI_INTERACT);
    }

    internal TextAndFormatFields CreateAlwaysVisibleInteractionHintLine(string description, params PlayerInput.InputType[] bindingActions)
    {
        return StorageTerminalUIUtility.CreateInteractionHintLine(description, StorageTerminalUIUtility.ShouldPreferJoystickHints(), bindingActions);
    }

    internal void AppendInteractionHints(List<TextAndFormatFields> lines, params TextAndFormatFields[] hintLines)
    {
        if (!_showInteractionHints || hintLines == null || hintLines.Length == 0)
        {
            return;
        }

        int firstAddedIndex = -1;
        for (int i = 0; i < hintLines.Length; i++)
        {
            TextAndFormatFields hintLine = hintLines[i];
            if (hintLine == null)
            {
                continue;
            }

            if (firstAddedIndex < 0)
            {
                firstAddedIndex = lines.Count;
            }

            lines.Add(hintLine);
        }

        if (firstAddedIndex >= 0 && firstAddedIndex < lines.Count && firstAddedIndex > 0)
        {
            lines[firstAddedIndex].paddingBeneath = Mathf.Max(lines[firstAddedIndex].paddingBeneath, 0.125f);
        }
    }

    internal List<TextAndFormatFields> GetHintTextButtonHoverDescription()
    {
        List<TextAndFormatFields> lines = new()
        {
            new TextAndFormatFields
            {
                text = _showInteractionHints ? "Interaction hints are shown." : "Interaction hints are hidden.",
                dontLocalize = true,
                color = Color.white * 0.99f,
                paddingBeneath = 0.125f
            }
        };

        TextAndFormatFields toggleHint = CreateAlwaysVisibleInteractionHintLine("Toggle tooltip hints", PlayerInput.InputType.UI_INTERACT);
        if (toggleHint != null)
        {
            lines.Add(toggleHint);
        }

        return lines;
    }

    private void EnsureHintControlsBuilt()
    {
        hintTextButton?.Initialize(this);
        RefreshHintButtonVisuals();
    }

    private void RefreshHintButtonVisuals()
    {
        hintTextButton?.RefreshVisuals(force: true);
    }
}
