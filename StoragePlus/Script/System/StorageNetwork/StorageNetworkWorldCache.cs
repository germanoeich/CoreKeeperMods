using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public sealed partial class StorageNetworkWorldCache
{
    private const ulong ConnectorSalt = 0xC13FA9A902A6328FUL;
    private const ulong OutputConnectorRoleSalt = 0xD4C89F2AB71E6531UL;
    private const ulong InputConnectorRoleSalt = 0x6B17E2A48D39C50FUL;
    private const ulong PipeSalt = 0x91E10DA5C79E7B1DUL;
    private const ulong OutputMemberSalt = 0x0E5B1F6D7A2493C7UL;
    private const ulong InputMemberSalt = 0x5BF03635A0B5B9A5UL;
    private const ulong HopperMemberSalt = 0x28F5A14CD3E62B19UL;
    private const ulong CraftMemberSalt = 0xB9E6D6D54C8A93D1UL;
    private const ulong RelayMemberSalt = 0x73C7E27F15F2198BUL;
    private const ulong NetworkNodeSalt = 0xA3C59AC63D53B1F1UL;
    private const ulong HashMixMultiplier = 0x9E3779B185EBCA87UL;

    private static readonly int2[] CardinalOffsets =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1)
    };

    private readonly EntityManager _entityManager;
    private readonly EntityQuery _connectorQuery;
    private readonly EntityQuery _pipeQuery;
    private readonly EntityQuery _relayQuery;
    private readonly EntityQuery _inventoryQuery;

    private readonly Dictionary<long, Entity> _connectorsByPosition = new();
    private readonly HashSet<long> _outputConnectorPositions = new();
    private readonly HashSet<long> _inputConnectorPositions = new();
    private readonly HashSet<long> _pipePositions = new();
    private readonly Dictionary<long, Entity> _primaryInventoryByPosition = new();
    private readonly Dictionary<long, List<Entity>> _inventoriesByPosition = new();
    private readonly Dictionary<long, List<Entity>> _relaysByPosition = new();
    private readonly HashSet<Entity> _relayEntities = new();

    private readonly HashSet<long> _allNetworkNodes = new();
    private readonly HashSet<long> _visitedNodes = new();
    private readonly Queue<long> _floodQueue = new();
    private readonly List<long> _componentNodeTiles = new();
    private readonly HashSet<Entity> _componentEntityDeduplication = new();

    public readonly List<StorageNetworkSnapshot> Networks = new();
    public readonly Dictionary<Entity, StorageNetworkSnapshot> NetworkByConnector = new();
    public readonly Dictionary<Entity, StorageNetworkSnapshot> NetworkByRelay = new();
    public readonly Dictionary<Entity, StorageNetworkSnapshot> CraftNetworkByInventory = new();

    public ulong TopologyHash { get; private set; }
    public ulong IntakeMembershipHash { get; private set; }
    public ulong CraftMembershipHash { get; private set; }

    public EntityQuery RelayQuery => _relayQuery;

    private int _lastBuiltFrame = -1;

    public StorageNetworkWorldCache(EntityManager entityManager)
    {
        _entityManager = entityManager;
        _connectorQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<StorageConnectorTag>(),
            ComponentType.ReadOnly<LocalTransform>());
        _pipeQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<StoragePipeTag>(),
            ComponentType.ReadOnly<LocalTransform>());
        _relayQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<StorageCraftingRelayTag>(),
            ComponentType.ReadOnly<LocalTransform>());
        _inventoryQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<InventoryBuffer>(),
            ComponentType.ReadOnly<ContainedObjectsBuffer>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<ObjectDataCD>());
    }

    public void EnsureBuilt(PugDatabase.DatabaseBankCD database)
    {
        int currentFrame = Time.frameCount;
        if (_lastBuiltFrame == currentFrame)
        {
            return;
        }

        Rebuild(database);
        _lastBuiltFrame = currentFrame;
    }

    public bool TryGetNetworkForRelay(Entity relayEntity, out StorageNetworkSnapshot network)
    {
        return NetworkByRelay.TryGetValue(relayEntity, out network);
    }

    public bool TryGetNetworkForConnector(Entity connectorEntity, out StorageNetworkSnapshot network)
    {
        return NetworkByConnector.TryGetValue(connectorEntity, out network);
    }

    public bool TryGetCraftNetworkForInventory(Entity inventoryEntity, out StorageNetworkSnapshot network)
    {
        return CraftNetworkByInventory.TryGetValue(inventoryEntity, out network);
    }

    private void Rebuild(PugDatabase.DatabaseBankCD database)
    {
        _connectorsByPosition.Clear();
        _outputConnectorPositions.Clear();
        _inputConnectorPositions.Clear();
        _pipePositions.Clear();
        _primaryInventoryByPosition.Clear();
        _inventoriesByPosition.Clear();
        _relaysByPosition.Clear();
        _relayEntities.Clear();
        Networks.Clear();
        NetworkByConnector.Clear();
        NetworkByRelay.Clear();
        CraftNetworkByInventory.Clear();

        using (NativeArray<Entity> connectors = _connectorQuery.ToEntityArray(Allocator.Temp))
        {
            for (int i = 0; i < connectors.Length; i++)
            {
                Entity connectorEntity = connectors[i];
                LocalTransform transform = _entityManager.GetComponentData<LocalTransform>(connectorEntity);
                long tileKey = ToTileKey(transform.Position);
                _connectorsByPosition[tileKey] = connectorEntity;

                if (_entityManager.HasComponent<OutputConnectorTag>(connectorEntity))
                {
                    _outputConnectorPositions.Add(tileKey);
                }

                if (_entityManager.HasComponent<InputConnectorTag>(connectorEntity))
                {
                    _inputConnectorPositions.Add(tileKey);
                }
            }
        }

        using (NativeArray<Entity> pipes = _pipeQuery.ToEntityArray(Allocator.Temp))
        {
            for (int i = 0; i < pipes.Length; i++)
            {
                LocalTransform transform = _entityManager.GetComponentData<LocalTransform>(pipes[i]);
                _pipePositions.Add(ToTileKey(transform.Position));
            }
        }

        using (NativeArray<Entity> relays = _relayQuery.ToEntityArray(Allocator.Temp))
        {
            for (int i = 0; i < relays.Length; i++)
            {
                Entity relayEntity = relays[i];
                LocalTransform transform = _entityManager.GetComponentData<LocalTransform>(relayEntity);
                long tileKey = ToTileKey(transform.Position);
                _relayEntities.Add(relayEntity);

                if (!_relaysByPosition.TryGetValue(tileKey, out List<Entity> relaysAtTile))
                {
                    relaysAtTile = new List<Entity>(1);
                    _relaysByPosition.Add(tileKey, relaysAtTile);
                }

                relaysAtTile.Add(relayEntity);
            }
        }

        using (NativeArray<Entity> inventories = _inventoryQuery.ToEntityArray(Allocator.Temp))
        {
            for (int i = 0; i < inventories.Length; i++)
            {
                Entity inventoryEntity = inventories[i];
                ObjectDataCD objectData = _entityManager.GetComponentData<ObjectDataCD>(inventoryEntity);
                if (objectData.objectID == ObjectID.None)
                {
                    continue;
                }

                ref PugDatabase.EntityObjectInfo objectInfo = ref PugDatabase.GetEntityObjectInfo(objectData.objectID, database.databaseBankBlob, objectData.variation);
                if (objectInfo.objectType != ObjectType.PlaceablePrefab)
                {
                    continue;
                }

                LocalTransform transform = _entityManager.GetComponentData<LocalTransform>(inventoryEntity);
                AddInventoryTileMappings(
                    inventoryEntity,
                    transform,
                    objectInfo.prefabTileSize,
                    objectInfo.prefabCornerOffset,
                    objectInfo.centerIsAtEntityPosition,
                    allowAsPrimaryInventory: !ShouldIgnoreAsPrimaryConnectorInventory(inventoryEntity));
            }
        }

        TopologyHash = ComputeTopologyHash();
        IntakeMembershipHash = ComputeIntakeMembershipHash();
        CraftMembershipHash = ComputeCraftMembershipHash();

        BuildNetworks();
    }

    private void AddInventoryTileMappings(
        Entity inventoryEntity,
        in LocalTransform transform,
        int2 prefabTileSize,
        int2 prefabCornerOffset,
        bool centerIsAtEntityPosition,
        bool allowAsPrimaryInventory)
    {
        int2 entityTile = new int2((int)math.round(transform.Position.x), (int)math.round(transform.Position.z));
        int2 offset = prefabCornerOffset;
        int2 size = math.max(prefabTileSize, new int2(1, 1));

        if (_entityManager.HasComponent<DirectionCD>(inventoryEntity))
        {
            DirectionCD direction = _entityManager.GetComponentData<DirectionCD>(inventoryEntity);
            direction.GetPrefabOffsetAndTileSize(offset, size, out offset, out size);
        }

        int2 startTile = centerIsAtEntityPosition
            ? entityTile - (size - 1) / 2 + offset
            : entityTile + offset;

        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                long tileKey = ToTileKey(startTile + new int2(x, y));

                if (allowAsPrimaryInventory && !_primaryInventoryByPosition.ContainsKey(tileKey))
                {
                    _primaryInventoryByPosition.Add(tileKey, inventoryEntity);
                }

                if (!_inventoriesByPosition.TryGetValue(tileKey, out List<Entity> inventoriesAtTile))
                {
                    inventoriesAtTile = new List<Entity>(1);
                    _inventoriesByPosition.Add(tileKey, inventoriesAtTile);
                }

                inventoriesAtTile.Add(inventoryEntity);
            }
        }
    }

    private bool ShouldIgnoreAsPrimaryConnectorInventory(Entity inventoryEntity)
    {
        return _entityManager.HasComponent<EnemySpawnerPlatformCD>(inventoryEntity);
    }
}
