#if STORAGEPLUS_HOTSYNC
using System.IO;
using UnityEditor;
using UnityEngine;

internal sealed class StorageTerminalHotSyncExporter : AssetPostprocessor
{
    private const string GameInstallPathKey = "PugMod/SDKWindow/GamePath";

    private static bool _exportQueued;
    private static bool _hasWarnedAboutMissingInstallPath;

    [MenuItem("PugMod/StoragePlus/Export UI Hot Sync Snapshot")]
    private static void ExportSnapshotMenu()
    {
        ExportSnapshot(showDialogs: true);
    }

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (!ContainsWatchedPrefab(importedAssets) && !ContainsWatchedPrefab(movedAssets))
        {
            return;
        }

        QueueSnapshotExport();
    }

    private static void QueueSnapshotExport()
    {
        if (_exportQueued)
        {
            return;
        }

        _exportQueued = true;
        EditorApplication.delayCall += () =>
        {
            _exportQueued = false;
            ExportSnapshot(showDialogs: false);
        };
    }

    private static bool ContainsWatchedPrefab(string[] paths)
    {
        if (paths == null)
        {
            return false;
        }

        for (int i = 0; i < paths.Length; i++)
        {
            if (string.Equals(paths[i], StorageTerminalHotSyncSnapshot.PrefabAssetPath, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void ExportSnapshot(bool showDialogs)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StorageTerminalHotSyncSnapshot.PrefabAssetPath);
        if (prefab == null)
        {
            if (showDialogs)
            {
                EditorUtility.DisplayDialog(
                    "StoragePlus Hot Sync",
                    $"Could not load prefab at '{StorageTerminalHotSyncSnapshot.PrefabAssetPath}'.",
                    "OK");
            }

            return;
        }

        if (!TryResolveInstalledModDirectory(out string installedModDirectory, out string error))
        {
            if (showDialogs)
            {
                EditorUtility.DisplayDialog("StoragePlus Hot Sync", error, "OK");
            }
            else if (!_hasWarnedAboutMissingInstallPath)
            {
                _hasWarnedAboutMissingInstallPath = true;
                Debug.LogWarning($"[StoragePlus] UI hot-sync export skipped. {error}");
            }

            return;
        }

        _hasWarnedAboutMissingInstallPath = false;

        var snapshot = StorageTerminalHotSyncRuntime.CaptureSnapshot(prefab);
        string snapshotJson = JsonUtility.ToJson(snapshot, prettyPrint: true);
        string snapshotPath = Path.Combine(installedModDirectory, StorageTerminalHotSyncSnapshot.SnapshotRelativePath);
        string snapshotDirectory = Path.GetDirectoryName(snapshotPath);
        if (!string.IsNullOrEmpty(snapshotDirectory))
        {
            Directory.CreateDirectory(snapshotDirectory);
        }

        File.WriteAllText(snapshotPath, snapshotJson);

        if (showDialogs)
        {
            EditorUtility.DisplayDialog(
                "StoragePlus Hot Sync",
                $"Exported UI hot-sync snapshot to:\n{snapshotPath}",
                "OK");
        }
        else
        {
            Debug.Log($"[StoragePlus] Exported UI hot-sync snapshot to {snapshotPath}");
        }
    }

    private static bool TryResolveInstalledModDirectory(out string installedModDirectory, out string error)
    {
        installedModDirectory = null;
        error = null;

        if (!EditorPrefs.HasKey(GameInstallPathKey))
        {
            error = "Set the Core Keeper install path in the Mod SDK window before using UI hot sync.";
            return false;
        }

        string configuredPath = EditorPrefs.GetString(GameInstallPathKey);
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            error = "The configured game install path is empty.";
            return false;
        }

        string modsDirectory = ResolveModsDirectory(configuredPath);
        if (string.IsNullOrEmpty(modsDirectory))
        {
            error = $"Could not resolve a Mods directory from '{configuredPath}'.";
            return false;
        }

        installedModDirectory = Path.Combine(modsDirectory, StoragePlusMod.MOD_ID);
        Directory.CreateDirectory(installedModDirectory);
        return true;
    }

    private static string ResolveModsDirectory(string configuredPath)
    {
        if (Directory.Exists(Path.Combine(configuredPath, "CoreKeeper_Data")))
        {
            return Path.Combine(configuredPath, "CoreKeeper_Data", "StreamingAssets", "Mods");
        }

        if (Directory.Exists(Path.Combine(configuredPath, "CoreKeeperServer_Data")))
        {
            return Path.Combine(configuredPath, "CoreKeeperServer_Data", "StreamingAssets", "Mods");
        }

        if (Directory.Exists(Path.Combine(configuredPath, "Assets")))
        {
            return Path.Combine(configuredPath, "Assets", "StreamingAssets", "Mods");
        }

        string normalized = configuredPath.Replace('\\', '/').TrimEnd('/');
        if (normalized.EndsWith("/StreamingAssets/Mods", System.StringComparison.OrdinalIgnoreCase))
        {
            return configuredPath;
        }

        return null;
    }
}
#endif
