using Unity.Entities;

public struct ItemDropperTimingCD : IComponentData
{
    public double pickupCooldownSeconds;
    public double nextAllowedDropTime;
    public bool hadMatchingDroppedItemLastTick;
}
