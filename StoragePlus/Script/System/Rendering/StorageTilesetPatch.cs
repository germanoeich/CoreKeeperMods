using System.Collections.Generic;
using CoreLib.Submodule.TileSet;
using HarmonyLib;
using PugTilemap;
using UnityEngine;

[HarmonyPatch]
public static class StorageTilesetPatch
{
    private static readonly Dictionary<int, ModTileset> Sources = new();
    private static readonly Dictionary<int, TilesetDataBlock> RuntimeTilesets = new();

    internal static void Initialize(IEnumerable<Object> assets)
    {
        Reset();
        foreach (Object asset in assets)
        {
            if (asset is ModTileset tileset)
                Register((int)TileSetModule.GetTilesetId(tileset.tilesetId), tileset);
        }
    }

    internal static void Register(int id, ModTileset tileset)
    {
        Sources.Add(id, tileset);
    }

    internal static void Reset()
    {
        foreach (TilesetDataBlock tileset in RuntimeTilesets.Values)
        {
#if UNITY_EDITOR
            Object.DestroyImmediate(tileset);
#else
            Object.Destroy(tileset);
#endif
        }
        RuntimeTilesets.Clear();
        Sources.Clear();
    }

    // CoreLib 5 assigns IDs but does not bridge ModTileset to the game's 1.3 renderer.
    [HarmonyPatch(typeof(TilesetDataBlock), nameof(TilesetDataBlock.Get))]
    [HarmonyPrefix]
    private static bool GetTileset(int index, ref TilesetDataBlock __result)
    {
        if (!Sources.TryGetValue(index, out ModTileset source))
            return true;

        if (!RuntimeTilesets.TryGetValue(index, out TilesetDataBlock tileset))
        {
            tileset = ScriptableObject.CreateInstance<TilesetDataBlock>();
            tileset.name = source.tilesetId;
            tileset.tilesetType = (Tileset)index;
            tileset.layerDefinition = source.layers;
            tileset.overrideMaterials = source.overrideMaterials;
            tileset.overrideParticles = source.overrideParticles;
            tileset.tilesetTextures = source.tilesetTextures;
            tileset.adaptiveTilesetTextures = source.adaptiveTilesetTextures;
            RuntimeTilesets.Add(index, tileset);
        }

        __result = tileset;
        return false;
    }

    [HarmonyPatch(typeof(TilesetTextureLoader), nameof(TilesetTextureLoader.GetSourceTexture))]
    [HarmonyPrefix]
    private static bool GetSourceTexture(int tilesetIndex, TextureType textureType, ref Texture2D __result)
    {
        return GetEmbeddedTexture(tilesetIndex, textureType, ref __result);
    }

    [HarmonyPatch(typeof(TilesetTextureLoader), nameof(TilesetTextureLoader.GetResolvedSourceTexture))]
    [HarmonyPrefix]
    private static bool GetResolvedSourceTexture(int tilesetIndex, TextureType textureType, ref Texture2D __result)
    {
        return GetEmbeddedTexture(tilesetIndex, textureType, ref __result);
    }

    private static bool GetEmbeddedTexture(int id, TextureType type, ref Texture2D result)
    {
        if (!Sources.TryGetValue(id, out ModTileset source))
            return true;

        Texture2D texture = type switch
        {
            TextureType.REGULAR => source.tilesetTexture,
            TextureType.EMISSIVE => source.tilesetEmissiveTexture,
            _ => null
        };
        if (texture == null)
            return true;

        result = texture;
        return false;
    }
}
