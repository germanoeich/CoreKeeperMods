using System.Collections.Generic;
using System.Text;
using QFSW.QC;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Scripting;

public static class StoragePlusDebugCommands
{
    [Preserve]
    [Command("storageplus.listLoadedNetworkEntities", "Lists loaded StoragePlus pipes, connectors, droppers, and terminals and reports connector storage/network state.", QFSW.QC.Platform.AllPlatforms, MonoTargetType.Single)]
    public static void ListLoadedNetworkEntities()
    {
        World serverWorld = Manager.ecs.ServerWorld;
        if (serverWorld == null)
        {
            LogToConsole("StoragePlus debug commands require the server world. Host a world first.");
            return;
        }

        EntityManager entityManager = serverWorld.EntityManager;
        using EntityQuery databaseQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PugDatabase.DatabaseBankCD>());
        if (databaseQuery.IsEmptyIgnoreFilter)
        {
            LogToConsole("StoragePlus debug command could not find the database singleton.");
            return;
        }

        PugDatabase.DatabaseBankCD databaseBank = databaseQuery.GetSingleton<PugDatabase.DatabaseBankCD>();
        StorageNetworkWorldCache cache = StorageNetworkWorldCacheRegistry.GetOrCreate(serverWorld);
        cache.EnsureBuilt(databaseBank);

