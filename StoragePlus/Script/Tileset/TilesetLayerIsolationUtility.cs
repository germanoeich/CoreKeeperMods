using System;
using System.Collections.Generic;
using CoreLib.Submodule.TileSet;
using PugTilemap;
using PugTilemap.Quads;
using PugTilemap.Workshop;

internal static class TilesetLayerIsolationUtility
{
    /// <summary>
    /// Creates a runtime copy of the tileset layer definition and forces adaptation for
    /// <paramref name="targetTileType"/> to only match the same tileset.
    /// </summary>
    /// <param name="tileset">Tileset definition that will be registered in CoreLib.</param>
    /// <param name="targetTileType">Tile type whose adaptive quads should only match the same tileset.</param>
    /// <param name="isolationKey">Stable identifier used to build a unique layer name.</param>
    /// <returns>
    /// True when at least one quad generator targeting <paramref name="targetTileType"/> was found and isolated.
    /// </returns>
    public static bool TryCreateOwnTilesetAdaptiveLayers(ModTileset tileset, TileType targetTileType, string isolationKey)
    {
        if (tileset == null || tileset.layers == null)
        {
            return false;
        }

        PugMapTileset sourceLayers = ResolveSourceLayers(tileset.layers);
        if (sourceLayers == null)
        {
            return false;
        }

        PugMapTileset isolatedLayers = UnityEngine.Object.Instantiate(sourceLayers);
        isolatedLayers.name = BuildIsolatedLayerName(sourceLayers.name, isolationKey, tileset.tilesetId);

        int isolatedLayerCount = SetOnlyAdaptToOwnTileset(isolatedLayers, targetTileType);
        if (isolatedLayerCount <= 0)
        {
            return false;
        }

        tileset.layers = isolatedLayers;
        return true;
    }

    /// <summary>
    /// Resolves runtime layer data for placeholder CoreLib layer assets by matching names in the workshop tileset bank.
    /// Falls back to the configured layers if runtime lookup fails.
    /// </summary>
    private static PugMapTileset ResolveSourceLayers(PugMapTileset configuredLayers)
    {
        try
        {
            List<MapWorkshopTilesetBank.Tileset> workshopTilesets = TilesetTypeUtility.GetTilesets();
            if (workshopTilesets == null)
            {
                return configuredLayers;
            }

            string configuredLayerName = configuredLayers.name;
            for (int i = 0; i < workshopTilesets.Count; i++)
            {
                MapWorkshopTilesetBank.Tileset workshopTileset = workshopTilesets[i];
                if (workshopTileset?.layers == null)
                {
                    continue;
                }

                if (workshopTileset.layers.name == configuredLayerName)
                {
                    return workshopTileset.layers;
                }
            }
        }
        catch (Exception)
        {
            // Ignore lookup failures and use configured layers directly.
        }

        return configuredLayers;
    }

    /// <summary>
    /// Enables own-tileset-only adaptation for each quad generator that targets <paramref name="targetTileType"/>.
    /// </summary>
    private static int SetOnlyAdaptToOwnTileset(PugMapTileset layers, TileType targetTileType)
    {
        if (layers.layers == null)
        {
            return 0;
        }

        int isolatedCount = 0;
        for (int i = 0; i < layers.layers.Count; i++)
        {
            QuadGenerator generator = layers.layers[i];
            if (generator == null || generator.targetTile != targetTileType)
            {
                continue;
            }

            generator.onlyAdaptToOwnTileset = true;
            isolatedCount++;
        }

        return isolatedCount;
    }

    /// <summary>
    /// Builds a unique runtime layer name so CoreLib does not replace this layer with shared vanilla layers.
    /// </summary>
    private static string BuildIsolatedLayerName(string sourceLayerName, string isolationKey, string tilesetId)
    {
        return $"{SanitizeName(sourceLayerName)}_{SanitizeName(isolationKey)}_{SanitizeName(tilesetId)}_isolated";
    }

    /// <summary>
    /// Converts arbitrary text into a simple layer-name-safe token.
    /// </summary>
    private static string SanitizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unnamed";
        }

        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }
}
