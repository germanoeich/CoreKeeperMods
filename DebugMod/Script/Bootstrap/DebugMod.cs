using CoreLib;
using CoreLib.Submodule.Command;
using CoreLib.Util.Extension;
using PugMod;
using UnityEngine;
using Logger = CoreLib.Util.Logger;

public sealed class DebugMod : IMod
{
    public const string Version = "1.0.0";
    public const string ModId = "DebugMod";

    internal static readonly Logger Log = new("Debug Mod");

    internal static DebugMod Instance { get; private set; }
    internal static LoadedMod ModInfo { get; private set; }
    internal static string ModDirectory { get; private set; }

    public void EarlyInit()
    {
        Instance = this;

        Log.LogInfo($"Mod version: {Version}");
        CoreLibMod.LoadSubmodule(typeof(CommandModule));

        LoadedMod modInfo = this.GetModInfo();
        if (modInfo == null)
        {
            Log.LogError("Failed to load Debug Mod: mod metadata not found.");
            return;
        }

        ModInfo = modInfo;
        ModDirectory = API.ModLoader.GetDirectory(modInfo.ModId);
        API.ModLoader.ApplyHarmonyPatch(modInfo.ModId, typeof(DebugModReloadPatches));

        Log.LogInfo("Mod loaded successfully");
    }

    public void Init()
    {
    }

    public void Shutdown()
    {
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }

        ModInfo = null;
        ModDirectory = null;
    }

    public void ModObjectLoaded(Object obj)
    {
    }

    public bool CanBeUnloaded()
    {
        return true;
    }

    public void Update()
    {
    }
}
