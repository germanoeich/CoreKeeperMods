using CoreLib.Submodule.TileSet;
using PugTilemap;
using PugTilemap.Quads;
using UnityEngine;

internal static class TilesetAdaptiveTextureUtility
{
    public static int EnsureFallbackTexturesForTileType(ModTileset tileset, TileType targetTileType)
    {
        if (tileset?.layers?.layers == null)
        {
            return 0;
        }

        Texture2D fallbackTexture = tileset.tilesetTextures?.texture ?? tileset.tilesetTexture;
        Texture2D fallbackEmissiveTexture = tileset.tilesetTextures?.emissiveTexture ?? tileset.tilesetEmissiveTexture;
        int configuredLayerCount = 0;

        for (int i = 0; i < tileset.layers.layers.Count; i++)
        {
            QuadGenerator generator = tileset.layers.layers[i];
            if (!UsesTargetTile(generator, targetTileType))
            {
                continue;
            }

            if (!generator.isUsingFullAdaptiveTexture)
            {
                continue;
            }

            configuredLayerCount++;

            if (generator.emissiveTexture == null && fallbackEmissiveTexture != null)
            {
                generator.emissiveTexture = fallbackEmissiveTexture;
            }
        }

        if (configuredLayerCount > 0 && fallbackTexture != null)
        {
            tileset.layers.tilesetTexture = fallbackTexture;
        }

        return configuredLayerCount;
    }

    private static bool UsesTargetTile(QuadGenerator generator, TileType targetTileType)
    {
        return generator != null && generator.targetTile == targetTileType;
    }
}
