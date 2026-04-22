using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

public sealed partial class StorageNetworkWorldCache
{
    private void BuildNetworks()
    {
        _allNetworkNodes.Clear();
        foreach (long connectorTileKey in _connectorsByPosition.Keys)
        {
            _allNetworkNodes.Add(connectorTileKey);
        }

        foreach (long pipeTileKey in _pipePositions)
        {
            _allNetworkNodes.Add(pipeTileKey);
        }

        _visitedNodes.Clear();

        foreach (long startTileKey in _allNetworkNodes)
        {
            if (!_visitedNodes.Add(startTileKey))
            {
                continue;
            }

            _componentNodeTiles.Clear();
            _floodQueue.Clear();
            _floodQueue.Enqueue(startTileKey);

            while (_floodQueue.Count > 0)
            {
                long currentTileKey = _floodQueue.Dequeue();
                _componentNodeTiles.Add(currentTileKey);

                int2 currentTile = FromTileKey(currentTileKey);
                for (int i = 0; i < CardinalOffsets.Length; i++)
                {
                    long neighborTileKey = ToTileKey(currentTile + CardinalOffsets[i]);
                    if (!_allNetworkNodes.Contains(neighborTileKey) || !_visitedNodes.Add(neighborTileKey))
                    {
                        continue;
                    }

                    _floodQueue.Enqueue(neighborTileKey);
                }
            }

            StorageNetworkSnapshot network = new();
            for (int i = 0; i < _componentNodeTiles.Count; i++)
            {
                long nodeTileKey = _componentNodeTiles[i];
                network.NodeTiles.Add(nodeTileKey);

                if (_connectorsByPosition.TryGetValue(nodeTileKey, out Entity connectorEntity))
                {
                    network.ConnectorTiles.Add(nodeTileKey);
                    NetworkByConnector[connectorEntity] = network;
                }
            }

            if (network.ConnectorTiles.Count == 0)
            {
                continue;
            }

            _componentEntityDeduplication.Clear();
            for (int i = 0; i < _componentNodeTiles.Count; i++)
            {
                long nodeTileKey = _componentNodeTiles[i];
                if (!_relaysByPosition.TryGetValue(nodeTileKey, out List<Entity> relaysAtTile))
                {
                    continue;
                }

                for (int j = 0; j < relaysAtTile.Count; j++)
                {
                    Entity relayEntity = relaysAtTile[j];
                    if (_componentEntityDeduplication.Add(relayEntity))
                    {
                        network.Relays.Add(relayEntity);
                        NetworkByRelay[relayEntity] = network;
                    }
                }
            }

            _componentEntityDeduplication.Clear();
            for (int i = 0; i < network.ConnectorTiles.Count; i++)
            {
                long connectorTileKey = network.ConnectorTiles[i];
                if (!_inputConnectorPositions.Contains(connectorTileKey))
                {
                    continue;
                }

                if (!_primaryInventoryByPosition.TryGetValue(connectorTileKey, out Entity inventoryEntity))
                {
                    network.HopperTileKeys.Add(connectorTileKey);
                    continue;
                }

                if (!_componentEntityDeduplication.Add(inventoryEntity))
                {
                    continue;
                }

                network.OutputMembers.Add(new StorageNetworkMember(inventoryEntity, connectorTileKey));
            }

            _componentEntityDeduplication.Clear();
            for (int i = 0; i < network.ConnectorTiles.Count; i++)
            {
                long connectorTileKey = network.ConnectorTiles[i];
                if (!_outputConnectorPositions.Contains(connectorTileKey) ||
                    !_primaryInventoryByPosition.TryGetValue(connectorTileKey, out Entity inventoryEntity) ||
                    !_componentEntityDeduplication.Add(inventoryEntity))
                {
                    continue;
                }

                network.InputMembers.Add(new StorageNetworkMember(inventoryEntity, connectorTileKey));
            }

            _componentEntityDeduplication.Clear();
            for (int i = 0; i < network.ConnectorTiles.Count; i++)
            {
                long connectorTileKey = network.ConnectorTiles[i];
                if (!_inventoriesByPosition.TryGetValue(connectorTileKey, out List<Entity> inventoriesAtTile))
                {
                    continue;
                }

                for (int j = 0; j < inventoriesAtTile.Count; j++)
                {
                    Entity inventoryEntity = inventoriesAtTile[j];
                    if (_relayEntities.Contains(inventoryEntity) || !_componentEntityDeduplication.Add(inventoryEntity))
                    {
                        continue;
                    }

                    network.CraftVisibleInventories.Add(inventoryEntity);
                    CraftNetworkByInventory[inventoryEntity] = network;
                }
            }

            network.NetworkHash = ComputeNetworkHash(network);
            Networks.Add(network);
        }
    }

    private static long ToTileKey(float3 worldPosition)
    {
        return ToTileKey(new int2((int)math.round(worldPosition.x), (int)math.round(worldPosition.z)));
    }

    private static long ToTileKey(int2 tile)
    {
        return ((long)tile.x << 32) ^ (uint)tile.y;
    }

    private static int2 FromTileKey(long tileKey)
    {
        return new int2((int)(tileKey >> 32), (int)tileKey);
    }
}
