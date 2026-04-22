using CoreLib;
using CoreLib.Submodule.Command;
using CoreLib.Submodule.Entity;
using CoreLib.Submodule.TileSet;
using CoreLib.Submodule.UserInterface;
using CoreLib.Util.Extension;
using PugTilemap;
using PugMod;
using UnityEngine;
using Logger = CoreLib.Util.Logger;

public class StoragePlusMod : IMod
{
    public const string VERSION = "1.0.0";
    public const string MOD_ID = "StoragePlus";

    internal static Logger Log = new("Storage Plus");
    internal static LoadedMod ModInfo { get; private set; }
    internal static string ModDirectory { get; private set; }
    
    public void EarlyInit()
    {
        Log.LogInfo($"Mod version: {VERSION}");
        CoreLibMod.LoadSubmodule(
            typeof(UserInterfaceModule),
            typeof(EntityModule),
            typeof(TileSetModule));

        var modInfo = this.GetModInfo();
        if (modInfo == null)
        {
            Log.LogError("Failed to load Storage Plus mod: mod metadata not found!");
            return;
        }

        ModInfo = modInfo;
        ModDirectory = API.ModLoader.GetDirectory(modInfo.ModId);
        ModPlaceableObjectConversionPatch.Reset();
        API.ModLoader.ApplyHarmonyPatch(modInfo.ModId, typeof(ModPlaceableObjectConversionPatch));
        //modInfo.TryLoadBurstAssembly();

        Log.LogInfo("Mod loaded successfully");
    }

    public void Init()
    {
    }

    public void Shutdown()
    {
    }

    public void ModObjectLoaded(Object obj)
    {
        if (obj is ModTileset tileset)
        {
            const TileType targetTileType = TileType.circuitPlate;

            bool isolated = TilesetLayerIsolationUtility.TryCreateOwnTilesetAdaptiveLayers(tileset, targetTileType, MOD_ID);
            if (!isolated)
            {
                Log.LogWarning($"Did not find a '{targetTileType}' layer to isolate for tileset '{tileset.tilesetId}'.");
            }

            tileset.overrideMaterials ??= new();
            tileset.overrideParticles ??= new();

            // int adaptiveFallbackLayerCount = TilesetAdaptiveTextureUtility.EnsureFallbackTexturesForTileType(tileset, targetTileType);
            // if (adaptiveFallbackLayerCount > 0)
            // {
            //     Log.LogInfo($"Configured fallback textures for {adaptiveFallbackLayerCount} adaptive '{targetTileType}' layer(s) in '{tileset.tilesetId}'.");
            // }
            
            TileSetModule.AddCustomTileset(tileset);
            return;
        }
        
        if (obj is not GameObject go) return;

        UserInterfaceModule.RegisterModUI(go);
    }

    public void Update()
    {
    }
}
