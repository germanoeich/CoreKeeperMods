using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(DisableEntitiesSystem))]
public partial class StorageConnectorEnableCircleSyncSystem : PugSimulationSystemBase
{
    private EntityQuery _missingEnableCircleQuery;
    private EntityQuery _existingEnableCircleQuery;
    private bool _syncedExistingCirclesOnce;

    protected override void OnCreate()
    {
        _missingEnableCircleQuery = GetEntityQuery(
            ComponentType.ReadOnly<StorageConnectorTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<EnableEntitiesInCircleCD>(),
            ComponentType.Exclude<StorageCraftingRelayTag>(),
            ComponentType.Exclude<Prefab>());
        _existingEnableCircleQuery = GetEntityQuery(
            ComponentType.ReadOnly<StorageConnectorTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadWrite<EnableEntitiesInCircleCD>(),
            ComponentType.Exclude<StorageCraftingRelayTag>(),
            ComponentType.Exclude<Prefab>());
        _existingEnableCircleQuery.AddChangedVersionFilter(ComponentType.ReadOnly<LocalTransform>());
        base.OnCreate();
    }

    protected override void OnUpdate()
    {
        SyncMissingEnableCircles();
        SyncExistingEnableCircles();
        base.OnUpdate();
    }

    private void SyncMissingEnableCircles()
    {
        if (_missingEnableCircleQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        EntityCommandBuffer commandBuffer = new(Allocator.TempJob);
        Dependency = new AddMissingEnableCircleJob
        {
            Radius = StorageNetworkLoadRetentionUtility.EnableRetentionRadius,
            CommandBuffer = commandBuffer.AsParallelWriter()
        }.ScheduleParallel(_missingEnableCircleQuery, Dependency);
        Dependency.Complete();

        commandBuffer.Playback(EntityManager);
        commandBuffer.Dispose();
    }

    private void SyncExistingEnableCircles()
    {
        if (!_syncedExistingCirclesOnce)
        {
            _existingEnableCircleQuery.ResetFilter();
        }

        Dependency = new SyncExistingEnableCircleJob
        {
            Radius = StorageNetworkLoadRetentionUtility.EnableRetentionRadius
        }.ScheduleParallel(_existingEnableCircleQuery, Dependency);
        Dependency.Complete();

        if (!_syncedExistingCirclesOnce)
        {
            _existingEnableCircleQuery.SetChangedVersionFilter(ComponentType.ReadOnly<LocalTransform>());
            _syncedExistingCirclesOnce = true;
        }
    }

    [BurstCompile]
    private partial struct AddMissingEnableCircleJob : IJobEntity
    {
        public float Radius;
        public EntityCommandBuffer.ParallelWriter CommandBuffer;

        private void Execute([ChunkIndexInQuery] int sortKey, Entity entity, in LocalTransform transform)
        {
            CommandBuffer.AddComponent(sortKey, entity, new EnableEntitiesInCircleCD
            {
                Center = transform.Position.xz,
                Radius = Radius
            });
        }
    }

    [BurstCompile]
    private partial struct SyncExistingEnableCircleJob : IJobEntity
    {
        public float Radius;

        private void Execute(ref EnableEntitiesInCircleCD circle, in LocalTransform transform)
        {
            float2 center = transform.Position.xz;
            if (center.Equals(circle.Center) && math.abs(circle.Radius - Radius) <= 0.001f)
            {
                return;
            }

            circle = new EnableEntitiesInCircleCD
            {
                Center = center,
                Radius = Radius
            };
        }
    }
}
