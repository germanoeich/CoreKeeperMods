using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(DisableEntitiesSystem))]
public partial class StorageConnectorEnableCircleSyncSystem : PugSimulationSystemBase
{
    private EntityQuery _connectorQuery;

    protected override void OnCreate()
    {
        _connectorQuery = GetEntityQuery(
            ComponentType.ReadOnly<StorageConnectorTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<StorageCraftingRelayTag>(),
            ComponentType.Exclude<Prefab>());
        base.OnCreate();
    }

    protected override void OnUpdate()
    {
        using NativeArray<Entity> entities = _connectorQuery.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            float2 center = EntityManager.GetComponentData<LocalTransform>(entity).Position.xz;

            if (!EntityManager.HasComponent<EnableEntitiesInCircleCD>(entity))
            {
                EntityManager.AddComponentData(entity, new EnableEntitiesInCircleCD
                {
                    Center = center,
                    Radius = StorageNetworkLoadRetentionUtility.EnableRetentionRadius
                });
                continue;
            }

            EnableEntitiesInCircleCD circle = EntityManager.GetComponentData<EnableEntitiesInCircleCD>(entity);
            if (center.Equals(circle.Center) && math.abs(circle.Radius - StorageNetworkLoadRetentionUtility.EnableRetentionRadius) <= 0.001f)
            {
                continue;
            }

            EntityManager.SetComponentData(entity, new EnableEntitiesInCircleCD
            {
                Center = center,
                Radius = StorageNetworkLoadRetentionUtility.EnableRetentionRadius
            });
        }

        base.OnUpdate();
    }
}
