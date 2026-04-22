using System.Collections.Generic;
using Unity.Entities;

internal sealed class StorageIntakeNetworkRoutes
{
    public readonly List<StorageIntakeOutputRoute> OutputRoutes = new();
    public readonly List<StorageIntakeHopperRoute> HopperRoutes = new();
}

internal sealed class StorageIntakeOutputRoute
{
    public readonly Entity OutputInventory;
    public readonly List<Entity> OrderedInputInventories = new();

    public StorageIntakeOutputRoute(Entity outputInventory)
    {
        OutputInventory = outputInventory;
    }
}

internal sealed class StorageIntakeHopperRoute
{
    public readonly long HopperTileKey;
    public readonly List<Entity> OrderedInputInventories = new();

    public StorageIntakeHopperRoute(long hopperTileKey)
    {
        HopperTileKey = hopperTileKey;
    }
}
