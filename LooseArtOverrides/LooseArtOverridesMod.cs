using System;
using System.Collections.Generic;
using System.IO;
using PugMod;
using UnityEngine;

public sealed class LooseArtOverridesMod : IMod
{
    private const string Version = "0.1.0";
    private const string ModName = "LooseArtOverrides";

    private readonly Dictionary<UnityEngine.Object, LoadedMod> _assetOwners = new();
    private readonly Dictionary<string, Dictionary<string, string>> _overrideFilesByModName = new(StringComparer.Ordinal);

    private string _gameModsDirectory = string.Empty;
    private bool _assetOwnersBuilt;

    public void EarlyInit()
    {
        _gameModsDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Mods"));
        BuildAssetOwnerLookup();

        Debug.Log($"[{ModName}] v{Version} ready. Looking for top-level PNG overrides in {_gameModsDirectory}/<TargetMod>/Art");
    }

    public void Init()
    {
    }

    public void Shutdown()
    {
    }

    public void Update()
    {
    }

    public void ModObjectLoaded(UnityEngine.Object obj)
    {
        if (obj is not Texture2D texture)
        {
            return;
        }

        EnsureAssetOwnerLookup();
        if (!_assetOwners.TryGetValue(texture, out LoadedMod owner) || owner?.Metadata == null)
        {
            return;
        }

        if (string.Equals(owner.Metadata.name, ModName, StringComparison.Ordinal))
        {
            return;
        }

        if (!TryGetOverridePath(owner.Metadata.name, texture.name, out string overridePath))
        {
            return;
        }

        ApplyOverride(texture, owner.Metadata.name, overridePath);
    }

    private void EnsureAssetOwnerLookup()
    {
        if (_assetOwnersBuilt && _assetOwners.Count > 0)
        {
            return;
        }

        BuildAssetOwnerLookup();
    }

    private void BuildAssetOwnerLookup()
    {
        _assetOwners.Clear();

        if (API.ModLoader?.LoadedMods == null)
        {
            _assetOwnersBuilt = true;
            return;
        }

        foreach (LoadedMod loadedMod in API.ModLoader.LoadedMods)
        {
            if (loadedMod?.Assets == null)
            {
                continue;
            }

            for (int i = 0; i < loadedMod.Assets.Count; i++)
            {
                UnityEngine.Object asset = loadedMod.Assets[i];
                if (asset == null || _assetOwners.ContainsKey(asset))
                {
                    continue;
                }

                _assetOwners.Add(asset, loadedMod);
            }
        }

        _assetOwnersBuilt = true;
    }

    private bool TryGetOverridePath(string targetModName, string textureName, out string overridePath)
    {
        overridePath = null;
        Dictionary<string, string> overrides = GetOrCreateOverrideFiles(targetModName);
        return overrides.TryGetValue(textureName, out overridePath);
    }

    private Dictionary<string, string> GetOrCreateOverrideFiles(string targetModName)
    {
        if (_overrideFilesByModName.TryGetValue(targetModName, out Dictionary<string, string> overrideFiles))
        {
            return overrideFiles;
        }

        overrideFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        string overrideDirectory = Path.Combine(_gameModsDirectory, targetModName, "Art");
        if (Directory.Exists(overrideDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(overrideDirectory, "*.png", SearchOption.TopDirectoryOnly))
            {
                string textureName = Path.GetFileNameWithoutExtension(file);
                if (overrideFiles.ContainsKey(textureName))
                {
                    Debug.LogWarning($"[{ModName}] Ignoring duplicate override '{textureName}' in {overrideDirectory}");
                    continue;
                }

                overrideFiles.Add(textureName, file);
            }

            if (overrideFiles.Count > 0)
            {
                Debug.Log($"[{ModName}] Found {overrideFiles.Count} override texture(s) for {targetModName} in {overrideDirectory}");
            }
        }

        _overrideFilesByModName.Add(targetModName, overrideFiles);
        return overrideFiles;
    }

    private static void ApplyOverride(Texture2D texture, string targetModName, string overridePath)
    {
        try
        {
            int originalWidth = texture.width;
            int originalHeight = texture.height;
            byte[] pngBytes = File.ReadAllBytes(overridePath);

            if (!texture.LoadImage(pngBytes, markNonReadable: false))
            {
                Debug.LogWarning($"[{ModName}] Failed to load override '{overridePath}' for texture '{texture.name}'");
                return;
            }

            texture.name = Path.GetFileNameWithoutExtension(overridePath);
            Debug.Log($"[{ModName}] Applied override for {targetModName}/{texture.name}: {originalWidth}x{originalHeight} -> {texture.width}x{texture.height}");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }
}
