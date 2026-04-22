using Pug.Conversion;
using UnityEngine;

public class StoragePipeAuthoring : MonoBehaviour
{
}

public class StoragePipeConverter : SingleAuthoringComponentConverter<StoragePipeAuthoring>
{
    protected override void Convert(StoragePipeAuthoring authoring)
    {
        AddComponentData(new StoragePipeTag());
        EnsureHasComponent<CantBeAttackedCD>();
        EnsureHasComponent<DontUnloadCD>();
        EnsureHasComponent<DontDisableCD>();
        SetProperty("dontUnload");
    }
}
