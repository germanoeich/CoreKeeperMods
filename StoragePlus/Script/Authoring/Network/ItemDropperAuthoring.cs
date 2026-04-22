using Pug.Conversion;
using UnityEngine;

public class ItemDropperAuthoring : MonoBehaviour
{
    [Min(0)]
    public int pickupCooldownMilliseconds = 500;
}

public class ItemDropperConverter : SingleAuthoringComponentConverter<ItemDropperAuthoring>
{
    protected override void Convert(ItemDropperAuthoring authoring)
    {
        AddComponentData(new StorageConnectorTag());
        AddComponentData(new OutputConnectorTag());
        EnsureHasComponent<CantBeAttackedCD>();
        AddComponentData(new ItemDropperTimingCD
        {
            pickupCooldownSeconds = authoring.pickupCooldownMilliseconds / 1000d,
            nextAllowedDropTime = 0d,
            hadMatchingDroppedItemLastTick = false
        });
        EnsureHasComponent<ObjectFilteringCD>();
        StorageNetworkLoadRetentionUtility.ApplyTo(this, authoring.transform.position);
    }
}
