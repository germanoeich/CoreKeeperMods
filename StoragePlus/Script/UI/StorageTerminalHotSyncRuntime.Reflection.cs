#if STORAGEPLUS_HOTSYNC
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CoreLib.Submodule.UserInterface.Component;
using Newtonsoft.Json;
using UnityEngine;

public static partial class StorageTerminalHotSyncRuntime
{
    private static StorageTerminalHotSyncSnapshot.FieldSnapshot CreateJsonField(string name, object value)
    {
        return new StorageTerminalHotSyncSnapshot.FieldSnapshot
        {
            name = name,
            kind = StorageTerminalHotSyncValueKind.Json,
            jsonValue = JsonConvert.SerializeObject(value, JsonSettings)
        };
    }

    private static StorageTerminalHotSyncSnapshot.FieldSnapshot CreateObjectReferenceField(
        string name,
        UnityEngine.Object value,
        Transform snapshotRoot)
    {
        return new StorageTerminalHotSyncSnapshot.FieldSnapshot
        {
            name = name,
            kind = StorageTerminalHotSyncValueKind.ObjectReference,
            objectReference = CaptureObjectReference(value, snapshotRoot)
        };
    }

    private static StorageTerminalHotSyncSnapshot.FieldSnapshot CreateObjectReferenceCollectionField(
        string name,
        IEnumerable values,
        Transform snapshotRoot)
    {
        StorageTerminalHotSyncSnapshot.FieldSnapshot fieldSnapshot = new()
        {
            name = name,
            kind = StorageTerminalHotSyncValueKind.ObjectReferenceCollection,
            objectReferences = new List<StorageTerminalHotSyncSnapshot.ObjectReferenceSnapshot>()
        };

        if (values == null)
        {
            return fieldSnapshot;
        }

        foreach (object value in values)
        {
            fieldSnapshot.objectReferences.Add(CaptureObjectReference(value as UnityEngine.Object, snapshotRoot));
        }

        return fieldSnapshot;
    }

    private static StorageTerminalHotSyncSnapshot.ObjectReferenceSnapshot CaptureObjectReference(
        UnityEngine.Object value,
        Transform snapshotRoot)
    {
        if (value == null)
        {
            return new StorageTerminalHotSyncSnapshot.ObjectReferenceSnapshot
            {
                kind = StorageTerminalHotSyncObjectReferenceKind.Null
            };
        }

        Transform hierarchyTransform = null;
        if (value is GameObject gameObject)
        {
            hierarchyTransform = gameObject.transform;
        }
        else if (value is Component componentValue)
        {
            hierarchyTransform = componentValue.transform;
        }

        if (hierarchyTransform != null && hierarchyTransform.IsChildOf(snapshotRoot))
        {
            string path = GetTransformPath(snapshotRoot, hierarchyTransform);
            if (value is GameObject)
            {
                return new StorageTerminalHotSyncSnapshot.ObjectReferenceSnapshot
                {
                    kind = StorageTerminalHotSyncObjectReferenceKind.HierarchyGameObject,
                    path = path
                };
            }

            Component component = (Component)value;
            return new StorageTerminalHotSyncSnapshot.ObjectReferenceSnapshot
            {
                kind = StorageTerminalHotSyncObjectReferenceKind.HierarchyComponent,
                path = path,
                typeName = GetTypeId(component.GetType()),
                componentIndex = GetComponentTypeIndex(component.gameObject, component)
            };
        }

        return new StorageTerminalHotSyncSnapshot.ObjectReferenceSnapshot
        {
            kind = StorageTerminalHotSyncObjectReferenceKind.Asset,
            assetName = value.name ?? string.Empty,
            typeName = GetTypeId(value.GetType())
        };
    }

