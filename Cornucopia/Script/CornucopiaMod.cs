using System.Collections.Generic;
using PugMod;
using Unity.Entities;
using UnityEngine;
using Object = UnityEngine.Object;

public class CornucopiaMod : IMod
{
    public const string ModId = "Cornucopia";
    public const string CornucopiaObjectName = "Cornucopia:Cornucopia";

    private static readonly ObjectID[] CraftingStations =
    {
        ObjectID.CopperWorkbench
    };

    private static readonly List<Entity> PendingCraftingStations = new();
    private static EntityManager PendingCraftingStationManager;

    public void EarlyInit()
    {
        API.Authoring.OnObjectTypeAdded += AddCornucopiaRecipe;
    }

    public void Init()
    {
    }

    public void Shutdown()
    {
        API.Authoring.OnObjectTypeAdded -= AddCornucopiaRecipe;
    }

    public void ModObjectLoaded(Object obj)
    {
    }

    public bool CanBeUnloaded()
    {
        return true;
    }

    public void Update()
    {
    }

    private static void AddCornucopiaRecipe(Entity entity, GameObject authoring, EntityManager entityManager)
    {
        if (IsCornucopiaAuthoring(authoring))
        {
            RetryPendingCraftingStations(entityManager);
            return;
        }

        if (!TryGetObjectId(authoring, out ObjectID objectId) || !ShouldAddRecipeTo(objectId))
        {
            return;
        }

        TrackCraftingStation(entity, entityManager);
        TryAddCornucopiaRecipe(entity, objectId, entityManager);
    }

    private static void TrackCraftingStation(Entity entity, EntityManager entityManager)
    {
        SetPendingCraftingStationManager(entityManager);

        if (!PendingCraftingStations.Contains(entity))
        {
            PendingCraftingStations.Add(entity);
        }
    }

    private static void RetryPendingCraftingStations(EntityManager entityManager)
    {
        SetPendingCraftingStationManager(entityManager);

        for (int i = PendingCraftingStations.Count - 1; i >= 0; i--)
        {
            Entity entity = PendingCraftingStations[i];
            if (!entityManager.Exists(entity) || !entityManager.HasComponent<ObjectDataCD>(entity))
            {
                PendingCraftingStations.RemoveAt(i);
                continue;
            }

            ObjectID objectId = entityManager.GetComponentData<ObjectDataCD>(entity).objectID;
            if (TryAddCornucopiaRecipe(entity, objectId, entityManager))
            {
                PendingCraftingStations.RemoveAt(i);
            }
        }
    }

    private static void SetPendingCraftingStationManager(EntityManager entityManager)
    {
        if (PendingCraftingStationManager.Equals(entityManager))
        {
            return;
        }

        PendingCraftingStationManager = entityManager;
        PendingCraftingStations.Clear();
    }

    private static bool TryAddCornucopiaRecipe(Entity entity, ObjectID craftingStationId, EntityManager entityManager)
    {
        ObjectID cornucopiaObjectId = API.Authoring.GetObjectID(CornucopiaObjectName);
        if (cornucopiaObjectId == ObjectID.None || !entityManager.HasBuffer<CanCraftObjectsBuffer>(entity))
        {
            return false;
        }

        DynamicBuffer<CanCraftObjectsBuffer> canCraft = entityManager.GetBuffer<CanCraftObjectsBuffer>(entity);
        int emptyIndex = -1;

        for (int i = 0; i < canCraft.Length; i++)
        {
            if (canCraft[i].objectID == cornucopiaObjectId)
            {
                return true;
            }

            if (emptyIndex == -1 && canCraft[i].objectID == ObjectID.None)
            {
                emptyIndex = i;
            }
        }

        if (emptyIndex == -1)
        {
            Debug.LogWarning($"[{ModId}] Could not add Cornucopia recipe to {craftingStationId}: no empty crafting slot.");
            return false;
        }

        canCraft[emptyIndex] = new CanCraftObjectsBuffer
        {
            objectID = cornucopiaObjectId,
            amount = 1,
            entityAmountToConsume = 0
        };

        return true;
    }

    private static bool ShouldAddRecipeTo(ObjectID objectId)
    {
        for (int i = 0; i < CraftingStations.Length; i++)
        {
            if (CraftingStations[i] == objectId)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetObjectId(GameObject authoring, out ObjectID objectId)
    {
        if (authoring.TryGetComponent(out EntityMonoBehaviourData entityData) && entityData.objectInfo != null)
        {
            objectId = entityData.objectInfo.objectID;
            return objectId != ObjectID.None;
        }

        if (authoring.TryGetComponent(out ObjectAuthoring objectAuthoring))
        {
            objectId = API.Authoring.GetObjectID(objectAuthoring.objectName);
            return objectId != ObjectID.None;
        }

        objectId = ObjectID.None;
        return false;
    }

    private static bool IsCornucopiaAuthoring(GameObject authoring)
    {
        return authoring.TryGetComponent(out ObjectAuthoring objectAuthoring) &&
               objectAuthoring.objectName == CornucopiaObjectName;
    }
}
