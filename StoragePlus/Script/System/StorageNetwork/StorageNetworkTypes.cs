using System.Collections.Generic;
using Unity.Entities;

public readonly struct StorageNetworkMember
{
    public readonly Entity Entity;
    public readonly long TileKey;

    public StorageNetworkMember(Entity entity, long tileKey)
    {
        Entity = entity;
        TileKey = tileKey;
    }
}

public sealed class StorageNetworkSnapshot
{
    public readonly HashSet<long> NodeTiles = new();
    public readonly List<long> ConnectorTiles = new();
    public readonly List<StorageNetworkMember> OutputMembers = new();
    public readonly List<StorageNetworkMember> InputMembers = new();
    public readonly List<long> HopperTileKeys = new();
    public readonly List<Entity> CraftVisibleInventories = new();
    public readonly List<Entity> Relays = new();

    public ulong NetworkHash;
}