    private static bool TryResolveObjectReference(
        StorageTerminalHotSyncSnapshot.ObjectReferenceSnapshot referenceSnapshot,
        Type targetType,
        ApplyContext context,
        out UnityEngine.Object resolvedObject)
    {
        resolvedObject = null;
        if (referenceSnapshot == null || referenceSnapshot.kind == StorageTerminalHotSyncObjectReferenceKind.Null)
        {
            return true;
        }

        switch (referenceSnapshot.kind)
        {
            case StorageTerminalHotSyncObjectReferenceKind.HierarchyGameObject:
                if (!context.NodeLookup.TryGetValue(referenceSnapshot.path ?? string.Empty, out Transform targetGameObjectTransform))
                {
                    return false;
                }

                resolvedObject = targetType == typeof(GameObject) || targetType == typeof(UnityEngine.Object)
                    ? targetGameObjectTransform.gameObject
                    : targetGameObjectTransform.GetComponent(targetType);
                return resolvedObject != null || targetType == typeof(GameObject) || targetType == typeof(UnityEngine.Object);

            case StorageTerminalHotSyncObjectReferenceKind.HierarchyComponent:
                if (!context.NodeLookup.TryGetValue(referenceSnapshot.path ?? string.Empty, out Transform targetComponentTransform))
                {
                    return false;
                }

                Type referenceType = ResolveType(referenceSnapshot.typeName) ?? targetType;
                resolvedObject = GetComponentByTypeIndex(targetComponentTransform.gameObject, referenceType, referenceSnapshot.componentIndex);
                return resolvedObject != null;

            case StorageTerminalHotSyncObjectReferenceKind.Asset:
                resolvedObject = ResolveAssetReference(referenceSnapshot, targetType);
                return resolvedObject != null;

            default:
                return false;
        }
    }

    private static UnityEngine.Object ResolveAssetReference(
        StorageTerminalHotSyncSnapshot.ObjectReferenceSnapshot referenceSnapshot,
        Type targetType)
    {
        if (StoragePlusMod.ModInfo == null)
        {
            return null;
        }

        Type referenceType = ResolveType(referenceSnapshot.typeName) ?? targetType;
        List<UnityEngine.Object> assets = StoragePlusMod.ModInfo.Assets;
        for (int i = 0; i < assets.Count; i++)
        {
            UnityEngine.Object asset = assets[i];
            if (asset == null)
            {
                continue;
            }

            if (!string.Equals(asset.name, referenceSnapshot.assetName, StringComparison.Ordinal))
            {
                continue;
            }

            if (referenceType.IsAssignableFrom(asset.GetType()) || targetType.IsAssignableFrom(asset.GetType()))
            {
                return asset;
            }
        }

        return null;
    }

    private static bool TryCaptureReflectionField(
        Transform snapshotRoot,
        FieldInfo field,
        object fieldValue,
        List<StorageTerminalHotSyncSnapshot.FieldSnapshot> output)
    {
        Type fieldType = field.FieldType;
        if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
        {
            output.Add(CreateObjectReferenceField(field.Name, fieldValue as UnityEngine.Object, snapshotRoot));
            return true;
        }

        if (TryGetUnityObjectCollectionElementType(fieldType, out _))
        {
            output.Add(CreateObjectReferenceCollectionField(field.Name, fieldValue as IEnumerable, snapshotRoot));
            return true;
        }

        output.Add(CreateJsonField(field.Name, fieldValue));
        return true;
    }

    private static bool TryApplyReflectionField(
        object target,
        FieldInfo field,
        StorageTerminalHotSyncSnapshot.FieldSnapshot fieldSnapshot,
        ApplyContext context)
    {
        switch (fieldSnapshot.kind)
        {
            case StorageTerminalHotSyncValueKind.Json:
                object value = JsonConvert.DeserializeObject(fieldSnapshot.jsonValue, field.FieldType, JsonSettings);
                field.SetValue(target, value);
                return true;

            case StorageTerminalHotSyncValueKind.ObjectReference:
                if (!TryResolveObjectReference(fieldSnapshot.objectReference, field.FieldType, context, out UnityEngine.Object reference))
                {
                    return false;
                }

                field.SetValue(target, reference);
                return true;

            case StorageTerminalHotSyncValueKind.ObjectReferenceCollection:
                if (!TryResolveObjectReferenceCollection(field.FieldType, fieldSnapshot.objectReferences, context, out object collectionValue))
                {
                    return false;
                }

                field.SetValue(target, collectionValue);
                return true;

            default:
                return false;
        }
    }

