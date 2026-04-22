#if STORAGEPLUS_HOTSYNC
using System;
using System.Collections.Generic;
using System.Reflection;
using CoreLib.Submodule.UserInterface.Component;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

public static partial class StorageTerminalHotSyncRuntime
{
    private static Dictionary<Type, IComponentSnapshotHandler> CreateHandlers()
    {
        return new Dictionary<Type, IComponentSnapshotHandler>
        {
            [typeof(Transform)] = new TransformSnapshotHandler(),
            [typeof(RectTransform)] = new RectTransformSnapshotHandler(),
            [typeof(SpriteRenderer)] = new SpriteRendererSnapshotHandler(),
            [typeof(BoxCollider)] = new BoxColliderSnapshotHandler(),
            [typeof(UIelement)] = new ReflectionSnapshotHandler<UIelement>(),
            [typeof(ButtonUIElement)] = new ReflectionSnapshotHandler<ButtonUIElement>(),
            [typeof(ItemSlotsUIContainer)] = new ReflectionSnapshotHandler<ItemSlotsUIContainer>(),
            [typeof(SlotUIBase)] = new ReflectionSnapshotHandler<SlotUIBase>(),
            [typeof(StorageTerminalUI)] = new ReflectionSnapshotHandler<StorageTerminalUI>(),
            [typeof(StorageTerminalGrid)] = new ReflectionSnapshotHandler<StorageTerminalGrid>(),
            [typeof(StorageTerminalSearchField)] = new ReflectionSnapshotHandler<StorageTerminalSearchField>(),
            [typeof(StorageTerminalSortButton)] = new ReflectionSnapshotHandler<StorageTerminalSortButton>(),
            [typeof(StorageTerminalShowFiltersButton)] = new ReflectionSnapshotHandler<StorageTerminalShowFiltersButton>(),
            [typeof(StorageTerminalHintTextButton)] = new ReflectionSnapshotHandler<StorageTerminalHintTextButton>(),
            [typeof(StorageTerminalFilterGrid)] = new ReflectionSnapshotHandler<StorageTerminalFilterGrid>(),
            [typeof(StorageTerminalFilterButton)] = new ReflectionSnapshotHandler<StorageTerminalFilterButton>(),
            [typeof(StorageTerminalFilterIconAuthoring)] = new ReflectionSnapshotHandler<StorageTerminalFilterIconAuthoring>(),
            [typeof(StorageTerminalItemSlot)] = new ReflectionSnapshotHandler<StorageTerminalItemSlot>(),
            [typeof(PugText)] = new ReflectionSnapshotHandler<PugText>(),
            [typeof(UIScrollWindow)] = new ReflectionSnapshotHandler<UIScrollWindow>(),
            [typeof(ScrollBar)] = new ReflectionSnapshotHandler<ScrollBar>(),
            [typeof(ScrollBarHandle)] = new ReflectionSnapshotHandler<ScrollBarHandle>(),
            [typeof(CharacterMarkBlinker)] = new ReflectionSnapshotHandler<CharacterMarkBlinker>(),
            [typeof(ColorReplacer)] = new ReflectionSnapshotHandler<ColorReplacer>(),
            [typeof(LinkToPlayerInventory)] = new ReflectionSnapshotHandler<LinkToPlayerInventory>(),
            [typeof(ModUIAuthoring)] = new ReflectionSnapshotHandler<ModUIAuthoring>()
        };
    }

    private sealed class FieldsOnlyContractResolver : DefaultContractResolver
    {
        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            List<JsonProperty> properties = new();
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.IsStatic || field.IsNotSerialized)
                {
                    continue;
                }

