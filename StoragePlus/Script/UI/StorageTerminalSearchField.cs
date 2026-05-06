using UnityEngine;

public sealed partial class StorageTerminalSearchField : UIelement, InputManager.TextInputInterface, IStorageTerminalHotSyncAware
{
    [SerializeField]
    private PugText pugText;

    [SerializeField]
    private PugText hintText;

    [SerializeField]
    private string hintString = "Search";

    [SerializeField]
    private GameObject selectedMarker;

    [SerializeField]
    private CharacterMarkBlinker characterMarkBlinker;

    [SerializeField]
    private float maxWidth = 6.4f;

    [SerializeField]
    private bool dontAllowNewLines = true;

    [SerializeField]
    private bool trim;

    [SerializeField]
    private string characterWhiteList = string.Empty;

    [SerializeField]
    private bool dontDeactivateOnDeselect;

    private int _currentCharIndex;

    public bool inputIsActive { get; private set; }

    public int TextVersion { get; private set; }

    [field: SerializeField]
    public int MaxCharactersForOnScreenKeyboard { get; private set; } = 255;

    public bool WasAutoActivated
    {
        get => wasAutoActivated;
        set => wasAutoActivated = value;
    }

    private void Reset()
    {
        AutoAssignReferences();
    }

    private void OnValidate()
    {
        AutoAssignReferences();
        ApplyTextSettings();
    }

    private void Awake()
    {
        StorageTerminalUIUtility.EnsureUiElementLists(this);
        AutoAssignReferences();
        ApplyTextSettings();

        if (characterMarkBlinker != null)
        {
            characterMarkBlinker.gameObject.SetActive(false);
        }

        if (selectedMarker != null)
        {
            selectedMarker.SetActive(false);
        }

        RenderInputText();
        UpdateHintText();
    }

    private void Update()
    {
        if (pugText == null || hintText == null)
        {
            AutoAssignReferences();
        }

        if (pugText == null)
        {
            return;
        }

        UpdateHintText();
        if (inputIsActive)
        {
            UpdateCaretPosition();
            _currentCharIndex = Mathf.Clamp(_currentCharIndex, 0, pugText.displayedTextString.Length);
        }
    }

    public override void OnSelected()
    {
        base.OnSelected();
        if (selectedMarker != null)
        {
            selectedMarker.SetActive(true);
        }
    }

    public override void OnDeselected(bool playEffect = true)
    {
        base.OnDeselected(playEffect);

        if (selectedMarker != null)
        {
            selectedMarker.SetActive(false);
        }

        if (!dontDeactivateOnDeselect)
        {
            Deactivate(commit: false);
        }
    }

    public override void OnLeftClicked(bool mod1, bool mod2)
    {
        AutoAssignReferences();

        Manager.input.SetActiveInputField(this);
        Manager.input.DisableInput();
        if (characterMarkBlinker != null)
        {
            characterMarkBlinker.EnableAndResetBlink();
        }
        inputIsActive = true;
    }

    private void AutoAssignReferences()
    {
        pugText ??= transform.Find("text")?.GetComponent<PugText>();
        hintText ??= transform.Find("hintText")?.GetComponent<PugText>();
        selectedMarker ??= transform.Find("selectedMarker")?.gameObject;
        characterMarkBlinker ??= transform.Find("characterMarkBlinker")?.GetComponent<CharacterMarkBlinker>();
    }

    private void ApplyTextSettings()
    {
        if (pugText != null)
        {
            pugText.maxWidth = maxWidth + (dontAllowNewLines ? 1f : 0f);
            pugText.localize = false;
            pugText.checkForProfanity = false;
        }

        if (hintText != null)
        {
            hintText.localize = false;
        }
    }

    private void UpdateHintText()
    {
        if (pugText == null || hintText == null)
        {
            return;
        }

        if (pugText.GetText() == string.Empty && hintText.GetText() == string.Empty)
        {
            hintText.Render(hintString);
        }
        else if (pugText.GetText() != string.Empty && hintText.GetText() != string.Empty)
        {
            hintText.Render(string.Empty);
        }
    }

    private void RenderInputText()
    {
        if (pugText == null)
        {
            return;
        }

        pugText.Render(rewindEffectAnims: false);
        TrimToMaxWidth();
        _currentCharIndex = Mathf.Clamp(_currentCharIndex, 0, pugText.displayedTextString.Length);
    }

    private void MarkTextChanged()
    {
        unchecked
        {
            TextVersion++;
        }
    }

    private void UpdateCaretPosition()
    {
        if (pugText == null || characterMarkBlinker == null)
        {
            return;
        }

        Vector2 textOrigin = new(pugText.transform.position.x, pugText.transform.position.y);
        float lineHeight = pugText.dimensions.height > 0f
            ? pugText.dimensions.height / Mathf.Max(pugText.displayedTextStringLinesAmount * 2f, 1f)
            : 0f;

        Vector2 caretPosition = textOrigin + new Vector2(pugText.dimensions.min.x, pugText.dimensions.max.y) + new Vector2(1f / 32f, -lineHeight);
        if (_currentCharIndex > 0 && _currentCharIndex <= pugText.localCharacterEndPositions.Count)
        {
            caretPosition += pugText.localCharacterEndPositions[_currentCharIndex - 1];
        }

        characterMarkBlinker.transform.position = new Vector3(caretPosition.x, caretPosition.y, characterMarkBlinker.transform.position.z);
    }

    private void TrimToMaxWidth()
    {
        if (pugText == null || maxWidth <= 0f)
        {
            return;
        }

        while (pugText.displayedTextString.Length > 0 && pugText.dimensions.width > maxWidth)
        {
            bool restoreProfanityCheck = false;
            if (pugText.checkForProfanity)
            {
                restoreProfanityCheck = true;
                pugText.checkForProfanity = false;
            }

            string trimmedText = RemoveLastTextElement(pugText.displayedTextString);
            if (trimmedText.Length == pugText.displayedTextString.Length)
            {
                break;
            }

            pugText.SetText(trimmedText);
            pugText.Render(rewindEffectAnims: false);
            _currentCharIndex = ClampCaretIndexToTextElementBoundary(pugText.displayedTextString, _currentCharIndex);

            if (restoreProfanityCheck)
            {
                pugText.checkForProfanity = true;
                pugText.Render(rewindEffectAnims: false);
            }
        }
    }

    public void OnHotSyncApplied()
    {
        AutoAssignReferences();
        ApplyTextSettings();
        UpdateHintText();

        if (selectedMarker != null)
        {
            selectedMarker.SetActive(Manager.ui != null && Manager.ui.currentSelectedUIElement == this);
        }

        if (characterMarkBlinker != null)
        {
            characterMarkBlinker.gameObject.SetActive(inputIsActive);
        }
    }
}
