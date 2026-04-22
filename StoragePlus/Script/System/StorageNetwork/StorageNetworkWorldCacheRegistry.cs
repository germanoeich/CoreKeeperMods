using System.Collections.Generic;
using Unity.Entities;

public static class StorageNetworkWorldCacheRegistry
{
    private static readonly Dictionary<World, StorageNetworkWorldCache> CacheByWorld = new();

    public static StorageNetworkWorldCache GetOrCreate(World world)
    {
        PruneInvalidWorlds();

        if (!CacheByWorld.TryGetValue(world, out StorageNetworkWorldCache cache))
        {
            cache = new StorageNetworkWorldCache(world.EntityManager);
            CacheByWorld.Add(world, cache);
        }

        return cache;
    }

    private static void PruneInvalidWorlds()
    {
        if (CacheByWorld.Count == 0)
        {
            return;
        }

        List<World> worldsToRemove = null;
        foreach (KeyValuePair<World, StorageNetworkWorldCache> pair in CacheByWorld)
        {
            if (pair.Key != null && pair.Key.IsCreated)
            {
                continue;
            }

            worldsToRemove ??= new List<World>();
            worldsToRemove.Add(pair.Key);
        }

        if (worldsToRemove == null)
        {
            return;
        }

        for (int i = 0; i < worldsToRemove.Count; i++)
        {
            CacheByWorld.Remove(worldsToRemove[i]);
        }
    }
}
