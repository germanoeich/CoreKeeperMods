using System.Globalization;
using UnityEngine;

public sealed partial class StorageTerminalSearchField
{
    public string GetInputText()
    {
        if (pugText == null)
        {
            return string.Empty;
        }

        if (pugText.checkForProfanity && !Manager.networking.OfflineSession)
        {
            return pugText.displayedTextString;
        }

        return pugText.GetText();
    }

    public void SetInputText(string input)
    {
        if (pugText == null)
        {
            return;
        }

        string text = input ?? string.Empty;
        if (pugText.GetText() == text)
        {
            _currentCharIndex = Mathf.Clamp(_currentCharIndex, 0, pugText.displayedTextString.Length);
            UpdateHintText();
            return;
        }

        _currentCharIndex = text.Length;
        pugText.SetText(text);
        RenderInputText();
        UpdateHintText();
        MarkTextChanged();
    }

    public void AppendString(string input)
    {
        if (pugText == null || string.IsNullOrEmpty(input))
        {
            return;
        }

        string sanitizedInput = trim ? input.Trim() : input;
        for (int i = sanitizedInput.Length - 1; i >= 0; i--)
        {
            if (dontAllowNewLines && (sanitizedInput[i] == '\n' || sanitizedInput[i] == '\r'))
            {
                sanitizedInput = sanitizedInput.Remove(i, 1);
                continue;
            }

            if (characterWhiteList.Length == 0)
            {
                continue;
            }

            int whiteListIndex = 0;
            for (; whiteListIndex < characterWhiteList.Length; whiteListIndex++)
            {
                if (sanitizedInput[i] == characterWhiteList[whiteListIndex])
                {
                    break;
                }
            }

            if (whiteListIndex == characterWhiteList.Length)
            {
                sanitizedInput = sanitizedInput.Remove(i, 1);
            }
        }

        if (sanitizedInput.Length == 0)
        {
            return;
        }

        string previousText = pugText.displayedTextString;
        if (_currentCharIndex > previousText.Length)
        {
            Debug.LogError("StorageTerminalSearchField current char index drifted out of range.");
            _currentCharIndex = previousText.Length;
        }

        _currentCharIndex = ClampCaretIndexToTextElementBoundary(previousText, _currentCharIndex);

        string nextText;
        if (_currentCharIndex == previousText.Length)
        {
            nextText = previousText + sanitizedInput;
        }
        else
        {
            nextText = previousText.Insert(_currentCharIndex, sanitizedInput);
        }

        bool appendedAtEnd = _currentCharIndex == previousText.Length;
        _currentCharIndex += sanitizedInput.Length;
        pugText.SetText(nextText);
        RenderInputText();

        if (maxWidth > 0f && pugText.dimensions.width > maxWidth)
        {
            pugText.SetText(previousText);
            _currentCharIndex -= sanitizedInput.Length;
            RenderInputText();
        }
        else if (appendedAtEnd)
        {
            _currentCharIndex = pugText.displayedTextString.Length;
            MarkTextChanged();
        }
        else
        {
            MarkTextChanged();
        }

        UpdateHintText();
        WasAutoActivated = false;
    }

    public void Deactivate(bool commit)
    {
        Manager.input.SetActiveInputField(null);
        Manager.input.EnableInput();

        if (characterMarkBlinker != null)
        {
            characterMarkBlinker.gameObject.SetActive(false);
        }

        inputIsActive = false;
    }

    public void MoveCharMarker(int delta)
    {
        if (pugText == null)
        {
            return;
        }

        string currentText = pugText.displayedTextString;
        _currentCharIndex = ClampCaretIndexToTextElementBoundary(currentText, _currentCharIndex);

        if (delta < 0)
        {
            for (int i = 0; i < -delta; i++)
            {
                _currentCharIndex = GetPreviousTextElementStart(currentText, _currentCharIndex);
            }
        }
        else
        {
            for (int i = 0; i < delta; i++)
            {
                _currentCharIndex = GetNextTextElementStart(currentText, _currentCharIndex);
            }
        }
    }

