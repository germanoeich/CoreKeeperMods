using CoreLib.Submodule.TileSet;
using HarmonyLib;
using PugTilemap;
using PugTilemap.Quads;
using UnityEngine;

[HarmonyPatch(typeof(TilesetDataBlock), nameof(TilesetDataBlock.GetOverrideMaterial))]
public static class StoragePipeMaterialPatch
{
    private static Material _pipeMaterial;

    [HarmonyPostfix]
    private static void UseGameMaterial(int tilesetIndex, LayerName layerName, ref Material __result)
    {
        var layerDef = TilesetDataBlock.Get(tilesetIndex)?.layerDefinition.Get()?.GetDef(layerName);
        if (layerDef == null || layerDef.targetTile != TileType.circuitPlate || !IsStoragePipeLayer(layerDef))
            return;

        if (_pipeMaterial == null)
            _pipeMaterial = FindGameMaterial();

        if (_pipeMaterial != null)
            __result = _pipeMaterial;
    }

    private static bool IsStoragePipeLayer(QuadGenerator layer)
    {
        if (StoragePlusMod.ModInfo == null)
            return false;

        foreach (Object asset in StoragePlusMod.ModInfo.Assets)
        {
            if (asset is ModTileset tileset && tileset.layers != null && tileset.layers.layers.Contains(layer))
                return true;
        }
        return false;
    }

    private static Material FindGameMaterial()
    {
        // Tile materials are not exposed through API.Rendering.GetMaterial in 1.3.
        foreach (var layers in ScriptableData.GetDataBlocks<TilesetLayerDefinitionDataBlock>())
        {
            if (layers.name != "tileset_extras")
                continue;

            var circuitPlate = layers.GetDef(TileType.circuitPlate);
            if (circuitPlate?.overrideMaterial != null)
                return circuitPlate.overrideMaterial;
        }

        StoragePlusMod.Log.LogWarning("Could not resolve the game's circuit-plate material for storage pipes.");
        return null;
    }
}
