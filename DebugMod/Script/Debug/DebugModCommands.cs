using System.Collections.Generic;
using System.Reflection;
using PugMod;
using QFSW.QC;
using UnityEngine.Scripting;

public static class DebugModCommands
{
    [Preserve]
    [Command("debug.fps", "Toggle the debug FPS/frame-time overlay.")]
    public static string ToggleFpsOverlay()
    {
        return DebugFpsOverlay.Toggle();
    }

    [Preserve]
    [Command("debug.fps", "Enable or disable the debug FPS/frame-time overlay.")]
    public static string SetFpsOverlay(bool enabled)
    {
        return DebugFpsOverlay.SetVisible(enabled);
    }

    [Preserve]
    [Command("debug.reloadmods", "Tick the local side-loader and queue a local mod reload if it finds changes.")]
    public static string ReloadMods()
    {
        if (Manager.mod == null)
        {
            return "Mod manager is not available.";
        }

        List<IModPlatform> modPlatforms = Manager.mod.ModPlatforms;
        if (modPlatforms == null || modPlatforms.Count == 0)
        {
            return "No mod platforms are registered.";
        }

        int sideLoaderCount = 0;
        bool sideLoaderDetectedChanges = false;
        for (int i = 0; i < modPlatforms.Count; i++)
        {
            IModPlatform modPlatform = modPlatforms[i];
            if (modPlatform == null || modPlatform.GetType().Name != "SideLoader")
            {
                continue;
            }

            sideLoaderCount++;
            TryCaptureSideLoaderSnapshot(modPlatform, out Dictionary<string, long> beforeSnapshot);
            modPlatform.Update();
            if (TryCaptureSideLoaderSnapshot(modPlatform, out Dictionary<string, long> afterSnapshot) &&
                !SnapshotsMatch(beforeSnapshot, afterSnapshot))
            {
                sideLoaderDetectedChanges = true;
            }
        }

        if (sideLoaderCount == 0)
        {
            return "SideLoader is not active in this run.";
        }

        if (Integration.Instance == null)
        {
            return "SideLoader ticked, but the mod integration is not available.";
        }

        if (!TryGetNeedsReload(Integration.Instance, out bool needsReload, out string message))
        {
            return message;
        }

        if (!needsReload && !sideLoaderDetectedChanges)
        {
            return "SideLoader scan complete. No local mod changes detected.";
        }

        List<string> blockers = FindReloadBlockers(Integration.Instance.LoadedMods);
        if (blockers.Count > 0)
        {
            return $"Reload blocked by: {string.Join(", ", blockers)}";
        }

        if (!needsReload)
        {
            return "SideLoader detected local mod changes.";
        }

        if (!TryQueueImmediateReload(Integration.Instance, out message))
        {
            return message;
        }

        return "SideLoader found local mod changes. Reload queued for the next update.";
    }

    private static bool TryCaptureSideLoaderSnapshot(IModPlatform modPlatform, out Dictionary<string, long> snapshot)
    {
        snapshot = null;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo loadedModsField = modPlatform.GetType().GetField("_loadedMods", flags);
        object loadedMods = loadedModsField?.GetValue(modPlatform);
        if (loadedMods == null)
        {
            return false;
        }

        PropertyInfo keysProperty = loadedMods.GetType().GetProperty("Keys");
        PropertyInfo itemProperty = loadedMods.GetType().GetProperty("Item");
        System.Type valueType = loadedMods.GetType().GetGenericArguments()[1];
        FieldInfo timestampField = valueType.GetField("Timestamp");
        if (keysProperty == null || itemProperty == null || timestampField == null)
        {
            return false;
        }

        snapshot = new Dictionary<string, long>();
        if (keysProperty.GetValue(loadedMods) is not IEnumerable<string> keys)
        {
            return false;
        }

        foreach (string key in keys)
        {
            object entry = itemProperty.GetValue(loadedMods, new object[] { key });
            if (entry == null)
            {
                continue;
            }

            object timestampValue = timestampField.GetValue(entry);
            if (timestampValue is long timestamp)
            {
                snapshot[key] = timestamp;
            }
        }

        return true;
    }

    private static bool SnapshotsMatch(
        Dictionary<string, long> beforeSnapshot,
        Dictionary<string, long> afterSnapshot)
    {
        if (beforeSnapshot == null || afterSnapshot == null)
        {
            return false;
        }

        if (beforeSnapshot.Count != afterSnapshot.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, long> pair in beforeSnapshot)
        {
            if (!afterSnapshot.TryGetValue(pair.Key, out long afterTimestamp) ||
                afterTimestamp != pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> FindReloadBlockers(IEnumerable<LoadedMod> loadedMods)
    {
        List<string> blockers = new();
        foreach (LoadedMod loadedMod in loadedMods)
        {
            if (loadedMod?.Handlers == null)
            {
                continue;
            }

            for (int i = 0; i < loadedMod.Handlers.Count; i++)
            {
                IMod handler = loadedMod.Handlers[i];
                if (handler == null || DebugModReloadPatches.CanBeUnloadedForDebugReload(handler))
                {
                    continue;
                }

                blockers.Add(handler.GetType().FullName ?? handler.GetType().Name);
            }
        }

        return blockers;
    }

    private static bool TryGetNeedsReload(object integration, out bool needsReload, out string message)
    {
        needsReload = false;
        message = null;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo needsReloadField = integration.GetType().GetField("_needsReload", flags);
        if (needsReloadField == null)
        {
            message = "SideLoader ticked, but loader internals were not found.";
            return false;
        }

        object needsReloadValue = needsReloadField.GetValue(integration);
        if (needsReloadValue is not bool reloadRequested)
        {
            message = "SideLoader ticked, but loader state could not be read.";
            return false;
        }

        needsReload = reloadRequested;
        return true;
    }

    private static bool TryQueueImmediateReload(object integration, out string message)
    {
        message = null;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo timeWaitingField = integration.GetType().GetField("_timeWaitingForReload", flags);
        if (timeWaitingField == null)
        {
            message = "SideLoader found changes, but the reload timer could not be updated.";
            return false;
        }

        timeWaitingField.SetValue(integration, float.PositiveInfinity);
        return true;
    }
}
