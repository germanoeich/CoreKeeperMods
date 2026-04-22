using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

internal sealed class StorageIntakeRouteBuilder
{
    private static readonly int2[] CardinalOffsets =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1)
    };

    private readonly Queue<long> _distanceQueue = new();
    private readonly HashSet<long> _distanceVisitedNodes = new();
    private readonly Dictionary<long, Entity> _inputByTile = new();

    public void Rebuild(List<StorageNetworkSnapshot> networks, List<StorageIntakeNetworkRoutes> cachedNetworks)
    {
        cachedNetworks.Clear();

        for (int networkIndex = 0; networkIndex < networks.Count; networkIndex++)
        {
            StorageNetworkSnapshot snapshot = networks[networkIndex];
            if ((snapshot.OutputMembers.Count == 0 && snapshot.HopperTileKeys.Count == 0) ||
                snapshot.InputMembers.Count == 0)
            {
                continue;
            }

            StorageIntakeNetworkRoutes networkRoutes = new();
            for (int outputIndex = 0; outputIndex < snapshot.OutputMembers.Count; outputIndex++)
            {
                StorageNetworkMember outputMember = snapshot.OutputMembers[outputIndex];
                StorageIntakeOutputRoute route = new(outputMember.Entity);
                BuildInputPriorityByDistance(
                    outputMember.TileKey,
                    snapshot.InputMembers,
                    snapshot.NodeTiles,
                    route.OrderedInputInventories);

                if (route.OrderedInputInventories.Count > 0)
                {
                    networkRoutes.OutputRoutes.Add(route);
                }
            }

            for (int hopperIndex = 0; hopperIndex < snapshot.HopperTileKeys.Count; hopperIndex++)
            {
                long hopperTileKey = snapshot.HopperTileKeys[hopperIndex];
                StorageIntakeHopperRoute route = new(hopperTileKey);
                BuildInputPriorityByDistance(
                    hopperTileKey,
                    snapshot.InputMembers,
                    snapshot.NodeTiles,
                    route.OrderedInputInventories);

                if (route.OrderedInputInventories.Count > 0)
                {
                    networkRoutes.HopperRoutes.Add(route);
                }
            }

            if (networkRoutes.OutputRoutes.Count > 0 || networkRoutes.HopperRoutes.Count > 0)
            {
                cachedNetworks.Add(networkRoutes);
            }
        }
    }

    private void BuildInputPriorityByDistance(
        long outputTileKey,
        List<StorageNetworkMember> inputMembers,
        HashSet<long> networkNodes,
        List<Entity> orderedInputInventories)
    {
        orderedInputInventories.Clear();
        _inputByTile.Clear();

        for (int i = 0; i < inputMembers.Count; i++)
        {
            StorageNetworkMember inputMember = inputMembers[i];
            _inputByTile[inputMember.TileKey] = inputMember.Entity;
        }

        _distanceQueue.Clear();
        _distanceVisitedNodes.Clear();
        _distanceQueue.Enqueue(outputTileKey);
        _distanceVisitedNodes.Add(outputTileKey);

        while (_distanceQueue.Count > 0 && _inputByTile.Count > 0)
        {
            long currentTileKey = _distanceQueue.Dequeue();
            if (_inputByTile.TryGetValue(currentTileKey, out Entity inputInventory))
            {
                orderedInputInventories.Add(inputInventory);
                _inputByTile.Remove(currentTileKey);
            }

            int2 currentTile = FromTileKey(currentTileKey);
            for (int i = 0; i < CardinalOffsets.Length; i++)
            {
                long neighborTileKey = ToTileKey(currentTile + CardinalOffsets[i]);
                if (!networkNodes.Contains(neighborTileKey) || !_distanceVisitedNodes.Add(neighborTileKey))
                {
                    continue;
                }

                _distanceQueue.Enqueue(neighborTileKey);
            }
        }
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
