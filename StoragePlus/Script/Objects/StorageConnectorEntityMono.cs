using CoreLib.Submodule.TileSet;
using Pug.Conversion;
using Pug.UnityExtensions;
using PugTilemap;
using Unity.Entities;
using Unity.Mathematics;

public class StorageConnectorEntityMono : EntityMonoBehaviour
{
    private bool _usesFallbackHiddenTile;

    protected override void OnShow()
    {
        _usesFallbackHiddenTile = StorageNetworkHiddenTileUtility.TryEnable(base.entity, base.world, base.WorldPosition.RoundToInt2());

        base.OnShow();
    }

    protected override void OnHide()
    {
        if (_usesFallbackHiddenTile)
        {
            StorageNetworkHiddenTileUtility.Disable(base.WorldPosition.RoundToInt2());
            _usesFallbackHiddenTile = false;
        }

        base.OnHide();
    }
}

public static class StorageNetworkHiddenTileUtility
{
    private const string StorageNetworkTilesetId = "StorageNetworkTileset";
    private const TileType StorageNetworkTileType = TileType.circuitPlate;

    private static int _cachedTileset = -1;

    public static bool TryEnable(Entity entity, World world, int2 tilePosition)
    {
        if (EntityUtility.HasComponentData<PseudoTileCD>(entity, world))
        {
            EntityUtility.RemoveComponentData<PseudoTileCD>(entity, world);
        }

        if (!TryGetTilesetIndex(out int tilesetIndex))
        {
            return false;
        }

        Manager.multiMap.SetHiddenTile(tilePosition, tilesetIndex, StorageNetworkTileType, 0);
        return true;
    }

    public static void Disable(int2 tilePosition)
    {
        Manager.multiMap.ClearHiddenTileOfType(tilePosition, StorageNetworkTileType);
    }

    private static bool TryGetTilesetIndex(out int tilesetIndex)
    {
        if (_cachedTileset >= 0)
        {
            tilesetIndex = _cachedTileset;
            return true;
        }

        try
        {
            _cachedTileset = (int)TileSetModule.GetTilesetId(StorageNetworkTilesetId);
        }
        catch
        {
            _cachedTileset = -1;
        }

        tilesetIndex = _cachedTileset;
        return tilesetIndex >= 0;
    }
}
