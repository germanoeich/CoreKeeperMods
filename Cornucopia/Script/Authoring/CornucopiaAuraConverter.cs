using Pug.Conversion;
using Unity.Mathematics;

public class CornucopiaAuraConverter : SingleAuthoringComponentConverter<CornucopiaAuraAuthoring>
{
    protected override void Convert(CornucopiaAuraAuthoring authoring)
    {
        EnsureHasComponent<DontUnloadCD>();
        EnsureHasComponent<DontDisableCD>();
        SetProperty("dontUnload");

        AddComponentData(new CornucopiaAuraSourceCD
        {
            radius = math.max(0f, authoring.radius),
            drainLessHungerPercent = math.clamp(authoring.drainLessHungerPercent, 0, 100),
            buffRefreshDuration = math.max(0.1f, authoring.buffRefreshDuration)
        });
    }
}
