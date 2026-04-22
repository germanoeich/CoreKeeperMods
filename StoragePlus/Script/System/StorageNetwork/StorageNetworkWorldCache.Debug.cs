using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

public sealed partial class StorageNetworkWorldCache
{
    public bool TryGetLoadedPrimaryInventoryAtTile(long tileKey, out Entity inventoryEntity)
    {
        return _primaryInventoryByPosition.TryGetValue(tileKey, out inventoryEntity);
    }

    public IReadOnlyList<Entity> GetLoadedInventoriesAtTile(long tileKey)
    {
        if (_inventoriesByPosition.TryGetValue(tileKey, out List<Entity> inventories))
        {
            return inventories;
        }

        return Array.Empty<Entity>();
    }

    public static long ToDebugTileKey(float3 worldPosition)
    {
        return ToTileKey(worldPosition);
    }

    public static int2 FromDebugTileKey(long tileKey)
    {
        return FromTileKey(tileKey);
    }
}
