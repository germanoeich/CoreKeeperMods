using Pug.Conversion;
using UnityEngine;

public class StorageCraftingRelayAuthoring : MonoBehaviour
{
}

public class StorageCraftingRelayConverter : SingleAuthoringComponentConverter<StorageCraftingRelayAuthoring>
{
    protected override void Convert(StorageCraftingRelayAuthoring authoring)
    {
        AddComponentData(new StorageConnectorTag());
        AddComponentData(new StorageCraftingRelayTag());
        AddComponentData(new StorageCraftingNetworkSummaryState());
        EnsureHasBuffer<StorageCraftingNetworkSummaryEntry>();
        EnsureHasBuffer<StorageCraftingNetworkResolvedInventory>();
        EnsureHasComponent<InventoryAutoTransferEnabledCD>();
    }
}
