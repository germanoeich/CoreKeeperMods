#if STORAGEPLUS_HOTSYNC
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using PugMod;
using UnityEngine;

public static partial class StorageTerminalHotSyncRuntime
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.None,
        ContractResolver = new FieldsOnlyContractResolver()
    };

    private static readonly Dictionary<Type, FieldInfo[]> ReflectionFieldCache = new();
    private static readonly Dictionary<Type, IComponentSnapshotHandler> SnapshotHandlers = CreateHandlers();
    private static readonly Dictionary<Type, HashSet<string>> ReflectionFieldExclusions = CreateReflectionFieldExclusions();

    private static long _lastAppliedSnapshotTicks = -1;
    private static long _lastObservedSnapshotTicks = -1;
    private static float _nextCheckTime;

    public static StorageTerminalHotSyncSnapshot CaptureSnapshot(GameObject root)
    {
        StorageTerminalHotSyncSnapshot snapshot = new();
        if (root == null)
        {
            return snapshot;
        }

        CaptureNode(root.transform, string.Empty, snapshot.nodes, root.transform);
        return snapshot;
    }

    public static bool TryApplyLatest(StorageTerminalUI ui, bool forceCheck = false)
    {
        if (ui == null || string.IsNullOrEmpty(StoragePlusMod.ModDirectory))
        {
            return false;
        }

        if (!forceCheck && Time.unscaledTime < _nextCheckTime)
        {
            return false;
        }

        _nextCheckTime = Time.unscaledTime + 0.2f;

        string snapshotPath = Path.Combine(StoragePlusMod.ModDirectory, StorageTerminalHotSyncSnapshot.SnapshotRelativePath);
        if (!File.Exists(snapshotPath))
        {
            return false;
        }

        long lastWriteTicks = File.GetLastWriteTimeUtc(snapshotPath).Ticks;
        if (!forceCheck && lastWriteTicks == _lastObservedSnapshotTicks)
        {
            return false;
        }

        _lastObservedSnapshotTicks = lastWriteTicks;

        try
        {
            string json = File.ReadAllText(snapshotPath);
            StorageTerminalHotSyncSnapshot snapshot = JsonConvert.DeserializeObject<StorageTerminalHotSyncSnapshot>(json, JsonSettings);
            if (snapshot == null)
            {
                Debug.LogWarning("[StoragePlus] Ignoring empty UI hot-sync snapshot.");
                return false;
            }

            if (snapshot.version != StorageTerminalHotSyncSnapshot.CurrentVersion)
            {
                Debug.LogWarning($"[StoragePlus] Ignoring UI hot-sync snapshot version {snapshot.version}. Expected {StorageTerminalHotSyncSnapshot.CurrentVersion}.");
                return false;
            }

            if (!string.Equals(snapshot.interfaceId, StorageTerminalUI.InterfaceId, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[StoragePlus] Ignoring UI hot-sync snapshot for interface '{snapshot.interfaceId}'.");
                return false;
            }

            bool applied = ApplySnapshot(ui.Root, snapshot);
            if (applied)
            {
                _lastAppliedSnapshotTicks = lastWriteTicks;
            }

            return applied;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[StoragePlus] Failed to apply UI hot-sync snapshot at {snapshotPath}.");
            Debug.LogException(exception);
            return false;
        }
    }

    private static bool ApplySnapshot(GameObject runtimeRoot, StorageTerminalHotSyncSnapshot snapshot)
    {
        if (runtimeRoot == null || snapshot == null)
        {
            return false;
        }

        Dictionary<string, Transform> runtimeNodes = BuildNodeLookup(runtimeRoot.transform);
        HashSet<string> snapshotPaths = new(StringComparer.Ordinal);
        for (int i = 0; i < snapshot.nodes.Count; i++)
        {
            snapshotPaths.Add(snapshot.nodes[i].path ?? string.Empty);
        }

        HashSet<string> parentsWithRuntimeExtras = FindParentsWithRuntimeExtras(runtimeNodes, snapshotPaths);
        ApplyContext context = new(runtimeRoot.transform, runtimeNodes, parentsWithRuntimeExtras);

        bool applied = false;
        for (int i = 0; i < snapshot.nodes.Count; i++)
        {
            StorageTerminalHotSyncSnapshot.NodeSnapshot nodeSnapshot = snapshot.nodes[i];
            if (!runtimeNodes.TryGetValue(nodeSnapshot.path ?? string.Empty, out Transform runtimeTransform))
            {
                continue;
            }

            for (int componentIndex = 0; componentIndex < nodeSnapshot.components.Count; componentIndex++)
            {
                StorageTerminalHotSyncSnapshot.ComponentSnapshot componentSnapshot = nodeSnapshot.components[componentIndex];
                Type componentType = ResolveType(componentSnapshot.typeName);
                if (componentType == null)
                {
                    continue;
                }

                Component runtimeComponent = GetComponentByTypeIndex(runtimeTransform.gameObject, componentType, componentSnapshot.componentIndex);
                if (runtimeComponent == null)
                {
                    continue;
                }

                if (!SnapshotHandlers.TryGetValue(runtimeComponent.GetType(), out IComponentSnapshotHandler handler))
                {
                    continue;
                }

                handler.Apply(runtimeComponent, nodeSnapshot.path ?? string.Empty, componentSnapshot, context);
                applied = true;
            }
        }

        if (!applied)
        {
            return false;
        }

        InvokeHotSyncHooks(runtimeRoot);
        return true;
    }

    private static void InvokeHotSyncHooks(GameObject runtimeRoot)
    {
        MonoBehaviour[] hotSyncAwareComponents = runtimeRoot.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
        for (int i = 0; i < hotSyncAwareComponents.Length; i++)
        {
            if (hotSyncAwareComponents[i] is IStorageTerminalHotSyncAware aware)
            {
                aware.OnHotSyncApplied();
            }
        }

        PugText[] pugTexts = runtimeRoot.GetComponentsInChildren<PugText>(includeInactive: true);
        for (int i = 0; i < pugTexts.Length; i++)
        {
            pugTexts[i].MarkUIComponentAsDirty();
            pugTexts[i].Render(rewindEffectAnims: false, activate: pugTexts[i].gameObject.activeSelf);
        }
    }
}
#endif
