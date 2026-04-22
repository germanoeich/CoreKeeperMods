using Pug.Conversion;
using Pug.UnityExtensions;

public class StoragePipeEntityMono : EntityMonoBehaviour
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
