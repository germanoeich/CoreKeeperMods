using System.Collections.Generic;
using CoreLib.Submodule.UserInterface;
using CoreLib.Submodule.UserInterface.Component;
using CoreLib.Submodule.UserInterface.Interface;
using Inventory;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

public sealed partial class StorageTerminalUI : UIelement, IModUI, IStorageTerminalHotSyncAware
{
    public const string InterfaceId = "StoragePlus.StorageTerminal";

    private readonly List<StorageTerminalItemEntry> _filteredEntries = new();

    [SerializeField]
    private Transform layoutRoot;

    [SerializeField]
    private SpriteRenderer panelBackdrop;

    [SerializeField]
    private StorageTerminalSearchField searchField;

    [SerializeField]
    private StorageTerminalSortButton sortButton;

    [SerializeField]
    private StorageTerminalSortButton sortOrderButton;

    [SerializeField]
    private StorageTerminalShowFiltersButton showFiltersButton;

    [SerializeField]
    private StorageTerminalHintTextButton hintTextButton;

    [SerializeField]
    private Transform filtersPanel;

    [SerializeField]
    private StorageTerminalFilterGrid filterGrid;

    [SerializeField]
    private StorageTerminalGrid grid;

    [SerializeField]
    private PugText emptyStateText;

    private Entity _lastRelayEntity = Entity.Null;
    private Entity _cachedRelayEntity = Entity.Null;
    private ulong _lastContentsHash;
    private int _lastEntryCount = -1;
    private string _lastFilter = string.Empty;
    private bool _built;

    public GameObject Root => gameObject;

    public bool ShowWithPlayerInventory => true;

    public bool ShouldPlayerCraftingShow => false;

    private void Reset()
    {
        AutoAssignReferences();
    }

    private void OnValidate()
    {
        AutoAssignReferences();
    }

    private void Awake()
    {
        StorageTerminalUIUtility.EnsureUiElementLists(this);
        AutoAssignReferences();
        UpdateLayoutScale();
        gameObject.SetActive(false);
    }

    public void ShowUI()
    {
        TryApplyHotSync(forceCheck: true);
        if (!EnsureBuilt())
        {
            return;
        }

        UpdateLayoutScale();
        ResetSearch();
        CacheInteractionEntity();
        gameObject.SetActive(true);
        EnsureSortControlsBuilt();
        RefreshEntries(force: true);
        grid.ShowContainerUI();
    }

    public void HideUI()
    {
        if (searchField != null && searchField.inputIsActive)
        {
            searchField.Deactivate(commit: false);
        }

        if (grid != null)
        {
            grid.HideContainerUI();
        }

        if (Manager.ui != null && IsSelectionInsideRoot())
        {
            Manager.ui.DeselectAnySelectedUIElement();
        }

        _cachedRelayEntity = Entity.Null;
        gameObject.SetActive(false);
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();

        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        UpdateLayoutScale();
        if (ShouldCloseFromInteract())
        {
            Manager.ui.HideAllInventoryAndCraftingUI();
            return;
        }

        bool hotSyncApplied = TryApplyHotSync();
        RefreshEntries(force: hotSyncApplied);
    }

    internal void RequestWithdraw(StorageTerminalItemEntry entry, int amount)
    {
        if (entry.ObjectId == ObjectID.None || amount <= 0 || world == null || !world.IsCreated)
        {
            return;
        }

        Entity relayEntity = GetRelayEntity();
        if (relayEntity == Entity.Null)
        {
            return;
        }

        EntityManager entityManager = world.EntityManager;
        if (!entityManager.Exists(relayEntity))
        {
            return;
        }

        Entity rpcEntity = entityManager.CreateEntity(typeof(StorageTerminalWithdrawRpc), typeof(SendRpcCommandRequest));
        int requestedAmount = amount == int.MaxValue ? entry.TotalAmount : Mathf.Min(amount, entry.TotalAmount);
        entityManager.SetComponentData(rpcEntity, new StorageTerminalWithdrawRpc
        {
            relayEntity = relayEntity,
            objectId = entry.ObjectId,
            variation = entry.Variation,
            itemAmount = entry.ContainedObject.amount,
            auxDataIndex = entry.ContainedObject.auxDataIndex,
            entryId = entry.EntryId,
            flags = (byte)entry.Flags,
            amount = requestedAmount
        });
        entityManager.SetComponentData(rpcEntity, new SendRpcCommandRequest());
        InvalidateSummaryCache();
    }

