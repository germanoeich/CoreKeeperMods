using I2.Loc;

public class ItemDropperEntityMono : StorageConnectorEntityMono, IFilteringBuilding
{
    public void Use()
    {
        if (Manager.main?.player == null || Manager.ui == null)
        {
            return;
        }

        Manager.main.player.SetActiveFilterStructure(this);
        Manager.ui.OnFilterWindowOpen();
    }

    public void OnPlayerLeftBuilding()
    {
        PlayerController player = Manager.main?.player;
        if (player == null || !ReferenceEquals(player.GetActiveFilteringBuilding(), this))
        {
            return;
        }

        Manager.ui?.HideAllInventoryAndCraftingUI();
        player.SetActiveFilterStructure(null);
    }

    public bool RequiresElectricity()
    {
        return false;
    }

    public bool HasElectricity()
    {
        return false;
    }

    public LocalizedString GetUITitle()
    {
        return "filtering";
    }

    public override void OnFree()
    {
        OnPlayerLeftBuilding();
        base.OnFree();
    }
}
