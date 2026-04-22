using System.Collections.Generic;
using Unity.Entities;

public sealed partial class StorageNetworkWorldCache
{
    private ulong ComputeTopologyHash()
    {
        ulong xor = 0UL;
        ulong sum = 0UL;
        int count = 0;

        foreach (long connectorTileKey in _connectorsByPosition.Keys)
        {
            MixHash(ref xor, ref sum, HashTileKey(connectorTileKey, ConnectorSalt));
            count++;

            if (_outputConnectorPositions.Contains(connectorTileKey))
            {
                MixHash(ref xor, ref sum, HashTileKey(connectorTileKey, OutputConnectorRoleSalt));
                count++;
            }

            if (_inputConnectorPositions.Contains(connectorTileKey))
            {
                MixHash(ref xor, ref sum, HashTileKey(connectorTileKey, InputConnectorRoleSalt));
                count++;
            }
        }

        foreach (long pipeTileKey in _pipePositions)
        {
            MixHash(ref xor, ref sum, HashTileKey(pipeTileKey, PipeSalt));
            count++;
        }

        return FinalizeHash(xor, sum, count);
    }

    private ulong ComputeIntakeMembershipHash()
    {
        ulong xor = 0UL;
        ulong sum = 0UL;
        int count = 0;

        foreach (long tileKey in _connectorsByPosition.Keys)
        {
            bool hasInventory = _primaryInventoryByPosition.TryGetValue(tileKey, out Entity inventoryEntity);
            if (hasInventory && _outputConnectorPositions.Contains(tileKey))
            {
                MixHash(ref xor, ref sum, HashEntityAtTile(tileKey, inventoryEntity, InputMemberSalt));
                count++;
            }

            if (hasInventory && _inputConnectorPositions.Contains(tileKey))
            {
                MixHash(ref xor, ref sum, HashEntityAtTile(tileKey, inventoryEntity, OutputMemberSalt));
                count++;
            }

            if (!hasInventory && _inputConnectorPositions.Contains(tileKey))
            {
                MixHash(ref xor, ref sum, HashTileKey(tileKey, HopperMemberSalt));
                count++;
            }
        }

        return FinalizeHash(xor, sum, count);
    }

    private ulong ComputeCraftMembershipHash()
    {
        ulong xor = 0UL;
        ulong sum = 0UL;
        int count = 0;

        foreach (KeyValuePair<long, List<Entity>> pair in _inventoriesByPosition)
        {
            long tileKey = pair.Key;
            if (!_connectorsByPosition.ContainsKey(tileKey))
            {
                continue;
            }

            List<Entity> inventoriesAtTile = pair.Value;
            for (int i = 0; i < inventoriesAtTile.Count; i++)
            {
                Entity inventoryEntity = inventoriesAtTile[i];
                if (_relayEntities.Contains(inventoryEntity))
                {
                    continue;
                }

                MixHash(ref xor, ref sum, HashEntityAtTile(tileKey, inventoryEntity, CraftMemberSalt));
                count++;
            }
        }

        foreach (KeyValuePair<long, List<Entity>> pair in _relaysByPosition)
        {
            List<Entity> relaysAtTile = pair.Value;
            for (int i = 0; i < relaysAtTile.Count; i++)
            {
                MixHash(ref xor, ref sum, HashEntityAtTile(pair.Key, relaysAtTile[i], RelayMemberSalt));
                count++;
            }
        }

        return FinalizeHash(xor, sum, count);
    }

    private static ulong ComputeNetworkHash(StorageNetworkSnapshot network)
    {
        ulong xor = 0UL;
        ulong sum = 0UL;
        int count = 0;

        foreach (long tileKey in network.NodeTiles)
        {
            MixHash(ref xor, ref sum, HashTileKey(tileKey, NetworkNodeSalt));
            count++;
        }

        for (int i = 0; i < network.CraftVisibleInventories.Count; i++)
        {
            MixHash(ref xor, ref sum, HashEntity(network.CraftVisibleInventories[i], CraftMemberSalt));
            count++;
        }

        for (int i = 0; i < network.HopperTileKeys.Count; i++)
        {
            MixHash(ref xor, ref sum, HashTileKey(network.HopperTileKeys[i], HopperMemberSalt));
            count++;
        }

        for (int i = 0; i < network.Relays.Count; i++)
        {
            MixHash(ref xor, ref sum, HashEntity(network.Relays[i], RelayMemberSalt));
            count++;
        }

        return FinalizeHash(xor, sum, count);
    }

    private static ulong HashEntityAtTile(long tileKey, Entity entity, ulong salt)
    {
        unchecked
        {
            ulong hash = HashTileKey(tileKey, salt);
            hash ^= (ulong)(uint)entity.Index * 0xD6E8FEB86659FD93UL;
            hash ^= (ulong)(uint)entity.Version * 0xA5A3564E27F6CB5DUL;
            return hash;
        }
    }

    private static ulong HashEntity(Entity entity, ulong salt)
    {
        unchecked
        {
            ulong hash = salt;
            hash ^= (ulong)(uint)entity.Index * 0xD6E8FEB86659FD93UL;
            hash ^= (ulong)(uint)entity.Version * 0xA5A3564E27F6CB5DUL;
            return hash;
        }
    }

    private static ulong HashTileKey(long tileKey, ulong salt)
    {
        unchecked
        {
            ulong hash = 1469598103934665603UL;
            hash = (hash ^ (ulong)(uint)(tileKey >> 32) ^ salt) * 1099511628211UL;
            hash = (hash ^ (ulong)(uint)tileKey) * 1099511628211UL;
            return hash;
        }
    }

    private static void MixHash(ref ulong xor, ref ulong sum, ulong value)
    {
        unchecked
        {
            xor ^= value;
            sum += value * HashMixMultiplier + 0xC2B2AE3D27D4EB4FUL;
        }
    }

    private static ulong FinalizeHash(ulong xor, ulong sum, int count)
    {
        unchecked
        {
            ulong hash = xor ^ (sum << 1) ^ ((ulong)(uint)count * 0x165667B19E3779F9UL);
            hash ^= hash >> 33;
            hash *= 0xFF51AFD7ED558CCDUL;
            hash ^= hash >> 33;
            hash *= 0xC4CEB9FE1A85EC53UL;
            hash ^= hash >> 33;
            return hash;
        }
    }
}
