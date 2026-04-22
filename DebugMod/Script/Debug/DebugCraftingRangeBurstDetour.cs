using System;
using System.Runtime.InteropServices;
using Inventory;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

internal static class DebugCraftingRangeBurstDetour
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CraftingRangeDirectCallDelegate(
        ref float3 position,
        ref CollisionWorld collisionWorld,
        ref ComponentLookup<InventoryAutoTransferEnabledCD> inventoryAutoTransferEnabledLookup,
        ref ComponentLookup<LocalTransform> localTransformLookup,
        ref NativeList<Entity> inventories);

    private const float DefaultRange = 10f;
    private const int MaxInventories = 20;

    private static readonly CraftingRangeDirectCallDelegate DetourDelegate = DetourInvoke;
    private static readonly BurstDirectCallPointerHook Hook = new BurstDirectCallPointerHook(
        typeof(InventoryUtility),
        nameof(InventoryUtility.GetNearbyChestsForCraftingByDistance),
        DetourDelegate);

    public static bool IsEnabled { get; private set; }

    public static float CurrentRange { get; private set; } = DefaultRange;

    public static string GetStatus()
    {
        if (!Hook.TryResolve(out string error))
        {
            return error;
        }

        string originalPointer = Hook.OriginalPointerCaptured ? FormatPointer(Hook.OriginalPointer) : "<not captured>";
        string directCallType = Hook.BurstDirectCallType?.FullName ?? "<unresolved>";

        return $"crafting range detour enabled={IsEnabled} range={CurrentRange:0.##} directCallType={directCallType} currentPointer={FormatPointer(Hook.CurrentPointer)} originalPointer={originalPointer} detourPointer={FormatPointer(Hook.DetourPointer)}";
    }

    public static string Enable(float range)
    {
        if (float.IsNaN(range) || float.IsInfinity(range) || range <= 0f)
        {
            return $"range must be > 0, got {range}";
        }

        if (!Hook.TryEnable(out string error))
        {
            return error;
        }

        CurrentRange = range;
        IsEnabled = true;
        return $"crafting range detour enabled with range {CurrentRange:0.##}";
    }

    public static string Disable()
    {
        if (!Hook.TryDisable(out string error))
        {
            return error;
        }

        IsEnabled = false;
        CurrentRange = DefaultRange;
        return Hook.OriginalPointerCaptured
            ? $"crafting range detour disabled; restored original pointer {FormatPointer(Hook.OriginalPointer)}"
            : "crafting range detour disabled; original pointer was not captured, reset to zero";
    }

    private static void DetourInvoke(
        ref float3 position,
        ref CollisionWorld collisionWorld,
        ref ComponentLookup<InventoryAutoTransferEnabledCD> inventoryAutoTransferEnabledLookup,
        ref ComponentLookup<LocalTransform> localTransformLookup,
        ref NativeList<Entity> inventories)
    {
        InventoryUtility.GetNearbyChestsByDistance(
            in position,
            in collisionWorld,
            in inventoryAutoTransferEnabledLookup,
            in localTransformLookup,
            ref inventories,
            CurrentRange,
            MaxInventories);
    }

    private static string FormatPointer(IntPtr pointer)
    {
        return "0x" + pointer.ToInt64().ToString("X");
    }
}
