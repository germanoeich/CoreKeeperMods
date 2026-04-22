#if STORAGEPLUS_HOTSYNC
using System;
using System.Collections.Generic;

[Serializable]
public sealed class StorageTerminalHotSyncSnapshot
{
    public const string PrefabAssetPath = "Assets/Mods/StoragePlus/Prefabs/UI/StorageTerminalUI.prefab";
    public const string SnapshotRelativePath = "Dev/StorageTerminalUI.hot-sync.json";
    public const int CurrentVersion = 1;

    public int version = CurrentVersion;
    public string interfaceId = StorageTerminalUI.InterfaceId;
    public List<NodeSnapshot> nodes = new();

    [Serializable]
    public sealed class NodeSnapshot
    {
        public string path = string.Empty;
        public List<ComponentSnapshot> components = new();
    }

    [Serializable]
    public sealed class ComponentSnapshot
    {
        public string typeName = string.Empty;
        public int componentIndex;
        public List<FieldSnapshot> fields = new();
    }

    [Serializable]
    public sealed class FieldSnapshot
    {
        public string name = string.Empty;
        public StorageTerminalHotSyncValueKind kind;
        public string jsonValue;
        public ObjectReferenceSnapshot objectReference;
        public List<ObjectReferenceSnapshot> objectReferences;
    }

    [Serializable]
    public sealed class ObjectReferenceSnapshot
    {
        public StorageTerminalHotSyncObjectReferenceKind kind;
        public string path = string.Empty;
        public string typeName = string.Empty;
        public int componentIndex;
        public string assetName = string.Empty;
    }
}

public enum StorageTerminalHotSyncValueKind
{
    Json,
    ObjectReference,
    ObjectReferenceCollection
}

public enum StorageTerminalHotSyncObjectReferenceKind
{
    Null,
    HierarchyGameObject,
    HierarchyComponent,
    Asset
}
#endif
