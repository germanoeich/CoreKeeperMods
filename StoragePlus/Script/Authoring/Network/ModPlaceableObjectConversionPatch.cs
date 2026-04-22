using System;
using System.Collections.Generic;
using HarmonyLib;
using Pug.Conversion;
using UnityEngine;
using Object = UnityEngine.Object;

[HarmonyPatch(typeof(TransformConverter), nameof(TransformConverter.Convert))]
public static class ModPlaceableObjectConversionPatch
{
    private const int MaxVanillaObjectIdFloor = 32767;
    private static Dictionary<string, ObjectID> _objectIdsByName;

    public static void Reset()
    {
        _objectIdsByName = null;
    }

    [HarmonyPrefix]
    public static void TransformConverterConvertPrefix(GameObject authoring)
    {
        if (authoring == null || !authoring.TryGetComponent(out ModPlaceableObjectAuthoring helper))
        {
            return;
        }

        if (!authoring.TryGetComponent(out PlaceableObjectAuthoring placeableObject))
        {
            StoragePlusMod.Log.LogWarning($"Found {nameof(ModPlaceableObjectAuthoring)} on '{authoring.name}' without {nameof(PlaceableObjectAuthoring)}.");
            Object.DestroyImmediate(helper);
            return;
        }

        EnsureLookup();
        placeableObject.canBePlacedOnObjects ??= new List<ObjectID>();
        placeableObject.canNotBePlacedOnObjects ??= new List<ObjectID>();

        MergeResolvedObjects(placeableObject.canBePlacedOnObjects, helper.canBePlacedOnObjects, authoring.name);
        MergeResolvedObjects(placeableObject.canNotBePlacedOnObjects, helper.canNotBePlacedOnObjects, authoring.name);

        Object.DestroyImmediate(helper);
    }

    private static void EnsureLookup()
    {
        if (_objectIdsByName != null)
        {
            return;
        }

        _objectIdsByName = new Dictionary<string, ObjectID>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectID objectId in Enum.GetValues(typeof(ObjectID)))
        {
            _objectIdsByName[objectId.ToString()] = objectId;
        }

        int nextModObjectId = Math.Max((int)ObjectIDExtensions.GetHighestObjectID(), MaxVanillaObjectIdFloor) + 1;
        foreach (MonoBehaviour authoring in Manager.mod.ExtraAuthoring)
        {
            SimulateHierarchy(authoring.gameObject, ref nextModObjectId);
        }
    }

    private static void SimulateHierarchy(GameObject gameObject, ref int nextModObjectId)
    {
        int objectIndex = TryGetPreferredObjectIndex(gameObject, out int preferredObjectIndex)
            ? preferredObjectIndex
            : nextModObjectId++;

        if (gameObject.TryGetComponent(out ObjectAuthoring objectAuthoring) &&
            !string.IsNullOrWhiteSpace(objectAuthoring.objectName))
        {
            _objectIdsByName[objectAuthoring.objectName] = (ObjectID)objectIndex;
        }

        Transform transform = gameObject.transform;
        for (int index = 0; index < transform.childCount; index++)
        {
            SimulateHierarchy(transform.GetChild(index).gameObject, ref nextModObjectId);
        }
    }

    private static bool TryGetPreferredObjectIndex(GameObject gameObject, out int objectIndex)
    {
        if (gameObject.TryGetComponent(out EntityMonoBehaviourData entityData) &&
            entityData.objectInfo != null &&
            entityData.objectInfo.objectID != ObjectID.None)
        {
            objectIndex = (int)entityData.objectInfo.objectID;
            return true;
        }

        if (gameObject.TryGetComponent(out ObjectAuthoring objectAuthoring) &&
            !string.IsNullOrWhiteSpace(objectAuthoring.objectName) &&
            _objectIdsByName.TryGetValue(objectAuthoring.objectName, out ObjectID existingObjectId))
        {
            objectIndex = (int)existingObjectId;
            return true;
        }

        objectIndex = 0;
        return false;
    }

    private static void MergeResolvedObjects(List<ObjectID> target, IEnumerable<string> source, string authoringName)
    {
        if (source == null)
        {
            return;
        }

        foreach (string value in source)
        {
            if (!TryResolveObjectId(value, out ObjectID objectId))
            {
                StoragePlusMod.Log.LogWarning($"Failed to resolve placeable object reference '{value}' on '{authoringName}'.");
                continue;
            }

            if (objectId == ObjectID.None || target.Contains(objectId))
            {
                continue;
            }

            target.Add(objectId);
        }
    }

    private static bool TryResolveObjectId(string value, out ObjectID objectId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            objectId = ObjectID.None;
            return false;
        }

        if (_objectIdsByName.TryGetValue(value, out objectId))
        {
            return true;
        }

        if (Enum.TryParse(value, true, out objectId))
        {
            return true;
        }

        if (int.TryParse(value, out int numericId))
        {
            objectId = (ObjectID)numericId;
            return true;
        }

        objectId = ObjectID.None;
        return false;
    }
}
