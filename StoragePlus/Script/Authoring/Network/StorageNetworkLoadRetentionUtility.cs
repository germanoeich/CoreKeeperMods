using Pug.Conversion;
using Unity.Mathematics;
using UnityEngine;

public static class StorageNetworkLoadRetentionUtility
{
    public const float LoadRetentionRadius = 2f;
    public const float EnableRetentionRadius = 2f;

    public static KeepAreaLoadedCD CreateKeepAreaLoaded()
    {
        return new KeepAreaLoadedCD
        {
            KeepLoadedRadius = LoadRetentionRadius,
            StartLoadRadius = LoadRetentionRadius,
            ImmediateLoadRadius = LoadRetentionRadius
        };
    }

    public static EnableEntitiesInCircleCD CreateEnableCircle(float3 position)
    {
        return new EnableEntitiesInCircleCD
        {
            Center = position.xz,
            Radius = EnableRetentionRadius
        };
    }

    public static void ApplyTo(Converter converter, Vector3 position)
    {
        converter.EnsureHasComponent<DontUnloadCD>();
        converter.EnsureHasComponent<DontDisableCD>();
        converter.SetProperty("dontUnload");
        converter.AddComponentData(CreateKeepAreaLoaded());
        converter.AddComponentData(CreateEnableCircle(new float3(position.x, position.y, position.z)));
    }
}
