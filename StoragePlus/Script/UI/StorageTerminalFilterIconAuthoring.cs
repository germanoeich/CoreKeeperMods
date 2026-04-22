using System;
using System.Collections.Generic;
using Pug.UnityExtensions;
using UnityEngine;

public sealed class StorageTerminalFilterIconAuthoring : MonoBehaviour, IStorageTerminalHotSyncAware
{
    [SerializeField]
    private Transform iconContainer;

    [SerializeField]
    private SpriteRenderer iconRenderer;

    private ColorReplacer colorReplacer;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string foodIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string ingredientsIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string cropsIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string fishIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string materialsIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string blocksIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string groundIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string floorIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string bridgesIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string decorationIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string placeablesIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string weaponsIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string toolsIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string armorIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string accessoriesIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string valuablesIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string petsIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string crittersIconObject;

    [SerializeField, PickStringFromEnum(typeof(ObjectID))]
    private string miscIconObject;

    private void Reset()
    {
        AssignSerializedReferences();
    }

    private void OnValidate()
    {
        AssignSerializedReferences();
    }

    public void OnHotSyncApplied()
    {
        AssignSerializedReferences();
        StorageTerminalUIUtility.ApplyVanillaItemIconMaterial(iconRenderer);
    }

    internal int IconEntryCount => CountConfiguredIcons();

    internal string GetDebugEntrySummary()
    {
        return BuildEntrySummary();
    }

    internal void CopyConfigurationFrom(StorageTerminalFilterIconAuthoring source)
    {
        if (source == null || ReferenceEquals(source, this))
        {
            return;
        }

        foodIconObject = source.foodIconObject;
        ingredientsIconObject = source.ingredientsIconObject;
        cropsIconObject = source.cropsIconObject;
        fishIconObject = source.fishIconObject;
        materialsIconObject = source.materialsIconObject;
        blocksIconObject = source.blocksIconObject;
        groundIconObject = source.groundIconObject;
        floorIconObject = source.floorIconObject;
        bridgesIconObject = source.bridgesIconObject;
        decorationIconObject = source.decorationIconObject;
        placeablesIconObject = source.placeablesIconObject;
        weaponsIconObject = source.weaponsIconObject;
        toolsIconObject = source.toolsIconObject;
        armorIconObject = source.armorIconObject;
        accessoriesIconObject = source.accessoriesIconObject;
        valuablesIconObject = source.valuablesIconObject;
        petsIconObject = source.petsIconObject;
        crittersIconObject = source.crittersIconObject;
        miscIconObject = source.miscIconObject;
    }

    internal void Apply(StorageTerminalItemCategory category)
    {
        AssignSerializedReferences();

        string iconObjectName = GetIconObjectName(category);
        ObjectID objectId = ResolveObjectId(iconObjectName);
        ObjectInfo objectInfo = objectId != ObjectID.None ? PugDatabase.GetObjectInfo(objectId) : null;
        ContainedObjectsBuffer containedObject = CreateContainedObject(objectId);
        Sprite sprite = GetIconSprite(iconObjectName);
        if (iconRenderer != null)
        {
            StorageTerminalUIUtility.ApplyVanillaItemIconMaterial(iconRenderer);
            iconRenderer.sprite = sprite;
            iconRenderer.enabled = sprite != null;
            iconRenderer.transform.localPosition = objectInfo?.iconOffset ?? Vector3.zero;
            iconRenderer.transform.localScale = Vector3.one;
            iconRenderer.color = Color.white;
            colorReplacer?.UpdateColorReplacerFromObjectData(containedObject);
            if (Manager.ui != null)
            {
                Manager.ui.ApplyAnyIconGradientMap(containedObject, iconRenderer);
            }
        }

        if (iconContainer != null)
        {
            iconContainer.gameObject.SetActive(sprite != null);
        }

        if (sprite == null)
        {
            Debug.LogWarning(
                $"StorageTerminal filter icon missing for category={category}, iconObject='{iconObjectName}', " +
                $"resolvedObjectId={objectId}, hasObjectInfo={objectInfo != null}, " +
                $"hasIcon={objectInfo?.icon != null}, hasSmallIcon={objectInfo?.smallIcon != null}, " +
                $"hasIconContainer={iconContainer != null}, hasIconRenderer={iconRenderer != null}, " +
                $"iconEntries={CountConfiguredIcons()}, entrySummary={BuildEntrySummary()}.",
                this);
        }
        else
        {
            Debug.Log(
                $"StorageTerminal filter icon applied for category={category}, iconObject='{iconObjectName}', " +
                $"resolvedObjectId={objectId}, sprite='{sprite.name}', hasIconContainer={iconContainer != null}, " +
                $"hasIconRenderer={iconRenderer != null}, iconEntries={CountConfiguredIcons()}.",
                this);
        }
    }

    internal void ApplyManualSprite(Sprite sprite)
    {
        AssignSerializedReferences();

        if (iconRenderer != null)
        {
            StorageTerminalUIUtility.ApplyVanillaItemIconMaterial(iconRenderer);
            iconRenderer.sprite = sprite;
            iconRenderer.enabled = sprite != null;
            iconRenderer.transform.localPosition = Vector3.zero;
            iconRenderer.transform.localScale = Vector3.one;
            iconRenderer.color = Color.white;
            colorReplacer?.UpdateColorReplacerFromObjectData(default);
            if (Manager.ui != null)
            {
                Manager.ui.ApplyAnyIconGradientMap(default, iconRenderer);
            }
        }

        if (iconContainer != null)
        {
            iconContainer.gameObject.SetActive(sprite != null);
        }
    }

