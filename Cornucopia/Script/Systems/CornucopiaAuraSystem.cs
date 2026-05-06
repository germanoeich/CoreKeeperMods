using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation, WorldSystemFilterFlags.Default)]
[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
[UpdateAfter(typeof(ConditionEffectsUpdateSystemGroup))]
[UpdateBefore(typeof(HungerAndRunningSystem))]
public partial class CornucopiaAuraSystem : PugSimulationSystemBase
{
    private const ConditionID AuraConditionId = ConditionID.DrainLessHunger;
    private const ConditionEffect AuraEffect = ConditionEffect.DrainLessHunger;

    private EntityQuery _sourceQuery;

    protected override void OnCreate()
    {
        _sourceQuery = GetEntityQuery(
            ComponentType.ReadOnly<CornucopiaAuraSourceCD>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<EntityDestroyedCD>(),
            ComponentType.Exclude<Prefab>());

        RequireForUpdate(_sourceQuery);
        RequireForUpdate<ConditionsTableCD>();

        base.OnCreate();
    }

    protected override void OnUpdate()
    {
        using NativeArray<CornucopiaAuraSourceCD> sources = _sourceQuery.ToComponentDataArray<CornucopiaAuraSourceCD>(Allocator.Temp);
        using NativeArray<LocalTransform> sourceTransforms = _sourceQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        if (sources.Length == 0)
        {
            base.OnUpdate();
            return;
        }

        ConditionsTableCD conditionsTable = SystemAPI.GetSingleton<ConditionsTableCD>();
        if (!SystemAPI.TryGetSingleton(out ClientServerTickRate tickRate))
        {
            tickRate.ResolveDefaults();
        }

        SystemAPI.TryGetSingleton(out NetworkTime networkTime);
        NetworkTick currentTick = networkTime.ServerTick;
        uint simulationTickRate = (uint)tickRate.SimulationTickRate;

        foreach (var (hunger, transform, conditions, summarizedConditions, summarizedEffects) in
                 SystemAPI.Query<RefRW<HungerCD>, RefRO<LocalTransform>, DynamicBuffer<ConditionsBuffer>, DynamicBuffer<SummarizedConditionsBuffer>, DynamicBuffer<SummarizedConditionEffectsBuffer>>()
                     .WithAll<PlayerGhost, Simulate>()
                     .WithNone<EntityDestroyedCD>())
        {
            if (!TryGetStrongestAura(sources, sourceTransforms, transform.ValueRO.Position.xz, out CornucopiaAuraSourceCD source))
            {
                continue;
            }

            ApplyAura(
                hunger,
                conditions,
                summarizedConditions,
                summarizedEffects,
                source,
                conditionsTable,
                currentTick,
                simulationTickRate);
        }

        base.OnUpdate();
    }

    private static bool TryGetStrongestAura(
        NativeArray<CornucopiaAuraSourceCD> sources,
        NativeArray<LocalTransform> sourceTransforms,
        float2 playerPosition,
        out CornucopiaAuraSourceCD strongestSource)
    {
        strongestSource = default;
        bool found = false;

        for (int i = 0; i < sources.Length; i++)
        {
            CornucopiaAuraSourceCD source = sources[i];
            if (source.radius <= 0f || source.drainLessHungerPercent <= 0)
            {
                continue;
            }

            float radiusSq = source.radius * source.radius;
            if (math.distancesq(sourceTransforms[i].Position.xz, playerPosition) > radiusSq)
            {
                continue;
            }

            if (!found || source.drainLessHungerPercent > strongestSource.drainLessHungerPercent)
            {
                strongestSource = source;
                found = true;
            }
        }

        return found;
    }

    private static void ApplyAura(
        RefRW<HungerCD> hunger,
        DynamicBuffer<ConditionsBuffer> conditions,
        DynamicBuffer<SummarizedConditionsBuffer> summarizedConditions,
        DynamicBuffer<SummarizedConditionEffectsBuffer> summarizedEffects,
        CornucopiaAuraSourceCD source,
        ConditionsTableCD conditionsTable,
        NetworkTick currentTick,
        uint tickRate)
    {
        int effectIndex = (int)AuraEffect;
        if (effectIndex < summarizedEffects.Length && summarizedEffects[effectIndex].value < source.drainLessHungerPercent)
        {
            summarizedEffects[effectIndex] = new SummarizedConditionEffectsBuffer
            {
                value = source.drainLessHungerPercent
            };
        }

        hunger.ValueRW.accumulatedMovement = 0f;

        EntityUtility.AddOrRefreshCondition(new ConditionData
        {
            conditionID = AuraConditionId,
            value = source.drainLessHungerPercent,
            duration = source.buffRefreshDuration,
            valueMultiplier = 1f
        }, conditions, conditionsTable, currentTick, tickRate, summarizedConditions);
    }
}
