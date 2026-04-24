using Unity.Entities;
using Unity.NetCode;

[GhostComponent(PrefabType = GhostPrefabType.All)]
public struct CornucopiaAuraSourceCD : IComponentData
{
    [GhostField]
    public float radius;

    [GhostField]
    public int drainLessHungerPercent;

    [GhostField]
    public float buffRefreshDuration;
}