    private string GetIconObjectName(StorageTerminalItemCategory category)
    {
        return GetConfiguredIconObjectName(category);
    }

    private static Sprite GetIconSprite(string iconObjectName)
    {
        ObjectID objectId = ResolveObjectId(iconObjectName);
        if (objectId == ObjectID.None)
        {
            return null;
        }

        Sprite overrideIcon = Manager.ui != null && Manager.ui.itemOverridesTable != null
            ? Manager.ui.itemOverridesTable.GetIconOverride(new ObjectData
            {
                objectID = objectId,
                variation = 0,
                amount = 1
            }, getSmallIcon: false)
            : null;
        if (overrideIcon != null)
        {
            return overrideIcon;
        }

        ObjectInfo objectInfo = PugDatabase.GetObjectInfo(objectId);
        return objectInfo?.icon ?? objectInfo?.smallIcon;
    }

    private static ContainedObjectsBuffer CreateContainedObject(ObjectID objectId)
    {
        if (objectId == ObjectID.None)
        {
            return default;
        }

        return new ContainedObjectsBuffer
        {
            objectData = new ObjectDataCD
            {
                objectID = objectId,
                amount = 1
            }
        };
    }

    private static ObjectID ResolveObjectId(string iconObjectName)
    {
        if (string.IsNullOrWhiteSpace(iconObjectName))
        {
            return ObjectID.None;
        }

        string trimmedName = iconObjectName.Trim();
        if (trimmedName.StartsWith("ObjectID.", StringComparison.Ordinal))
        {
            trimmedName = trimmedName.Substring("ObjectID.".Length);
        }

        return Enum.TryParse(trimmedName, ignoreCase: true, out ObjectID parsedObjectId)
            ? parsedObjectId
            : ObjectID.None;
    }

    private int CountConfiguredIcons()
    {
        int count = 0;
        IReadOnlyList<StorageTerminalItemCategory> categories = StorageTerminalItemCategoryUtility.GetOrderedCategories();
        for (int i = 0; i < categories.Count; i++)
        {
            StorageTerminalItemCategory category = categories[i];
            if (category == StorageTerminalItemCategory.All)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(GetConfiguredIconObjectName(category)))
            {
                count++;
            }
        }

        return count;
    }

    private string BuildEntrySummary()
    {
        IReadOnlyList<StorageTerminalItemCategory> categories = StorageTerminalItemCategoryUtility.GetOrderedCategories();
        System.Text.StringBuilder builder = new();
        builder.Append('[');

        int shown = 0;
        for (int i = 0; i < categories.Count; i++)
        {
            StorageTerminalItemCategory category = categories[i];
            if (category == StorageTerminalItemCategory.All)
            {
                continue;
            }

            string iconObjectName = GetConfiguredIconObjectName(category);
            if (string.IsNullOrWhiteSpace(iconObjectName))
            {
                continue;
            }

            if (shown > 0)
            {
                builder.Append(", ");
            }

            builder.Append('{');
            builder.Append(category);
            builder.Append(':');
            builder.Append(iconObjectName);
            builder.Append('}');
            shown++;

            if (shown >= 6)
            {
                break;
            }
        }

        if (shown == 0)
        {
            return "[]";
        }

        if (CountConfiguredIcons() > shown)
        {
            builder.Append(", ...");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private string GetConfiguredIconObjectName(StorageTerminalItemCategory category)
    {
        return category switch
        {
            StorageTerminalItemCategory.Food => foodIconObject,
            StorageTerminalItemCategory.Ingredients => ingredientsIconObject,
            StorageTerminalItemCategory.Crops => cropsIconObject,
            StorageTerminalItemCategory.Fish => fishIconObject,
            StorageTerminalItemCategory.Materials => materialsIconObject,
            StorageTerminalItemCategory.Blocks => blocksIconObject,
            StorageTerminalItemCategory.Ground => groundIconObject,
            StorageTerminalItemCategory.Floor => floorIconObject,
            StorageTerminalItemCategory.Bridges => bridgesIconObject,
            StorageTerminalItemCategory.Decoration => decorationIconObject,
            StorageTerminalItemCategory.Placeables => placeablesIconObject,
            StorageTerminalItemCategory.Weapons => weaponsIconObject,
            StorageTerminalItemCategory.Tools => toolsIconObject,
            StorageTerminalItemCategory.Armor => armorIconObject,
            StorageTerminalItemCategory.Accessories => accessoriesIconObject,
            StorageTerminalItemCategory.Valuables => valuablesIconObject,
            StorageTerminalItemCategory.Pets => petsIconObject,
            StorageTerminalItemCategory.Critters => crittersIconObject,
            StorageTerminalItemCategory.Misc => miscIconObject,
            _ => null
        };
    }


    private void AssignSerializedReferences()
    {
        iconContainer ??= transform.Find("IconContainer");
        iconRenderer ??= iconContainer != null ? iconContainer.GetComponentInChildren<SpriteRenderer>(includeInactive: true) : null;
        colorReplacer ??= iconRenderer != null ? iconRenderer.GetComponent<ColorReplacer>() : null;
    }
}
