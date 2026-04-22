using Pug.Conversion;
using UnityEngine;

public class InputConnectorAuthoring : MonoBehaviour
{
}

public class InputConnectorConverter : SingleAuthoringComponentConverter<InputConnectorAuthoring>
{
    protected override void Convert(InputConnectorAuthoring authoring)
    {
        AddComponentData(new StorageConnectorTag());
        AddComponentData(new InputConnectorTag());
        EnsureHasComponent<CantBeAttackedCD>();
        StorageNetworkLoadRetentionUtility.ApplyTo(this, authoring.transform.position);
    }
}
