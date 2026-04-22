#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Pug.Automation;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

internal enum AnchorMode
{
    Manual,
    WorldSpawnPoint
}

internal sealed class StorageNearbyChestComparerWindow : EditorWindow
{
    private const float DefaultRadius = 10f;

    private string _beforeWorldPath = string.Empty;
    private string _afterWorldPath = string.Empty;
    private float _radius = DefaultRadius;
    private AnchorMode _anchorMode = AnchorMode.Manual;
    private Vector2Int _manualCenter = Vector2Int.zero;
    private bool _distinguishVariation = true;
    private Vector2 _reportScroll;
    private string _report = string.Empty;
    private string _lastReportPath = string.Empty;

    [MenuItem("PugMod/StoragePlus/Compare Nearby Chest Contents")]
    private static void OpenWindow()
    {
        StorageNearbyChestComparerWindow window = GetWindow<StorageNearbyChestComparerWindow>();
        window.titleContent = new GUIContent("Chest Compare");
        window.minSize = new Vector2(720f, 520f);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Nearby Chest Save Comparison", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Loads two world saves offline, scans nearby chest-like inventories around a chosen center, and diffs the aggregate item totals.",
            MessageType.Info);

        EditorGUILayout.Space();
        DrawPathField("Before World", ref _beforeWorldPath, "gzip");
        EditorGUILayout.Space(6f);
        DrawPathField("After World", ref _afterWorldPath, "gzip");

        EditorGUILayout.Space();
        _radius = EditorGUILayout.FloatField("Radius", _radius);
        _radius = Mathf.Max(0.5f, _radius);
        _anchorMode = (AnchorMode)EditorGUILayout.EnumPopup("Anchor", _anchorMode);
        if (_anchorMode == AnchorMode.Manual)
        {
            _manualCenter = EditorGUILayout.Vector2IntField("Manual Center (X,Z)", _manualCenter);
        }
        _distinguishVariation = EditorGUILayout.ToggleLeft(
            "Treat item variations as different entries",
            _distinguishVariation);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Compare", GUILayout.Height(28f)))
            {
                RunComparison();
            }

            EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(_report));
            if (GUILayout.Button("Copy Report", GUILayout.Height(28f)))
            {
                EditorGUIUtility.systemCopyBuffer = _report;
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(_lastReportPath) || !File.Exists(_lastReportPath));
            if (GUILayout.Button("Reveal Report", GUILayout.Height(28f)))
            {
                EditorUtility.RevealInFinder(_lastReportPath);
            }
            EditorGUI.EndDisabledGroup();
        }

        EditorGUILayout.Space();
        if (!string.IsNullOrWhiteSpace(_lastReportPath))
        {
            EditorGUILayout.LabelField("Last Report", _lastReportPath);
        }

        EditorGUILayout.Space(4f);
        _reportScroll = EditorGUILayout.BeginScrollView(_reportScroll);
        EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private void DrawPathField(string label, ref string path, string extension)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            path = EditorGUILayout.TextField(label, path);
            if (GUILayout.Button("Browse", GUILayout.Width(80f)))
            {
                string directory = ResolveStartingDirectory(path);
                string selectedPath = EditorUtility.OpenFilePanel(label, directory, extension);
                if (!string.IsNullOrWhiteSpace(selectedPath))
                {
                    path = selectedPath;
                }
            }
        }
    }

    private static string ResolveStartingDirectory(string existingPath)
    {
        if (!string.IsNullOrWhiteSpace(existingPath))
        {
            string existingDirectory = Path.GetDirectoryName(existingPath);
            if (!string.IsNullOrWhiteSpace(existingDirectory) && Directory.Exists(existingDirectory))
            {
                return existingDirectory;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.Personal);
    }

    private void RunComparison()
    {
        try
        {
            NearbyChestComparisonResult result = StorageNearbyChestComparer.Compare(
                _beforeWorldPath,
                _afterWorldPath,
                _radius,
                _anchorMode,
                new int2(_manualCenter.x, _manualCenter.y),
                _distinguishVariation);

            _report = result.Report;
            _lastReportPath = result.ReportPath;
            _reportScroll = Vector2.zero;

            Debug.Log(_report);
            EditorGUIUtility.systemCopyBuffer = _report;

            EditorUtility.DisplayDialog(
                "Nearby Chest Comparison",
                $"Comparison complete.\nReport saved to:\n{result.ReportPath}",
                "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Nearby Chest Comparison", exception.Message, "OK");
        }
    }
}