    internal bool TryRequestDeposit(InventorySlotUI slot)
    {
        if (slot == null || world == null || !world.IsCreated)
        {
            return false;
        }

        InventoryHandler sourceInventory = slot.GetInventoryHandler();
        if (sourceInventory == null)
        {
            return false;
        }

        int sourceSlot = sourceInventory.startPosInBuffer + slot.inventorySlotIndex;
        if (sourceSlot < 0 || !sourceInventory.HasObject(slot.inventorySlotIndex))
        {
            return false;
        }

        Entity relayEntity = GetRelayEntity();
        if (relayEntity == Entity.Null)
        {
            return false;
        }

        EntityManager entityManager = world.EntityManager;
        if (!entityManager.Exists(relayEntity))
        {
            return false;
        }

        Entity rpcEntity = entityManager.CreateEntity(typeof(StorageTerminalDepositRpc), typeof(SendRpcCommandRequest));
        entityManager.SetComponentData(rpcEntity, new StorageTerminalDepositRpc
        {
            relayEntity = relayEntity,
            sourceSlot = sourceSlot,
            amount = int.MaxValue
        });
        entityManager.SetComponentData(rpcEntity, new SendRpcCommandRequest());
        InvalidateSummaryCache();
        return true;
    }

    private bool EnsureBuilt()
    {
        if (_built)
        {
            return true;
        }

        AutoAssignReferences();
        if (layoutRoot == null || panelBackdrop == null || searchField == null || sortButton == null || sortOrderButton == null || grid == null || emptyStateText == null)
        {
            Debug.LogError("StorageTerminalUI prefab is missing required references.", this);
            return false;
        }

        emptyStateText.Render(string.Empty, force: true);
        emptyStateText.gameObject.SetActive(false);

        grid.Initialize(this);
        EnsureFilterButtonsBuilt();
        RefreshNavigationLinks();
        EnsureHintControlsBuilt();

        _built = true;
        return true;
    }

    public void OnHotSyncApplied()
    {
        AutoAssignReferences();
        ReapplyAuthoringPlacement();
        UpdateLayoutScale();

        if (_built)
        {
            EnsureSortControlsBuilt();
            EnsureFilterButtonsBuilt();
            EnsureHintControlsBuilt();
            RefreshFilterButtonVisuals();
            RefreshNavigationLinks();
        }
    }

    private void ResetSearch()
    {
        if (searchField == null)
        {
            return;
        }

        if (searchField.inputIsActive)
        {
            searchField.Deactivate(commit: false);
        }

        searchField.SetInputText(string.Empty);
        _lastFilter = string.Empty;
    }