    private static bool TryResolveObjectReferenceCollection(
        Type fieldType,
        List<StorageTerminalHotSyncSnapshot.ObjectReferenceSnapshot> references,
        ApplyContext context,
        out object resolvedCollection)
    {
        resolvedCollection = null;
        if (!TryGetUnityObjectCollectionElementType(fieldType, out Type elementType))
        {
            return false;
        }

        int count = references?.Count ?? 0;
        if (fieldType.IsArray)
        {
            Array array = Array.CreateInstance(elementType, count);
            for (int i = 0; i < count; i++)
            {
                if (!TryResolveObjectReference(references[i], elementType, context, out UnityEngine.Object elementValue))
                {
                    return false;
                }

                array.SetValue(elementValue, i);
            }

            resolvedCollection = array;
            return true;
        }

        IList list = (IList)Activator.CreateInstance(fieldType);
        for (int i = 0; i < count; i++)
        {
            if (!TryResolveObjectReference(references[i], elementType, context, out UnityEngine.Object elementValue))
            {
                return false;
            }

            list.Add(elementValue);
        }

        resolvedCollection = list;
        return true;
    }

    private static bool TryGetUnityObjectCollectionElementType(Type fieldType, out Type elementType)
    {
        elementType = null;
        if (fieldType.IsArray)
        {
            Type arrayElementType = fieldType.GetElementType();
            if (arrayElementType != null && typeof(UnityEngine.Object).IsAssignableFrom(arrayElementType))
            {
                elementType = arrayElementType;
                return true;
            }

            return false;
        }

        if (!fieldType.IsGenericType || fieldType.GetGenericTypeDefinition() != typeof(List<>))
        {
            return false;
        }

        Type listElementType = fieldType.GetGenericArguments()[0];
        if (!typeof(UnityEngine.Object).IsAssignableFrom(listElementType))
        {
            return false;
        }

        elementType = listElementType;
        return true;
    }

    private static FieldInfo[] GetReflectionSyncFields(Type type)
    {
        if (ReflectionFieldCache.TryGetValue(type, out FieldInfo[] cached))
        {
            return cached;
        }

        List<FieldInfo> fields = new();
        Type current = type;
        while (current != null &&
               current != typeof(MonoBehaviour) &&
               current != typeof(Behaviour) &&
               current != typeof(Component) &&
               current != typeof(object))
        {
            ReflectionFieldExclusions.TryGetValue(current, out HashSet<string> exclusionsForType);

            FieldInfo[] declaredFields = current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int i = 0; i < declaredFields.Length; i++)
            {
                FieldInfo field = declaredFields[i];
                if (!IsUnitySerializableField(field))
                {
                    continue;
                }

                if (exclusionsForType != null && exclusionsForType.Contains(field.Name))
                {
                    continue;
                }

                fields.Add(field);
            }

            current = current.BaseType;
        }