        using EntityQuery connectorQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<StorageConnectorTag>(),
            ComponentType.Exclude<StorageCraftingRelayTag>(),
            ComponentType.ReadOnly<LocalTransform>());
        using EntityQuery pipeQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<StoragePipeTag>(),
            ComponentType.ReadOnly<LocalTransform>());
        using EntityQuery terminalQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<StorageCraftingRelayTag>(),
            ComponentType.ReadOnly<LocalTransform>());

        using NativeArray<Entity> connectorEntities = connectorQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Entity> pipeEntities = pipeQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Entity> terminalEntities = terminalQuery.ToEntityArray(Allocator.Temp);

        List<Entity> connectors = new();
        List<Entity> droppers = new();
        for (int i = 0; i < connectorEntities.Length; i++)
        {
            Entity entity = connectorEntities[i];
            if (entityManager.HasComponent<ItemDropperTimingCD>(entity))
            {
                droppers.Add(entity);
            }
            else
            {
                connectors.Add(entity);
            }
        }

        SortByTile(connectors, entityManager);
        SortByTile(droppers, entityManager);
        List<Entity> pipes = new(pipeEntities.Length);
        for (int i = 0; i < pipeEntities.Length; i++)
        {
            pipes.Add(pipeEntities[i]);
        }
        SortByTile(pipes, entityManager);

        List<Entity> terminals = new(terminalEntities.Length);
        for (int i = 0; i < terminalEntities.Length; i++)
        {
            terminals.Add(terminalEntities[i]);
        }
        SortByTile(terminals, entityManager);

        StringBuilder builder = new StringBuilder(4096);
        builder.AppendLine("StoragePlus loaded entities");
        builder.Append("connectors=").Append(connectors.Count)
            .Append(" droppers=").Append(droppers.Count)
            .Append(" pipes=").Append(pipes.Count)
            .Append(" terminals=").Append(terminals.Count)
            .AppendLine();

        AppendConnectors(builder, "Connectors", connectors, entityManager, cache);
        AppendConnectors(builder, "Droppers", droppers, entityManager, cache);
        AppendPipes(builder, pipes, entityManager);
        AppendTerminals(builder, terminals, entityManager, cache);

        string report = builder.ToString().TrimEnd();
        LogToConsole(report);
    }

    private static void AppendConnectors(
        StringBuilder builder,
        string title,
        List<Entity> connectors,
        EntityManager entityManager,
        StorageNetworkWorldCache cache)
    {
        builder.AppendLine();
        builder.Append(title).Append(" (").Append(connectors.Count).AppendLine("):");
        if (connectors.Count == 0)
        {
            builder.AppendLine("  none");
            return;
        }

        for (int i = 0; i < connectors.Count; i++)
        {
            Entity connectorEntity = connectors[i];
            LocalTransform transform = entityManager.GetComponentData<LocalTransform>(connectorEntity);
            long tileKey = StorageNetworkWorldCache.ToDebugTileKey(transform.Position);
            int2 tile = StorageNetworkWorldCache.FromDebugTileKey(tileKey);

            bool hasInputTag = entityManager.HasComponent<InputConnectorTag>(connectorEntity);
            bool hasOutputTag = entityManager.HasComponent<OutputConnectorTag>(connectorEntity);
            bool isDropper = entityManager.HasComponent<ItemDropperTimingCD>(connectorEntity);
            string kind = isDropper ? "dropper" : hasInputTag ? "input" : hasOutputTag ? "output" : "connector";

            IReadOnlyList<Entity> loadedInventories = cache.GetLoadedInventoriesAtTile(tileKey);
            string loadedInventoryText = FormatEntityList(loadedInventories, entityManager);

            bool hasPrimaryInventory = cache.TryGetLoadedPrimaryInventoryAtTile(tileKey, out Entity primaryInventory);
            bool hasNetwork = cache.TryGetNetworkForConnector(connectorEntity, out StorageNetworkSnapshot network);

            string networkState = hasNetwork
                ? DescribeConnectorNetworkState(tileKey, kind, hasPrimaryInventory, primaryInventory, network)
                : "missing-network";

            builder.Append("  ");
            builder.Append(FormatEntity(connectorEntity));
            builder.Append(" tile=(").Append(tile.x).Append(", ").Append(tile.y).Append(')');
            builder.Append(" kind=").Append(kind);
            builder.Append(" dontUnload=").Append(Bool01(entityManager.HasComponent<DontUnloadCD>(connectorEntity)));
            builder.Append(" dontDisable=").Append(Bool01(entityManager.HasComponent<DontDisableCD>(connectorEntity)));
            builder.Append(" keepArea=").Append(Bool01(entityManager.HasComponent<KeepAreaLoadedCD>(connectorEntity)));
            builder.Append(" enableCircle=").Append(Bool01(entityManager.HasComponent<EnableEntitiesInCircleCD>(connectorEntity)));
            builder.Append(" chunkTracked=").Append(Bool01(entityManager.HasChunkComponent<SerializedChunkMinMaxPosition>(connectorEntity)));
            builder.Append(" loadedStorage=").Append(hasPrimaryInventory ? DescribeInventory(primaryInventory, entityManager) : "none");
            builder.Append(" loadedAtTile=").Append(loadedInventoryText);
            builder.Append(" network=").Append(networkState);
            builder.AppendLine();
        }
    }

    private static void AppendPipes(
        StringBuilder builder,
        List<Entity> pipes,
        EntityManager entityManager)
    {
        builder.AppendLine();
        builder.Append("Pipes (").Append(pipes.Count).AppendLine("):");
        if (pipes.Count == 0)
        {
            builder.AppendLine("  none");
            return;
        }

        for (int i = 0; i < pipes.Count; i++)
        {
            Entity pipeEntity = pipes[i];
            int2 tile = StorageNetworkWorldCache.FromDebugTileKey(StorageNetworkWorldCache.ToDebugTileKey(entityManager.GetComponentData<LocalTransform>(pipeEntity).Position));
            builder.Append("  ")
                .Append(FormatEntity(pipeEntity))
                .Append(" tile=(").Append(tile.x).Append(", ").Append(tile.y).Append(')')
                .Append(" dontUnload=").Append(Bool01(entityManager.HasComponent<DontUnloadCD>(pipeEntity)))
                .Append(" dontDisable=").Append(Bool01(entityManager.HasComponent<DontDisableCD>(pipeEntity)))
                .Append(" keepArea=").Append(Bool01(entityManager.HasComponent<KeepAreaLoadedCD>(pipeEntity)))
                .Append(" enableCircle=").Append(Bool01(entityManager.HasComponent<EnableEntitiesInCircleCD>(pipeEntity)))
                .Append(" chunkTracked=").Append(Bool01(entityManager.HasChunkComponent<SerializedChunkMinMaxPosition>(pipeEntity)))
                .AppendLine();
        }
    }

    private static void AppendTerminals(
        StringBuilder builder,
        List<Entity> terminals,
        EntityManager entityManager,
        StorageNetworkWorldCache cache)
    {
        builder.AppendLine();
        builder.Append("Terminals (").Append(terminals.Count).AppendLine("):");
        if (terminals.Count == 0)
        {
            builder.AppendLine("  none");
            return;
        }

        for (int i = 0; i < terminals.Count; i++)
        {
            Entity terminalEntity = terminals[i];
            int2 tile = StorageNetworkWorldCache.FromDebugTileKey(StorageNetworkWorldCache.ToDebugTileKey(entityManager.GetComponentData<LocalTransform>(terminalEntity).Position));

            builder.Append("  ")
                .Append(FormatEntity(terminalEntity))
                .Append(" tile=(").Append(tile.x).Append(", ").Append(tile.y).Append(')')
                .Append(" dontUnload=").Append(Bool01(entityManager.HasComponent<DontUnloadCD>(terminalEntity)))
                .Append(" dontDisable=").Append(Bool01(entityManager.HasComponent<DontDisableCD>(terminalEntity)))
                .Append(" keepArea=").Append(Bool01(entityManager.HasComponent<KeepAreaLoadedCD>(terminalEntity)))
                .Append(" enableCircle=").Append(Bool01(entityManager.HasComponent<EnableEntitiesInCircleCD>(terminalEntity)))
                .Append(" chunkTracked=").Append(Bool01(entityManager.HasChunkComponent<SerializedChunkMinMaxPosition>(terminalEntity)));

            if (cache.TryGetNetworkForRelay(terminalEntity, out StorageNetworkSnapshot network))
            {
                builder.Append(" network=connected");
                builder.Append(" connectors=").Append(network.ConnectorTiles.Count);
                builder.Append(" storages=").Append(network.CraftVisibleInventories.Count);
                builder.Append(" relays=").Append(network.Relays.Count);
            }
            else
            {
                builder.Append(" network=missing");
            }

            builder.AppendLine();
        }
    }

    private static string DescribeConnectorNetworkState(
        long tileKey,
        string kind,
        bool hasPrimaryInventory,
        Entity primaryInventory,
        StorageNetworkSnapshot network)
    {
        if (kind == "input")
        {
            if (TryFindMember(network.OutputMembers, tileKey, out Entity memberEntity))
            {
                return hasPrimaryInventory && memberEntity == primaryInventory
                    ? "source-storage-connected"
                    : "source-storage-mismatch";
            }

            return ContainsTile(network.HopperTileKeys, tileKey)
                ? "hopper-no-storage"
                : hasPrimaryInventory
                    ? "loaded-storage-not-in-network"
                    : "no-storage";
        }

        if (kind == "output")
        {
            if (TryFindMember(network.InputMembers, tileKey, out Entity memberEntity))
            {
                return hasPrimaryInventory && memberEntity == primaryInventory
                    ? "destination-storage-connected"
                    : "destination-storage-mismatch";
            }

            return hasPrimaryInventory ? "loaded-storage-not-in-network" : "no-storage";
        }

        return network.CraftVisibleInventories.Count > 0
            ? "connected craftVisible=" + network.CraftVisibleInventories.Count
            : "connected craftVisible=0";
    }

    private static bool TryFindMember(List<StorageNetworkMember> members, long tileKey, out Entity entity)
    {
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].TileKey == tileKey)
            {
                entity = members[i].Entity;
                return true;
            }
        }

        entity = Entity.Null;
        return false;
    }

    private static bool ContainsTile(List<long> tileKeys, long tileKey)
    {
        for (int i = 0; i < tileKeys.Count; i++)
        {
            if (tileKeys[i] == tileKey)
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatEntityList(IReadOnlyList<Entity> entities, EntityManager entityManager)
    {
        if (entities.Count == 0)
        {
            return "[]";
        }

        StringBuilder builder = new StringBuilder();
        builder.Append('[');
        for (int i = 0; i < entities.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(DescribeInventory(entities[i], entityManager));
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string DescribeInventory(Entity entity, EntityManager entityManager)
    {
        if (entityManager.HasComponent<ObjectDataCD>(entity))
        {
            ObjectDataCD objectData = entityManager.GetComponentData<ObjectDataCD>(entity);
            return FormatEntity(entity) + ":" + objectData.objectID + "/" + objectData.variation;
        }

        return FormatEntity(entity);
    }

    private static string FormatEntity(Entity entity)
    {
        return "E(" + entity.Index + ":" + entity.Version + ")";
    }

    private static char Bool01(bool value)
    {
        return value ? '1' : '0';
    }

    private static void SortByTile(List<Entity> entities, EntityManager entityManager)
    {
        entities.Sort((left, right) =>
        {
            int2 leftTile = StorageNetworkWorldCache.FromDebugTileKey(StorageNetworkWorldCache.ToDebugTileKey(entityManager.GetComponentData<LocalTransform>(left).Position));
            int2 rightTile = StorageNetworkWorldCache.FromDebugTileKey(StorageNetworkWorldCache.ToDebugTileKey(entityManager.GetComponentData<LocalTransform>(right).Position));

            int xCompare = leftTile.x.CompareTo(rightTile.x);
            if (xCompare != 0)
            {
                return xCompare;
            }

            int yCompare = leftTile.y.CompareTo(rightTile.y);
            if (yCompare != 0)
            {
                return yCompare;
            }

            return left.Index.CompareTo(right.Index);
        });
    }

    private static void LogToConsole(string message)
    {
        if (Manager.menu?.quantumConsole != null)
        {
            Manager.menu.quantumConsole.LogToConsole(message);
        }
        else
        {
            Debug.Log(message);
        }
    }
}