    private void AutoAssignReferences()
    {
        layoutRoot ??= transform.Find("Root");

        Transform referenceRoot = layoutRoot != null ? layoutRoot : transform;

        panelBackdrop ??= referenceRoot.Find("Background")?.GetComponent<SpriteRenderer>();
        searchField ??= referenceRoot.GetComponentInChildren<StorageTerminalSearchField>(includeInactive: true);
        sortButton ??= FindSortButton(referenceRoot, "SortButton");
        sortOrderButton ??= FindSortButton(referenceRoot, "SortOrderButton");
        showFiltersButton ??= referenceRoot.Find("ShowFiltersButton")?.GetComponent<StorageTerminalShowFiltersButton>();
        hintTextButton ??= referenceRoot.Find("HintTextButton")?.GetComponent<StorageTerminalHintTextButton>();
        filtersPanel ??= referenceRoot.Find("FiltersPanel");
        filterGrid ??= filtersPanel != null ? filtersPanel.GetComponent<StorageTerminalFilterGrid>() : null;
        grid ??= referenceRoot.GetComponentInChildren<StorageTerminalGrid>(includeInactive: true);
        emptyStateText ??= referenceRoot.Find("EmptyState")?.GetComponent<PugText>();
        networkFullnessBarRoot ??= referenceRoot.Find("ButtonsAnchor/NetworkFullnessBar");
        networkFullnessBarFillScaleRoot ??= networkFullnessBarRoot?.Find("FillScaleRoot");
        networkFullnessBarHover ??= networkFullnessBarRoot?.GetComponent<StorageTerminalNetworkFullnessHover>();
        networkFullnessBarBackground ??= networkFullnessBarRoot?.Find("Background")?.GetComponent<SpriteRenderer>();
        networkFullnessBarFill ??= networkFullnessBarFillScaleRoot?.Find("Fill")?.GetComponent<SpriteRenderer>();
        networkFullnessBarFrame ??= networkFullnessBarRoot?.Find("Frame")?.GetComponent<SpriteRenderer>();
    }

    private static StorageTerminalSortButton FindSortButton(Transform referenceRoot, string objectName)
    {
        StorageTerminalSortButton[] buttons = referenceRoot.GetComponentsInChildren<StorageTerminalSortButton>(includeInactive: true);
        for (int i = 0; i < buttons.Length; i++)
        {
            StorageTerminalSortButton button = buttons[i];
            if (button != null && button.name == objectName)
            {
                return button;
            }
        }

        return null;
    }

    private bool TryApplyHotSync(bool forceCheck = false)
    {
#if STORAGEPLUS_HOTSYNC
        return StorageTerminalHotSyncRuntime.TryApplyLatest(this, forceCheck);
#else
        return false;
#endif
    }

    private void UpdateLayoutScale()
    {
        if (layoutRoot == null || Manager.ui == null)
        {
            return;
        }

        layoutRoot.localScale = Manager.ui.CalcGameplayUITargetScaleMultiplier();
    }

    private void ReapplyAuthoringPlacement()
    {
        ModUIAuthoring authoring = GetComponent<ModUIAuthoring>();
        if (authoring == null)
        {
            return;
        }

        transform.localPosition = authoring.initialInterfacePosition;
    }

    private bool IsSelectionInsideRoot()
    {
        UIelement selected = Manager.ui.currentSelectedUIElement;
        return selected != null && (selected == this || selected.transform.IsChildOf(transform));
    }

    private bool ShouldCloseFromInteract()
    {
        if (searchField != null && searchField.inputIsActive)
        {
            return false;
        }

        if (!Manager.input.SystemPrefersKeyboardAndMouse() || Manager.input.singleplayerInputModule == null)
        {
            return false;
        }

        return Manager.input.singleplayerInputModule.WasButtonPressedDownThisFrame(PlayerInput.InputType.INTERACT_WITH_OBJECT);
    }

    private void CacheInteractionEntity()
    {
        _cachedRelayEntity = UserInterfaceModule.GetInteractionEntity();
    }

    private void InvalidateSummaryCache()
    {
        _lastContentsHash = 0UL;
        _lastEntryCount = -1;
        ResetNetworkFullnessCache();
    }

    private Entity GetRelayEntity()
    {
        if (_cachedRelayEntity != Entity.Null && world != null && world.IsCreated && world.EntityManager.Exists(_cachedRelayEntity))
        {
            return _cachedRelayEntity;
        }

        _cachedRelayEntity = UserInterfaceModule.GetInteractionEntity();
        return _cachedRelayEntity;
    }
}