    public void RemoveCharAtMarker()
    {
        if (pugText == null)
        {
            return;
        }

        string currentText = pugText.displayedTextString;
        _currentCharIndex = ClampCaretIndexToTextElementBoundary(currentText, _currentCharIndex);
        int nextTextElementStart = GetNextTextElementStart(currentText, _currentCharIndex);
        if (nextTextElementStart <= _currentCharIndex)
        {
            return;
        }

        pugText.SetText(currentText.Remove(_currentCharIndex, nextTextElementStart - _currentCharIndex));
        RenderInputText();
        UpdateHintText();
        MarkTextChanged();
    }

    public void RemoveCharBehindMarker()
    {
        if (pugText == null)
        {
            return;
        }

        string currentText = pugText.displayedTextString;
        _currentCharIndex = ClampCaretIndexToTextElementBoundary(currentText, _currentCharIndex);
        if (_currentCharIndex <= 0)
        {
            return;
        }

        int previousTextElementStart = GetPreviousTextElementStart(currentText, _currentCharIndex);
        int removedLength = _currentCharIndex - previousTextElementStart;
        if (removedLength <= 0)
        {
            return;
        }

        pugText.SetText(currentText.Remove(previousTextElementStart, removedLength));
        _currentCharIndex = previousTextElementStart;
        RenderInputText();
        UpdateHintText();
        MarkTextChanged();
    }

    public string GetHintString()
    {
        if (hintText == null)
        {
            return hintString;
        }

        return hintText.ProcessText(hintString);
    }

    public bool IsHidden()
    {
        return pugText != null && pugText.isHidden;
    }

    public void SetHintString(string value)
    {
        hintString = value ?? string.Empty;
        UpdateHintText();
    }

    private static int ClampCaretIndexToTextElementBoundary(string text, int caretIndex)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        int clampedIndex = Mathf.Clamp(caretIndex, 0, text.Length);
        if (clampedIndex == 0 || clampedIndex == text.Length)
        {
            return clampedIndex;
        }

        int[] boundaries = StringInfo.ParseCombiningCharacters(text);
        int previousBoundary = 0;
        for (int i = 0; i < boundaries.Length; i++)
        {
            int boundary = boundaries[i];
            if (boundary == clampedIndex)
            {
                return boundary;
            }

            if (boundary > clampedIndex)
            {
                return previousBoundary;
            }

            previousBoundary = boundary;
        }

        return text.Length;
    }

    private static int GetPreviousTextElementStart(string text, int caretIndex)
    {
        if (string.IsNullOrEmpty(text) || caretIndex <= 0)
        {
            return 0;
        }

        int clampedIndex = Mathf.Clamp(caretIndex, 0, text.Length);
        int[] boundaries = StringInfo.ParseCombiningCharacters(text);
        int previousBoundary = 0;
        for (int i = 0; i < boundaries.Length; i++)
        {
            int boundary = boundaries[i];
            if (boundary >= clampedIndex)
            {
                break;
            }

            previousBoundary = boundary;
        }

        return previousBoundary;
    }

    private static int GetNextTextElementStart(string text, int caretIndex)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        int clampedIndex = Mathf.Clamp(caretIndex, 0, text.Length);
        if (clampedIndex >= text.Length)
        {
            return text.Length;
        }

        int[] boundaries = StringInfo.ParseCombiningCharacters(text);
        for (int i = 0; i < boundaries.Length; i++)
        {
            int boundary = boundaries[i];
            if (boundary > clampedIndex)
            {
                return boundary;
            }
        }

        return text.Length;
    }

    private static string RemoveLastTextElement(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        int lastTextElementStart = GetPreviousTextElementStart(text, text.Length);
        return text.Remove(lastTextElementStart);
    }
}
