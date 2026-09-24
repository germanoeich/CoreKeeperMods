using CoreLib;
using CoreLib.Submodule.Entity;
using CoreLib.Submodule.TileSet;
using CoreLib.Submodule.UserInterface;
using CoreLib.Util.Extension;
using PugMod;
using UnityEngine;
using Logger = CoreLib.Util.Logger;

public class StoragePlusMod : IMod
{
    public const string VERSION = "1.0.0-ck1.3-local";
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
        StorageTilesetPatch.Initialize(modInfo.Assets);
        API.ModLoader.ApplyHarmonyPatch(modInfo.ModId, typeof(StorageTilesetPatch));
        ModPlaceableObjectConversionPatch.Reset();
        API.ModLoader.ApplyHarmonyPatch(modInfo.ModId, typeof(ModPlaceableObjectConversionPatch));
        API.ModLoader.ApplyHarmonyPatch(modInfo.ModId, typeof(StoragePipeMaterialPatch));
        //modInfo.TryLoadBurstAssembly();

        Log.LogInfo("Mod loaded successfully");
    }

    public void Init()
    {
    }

    public void Shutdown()
    {
        StorageTilesetPatch.Reset();
    }

    public void ModObjectLoaded(Object obj)
    {
        if (obj is not GameObject go) return;

        SpriteObjectMaterialSwap.Apply(go);
        UserInterfaceModule.RegisterModUI(go);
    }

    public void Update()
    {
    }
}
