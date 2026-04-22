using HarmonyLib;
using PugMod;
using Pug.UnityExtensions;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class MapTeleportMod : IMod
{
    public void EarlyInit() { }
    public void Init() { }
    public void Shutdown() { }
    public void ModObjectLoaded(Object obj) { }
    public bool CanBeUnloaded() { return true; }
    public void Update() { }
}

[HarmonyPatch(typeof(MapMarkerUIElement), "OnLeftClicked")]
public static class MapMarkerClickTeleportPatch
{
    [HarmonyPrefix]
    public static bool Prefix(MapMarkerUIElement __instance)
    {
        if (!CanTeleportToMarker(__instance.markerType))
        {
            return true;
        }

        var playerController = Manager.main?.player;
        if (playerController == null)
        {
            return true;
        }

        var markerPosition = EntityUtility.GetComponentData<LocalTransform>(__instance.mapMarkerEntity, __instance.world).Position;
        var markerVariation = EntityUtility.GetObjectData(__instance.mapMarkerEntity, __instance.world).variation;
        var offset = markerVariation == 20 ? new float2(1f, 1f) : new float2(1f, -0.25f);

        playerController.QueueInputAction(new UIInputActionData
        {
            action = UIInputAction.Teleport,
            position = markerPosition.ToFloat2() + offset
        });

        if (Manager.ui != null && Manager.ui.isShowingMap)
        {
            Manager.ui.OnMapToggle();
        }

        return false;
    }

    private static bool CanTeleportToMarker(MapMarkerType markerType)
    {
        switch (markerType)
        {
            case MapMarkerType.Portal:
            case MapMarkerType.Waypoint:
            case MapMarkerType.PlayerGrave:
            case MapMarkerType.Ping:
            case MapMarkerType.UniqueBoss:
            case MapMarkerType.UniqueScene:
            case MapMarkerType.UserPlacedMarker:
                return true;
            default:
                return false;
        }
    }
}