internal static class StorageNearbyChestComparer
{
    private sealed class SnapshotResult
    {
        public string Label;
        public string WorldPath;
        public float3 ScanCenter;
        public int NearbyChestCount;
        public int UniqueItemCount;
        public int TotalItemCount;
        public string AnchorDescription;
        public bool UsedDatabaseBank;
        public string Diagnostics;
        public Dictionary<ItemKey, int> Counts;
    }

    private readonly struct ItemKey : IEquatable<ItemKey>
    {
        public readonly ObjectID ObjectId;
        public readonly int Variation;

        public ItemKey(ObjectID objectId, int variation)
        {
            ObjectId = objectId;
            Variation = variation;
        }

        public bool Equals(ItemKey other)
        {
            return ObjectId == other.ObjectId && Variation == other.Variation;
        }

        public override bool Equals(object obj)
        {
            return obj is ItemKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)ObjectId * 397) ^ Variation;
            }
        }
    }

    public static NearbyChestComparisonResult Compare(
        string beforeWorldPath,
        string afterWorldPath,
        float radius,
        AnchorMode anchorMode,
        int2 manualCenter,
        bool distinguishVariation)
    {
        ValidatePath(beforeWorldPath, "Before world");
        ValidatePath(afterWorldPath, "After world");

        if (radius <= 0f)
        {
            throw new InvalidOperationException("Radius must be greater than 0.");
        }

        SnapshotResult before = LoadSnapshot(
            "Before",
            beforeWorldPath,
            radius,
            distinguishVariation,
            anchorMode,
            manualCenter);

        SnapshotResult after = LoadSnapshot(
            "After",
            afterWorldPath,
            radius,
            distinguishVariation,
            anchorMode,
            manualCenter);

        List<DiffEntry> missing = new();
        List<DiffEntry> extra = new();
        BuildDiff(before.Counts, after.Counts, missing, extra);

        missing.Sort(DiffEntryComparer.Instance);
        extra.Sort(DiffEntryComparer.Instance);

        string report = BuildReport(before, after, radius, distinguishVariation, missing, extra);
        string reportPath = WriteReport(report);

        return new NearbyChestComparisonResult(report, reportPath);
    }

    private static SnapshotResult LoadSnapshot(
        string label,
        string worldPath,
        float radius,
        bool distinguishVariation,
        AnchorMode anchorMode,
        int2 manualCenter)
    {
        World world = new World($"StorageNearbyChestCompare_{label}");
        try
        {
            LoadWorldIntoWorld(world, worldPath, label);

            EntityManager entityManager = world.EntityManager;
            float3 scanCenter = ResolveScanCenter(entityManager, anchorMode, manualCenter, out string anchorDescription);
            Dictionary<ItemKey, int> counts = CollectNearbyChestCounts(
                entityManager,
                scanCenter,
                radius,
                distinguishVariation,
                out int nearbyChestCount,
                out int totalItemCount,
                out bool usedDatabaseBank,
                out string diagnostics);

            return new SnapshotResult
            {
                Label = label,
                WorldPath = worldPath,
                ScanCenter = scanCenter,
                NearbyChestCount = nearbyChestCount,
                UniqueItemCount = counts.Count,
                TotalItemCount = totalItemCount,
                AnchorDescription = anchorDescription,
                UsedDatabaseBank = usedDatabaseBank,
                Diagnostics = diagnostics,
                Counts = counts
            };
        }
        finally
        {
            if (world.IsCreated)
            {
                world.Dispose();
            }
        }
    }

    private static void LoadWorldIntoWorld(World world, string worldPath, string label)
    {
        byte[] compressedData = File.ReadAllBytes(worldPath);
        byte[] decompressedData = DecompressWorldData(compressedData, worldPath);
        int version = ReadSerializedWorldVersion(decompressedData, worldPath);

        world.EntityManager.PrepareForDeserialize();

        switch (version)
        {
            case 77:
                DeserializeCurrentWorld(world, decompressedData);
                return;
            case 76:
                DeserializeDots100World(world, decompressedData);
                return;
            default:
                throw new InvalidOperationException(
                    $"{label} world uses unsupported serialized version {version}. " +
                    "This tool currently supports versions 76 and 77.");
        }
    }

    private static byte[] DecompressWorldData(byte[] compressedData, string worldPath)
    {
        if (compressedData == null || compressedData.Length == 0)
        {
            throw new InvalidOperationException($"World file is empty: {worldPath}");
        }

        bool looksLikeGzip = compressedData.Length >= 2 &&
                             compressedData[0] == 0x1f &&
                             compressedData[1] == 0x8b;

        try
        {
            return Decompress(compressedData, looksLikeGzip ? CompressionKind.Gzip : CompressionKind.Brotli);
        }
        catch (Exception firstException)
        {
            CompressionKind fallbackKind = looksLikeGzip ? CompressionKind.Brotli : CompressionKind.Gzip;

            try
            {
                return Decompress(compressedData, fallbackKind);
            }
            catch (Exception secondException)
            {
                throw new InvalidOperationException(
                    $"Could not decompress world file '{worldPath}' as gzip or brotli.\n" +
                    $"Primary error: {firstException.Message}\n" +
                    $"Fallback error: {secondException.Message}");
            }
        }
    }

    private static byte[] Decompress(byte[] compressedData, CompressionKind kind)
    {
        using MemoryStream input = new MemoryStream(compressedData, writable: false);
        using Stream compressedStream = kind == CompressionKind.Gzip
            ? new GZipStream(input, CompressionMode.Decompress)
            : new BrotliStream(input, CompressionMode.Decompress);
        using MemoryStream output = new MemoryStream();
        compressedStream.CopyTo(output);
        return output.ToArray();
    }

    private static int ReadSerializedWorldVersion(byte[] decompressedData, string worldPath)
    {
        if (decompressedData.Length < 12)
        {
            throw new InvalidOperationException($"Decompressed world data is too small: {worldPath}");
        }

        const string expectedMagic = "DOTSBIN!";
        string actualMagic = Encoding.ASCII.GetString(decompressedData, 0, 8);
        if (!string.Equals(actualMagic, expectedMagic, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"World file '{worldPath}' does not start with the expected DOTSBIN header.");
        }

        return BitConverter.ToInt32(decompressedData, 8);
    }

    private static unsafe void DeserializeCurrentWorld(World world, byte[] decompressedData)
    {
        fixed (byte* ptr = decompressedData)
        {
            using MemoryBinaryReader reader = new MemoryBinaryReader(ptr, decompressedData.Length);
            ExclusiveEntityTransaction transaction = world.EntityManager.BeginExclusiveEntityTransaction();
            try
            {
                SerializeUtility.DeserializeWorld(transaction, reader);
            }
            finally
            {
                world.EntityManager.EndExclusiveEntityTransaction();
            }
        }
    }

    private static unsafe void DeserializeDots100World(World world, byte[] decompressedData)
    {
        fixed (byte* ptr = decompressedData)
        {
            using Pug.ECS.Serialization.DOTS100.MemoryBinaryReader reader =
                new Pug.ECS.Serialization.DOTS100.MemoryBinaryReader(ptr, decompressedData.Length);
            ExclusiveEntityTransaction transaction = world.EntityManager.BeginExclusiveEntityTransaction();
            try
            {
                Pug.ECS.Serialization.DOTS100.SerializeUtility.DeserializeWorld(transaction, reader);
            }
            finally
            {
                world.EntityManager.EndExclusiveEntityTransaction();
            }
        }
    }

    private static float3 ResolveScanCenter(
        EntityManager entityManager,
        AnchorMode anchorMode,
        int2 manualCenter,
        out string anchorDescription)
    {
        switch (anchorMode)
        {
            case AnchorMode.Manual:
                anchorDescription = $"manual center ({manualCenter.x}, {manualCenter.y})";
                return new float3(manualCenter.x, 0f, manualCenter.y);
            case AnchorMode.WorldSpawnPoint:
                if (TryFindWorldSpawnPoint(entityManager, out float3 spawnPoint))
                {
                    anchorDescription = $"world spawn point {FormatFloat3(spawnPoint)}";
                    return spawnPoint;
                }

                anchorDescription = $"spawn point not found, manual fallback ({manualCenter.x}, {manualCenter.y})";
                return new float3(manualCenter.x, 0f, manualCenter.y);
            default:
                throw new InvalidOperationException($"Unsupported anchor mode: {anchorMode}");
        }
    }

    private static bool TryFindWorldSpawnPoint(EntityManager entityManager, out float3 spawnPoint)
    {
        using EntityQuery spawnQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<SpawnPointCD>());
        using NativeArray<SpawnPointCD> spawnPoints = spawnQuery.ToComponentDataArray<SpawnPointCD>(Allocator.Temp);
        if (spawnPoints.Length == 0)
        {
            spawnPoint = default;
            return false;
        }

        spawnPoint = spawnPoints[0].position;
        float bestDistanceSq = math.lengthsq(spawnPoint.xz);
        for (int i = 1; i < spawnPoints.Length; i++)
        {
            float3 candidate = spawnPoints[i].position;
            float candidateDistanceSq = math.lengthsq(candidate.xz);
            if (candidateDistanceSq < bestDistanceSq)
            {
                spawnPoint = candidate;
                bestDistanceSq = candidateDistanceSq;
            }
        }

        return true;
    }

    private static Dictionary<ItemKey, int> CollectNearbyChestCounts(
        EntityManager entityManager,
        float3 center,
        float radius,
        bool distinguishVariation,
        out int nearbyChestCount,
        out int totalItemCount,
        out bool usedDatabaseBank,
        out string diagnostics)
    {
        Dictionary<ItemKey, int> counts = new();
        float radiusSq = radius * radius;
        nearbyChestCount = 0;
        totalItemCount = 0;
        usedDatabaseBank = TryGetDatabaseBlob(entityManager, out BlobAssetReference<PugDatabase.PugDatabaseBank> databaseBlob);
        int totalEntityCount = entityManager.UniversalQuery.CalculateEntityCount();

        HashSet<Entity> nearbyInventories = new();
        int totalSerializedInventoryEntityCount;
        int serializedInventoryInRadiusCount = 0;
        using EntityQuery serializedInventoryQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Translation, ObjectDataSerializedCD, ContainedObjectsSerializedBuffer>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
            .Build(entityManager);
        totalSerializedInventoryEntityCount = serializedInventoryQuery.CalculateEntityCount();
        using (NativeArray<Entity> serializedEntities = serializedInventoryQuery.ToEntityArray(Allocator.Temp))
        {
            for (int i = 0; i < serializedEntities.Length; i++)
            {
                Entity entity = serializedEntities[i];
                if (entityManager.HasComponent<Prefab>(entity))
                {
                    continue;
                }

                Translation translation = entityManager.GetComponentData<Translation>(entity);
                if (math.distancesq(center, translation.Value) > radiusSq)
                {
                    continue;
                }

                serializedInventoryInRadiusCount++;
                nearbyInventories.Add(entity);
            }
        }

        int totalStorageCount;
        int storageInRadiusCount = 0;
        using EntityQuery storageQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<StorageCD>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
            .Build(entityManager);
        totalStorageCount = storageQuery.CalculateEntityCount();
        using (NativeArray<StorageCD> storages = storageQuery.ToComponentDataArray<StorageCD>(Allocator.Temp))
        {
            for (int i = 0; i < storages.Length; i++)
            {
                StorageCD storage = storages[i];
                float3 storagePosition = new float3(storage.position.x, 0f, storage.position.y);
                if (math.distancesq(center, storagePosition) > radiusSq || storage.inventoryEntity == Entity.Null)
                {
                    continue;
                }

                storageInRadiusCount++;
                nearbyInventories.Add(storage.inventoryEntity);
            }
        }

        int totalInventoryEntityCount;
        int rawInventoryInRadiusCount = 0;
        if (nearbyInventories.Count == 0)
        {
            using EntityQuery chestQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<InventoryBuffer, ContainedObjectsBuffer, LocalTransform>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(entityManager);
            totalInventoryEntityCount = chestQuery.CalculateEntityCount();

            using NativeArray<Entity> entities = chestQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (entityManager.HasComponent<Prefab>(entity))
                {
                    continue;
                }

                LocalTransform transform = entityManager.GetComponentData<LocalTransform>(entity);
                if (math.distancesq(center, transform.Position) > radiusSq)
                {
                    continue;
                }

                rawInventoryInRadiusCount++;
                nearbyInventories.Add(entity);
            }
        }
        else
        {
            using EntityQuery chestQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<InventoryBuffer, ContainedObjectsBuffer, LocalTransform>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(entityManager);
            totalInventoryEntityCount = chestQuery.CalculateEntityCount();
        }

        diagnostics =
            $"World entities: {totalEntityCount}, " +
            $"Serialized inventory entities: {totalSerializedInventoryEntityCount}, " +
            $"Serialized inventory entities in radius: {serializedInventoryInRadiusCount}, " +
            $"StorageCD entities: {totalStorageCount}, " +
            $"StorageCD in radius: {storageInRadiusCount}, " +
            $"Inventory entities: {totalInventoryEntityCount}, " +
            $"Raw inventory entities in radius: {rawInventoryInRadiusCount}, " +
            $"Candidate inventories: {nearbyInventories.Count}";

        foreach (Entity inventoryEntity in nearbyInventories)
        {
            if (!entityManager.Exists(inventoryEntity))
            {
                continue;
            }

            if (entityManager.HasBuffer<ContainedObjectsSerializedBuffer>(inventoryEntity))
            {
                DynamicBuffer<ContainedObjectsSerializedBuffer> serializedContents = entityManager.GetBuffer<ContainedObjectsSerializedBuffer>(inventoryEntity);
                if (serializedContents.Length == 0)
                {
                    continue;
                }

                nearbyChestCount++;
                for (int slot = 0; slot < serializedContents.Length; slot++)
                {
                    ObjectDataSerializedCD containedObject = serializedContents[slot].ObjectData;
                    if (containedObject.ObjectID == ObjectID.None)
                    {
                        continue;
                    }

                    int contribution = GetCountContribution(
                        containedObject.ObjectID,
                        containedObject.Amount,
                        containedObject.Variation,
                        usedDatabaseBank,
                        databaseBlob);
                    if (contribution <= 0)
                    {
                        continue;
                    }

                    ItemKey key = new ItemKey(containedObject.ObjectID, distinguishVariation ? containedObject.Variation : 0);
                    if (counts.TryGetValue(key, out int existingCount))
                    {
                        counts[key] = existingCount + contribution;
                    }
                    else
                    {
                        counts.Add(key, contribution);
                    }

                    totalItemCount += contribution;
                }

                continue;
            }

            if (!entityManager.HasBuffer<InventoryBuffer>(inventoryEntity) ||
                !entityManager.HasBuffer<ContainedObjectsBuffer>(inventoryEntity))
            {
                continue;
            }

            if (usedDatabaseBank && entityManager.HasComponent<ObjectDataCD>(inventoryEntity))
            {
                ObjectDataCD objectData = entityManager.GetComponentData<ObjectDataCD>(inventoryEntity);
                if (objectData.objectID == ObjectID.None)
                {
                    continue;
                }

                ref PugDatabase.EntityObjectInfo objectInfo = ref PugDatabase.GetEntityObjectInfo(
                    objectData.objectID,
                    databaseBlob,
                    objectData.variation);
                if (objectInfo.objectType != ObjectType.PlaceablePrefab)
                {
                    continue;
                }
            }

            DynamicBuffer<InventoryBuffer> inventories = entityManager.GetBuffer<InventoryBuffer>(inventoryEntity);
            DynamicBuffer<ContainedObjectsBuffer> contents = entityManager.GetBuffer<ContainedObjectsBuffer>(inventoryEntity);
            if (inventories.Length == 0 || contents.Length == 0)
            {
                continue;
            }

            nearbyChestCount++;
            for (int inventoryIndex = 0; inventoryIndex < inventories.Length; inventoryIndex++)
            {
                InventoryBuffer inventory = inventories[inventoryIndex];
                int endSlot = math.min(contents.Length, inventory.startIndex + inventory.size);
                for (int slot = inventory.startIndex; slot < endSlot; slot++)
                {
                    ContainedObjectsBuffer containedObject = contents[slot];
                    if (containedObject.objectID == ObjectID.None)
                    {
                        continue;
                    }

                    int contribution = GetCountContribution(containedObject, usedDatabaseBank, databaseBlob);
                    if (contribution <= 0)
                    {
                        continue;
                    }

                    ItemKey key = new ItemKey(containedObject.objectID, distinguishVariation ? containedObject.variation : 0);
                    if (counts.TryGetValue(key, out int existingCount))
                    {
                        counts[key] = existingCount + contribution;
                    }
                    else
                    {
                        counts.Add(key, contribution);
                    }

                    totalItemCount += contribution;
                }
            }
        }

        return counts;
    }

    private static bool TryGetDatabaseBlob(EntityManager entityManager, out BlobAssetReference<PugDatabase.PugDatabaseBank> databaseBlob)
    {
        using EntityQuery databaseQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PugDatabase.DatabaseBankCD>());
        if (!databaseQuery.TryGetSingleton(out PugDatabase.DatabaseBankCD databaseBank))
        {
            databaseBlob = default;
            return false;
        }

        databaseBlob = databaseBank.databaseBankBlob;
        return databaseBlob.IsCreated;
    }

    private static int GetCountContribution(
        ContainedObjectsBuffer containedObject,
        bool usedDatabaseBank,
        BlobAssetReference<PugDatabase.PugDatabaseBank> databaseBlob)
    {
        return GetCountContribution(
            containedObject.objectID,
            containedObject.amount,
            containedObject.variation,
            usedDatabaseBank,
            databaseBlob);
    }

    private static int GetCountContribution(
        ObjectID objectId,
        int amount,
        int variation,
        bool usedDatabaseBank,
        BlobAssetReference<PugDatabase.PugDatabaseBank> databaseBlob)
    {
        if (!usedDatabaseBank)
        {
            return amount > 0 ? amount : 1;
        }

        bool isStackable = PugDatabase.GetEntityObjectInfo(
            objectId,
            databaseBlob,
            variation).isStackable;

        if (!isStackable)
        {
            return amount > 0 ? 1 : 0;
        }

        return math.max(amount, 0);
    }

    private static void BuildDiff(
        Dictionary<ItemKey, int> beforeCounts,
        Dictionary<ItemKey, int> afterCounts,
        List<DiffEntry> missing,
        List<DiffEntry> extra)
    {
        foreach (KeyValuePair<ItemKey, int> beforeEntry in beforeCounts)
        {
            afterCounts.TryGetValue(beforeEntry.Key, out int afterAmount);
            int delta = beforeEntry.Value - afterAmount;
            if (delta > 0)
            {
                missing.Add(new DiffEntry(beforeEntry.Key, delta));
            }
        }

        foreach (KeyValuePair<ItemKey, int> afterEntry in afterCounts)
        {
            beforeCounts.TryGetValue(afterEntry.Key, out int beforeAmount);
            int delta = afterEntry.Value - beforeAmount;
            if (delta > 0)
            {
                extra.Add(new DiffEntry(afterEntry.Key, delta));
            }
        }
    }

    private static string BuildReport(
        SnapshotResult before,
        SnapshotResult after,
        float radius,
        bool distinguishVariation,
        List<DiffEntry> missing,
        List<DiffEntry> extra)
    {
        StringBuilder builder = new StringBuilder(4096);
        builder.AppendLine("StoragePlus Nearby Chest Comparison");
        builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Radius: {radius.ToString("0.##", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"Grouping: {(distinguishVariation ? "ObjectID + variation" : "ObjectID only")}");
        builder.AppendLine();

        AppendSnapshotSummary(builder, before);
        builder.AppendLine();
        AppendSnapshotSummary(builder, after);
        builder.AppendLine();

        if (missing.Count == 0 && extra.Count == 0)
        {
            builder.AppendLine("Result: exact aggregate match.");
            return builder.ToString();
        }

        builder.AppendLine("Missing From After");
        if (missing.Count == 0)
        {
            builder.AppendLine("  none");
        }
        else
        {
            for (int i = 0; i < missing.Count; i++)
            {
                builder.AppendLine($"  {FormatItemKey(missing[i].Key, distinguishVariation)} x{missing[i].Amount}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Extra In After");
        if (extra.Count == 0)
        {
            builder.AppendLine("  none");
        }
        else
        {
            for (int i = 0; i < extra.Count; i++)
            {
                builder.AppendLine($"  {FormatItemKey(extra[i].Key, distinguishVariation)} x{extra[i].Amount}");
            }
        }

        return builder.ToString();
    }

    private static void AppendSnapshotSummary(StringBuilder builder, SnapshotResult snapshot)
    {
        builder.AppendLine($"{snapshot.Label} Snapshot");
        builder.AppendLine($"  World: {snapshot.WorldPath}");
        builder.AppendLine($"  Anchor: {snapshot.AnchorDescription}");
        builder.AppendLine($"  Scan center: {FormatFloat3(snapshot.ScanCenter)}");
        builder.AppendLine($"  Nearby chests found: {snapshot.NearbyChestCount}");
        builder.AppendLine($"  Unique item entries: {snapshot.UniqueItemCount}");
        builder.AppendLine($"  Total counted items: {snapshot.TotalItemCount}");
        builder.AppendLine($"  Database blob available: {snapshot.UsedDatabaseBank}");
        builder.AppendLine($"  Diagnostics: {snapshot.Diagnostics}");
    }

    private static string FormatFloat3(float3 value)
    {
        return $"({value.x.ToString("0.##", CultureInfo.InvariantCulture)}, {value.y.ToString("0.##", CultureInfo.InvariantCulture)}, {value.z.ToString("0.##", CultureInfo.InvariantCulture)})";
    }

    private static string FormatItemKey(ItemKey key, bool distinguishVariation)
    {
        string baseName = Enum.GetName(typeof(ObjectID), key.ObjectId) ?? $"ObjectID {(int)key.ObjectId}";
        if (!distinguishVariation)
        {
            return baseName;
        }

        return $"{baseName} [variation {key.Variation}]";
    }

    private static string WriteReport(string report)
    {
        string reportDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Temp");
        Directory.CreateDirectory(reportDirectory);

        string reportPath = Path.Combine(
            reportDirectory,
            $"StoragePlusNearbyChestComparison_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

        File.WriteAllText(reportPath, report);
        return reportPath;
    }

    private static void ValidatePath(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"{label} path is empty.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{label} file was not found.", path);
        }
    }

    private readonly struct DiffEntry
    {
        public readonly ItemKey Key;
        public readonly int Amount;

        public DiffEntry(ItemKey key, int amount)
        {
            Key = key;
            Amount = amount;
        }
    }

    private sealed class DiffEntryComparer : IComparer<DiffEntry>
    {
        public static readonly DiffEntryComparer Instance = new();

        public int Compare(DiffEntry left, DiffEntry right)
        {
            int amountComparison = right.Amount.CompareTo(left.Amount);
            if (amountComparison != 0)
            {
                return amountComparison;
            }

            int objectComparison = left.Key.ObjectId.CompareTo(right.Key.ObjectId);
            if (objectComparison != 0)
            {
                return objectComparison;
            }

            return left.Key.Variation.CompareTo(right.Key.Variation);
        }
    }

    private enum CompressionKind
    {
        Gzip,
        Brotli
    }

}

internal readonly struct NearbyChestComparisonResult
{
    public readonly string Report;
    public readonly string ReportPath;

    public NearbyChestComparisonResult(string report, string reportPath)
    {
        Report = report;
        ReportPath = reportPath;
    }
}
#endif
