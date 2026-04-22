#if STORAGEPLUS_HOTSYNC
using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class StorageTerminalHotSyncRuntime
{
    private static void CaptureNode(
        Transform transform,
        string path,
        List<StorageTerminalHotSyncSnapshot.NodeSnapshot> output,
        Transform snapshotRoot)
    {
        StorageTerminalHotSyncSnapshot.NodeSnapshot nodeSnapshot = new()
        {
            path = path
        };

        Component[] components = transform.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null)
            {
                continue;
            }

            if (!SnapshotHandlers.TryGetValue(component.GetType(), out IComponentSnapshotHandler handler))
            {
                continue;
            }

            StorageTerminalHotSyncSnapshot.ComponentSnapshot componentSnapshot = handler.Capture(component, snapshotRoot);
            if (componentSnapshot != null && componentSnapshot.fields.Count > 0)
            {
                nodeSnapshot.components.Add(componentSnapshot);
            }
        }

        output.Add(nodeSnapshot);

        Dictionary<string, int> sameNameCounts = new(StringComparer.Ordinal);
        for (int childIndex = 0; childIndex < transform.childCount; childIndex++)
        {
            Transform child = transform.GetChild(childIndex);
            string childName = child.name ?? string.Empty;
            sameNameCounts.TryGetValue(childName, out int sameNameIndex);
            sameNameCounts[childName] = sameNameIndex + 1;

            string childPath = string.IsNullOrEmpty(path)
                ? $"{childName}[{sameNameIndex}]"
                : $"{path}/{childName}[{sameNameIndex}]";

            CaptureNode(child, childPath, output, snapshotRoot);
        }
    }

    private static Dictionary<string, Transform> BuildNodeLookup(Transform root)
    {
        Dictionary<string, Transform> output = new(StringComparer.Ordinal);
        BuildNodeLookupRecursive(root, string.Empty, output);
        return output;
    }

    private static void BuildNodeLookupRecursive(Transform transform, string path, Dictionary<string, Transform> output)
    {
        output[path] = transform;

        Dictionary<string, int> sameNameCounts = new(StringComparer.Ordinal);
        for (int childIndex = 0; childIndex < transform.childCount; childIndex++)
        {
            Transform child = transform.GetChild(childIndex);
            string childName = child.name ?? string.Empty;
            sameNameCounts.TryGetValue(childName, out int sameNameIndex);
            sameNameCounts[childName] = sameNameIndex + 1;

            string childPath = string.IsNullOrEmpty(path)
                ? $"{childName}[{sameNameIndex}]"
                : $"{path}/{childName}[{sameNameIndex}]";

            BuildNodeLookupRecursive(child, childPath, output);
        }
    }

    private static HashSet<string> FindParentsWithRuntimeExtras(
        Dictionary<string, Transform> runtimeNodes,
        HashSet<string> snapshotPaths)
    {
        HashSet<string> parentsWithRuntimeExtras = new(StringComparer.Ordinal);
        foreach (string runtimePath in runtimeNodes.Keys)
        {
            if (snapshotPaths.Contains(runtimePath))
            {
                continue;
            }

            string parentPath = GetParentPath(runtimePath);
            parentsWithRuntimeExtras.Add(parentPath);
        }

        return parentsWithRuntimeExtras;
    }

    private static string GetParentPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        int separatorIndex = path.LastIndexOf('/');
        return separatorIndex < 0 ? string.Empty : path[..separatorIndex];
    }

    private static int GetComponentTypeIndex(GameObject gameObject, Component component)
    {
        Type type = component.GetType();
        Component[] matchingComponents = gameObject.GetComponents(type);
        for (int i = 0; i < matchingComponents.Length; i++)
        {
            if (ReferenceEquals(matchingComponents[i], component))
            {
                return i;
            }
        }

        return 0;
    }

    private static Component GetComponentByTypeIndex(GameObject gameObject, Type type, int componentIndex)
    {
        Component[] matchingComponents = gameObject.GetComponents(type);
        return componentIndex >= 0 && componentIndex < matchingComponents.Length ? matchingComponents[componentIndex] : null;
    }

    private static string GetTransformPath(Transform root, Transform target)
    {
        if (ReferenceEquals(root, target))
        {
            return string.Empty;
        }

        List<string> segments = new();
        Transform current = target;
        while (current != null && !ReferenceEquals(current, root))
        {
            Transform parent = current.parent;
            if (parent == null)
            {
                break;
            }

            int sameNameIndex = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform sibling = parent.GetChild(i);
                if (ReferenceEquals(sibling, current))
                {
                    break;
                }

                if (string.Equals(sibling.name, current.name, StringComparison.Ordinal))
                {
                    sameNameIndex++;
                }
            }

            segments.Add($"{current.name}[{sameNameIndex}]");
            current = parent;
        }

        segments.Reverse();
        return string.Join("/", segments);
    }

    private static string GetTypeId(Type type)
    {
        return type == null ? string.Empty : $"{type.FullName}, {type.Assembly.GetName().Name}";
    }

    private static Type ResolveType(string typeName)
    {
        return string.IsNullOrEmpty(typeName) ? null : Type.GetType(typeName);
    }
}
#endif
