using Pug.Conversion;
using UnityEngine;

public class StorageConnectorAuthoring : MonoBehaviour
{
}

public class StorageConnectorConverter : SingleAuthoringComponentConverter<StorageConnectorAuthoring>
{
    protected override void Convert(StorageConnectorAuthoring authoring)
    {
        AddComponentData(new StorageConnectorTag());
        AddComponentData(new OutputConnectorTag());
        EnsureHasComponent<CantBeAttackedCD>();
        StorageNetworkLoadRetentionUtility.ApplyTo(this, authoring.transform.position);
    }
}
