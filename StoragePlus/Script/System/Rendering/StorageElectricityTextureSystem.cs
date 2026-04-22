using PugTilemap;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation, WorldSystemFilterFlags.Default)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(ShaderTexturesSystem))]
[UpdateBefore(typeof(ShaderTexturesFinalizeSystem))]
public partial class StorageElectricityTextureSystem : SystemBase
{
    private const int WindowWidth = 36;
    private const int WindowHalfWidth = WindowWidth / 2;
    private const int WindowHeight = 24;
    private const int WindowHalfHeight = WindowHeight / 2;
    // Common levels: 255 max, 192 strong, 128 medium, 64 subtle.
    private const byte StoragePipeElectricityStrength = 128;

    private ShaderTexturesFinalizeSystem _finalizeSystem;
    private bool _logged;

    protected override void OnCreate()
    {
        _finalizeSystem = World.GetOrCreateSystemManaged<ShaderTexturesFinalizeSystem>();
    }

    protected override void OnUpdate()
    {
        if (Manager.sceneHandler == null || !Manager.sceneHandler.isInGame || _finalizeSystem?.electricityTex == null)
        {
            return;
        }

        Vector3Int renderOrigo = Manager.camera.RenderOrigo;
        int minX = renderOrigo.x - WindowHalfWidth;
        int maxX = renderOrigo.x + WindowHalfWidth;
        int minZ = renderOrigo.z - WindowHalfHeight;
        int maxZ = renderOrigo.z + WindowHalfHeight;
        NativeArray<Color32> electricityStrength = _finalizeSystem.electricityTex.GetRawTextureData<Color32>();

        Entities
            .WithAll<StoragePipeTag>()
            .ForEach((in LocalTransform transform) =>
            {
                MarkTile(electricityStrength, minX, maxX, minZ, maxZ, transform.Position);
            })
            .Run();

        if (_logged)
        {
            return;
        }

        _logged = true;
        StoragePlusMod.Log.LogWarning("StoragePlus is writing StoragePipe tiles into the global ElectricityTex window before ShaderTexturesFinalizeSystem.");
    }

    private static void MarkTile(NativeArray<Color32> electricityStrength, int minX, int maxX, int minZ, int maxZ, float3 position)
    {
        int x = (int)math.round(position.x);
        int z = (int)math.round(position.z);
        if (x < minX || x >= maxX || z < minZ || z >= maxZ)
        {
            return;
        }

        int index = x - minX + (z - minZ) * WindowWidth;
        Color32 color = electricityStrength[index];
        color.r = (byte)math.max((int)color.r, StoragePipeElectricityStrength);
        electricityStrength[index] = color;
    }
}