                JsonProperty property = base.CreateProperty(field, memberSerialization);
                property.Readable = true;
                property.Writable = true;
                properties.Add(property);
            }

            return properties;
        }
    }

    private sealed class ApplyContext
    {
        public ApplyContext(
            Transform runtimeRoot,
            Dictionary<string, Transform> nodeLookup,
            HashSet<string> parentsWithRuntimeExtras)
        {
            RuntimeRoot = runtimeRoot;
            NodeLookup = nodeLookup;
            ParentsWithRuntimeExtras = parentsWithRuntimeExtras;
        }

        public Transform RuntimeRoot { get; }
        public Dictionary<string, Transform> NodeLookup { get; }
        public HashSet<string> ParentsWithRuntimeExtras { get; }
    }

    private interface IComponentSnapshotHandler
    {
        StorageTerminalHotSyncSnapshot.ComponentSnapshot Capture(Component component, Transform snapshotRoot);

        void Apply(
            Component component,
            string nodePath,
            StorageTerminalHotSyncSnapshot.ComponentSnapshot componentSnapshot,
            ApplyContext context);
    }

    private sealed class ReflectionSnapshotHandler<TComponent> : IComponentSnapshotHandler where TComponent : Component
    {
        public StorageTerminalHotSyncSnapshot.ComponentSnapshot Capture(Component component, Transform snapshotRoot)
        {
            TComponent typedComponent = component as TComponent;
            if (typedComponent == null)
            {
                return null;
            }

            StorageTerminalHotSyncSnapshot.ComponentSnapshot componentSnapshot = new()
            {
                typeName = GetTypeId(component.GetType()),
                componentIndex = GetComponentTypeIndex(component.gameObject, component)
            };

            FieldInfo[] fields = GetReflectionSyncFields(component.GetType());
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                object fieldValue = field.GetValue(typedComponent);
                TryCaptureReflectionField(snapshotRoot, field, fieldValue, componentSnapshot.fields);
            }

            return componentSnapshot;
        }

        public void Apply(
            Component component,
            string nodePath,
            StorageTerminalHotSyncSnapshot.ComponentSnapshot componentSnapshot,
            ApplyContext context)
        {
            TComponent typedComponent = component as TComponent;
            if (typedComponent == null)
            {
                return;
            }

            FieldInfo[] fields = GetReflectionSyncFields(component.GetType());
            Dictionary<string, FieldInfo> fieldLookup = new(fields.Length, StringComparer.Ordinal);
            for (int i = 0; i < fields.Length; i++)
            {
                fieldLookup[fields[i].Name] = fields[i];
            }

            for (int i = 0; i < componentSnapshot.fields.Count; i++)
            {
                StorageTerminalHotSyncSnapshot.FieldSnapshot fieldSnapshot = componentSnapshot.fields[i];
                if (!fieldLookup.TryGetValue(fieldSnapshot.name, out FieldInfo field))
                {
                    continue;
                }

                TryApplyReflectionField(typedComponent, field, fieldSnapshot, context);
            }
        }
    }

    private sealed class TransformSnapshotHandler : IComponentSnapshotHandler
    {
        public StorageTerminalHotSyncSnapshot.ComponentSnapshot Capture(Component component, Transform snapshotRoot)
        {
            Transform transform = component as Transform;
            if (transform == null || component.GetType() != typeof(Transform))
            {
                return null;
            }

            StorageTerminalHotSyncSnapshot.ComponentSnapshot componentSnapshot = new()
            {
                typeName = GetTypeId(typeof(Transform)),
                componentIndex = GetComponentTypeIndex(component.gameObject, component)
            };

            componentSnapshot.fields.Add(CreateJsonField("localPosition", transform.localPosition));
            componentSnapshot.fields.Add(CreateJsonField("localRotation", transform.localRotation));
            componentSnapshot.fields.Add(CreateJsonField("localScale", transform.localScale));
            componentSnapshot.fields.Add(CreateJsonField("siblingIndex", transform.GetSiblingIndex()));
            return componentSnapshot;
        }

        public void Apply(
            Component component,
            string nodePath,
            StorageTerminalHotSyncSnapshot.ComponentSnapshot componentSnapshot,
            ApplyContext context)
        {
            Transform transform = component as Transform;
            if (transform == null || component.GetType() != typeof(Transform))
            {
                return;
            }

            for (int i = 0; i < componentSnapshot.fields.Count; i++)
            {
                StorageTerminalHotSyncSnapshot.FieldSnapshot fieldSnapshot = componentSnapshot.fields[i];
                switch (fieldSnapshot.name)
                {
                    case "localPosition":
                        transform.localPosition = JsonConvert.DeserializeObject<Vector3>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "localRotation":
                        transform.localRotation = JsonConvert.DeserializeObject<Quaternion>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "localScale":
                        transform.localScale = JsonConvert.DeserializeObject<Vector3>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "siblingIndex":
                        string parentPath = GetParentPath(nodePath);
                        if (!context.ParentsWithRuntimeExtras.Contains(parentPath))
                        {
                            transform.SetSiblingIndex(JsonConvert.DeserializeObject<int>(fieldSnapshot.jsonValue, JsonSettings));
                        }
                        break;
                }
            }
        }
    }

    private sealed class RectTransformSnapshotHandler : IComponentSnapshotHandler
    {
        public StorageTerminalHotSyncSnapshot.ComponentSnapshot Capture(Component component, Transform snapshotRoot)
        {
            RectTransform rectTransform = component as RectTransform;
            if (rectTransform == null)
            {
                return null;
            }

            StorageTerminalHotSyncSnapshot.ComponentSnapshot componentSnapshot = new()
            {
                typeName = GetTypeId(typeof(RectTransform)),
                componentIndex = GetComponentTypeIndex(component.gameObject, component)
            };

            componentSnapshot.fields.Add(CreateJsonField("localPosition", rectTransform.localPosition));
            componentSnapshot.fields.Add(CreateJsonField("localRotation", rectTransform.localRotation));
            componentSnapshot.fields.Add(CreateJsonField("localScale", rectTransform.localScale));
            componentSnapshot.fields.Add(CreateJsonField("siblingIndex", rectTransform.GetSiblingIndex()));
            componentSnapshot.fields.Add(CreateJsonField("anchoredPosition", rectTransform.anchoredPosition));
            componentSnapshot.fields.Add(CreateJsonField("sizeDelta", rectTransform.sizeDelta));
            componentSnapshot.fields.Add(CreateJsonField("anchorMin", rectTransform.anchorMin));
            componentSnapshot.fields.Add(CreateJsonField("anchorMax", rectTransform.anchorMax));
            componentSnapshot.fields.Add(CreateJsonField("pivot", rectTransform.pivot));
            componentSnapshot.fields.Add(CreateJsonField("offsetMin", rectTransform.offsetMin));
            componentSnapshot.fields.Add(CreateJsonField("offsetMax", rectTransform.offsetMax));
            return componentSnapshot;
        }

        public void Apply(
            Component component,
            string nodePath,
            StorageTerminalHotSyncSnapshot.ComponentSnapshot componentSnapshot,
            ApplyContext context)
        {
            RectTransform rectTransform = component as RectTransform;
            if (rectTransform == null)
            {
                return;
            }

            for (int i = 0; i < componentSnapshot.fields.Count; i++)
            {
                StorageTerminalHotSyncSnapshot.FieldSnapshot fieldSnapshot = componentSnapshot.fields[i];
                switch (fieldSnapshot.name)
                {
                    case "localPosition":
                        rectTransform.localPosition = JsonConvert.DeserializeObject<Vector3>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "localRotation":
                        rectTransform.localRotation = JsonConvert.DeserializeObject<Quaternion>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "localScale":
                        rectTransform.localScale = JsonConvert.DeserializeObject<Vector3>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "siblingIndex":
                        string parentPath = GetParentPath(nodePath);
                        if (!context.ParentsWithRuntimeExtras.Contains(parentPath))
                        {
                            rectTransform.SetSiblingIndex(JsonConvert.DeserializeObject<int>(fieldSnapshot.jsonValue, JsonSettings));
                        }
                        break;
                    case "anchoredPosition":
                        rectTransform.anchoredPosition = JsonConvert.DeserializeObject<Vector2>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "sizeDelta":
                        rectTransform.sizeDelta = JsonConvert.DeserializeObject<Vector2>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "anchorMin":
                        rectTransform.anchorMin = JsonConvert.DeserializeObject<Vector2>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "anchorMax":
                        rectTransform.anchorMax = JsonConvert.DeserializeObject<Vector2>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "pivot":
                        rectTransform.pivot = JsonConvert.DeserializeObject<Vector2>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "offsetMin":
                        rectTransform.offsetMin = JsonConvert.DeserializeObject<Vector2>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "offsetMax":
                        rectTransform.offsetMax = JsonConvert.DeserializeObject<Vector2>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                }
            }
        }
    }

    private sealed class SpriteRendererSnapshotHandler : IComponentSnapshotHandler
    {
        public StorageTerminalHotSyncSnapshot.ComponentSnapshot Capture(Component component, Transform snapshotRoot)
        {
            SpriteRenderer spriteRenderer = component as SpriteRenderer;
            if (spriteRenderer == null)
            {
                return null;
            }

            StorageTerminalHotSyncSnapshot.ComponentSnapshot componentSnapshot = new()
            {
                typeName = GetTypeId(typeof(SpriteRenderer)),
                componentIndex = GetComponentTypeIndex(component.gameObject, component)
            };

            componentSnapshot.fields.Add(CreateObjectReferenceField("sprite", spriteRenderer.sprite, snapshotRoot));
            componentSnapshot.fields.Add(CreateObjectReferenceField("sharedMaterial", spriteRenderer.sharedMaterial, snapshotRoot));
            componentSnapshot.fields.Add(CreateJsonField("color", spriteRenderer.color));
            componentSnapshot.fields.Add(CreateJsonField("size", spriteRenderer.size));
            componentSnapshot.fields.Add(CreateJsonField("drawMode", spriteRenderer.drawMode));
            componentSnapshot.fields.Add(CreateJsonField("flipX", spriteRenderer.flipX));
            componentSnapshot.fields.Add(CreateJsonField("flipY", spriteRenderer.flipY));
            componentSnapshot.fields.Add(CreateJsonField("maskInteraction", spriteRenderer.maskInteraction));
            componentSnapshot.fields.Add(CreateJsonField("sortingLayerID", spriteRenderer.sortingLayerID));
            componentSnapshot.fields.Add(CreateJsonField("sortingOrder", spriteRenderer.sortingOrder));
            componentSnapshot.fields.Add(CreateJsonField("enabled", spriteRenderer.enabled));
            return componentSnapshot;
        }

        public void Apply(
            Component component,
            string nodePath,
            StorageTerminalHotSyncSnapshot.ComponentSnapshot componentSnapshot,
            ApplyContext context)
        {
            SpriteRenderer spriteRenderer = component as SpriteRenderer;
            if (spriteRenderer == null)
            {
                return;
            }

            for (int i = 0; i < componentSnapshot.fields.Count; i++)
            {
                StorageTerminalHotSyncSnapshot.FieldSnapshot fieldSnapshot = componentSnapshot.fields[i];
                switch (fieldSnapshot.name)
                {
                    case "sprite":
                        if (TryResolveObjectReference(fieldSnapshot.objectReference, typeof(Sprite), context, out UnityEngine.Object spriteReference))
                        {
                            spriteRenderer.sprite = spriteReference as Sprite;
                        }
                        break;
                    case "sharedMaterial":
                        if (TryResolveObjectReference(fieldSnapshot.objectReference, typeof(Material), context, out UnityEngine.Object materialReference))
                        {
                            spriteRenderer.sharedMaterial = materialReference as Material;
                        }
                        break;
                    case "color":
                        spriteRenderer.color = JsonConvert.DeserializeObject<Color>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "size":
                        spriteRenderer.size = JsonConvert.DeserializeObject<Vector2>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "drawMode":
                        spriteRenderer.drawMode = JsonConvert.DeserializeObject<SpriteDrawMode>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "flipX":
                        spriteRenderer.flipX = JsonConvert.DeserializeObject<bool>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "flipY":
                        spriteRenderer.flipY = JsonConvert.DeserializeObject<bool>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "maskInteraction":
                        spriteRenderer.maskInteraction = JsonConvert.DeserializeObject<SpriteMaskInteraction>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "sortingLayerID":
                        spriteRenderer.sortingLayerID = JsonConvert.DeserializeObject<int>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "sortingOrder":
                        spriteRenderer.sortingOrder = JsonConvert.DeserializeObject<int>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "enabled":
                        spriteRenderer.enabled = JsonConvert.DeserializeObject<bool>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                }
            }
        }
    }

    private sealed class BoxColliderSnapshotHandler : IComponentSnapshotHandler
    {
        public StorageTerminalHotSyncSnapshot.ComponentSnapshot Capture(Component component, Transform snapshotRoot)
        {
            BoxCollider boxCollider = component as BoxCollider;
            if (boxCollider == null)
            {
                return null;
            }

            StorageTerminalHotSyncSnapshot.ComponentSnapshot componentSnapshot = new()
            {
                typeName = GetTypeId(typeof(BoxCollider)),
                componentIndex = GetComponentTypeIndex(component.gameObject, component)
            };

            componentSnapshot.fields.Add(CreateJsonField("center", boxCollider.center));
            componentSnapshot.fields.Add(CreateJsonField("size", boxCollider.size));
            componentSnapshot.fields.Add(CreateJsonField("isTrigger", boxCollider.isTrigger));
            componentSnapshot.fields.Add(CreateJsonField("enabled", boxCollider.enabled));
            return componentSnapshot;
        }

        public void Apply(
            Component component,
            string nodePath,
            StorageTerminalHotSyncSnapshot.ComponentSnapshot componentSnapshot,
            ApplyContext context)
        {
            BoxCollider boxCollider = component as BoxCollider;
            if (boxCollider == null)
            {
                return;
            }

            for (int i = 0; i < componentSnapshot.fields.Count; i++)
            {
                StorageTerminalHotSyncSnapshot.FieldSnapshot fieldSnapshot = componentSnapshot.fields[i];
                switch (fieldSnapshot.name)
                {
                    case "center":
                        boxCollider.center = JsonConvert.DeserializeObject<Vector3>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "size":
                        boxCollider.size = JsonConvert.DeserializeObject<Vector3>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "isTrigger":
                        boxCollider.isTrigger = JsonConvert.DeserializeObject<bool>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                    case "enabled":
                        boxCollider.enabled = JsonConvert.DeserializeObject<bool>(fieldSnapshot.jsonValue, JsonSettings);
                        break;
                }
            }
        }
    }
}
#endif