        FieldInfo[] result = fields.ToArray();
        ReflectionFieldCache[type] = result;
        return result;
    }

    private static bool IsUnitySerializableField(FieldInfo field)
    {
        if (field.IsStatic || field.IsNotSerialized)
        {
            return false;
        }

        if (field.IsPublic)
        {
            return true;
        }

        return field.GetCustomAttribute<SerializeField>() != null;
    }

    private static Dictionary<Type, HashSet<string>> CreateReflectionFieldExclusions()
    {
        return new Dictionary<Type, HashSet<string>>
        {
            [typeof(StorageTerminalUI)] = new HashSet<string>(StringComparer.Ordinal)
            {
                "_filteredEntries",
                "_lastRelayEntity",
                "_cachedRelayEntity",
                "_lastContentsHash",
                "_lastEntryCount",
                "_lastFilter",
                "_lastSortSignature",
                "_built"
            },
            [typeof(StorageTerminalGrid)] = new HashSet<string>(StringComparer.Ordinal)
            {
                "_entries",
                "_owner",
                "_currentScroll",
                "_authoredLocalScale",
                "_authoredItemsRootScale"
            },
            [typeof(StorageTerminalSearchField)] = new HashSet<string>(StringComparer.Ordinal)
            {
                "_currentCharIndex",
                "<inputIsActive>k__BackingField"
            },
            [typeof(StorageTerminalFilterGrid)] = new HashSet<string>(StringComparer.Ordinal)
            {
                "_buttons",
                "_owner"
            },
            [typeof(StorageTerminalItemSlot)] = new HashSet<string>(StringComparer.Ordinal)
            {
                "_entry",
                "_authoredLocalScale",
                "<DataIndex>k__BackingField"
            },
            [typeof(UIelement)] = new HashSet<string>(StringComparer.Ordinal)
            {
                "onElementSelectedEvent",
                "onElementDeselectedEvent",
                "leftClickWasHeldDownThisFrame",
                "rightClickWasHeldDownThisFrame",
                "mod1WasHeldDownThisFrame",
                "mod2WasHeldDownThisFrame",
                "wasAutoActivated",
                "<leftClickIsHeldDown>k__BackingField",
                "<rightClickIsHeldDown>k__BackingField",
                "<mod1IsHeldDown>k__BackingField",
                "<mod2IsHeldDown>k__BackingField"
            },
            [typeof(ItemSlotsUIContainer)] = new HashSet<string>(StringComparer.Ordinal)
            {
                "categoryWindowStartSlotIndex",
                "itemSlots",
                "itemHoverProxies",
                "currentCategoryIndex",
                "overrideSlotsBackgroundColor",
                "initDone",
                "activeSlotIndex",
                "<visibleRows>k__BackingField",
                "<visibleColumns>k__BackingField",
                "<firstSlot>k__BackingField"
            },
            [typeof(SlotUIBase)] = new HashSet<string>(StringComparer.Ordinal)
            {
                "currentlyRenderedAmountNumber",
                "slotsUIContainer",
                "_disableAnimatorTimer"
            },
            [typeof(PugText)] = new HashSet<string>(StringComparer.Ordinal)
            {
                "font",
                "dynamicText",
                "dynamicTextMeshRenderer",
                "_needsProfanityCheck",
                "displayedTextString",
                "displayedTextStringLinesAmount",
                "defaultStyle",
                "_sharedGlyphMaterial",
                "dimensions",
                "glyphs",
                "glyphTransforms",
                "glyphColorOverrides",
                "pooledTransforms",
                "localPositionBackups",
                "localCharacterEndPositions",
                "effects",
                "usePauseSigns",
                "hasCalledAwake",
                "startCalled",
                "m_dynamicFontInfo",
                "_keepColorOnStart",
                "prevLanguage",
                "prevFormatFields",
                "prevOrderInLayer",
                "prevMaxWidth",
                "<tmpColor>k__BackingField",
                "<isUsingDynamicText>k__BackingField",
                "textString",
                "formatFields"
            },
            [typeof(UIScrollWindow)] = new HashSet<string>(StringComparer.Ordinal)
            {
                "_scrollable",
                "_previousPosition",
                "_previousHeight",
                "_isCreditsMenu",
                "_spriteObjects",
                "_spriteObjectStartPositions",
                "_spriteObjectOffset",
                "<ScrollHeight>k__BackingField"
            },
            [typeof(ScrollBar)] = new HashSet<string>(StringComparer.Ordinal)
            {
                "handleIsPressed",
                "prevScrollHeight"
            },
            [typeof(ButtonUIElement)] = new HashSet<string>(StringComparer.Ordinal)
            {
                "onLeftClick",
                "onRightClick",
                "onSelected",
                "onDeselected",
                "_optionalSelectedMarkerSr",
                "_boxCollider",
                "_unpressedDefaultColors",
                "_pressedDefaultColors",
                "bindingTerms"
            },
            [typeof(ColorReplacer)] = new HashSet<string>(StringComparer.Ordinal)
            {
                "_materialPropertyBlock",
                "_sprite"
            }
        };
    }
}
#endif
