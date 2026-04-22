using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PugMod;
using PugTilemap;
using PlayerState;
using QFSW.QC;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Scripting;
using EntitiesHash128 = Unity.Entities.Hash128;

public sealed class RevealWholeMapMod : IMod
{
    private const int SubMapWindowWidth = 48;
    private const int SubMapWindowHeight = 48;
    private const int WorldRadiusBoundPadding = 32;
    private const int ProbeStep = 24;
    private const int ProgressLogStep = 16;
    private const int ReadyFramesBeforeReveal = 4;
    private const int RevealFramesPerProbe = 14;
    private const float MinimumProbeDwellSeconds = 0.45f;
    private const float ProbeTimeoutSeconds = 3f;
    private const int MaxAttemptsPerPoint = 3;
    private const float RevealDistanceOverride = 23f;
    private const int TileSampleStride = 4;
    private const int MinTileSamples = 32;
    private const float MinKnownTileRatio = 0.92f;
    private const int SubMapViewpointToleranceSq = 16;
    private const int MaxRepairPassesPerBiome = 2;
    private const int RepairSearchPadding = ProbeStep;
    private const int RepairSampleStep = 1;
    private const int RepairProbeMinSpacing = 12;

    private enum RevealState
    {
        Idle,
        Running,
        Canceling
    }

    private struct ProbeBatchRange
    {
        public int startIndex;
        public int count;
    }

    private readonly List<int2> _probePoints = new List<int2>();
    private readonly List<int> _centerXs = new List<int>();
    private readonly List<int> _centerYs = new List<int>();
    private readonly List<Biome> _queuedBiomeOrder = new List<Biome>();
    private readonly HashSet<long> _completedPoints = new HashSet<long>();
    private readonly Dictionary<long, int> _attemptCounts = new Dictionary<long, int>();
    private readonly Dictionary<long, Biome> _pointToBiome = new Dictionary<long, Biome>();
    private readonly Dictionary<Biome, ProbeBatchRange> _primaryBatchRanges = new Dictionary<Biome, ProbeBatchRange>();
    private readonly Dictionary<Biome, int2> _biomeMinPoints = new Dictionary<Biome, int2>();
    private readonly Dictionary<Biome, int2> _biomeMaxPoints = new Dictionary<Biome, int2>();
    private readonly Dictionary<int, int> _rowMinAllowedX = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _rowMaxAllowedX = new Dictionary<int, int>();

    private RevealState _state;
    private bool _hasActiveProbe;
    private int2 _activeProbePoint;
    private int _activeProbeAttempt;
    private double _activeProbeStartTime;
    private int _activeProbeReadyFrames;
    private int _activeProbeRevealFrames;
    private float _activeProbeKnownRatio;
    private int _activeProbeKnownSamples;
    private Biome _activeProbeBiome;
    private bool _activeServerSubMapReady;
    private bool _activeClientSubMapReady;
    private bool _activeClientViewpointAligned;
    private bool _activeTileCoverageReady;

    private int _processedProbeCount;
    private int _skippedProbeCount;
    private int _lastLoggedProcessed;
    private double _startTime;
    private int _currentBiomeOrderIndex;
    private int _currentBiomeRepairPassCount;
    private int _activeBatchStartIndex;
    private int _activeBatchCount;
    private int _nextBatchProbeOffset;
    private bool _activeBatchIsRepair;
    private Biome _activeBatchBiome;

    private int _minWorldX;
    private int _minWorldY;
    private int _maxWorldX;
    private int _maxWorldY;
    private bool _hasBiomeFilter;
    private Biome _selectedBiomeFilter;
    private EntitiesHash128 _localPlayerGuid;
    private Entity _cachedServerPlayerEntity = Entity.Null;
    private bool _loggedMissingServerPlayerEntity;

    private bool _capturedPlayerState;
    private float3 _initialPlayerPosition;
    private bool _initialGodModeEnabled;
    private bool _initialNoClipEnabled;

    private bool _capturedMapRevealState;
    private bool _previousLargeRevealDistance;

    private Harmony _harmony;
    private bool _harmonyPatched;

    private static FieldInfo _mapUpdateLargeRevealDistanceField;
    private static FieldInfo _mapUIPartsField;
    private static FieldInfo _mapPartTimestampTextureField;

    public static RevealWholeMapMod Instance { get; private set; }

    internal static bool IsRevealRunning()
    {
        return Instance != null && Instance._state == RevealState.Running;
    }

    internal static bool TryGetRevealDistanceOverride(out float revealDistance)
    {
        if (IsRevealRunning())
        {
            revealDistance = RevealDistanceOverride;
            return true;
        }

        revealDistance = default;
        return false;
    }

    public void EarlyInit()
    {
    }

    public void Init()
    {
        Instance = this;
        EnsureHarmonyPatches();
    }

    public void Shutdown()
    {
        ForceCleanup();
        RemoveHarmonyPatches();
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
    }

    public void ModObjectLoaded(UnityEngine.Object obj)
    {
    }

    public void Update()
    {
        if (_state == RevealState.Idle)
        {
            return;
        }

        if (_state == RevealState.Canceling)
        {
            ForceCleanup();
            Debug.Log("[RevealWholeMap] Reveal canceled.");
            ResetState();
            return;
        }

        if (!CanContinue())
        {
            RequestCancelInternal("Reveal canceled because the game context changed.");
            return;
        }

        if (_probePoints.Count == 0)
        {
            CompleteReveal();
            return;
        }

        while (!_hasActiveProbe)
        {
            if (TryAcquireNextProbe())
            {
                break;
            }

            if (!TryAdvanceBatch())
            {
                CompleteReveal();
                return;
            }
        }

        ProcessActiveProbe();
    }

    public bool TryStartReveal(out string message)
    {
        return TryStartRevealInternal(null, out message);
    }

    public bool TryStartRevealForBiome(Biome biome, out string message)
    {
        return TryStartRevealInternal(biome, out message);
    }

    private bool TryStartRevealInternal(Biome? biomeFilter, out string message)
    {
        if (_state == RevealState.Running)
        {
            message = "Reveal is already running.";
            return false;
        }

        if (_state == RevealState.Canceling)
        {
            message = "Reveal cancel is still in progress.";
            return false;
        }

        if (!CanStart(out message))
        {
            return false;
        }

        ForceCleanup();
        ResetState();
        _hasBiomeFilter = biomeFilter.HasValue;
        _selectedBiomeFilter = biomeFilter ?? Biome.None;
        if (!BuildProbePointWorkList(biomeFilter, out message))
        {
            return false;
        }

        if (_probePoints.Count == 0)
        {
            message = biomeFilter.HasValue
                ? $"No probe points were queued for biome {biomeFilter.Value}."
                : "No probe points were queued.";
            return false;
        }

        if (!TryCapturePlayerStateAndEnableCheats(out message))
        {
            ForceCleanup();
            ResetState();
            return false;
        }

        if (!TryCaptureAndEnableMapRevealOverride(out message))
        {
            ForceCleanup();
            ResetState();
            return false;
        }

        if (!ActivatePrimaryBiomeBatch(0))
        {
            message = "Failed to activate the first biome batch.";
            ForceCleanup();
            ResetState();
            return false;
        }

        _state = RevealState.Running;
        _startTime = Time.realtimeSinceStartupAsDouble;
        if (biomeFilter.HasValue)
        {
            message = $"Reveal started for biome {biomeFilter.Value}. Queued {_probePoints.Count} probes.";
        }
        else
        {
            message = $"Reveal started. Queued {_probePoints.Count} probes across {_queuedBiomeOrder.Count} biome(s).";
        }

        Debug.Log("[RevealWholeMap] " + message);
        return true;
    }

    public bool TryCancelReveal(out string message)
    {
        if (_state == RevealState.Idle)
        {
            message = "No reveal job is running.";
            return false;
        }

        if (_state == RevealState.Canceling)
        {
            message = "Reveal cancel is already in progress.";
            return false;
        }

        RequestCancelInternal("Reveal cancel requested.");
        message = "Reveal cancel requested.";
        return true;
    }

    public string GetStatus()
    {
        if (_state == RevealState.Idle)
        {
            return "Reveal status: idle.";
        }

        string activeProbeInfo = _hasActiveProbe
            ? $" Active probe ({_activeProbePoint.x}, {_activeProbePoint.y}) biome {_activeProbeBiome} attempt {_activeProbeAttempt}/{MaxAttemptsPerPoint}, ready(server={_activeServerSubMapReady}, client={_activeClientSubMapReady}, view={_activeClientViewpointAligned}, tiles={_activeTileCoverageReady}), known ratio {_activeProbeKnownRatio:P0} ({_activeProbeKnownSamples} samples)."
            : string.Empty;
        string batchInfo = _activeBatchCount > 0
            ? $" Active batch {(_activeBatchIsRepair ? "repair" : "primary")} biome {_activeBatchBiome} with {_activeBatchCount} probes."
            : string.Empty;
        string filterInfo = _hasBiomeFilter ? $" Filter biome {_selectedBiomeFilter}." : string.Empty;
        return $"Reveal status: {_state.ToString().ToLowerInvariant()}. Processed {_processedProbeCount}/{_probePoints.Count} probes (skipped {_skippedProbeCount}).{filterInfo}{batchInfo}{activeProbeInfo}";
    }

    private bool CanStart(out string message)
    {
        if (Manager.main == null || Manager.main.currentSceneHandler == null || !Manager.main.currentSceneHandler.isInGame)
        {
            message = "Start reveal while in-game.";
            return false;
        }

        if (Manager.main.player == null)
        {
            message = "Local player is not available.";
            return false;
        }

        if (Manager.ui == null || Manager.ui.mapUI == null)
        {
            message = "Map UI is not available.";
            return false;
        }

        if (Manager.ecs == null || Manager.ecs.ServerWorld == null || Manager.ecs.ClientWorld == null)
        {
            message = "Client/server ECS worlds are not initialized.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private bool CanContinue()
    {
        return Manager.main != null
            && Manager.main.currentSceneHandler != null
            && Manager.main.currentSceneHandler.isInGame
            && Manager.main.player != null
            && Manager.ui != null
            && Manager.ui.mapUI != null
            && Manager.ecs != null
            && Manager.ecs.ServerWorld != null
            && Manager.ecs.ClientWorld != null;
    }

    private bool BuildProbePointWorkList(Biome? biomeFilter, out string message)
    {
        _probePoints.Clear();
        _queuedBiomeOrder.Clear();
        _pointToBiome.Clear();
        _primaryBatchRanges.Clear();
        _biomeMinPoints.Clear();
        _biomeMaxPoints.Clear();
        _rowMinAllowedX.Clear();
        _rowMaxAllowedX.Clear();

        int worldRadius = math.max(0, Manager.worldGen.GetScaledWorldRadiusBound() - WorldRadiusBoundPadding);
        int probeRadius = math.max(0, worldRadius - (SubMapWindowWidth / 2) - ProbeStep);
        _minWorldX = -worldRadius;
        _minWorldY = -worldRadius;
        _maxWorldX = worldRadius;
        _maxWorldY = worldRadius;

        FillAxisCenters(_centerXs, -probeRadius, probeRadius, ProbeStep);
        FillAxisCenters(_centerYs, -probeRadius, probeRadius, ProbeStep);

        List<int2> candidatePoints = new List<int2>(_centerXs.Count * _centerYs.Count);
        long maxRadiusSq = (long)probeRadius * probeRadius;
        for (int y = 0; y < _centerYs.Count; y++)
        {
            bool reverse = (y & 1) != 0;
            if (!reverse)
            {
                for (int x = 0; x < _centerXs.Count; x++)
                {
                    TryAddCandidatePoint(candidatePoints, _centerXs[x], _centerYs[y], maxRadiusSq);
                }
            }
            else
            {
                for (int x = _centerXs.Count - 1; x >= 0; x--)
                {
                    TryAddCandidatePoint(candidatePoints, _centerXs[x], _centerYs[y], maxRadiusSq);
                }
            }
        }

        if (candidatePoints.Count == 0)
        {
            message = "No probe candidates were found.";
            return false;
        }

        if (!TryGetServerBiomeSamples(out BiomeSamplesCD biomeSamples))
        {
            message = "Biome samples are unavailable. Reveal can only start after biome sample initialization completes.";
            return false;
        }

        Dictionary<Biome, List<int2>> biomeToPoints = new Dictionary<Biome, List<int2>>();
        Dictionary<Biome, long> biomeMinDistance = new Dictionary<Biome, long>();
        for (int i = 0; i < candidatePoints.Count; i++)
        {
            int2 point = candidatePoints[i];
            Biome biome = biomeSamples.GetBiome(point);
            if (biome == Biome.None)
            {
                continue;
            }

            if (biomeFilter.HasValue && biome != biomeFilter.Value)
            {
                continue;
            }

            if (!biomeToPoints.TryGetValue(biome, out List<int2> pointsInBiome))
            {
                pointsInBiome = new List<int2>();
                biomeToPoints.Add(biome, pointsInBiome);
                biomeMinDistance.Add(biome, long.MaxValue);
            }

            pointsInBiome.Add(point);
            long distance = SqDistance(point, int2.zero);
            if (distance < biomeMinDistance[biome])
            {
                biomeMinDistance[biome] = distance;
            }

            if (_biomeMinPoints.TryGetValue(biome, out int2 currentMin))
            {
                _biomeMinPoints[biome] = new int2(math.min(currentMin.x, point.x), math.min(currentMin.y, point.y));
                int2 currentMax = _biomeMaxPoints[biome];
                _biomeMaxPoints[biome] = new int2(math.max(currentMax.x, point.x), math.max(currentMax.y, point.y));
            }
            else
            {
                _biomeMinPoints[biome] = point;
                _biomeMaxPoints[biome] = point;
            }
        }

        if (biomeToPoints.Count == 0)
        {
            message = biomeFilter.HasValue
                ? $"No probe candidates were found for biome {biomeFilter.Value}."
                : "No biome probe candidates were found.";
            return false;
        }

        _queuedBiomeOrder.AddRange(biomeToPoints.Keys);
        _queuedBiomeOrder.Sort((a, b) =>
        {
            int distanceCompare = biomeMinDistance[a].CompareTo(biomeMinDistance[b]);
            if (distanceCompare != 0)
            {
                return distanceCompare;
            }

            return a.CompareTo(b);
        });

        int2 nextBiomeStart = int2.zero;
        for (int i = 0; i < _queuedBiomeOrder.Count; i++)
        {
            Biome biome = _queuedBiomeOrder[i];
            List<int2> pointsInBiome = biomeToPoints[biome];
            OrderProbePointsByNearestNeighbor(pointsInBiome, nextBiomeStart);
            int batchStartIndex = _probePoints.Count;
            for (int j = 0; j < pointsInBiome.Count; j++)
            {
                int2 point = pointsInBiome[j];
                _probePoints.Add(point);
                _pointToBiome[ToKey(point)] = biome;
            }

            _primaryBatchRanges[biome] = new ProbeBatchRange
            {
                startIndex = batchStartIndex,
                count = pointsInBiome.Count
            };

            if (pointsInBiome.Count > 0)
            {
                nextBiomeStart = pointsInBiome[pointsInBiome.Count - 1];
            }
        }

        if (biomeFilter.HasValue)
        {
            message = $"Queued {_probePoints.Count} probes for biome {biomeFilter.Value}.";
        }
        else
        {
            message = $"Queued {_probePoints.Count} probes across {_queuedBiomeOrder.Count} biome(s), ordered from inner to outer.";
        }

        return true;
    }

    private static int CompareProbePointsByCoreDistance(int2 a, int2 b)
    {
        long aDistance = SqDistance(a, int2.zero);
        long bDistance = SqDistance(b, int2.zero);
        if (aDistance != bDistance)
        {
            return aDistance.CompareTo(bDistance);
        }

        int yCompare = a.y.CompareTo(b.y);
        if (yCompare != 0)
        {
            return yCompare;
        }

        return a.x.CompareTo(b.x);
    }

    private static void OrderProbePointsByNearestNeighbor(List<int2> points, int2 startPoint)
    {
        if (points.Count <= 2)
        {
            return;
        }

        List<int2> orderedPoints = new List<int2>(points.Count);
        bool[] used = new bool[points.Count];
        int nextIndex = FindNearestPointIndex(points, used, startPoint);
        if (nextIndex < 0)
        {
            return;
        }

        int2 currentPoint = points[nextIndex];
        orderedPoints.Add(currentPoint);
        used[nextIndex] = true;

        while (orderedPoints.Count < points.Count)
        {
            nextIndex = FindNearestPointIndex(points, used, currentPoint);
            if (nextIndex < 0)
            {
                break;
            }

            currentPoint = points[nextIndex];
            orderedPoints.Add(currentPoint);
            used[nextIndex] = true;
        }

        if (orderedPoints.Count == points.Count)
        {
            points.Clear();
            points.AddRange(orderedPoints);
        }
    }

    private static int FindNearestPointIndex(List<int2> points, bool[] used, int2 referencePoint)
    {
        int nearestIndex = -1;
        long nearestDistance = long.MaxValue;
        for (int i = 0; i < points.Count; i++)
        {
            if (used[i])
            {
                continue;
            }

            long distance = SqDistance(points[i], referencePoint);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
                continue;
            }

            if (distance == nearestDistance
                && nearestIndex >= 0
                && CompareProbePointsByCoreDistance(points[i], points[nearestIndex]) < 0)
            {
                nearestIndex = i;
            }
        }

        return nearestIndex;
    }

    private bool ActivatePrimaryBiomeBatch(int biomeOrderIndex)
    {
        if (biomeOrderIndex < 0 || biomeOrderIndex >= _queuedBiomeOrder.Count)
        {
            return false;
        }

        Biome biome = _queuedBiomeOrder[biomeOrderIndex];
        if (!_primaryBatchRanges.TryGetValue(biome, out ProbeBatchRange range) || range.count <= 0)
        {
            return false;
        }

        _currentBiomeOrderIndex = biomeOrderIndex;
        _currentBiomeRepairPassCount = 0;
        ActivateBatch(biome, range.startIndex, range.count, isRepair: false);
        Debug.Log($"[RevealWholeMap] Starting primary batch for biome {biome} with {range.count} probes.");
        return true;
    }

    private void ActivateRepairBatch(Biome biome, int repairProbeCount)
    {
        int repairStartIndex = _probePoints.Count - repairProbeCount;
        ActivateBatch(biome, repairStartIndex, repairProbeCount, isRepair: true);
        Debug.Log($"[RevealWholeMap] Starting repair pass {_currentBiomeRepairPassCount}/{MaxRepairPassesPerBiome} for biome {biome} with {repairProbeCount} probes.");
    }

    private void ActivateBatch(Biome biome, int startIndex, int count, bool isRepair)
    {
        _activeBatchBiome = biome;
        _activeBatchStartIndex = startIndex;
        _activeBatchCount = count;
        _nextBatchProbeOffset = 0;
        _activeBatchIsRepair = isRepair;
    }

    private bool TryAdvanceBatch()
    {
        while (_currentBiomeOrderIndex >= 0 && _currentBiomeOrderIndex < _queuedBiomeOrder.Count)
        {
            Biome biome = _queuedBiomeOrder[_currentBiomeOrderIndex];
            if (_currentBiomeRepairPassCount < MaxRepairPassesPerBiome
                && TryQueueRepairBatchForBiome(biome, out int repairProbeCount))
            {
                _currentBiomeRepairPassCount++;
                ActivateRepairBatch(biome, repairProbeCount);
                return true;
            }

            int nextBiomeOrderIndex = _currentBiomeOrderIndex + 1;
            if (nextBiomeOrderIndex >= _queuedBiomeOrder.Count)
            {
                _activeBatchCount = 0;
                return false;
            }

            if (ActivatePrimaryBiomeBatch(nextBiomeOrderIndex))
            {
                return true;
            }

            _currentBiomeOrderIndex = nextBiomeOrderIndex;
        }

        return false;
    }

    private bool TryQueueRepairBatchForBiome(Biome biome, out int repairProbeCount)
    {
        repairProbeCount = 0;
        if (!TryBuildRepairProbeList(biome, out List<int2> repairPoints) || repairPoints.Count == 0)
        {
            return false;
        }

        int repairStartIndex = _probePoints.Count;
        for (int i = 0; i < repairPoints.Count; i++)
        {
            int2 point = repairPoints[i];
            long key = ToKey(point);
            if (_pointToBiome.ContainsKey(key))
            {
                continue;
            }

            _probePoints.Add(point);
            _pointToBiome[key] = biome;
            _completedPoints.Remove(key);
            _attemptCounts.Remove(key);
        }

        repairProbeCount = _probePoints.Count - repairStartIndex;
        return repairProbeCount > 0;
    }

    private bool TryBuildRepairProbeList(Biome biome, out List<int2> repairPoints)
    {
        repairPoints = new List<int2>();
        if (!_biomeMinPoints.TryGetValue(biome, out int2 biomeMin) || !_biomeMaxPoints.TryGetValue(biome, out int2 biomeMax))
        {
            return false;
        }

        if (!TryGetServerBiomeSamples(out BiomeSamplesCD biomeSamples) || !TryGetMapPartsForRead(out IDictionary mapParts))
        {
            return false;
        }

        int minX = math.max(_minWorldX, biomeMin.x - RepairSearchPadding);
        int minY = math.max(_minWorldY, biomeMin.y - RepairSearchPadding);
        int maxX = math.min(_maxWorldX, biomeMax.x + RepairSearchPadding);
        int maxY = math.min(_maxWorldY, biomeMax.y + RepairSearchPadding);
        if (minX > maxX || minY > maxY)
        {
            return false;
        }

        Dictionary<Vector2Int, NativeArray<Pug.UnityExtensions.PugColorARGB32>> timestampCache =
            new Dictionary<Vector2Int, NativeArray<Pug.UnityExtensions.PugColorARGB32>>();
        for (int y = minY; y <= maxY; y += RepairSampleStep)
        {
            for (int x = minX; x <= maxX; x += RepairSampleStep)
            {
                int2 samplePoint = new int2(x, y);
                if (biomeSamples.GetBiome(samplePoint) != biome)
                {
                    continue;
                }

                if (IsTimestampRevealedAt(samplePoint, mapParts, timestampCache))
                {
                    continue;
                }

                if (!TryResolveRepairProbeCandidate(samplePoint, biome, biomeSamples, mapParts, timestampCache, out int2 repairPoint))
                {
                    continue;
                }

                TryAddRepairProbeCandidate(repairPoints, repairPoint);
            }
        }

        if (repairPoints.Count == 0)
        {
            return true;
        }

        if (!TryGetLocalPlayerWorldPoint(out int2 startPoint))
        {
            startPoint = int2.zero;
        }

        OrderProbePointsByNearestNeighbor(repairPoints, startPoint);
        return true;
    }

    private bool TryResolveRepairProbeCandidate(
        int2 samplePoint,
        Biome biome,
        BiomeSamplesCD biomeSamples,
        IDictionary mapParts,
        Dictionary<Vector2Int, NativeArray<Pug.UnityExtensions.PugColorARGB32>> timestampCache,
        out int2 repairPoint)
    {
        long sampleKey = ToKey(samplePoint);
        if (!_pointToBiome.ContainsKey(sampleKey))
        {
            repairPoint = samplePoint;
            return true;
        }

        int searchRadius = math.max(1, RepairProbeMinSpacing / 2);
        for (int radius = 1; radius <= searchRadius; radius++)
        {
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    int2 candidate = samplePoint + new int2(offsetX, offsetY);
                    if (candidate.x < _minWorldX || candidate.x > _maxWorldX || candidate.y < _minWorldY || candidate.y > _maxWorldY)
                    {
                        continue;
                    }

                    long candidateKey = ToKey(candidate);
                    if (_pointToBiome.ContainsKey(candidateKey) || biomeSamples.GetBiome(candidate) != biome)
                    {
                        continue;
                    }

                    if (IsTimestampRevealedAt(candidate, mapParts, timestampCache))
                    {
                        continue;
                    }

                    repairPoint = candidate;
                    return true;
                }
            }
        }

        repairPoint = default;
        return false;
    }

    private static void TryAddRepairProbeCandidate(List<int2> repairPoints, int2 candidate)
    {
        long minSpacingSq = (long)RepairProbeMinSpacing * RepairProbeMinSpacing;
        for (int i = 0; i < repairPoints.Count; i++)
        {
            if (SqDistance(repairPoints[i], candidate) <= minSpacingSq)
            {
                return;
            }
        }

        repairPoints.Add(candidate);
    }

    private static void TryAddCandidatePoint(List<int2> candidates, int x, int y, long maxRadiusSq)
    {
        int2 point = new int2(x, y);
        if (SqDistance(point, int2.zero) <= maxRadiusSq)
        {
            candidates.Add(point);
        }
    }

    private bool TryGetServerBiomeSamples(out BiomeSamplesCD biomeSamples)
    {
        biomeSamples = default;
        World serverWorld = Manager.ecs?.ServerWorld;
        if (serverWorld == null || !serverWorld.IsCreated)
        {
            return false;
        }

        EntityManager entityManager = serverWorld.EntityManager;
        using EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<BiomeSamplesCD>());
        if (query.IsEmptyIgnoreFilter)
        {
            return false;
        }

        biomeSamples = query.GetSingleton<BiomeSamplesCD>();
        return biomeSamples.Samples.IsCreated
            && biomeSamples.BasePosition.IsCreated
            && biomeSamples.SamplesPerDimension > 0
            && biomeSamples.Samples.Length > 0;
    }

    private static void FillAxisCenters(List<int> centers, int min, int max, int step)
    {
        centers.Clear();

        int halfLow = step / 2;
        int halfHigh = step - halfLow - 1;
        int firstCenter = min + halfLow;
        int lastCenter = max - halfHigh;
        if (firstCenter > lastCenter)
        {
            centers.Add((min + max) / 2);
            return;
        }

        for (int center = firstCenter; center <= lastCenter; center += step)
        {
            centers.Add(center);
        }

        if (centers[centers.Count - 1] != lastCenter)
        {
            centers.Add(lastCenter);
        }
    }

    private bool IsPointAllowedByRowBounds(int2 point)
    {
        if (_rowMinAllowedX.TryGetValue(point.y, out int minX) && point.x < minX)
        {
            return false;
        }

        if (_rowMaxAllowedX.TryGetValue(point.y, out int maxX) && point.x > maxX)
        {
            return false;
        }

        return true;
    }

    private void TightenRowBoundaryFromMissingServerSubMap(int2 point)
    {
        int y = point.y;
        if (point.x <= 0)
        {
            int newMin = point.x + ProbeStep;
            if (_rowMinAllowedX.TryGetValue(y, out int currentMin))
            {
                _rowMinAllowedX[y] = math.max(currentMin, newMin);
            }
            else
            {
                _rowMinAllowedX[y] = newMin;
            }
        }

        if (point.x >= 0)
        {
            int newMax = point.x - ProbeStep;
            if (_rowMaxAllowedX.TryGetValue(y, out int currentMax))
            {
                _rowMaxAllowedX[y] = math.min(currentMax, newMax);
            }
            else
            {
                _rowMaxAllowedX[y] = newMax;
            }
        }
    }

    private string GetRowBoundsDebug(int y)
    {
        string min = _rowMinAllowedX.TryGetValue(y, out int minX) ? minX.ToString() : "-inf";
        string max = _rowMaxAllowedX.TryGetValue(y, out int maxX) ? maxX.ToString() : "+inf";
        return $"[{min}, {max}]";
    }

    private bool TryAcquireNextProbe()
    {
        if (_activeBatchCount <= 0)
        {
            return false;
        }

        int checkedPoints = 0;
        while (checkedPoints < _activeBatchCount)
        {
            int probeIndex = _activeBatchStartIndex + _nextBatchProbeOffset;
            int2 point = _probePoints[probeIndex];
            _nextBatchProbeOffset = (_nextBatchProbeOffset + 1) % _activeBatchCount;
            checkedPoints++;

            if (_completedPoints.Contains(ToKey(point)))
            {
                continue;
            }

            if (!IsPointAllowedByRowBounds(point))
            {
                _completedPoints.Add(ToKey(point));
                _processedProbeCount++;
                _skippedProbeCount++;
                LogProgressIfNeeded();
                continue;
            }

            BeginProbe(point);
            return true;
        }

        return false;
    }

    private void BeginProbe(int2 point)
    {
        _activeProbePoint = point;
        _activeProbeBiome = _pointToBiome.TryGetValue(ToKey(point), out Biome biome) ? biome : Biome.None;
        _activeProbeReadyFrames = 0;
        _activeProbeRevealFrames = 0;
        _activeProbeKnownRatio = 0f;
        _activeProbeKnownSamples = 0;
        _activeServerSubMapReady = false;
        _activeClientSubMapReady = false;
        _activeClientViewpointAligned = false;
        _activeTileCoverageReady = false;
        _activeProbeStartTime = Time.realtimeSinceStartupAsDouble;
        _hasActiveProbe = true;

        long key = ToKey(point);
        _attemptCounts.TryGetValue(key, out int existingAttempts);
        _activeProbeAttempt = existingAttempts + 1;
        _attemptCounts[key] = _activeProbeAttempt;

        TeleportPlayerTo(point);
    }

    private void ProcessActiveProbe()
    {
        World serverWorld = Manager.ecs.ServerWorld;
        World clientWorld = Manager.ecs.ClientWorld;
        _activeServerSubMapReady = IsSubMapWindowReady(serverWorld, _activeProbePoint);
        _activeClientSubMapReady = IsSubMapWindowReady(clientWorld, _activeProbePoint);
        _activeClientViewpointAligned = IsClientSubMapReady(clientWorld, _activeProbePoint);
        _activeTileCoverageReady = false;

        bool subMapWindowReady = _activeServerSubMapReady && _activeClientSubMapReady;
        if (subMapWindowReady)
        {
            _activeTileCoverageReady = IsClientTileCoverageReady(_activeProbePoint, out _activeProbeKnownRatio, out _activeProbeKnownSamples);
        }
        else
        {
            _activeProbeKnownRatio = 0f;
            _activeProbeKnownSamples = 0;
        }

        bool probeReadyForCompletion = subMapWindowReady && _activeClientViewpointAligned && _activeTileCoverageReady;
        if (probeReadyForCompletion)
        {
            _activeProbeReadyFrames++;
        }
        else
        {
            _activeProbeReadyFrames = 0;
            _activeProbeRevealFrames = 0;
        }

        if (_activeProbeReadyFrames >= ReadyFramesBeforeReveal)
        {
            _activeProbeRevealFrames++;
            if (_activeProbeRevealFrames >= RevealFramesPerProbe
                && Time.realtimeSinceStartupAsDouble - _activeProbeStartTime >= MinimumProbeDwellSeconds)
            {
                CompleteActiveProbe(skipped: false);
                return;
            }
        }

        if (Time.realtimeSinceStartupAsDouble - _activeProbeStartTime >= ProbeTimeoutSeconds)
        {
            if (!_activeServerSubMapReady && _activeClientSubMapReady && _activeProbeAttempt >= 2)
            {
                TightenRowBoundaryFromMissingServerSubMap(_activeProbePoint);
                if (!IsPointAllowedByRowBounds(_activeProbePoint))
                {
                    Debug.LogWarning($"[RevealWholeMap] Row-bound prune at ({_activeProbePoint.x}, {_activeProbePoint.y}) biome {_activeProbeBiome}. New bounds for y={_activeProbePoint.y}: {GetRowBoundsDebug(_activeProbePoint.y)}.");
                    CompleteActiveProbe(skipped: true);
                    return;
                }
            }

            if (!_activeServerSubMapReady && !_activeClientSubMapReady)
            {
                Debug.LogWarning($"[RevealWholeMap] Skipping probe ({_activeProbePoint.x}, {_activeProbePoint.y}) biome {_activeProbeBiome} because no submap window exists in server/client.");
                CompleteActiveProbe(skipped: true);
                return;
            }

            if (_activeProbeAttempt >= MaxAttemptsPerPoint)
            {
                Debug.LogWarning($"[RevealWholeMap] Skipping probe ({_activeProbePoint.x}, {_activeProbePoint.y}) biome {_activeProbeBiome} after {_activeProbeAttempt} attempts. ready(server={_activeServerSubMapReady}, client={_activeClientSubMapReady}, view={_activeClientViewpointAligned}, tiles={_activeTileCoverageReady}, ratio={_activeProbeKnownRatio:P0}, samples={_activeProbeKnownSamples}).");
                CompleteActiveProbe(skipped: true);
                return;
            }

            Debug.LogWarning($"[RevealWholeMap] Retrying probe ({_activeProbePoint.x}, {_activeProbePoint.y}) biome {_activeProbeBiome} attempt {_activeProbeAttempt + 1}/{MaxAttemptsPerPoint}. ready(server={_activeServerSubMapReady}, client={_activeClientSubMapReady}, view={_activeClientViewpointAligned}, tiles={_activeTileCoverageReady}, ratio={_activeProbeKnownRatio:P0}, samples={_activeProbeKnownSamples}).");
            BeginProbe(_activeProbePoint);
        }
    }

    private void CompleteActiveProbe(bool skipped)
    {
        _completedPoints.Add(ToKey(_activeProbePoint));
        _processedProbeCount++;
        if (skipped)
        {
            _skippedProbeCount++;
        }

        _hasActiveProbe = false;
        LogProgressIfNeeded();
    }

    private void CompleteReveal()
    {
        if (Manager.ui?.mapUI != null)
        {
            Manager.ui.mapUI.SaveAllMaps();
        }

        double elapsedSeconds = Time.realtimeSinceStartupAsDouble - _startTime;
        int processed = _processedProbeCount;
        int total = _probePoints.Count;
        int skipped = _skippedProbeCount;
        string mode = _hasBiomeFilter ? $"biome {_selectedBiomeFilter}" : $"{_queuedBiomeOrder.Count} biome(s)";
        ForceCleanup();
        Debug.Log($"[RevealWholeMap] Reveal complete for {mode}. Processed {processed}/{total} probes (skipped {skipped}) in {elapsedSeconds:0.0}s.");
        ResetState();
    }

    private void TeleportPlayerTo(int2 point)
    {
        PlayerController player = Manager.main?.player;
        if (player == null)
        {
            return;
        }

        EnsurePlayerCheats(player);
        float3 worldPos = new float3(point.x, player.WorldPosition.y, point.y);
        bool serverTeleported = TrySetServerPlayerPosition(worldPos);
        bool clientTeleported = TrySetEntityPosition(player.world, player.entity, worldPos);
        if (!serverTeleported)
        {
            player.DebugSetPlayerPosition(worldPos);
        }

        if (!clientTeleported)
        {
            player.SetPlayerPosition(worldPos);
        }
    }

    private static bool TryGetLocalPlayerWorldPoint(out int2 playerPoint)
    {
        PlayerController player = Manager.main?.player;
        if (player == null)
        {
            playerPoint = default;
            return false;
        }

        Vector3 worldPosition = player.WorldPosition;
        playerPoint = new int2((int)math.round(worldPosition.x), (int)math.round(worldPosition.z));
        return true;
    }

    private bool TryCapturePlayerStateAndEnableCheats(out string message)
    {
        PlayerController player = Manager.main?.player;
        if (player == null)
        {
            message = "Local player is not available.";
            return false;
        }

        Vector3 playerPosition = player.WorldPosition;
        _initialPlayerPosition = new float3(playerPosition.x, playerPosition.y, playerPosition.z);
        _initialGodModeEnabled = player.GetLastLocalGodModeState();
        _initialNoClipEnabled = TryGetNoClipState(player, out bool noClipEnabled) && noClipEnabled;
        _localPlayerGuid = default;
        _cachedServerPlayerEntity = Entity.Null;
        _loggedMissingServerPlayerEntity = false;
        if (EntityUtility.HasComponentData<PlayerGuidCD>(player.entity, player.world))
        {
            _localPlayerGuid = EntityUtility.GetComponentData<PlayerGuidCD>(player.entity, player.world).Value;
        }

        _capturedPlayerState = true;

        EnsurePlayerCheats(player);
        message = string.Empty;
        return true;
    }

    private void EnsurePlayerCheats(PlayerController player)
    {
        if (player == null)
        {
            return;
        }

        player.SetGodMode(true);
        SetNoClipEnabled(player, enabled: true);
    }

    private void RestorePlayerState()
    {
        if (!_capturedPlayerState)
        {
            return;
        }

        _capturedPlayerState = false;
        PlayerController player = Manager.main?.player;
        if (player == null)
        {
            return;
        }

        player.DebugSetPlayerPosition(_initialPlayerPosition);
        player.SetPlayerPosition(_initialPlayerPosition);
        player.SetGodMode(_initialGodModeEnabled);
        SetNoClipEnabled(player, _initialNoClipEnabled);
    }

    private static void SetNoClipEnabled(PlayerController player, bool enabled)
    {
        if (player == null || !TryGetNoClipState(player, out bool currentNoClipEnabled))
        {
            return;
        }

        if (currentNoClipEnabled == enabled)
        {
            return;
        }

        player.SetNoClipActive(currentNoClipEnabled);
    }

    private static bool TryGetNoClipState(PlayerController player, out bool noClipEnabled)
    {
        noClipEnabled = false;
        if (player == null || player.entity == Entity.Null || player.world == null || !player.world.IsCreated)
        {
            return false;
        }

        if (!EntityUtility.HasComponentData<PlayerStateCD>(player.entity, player.world))
        {
            return false;
        }

        PlayerStateCD playerState = EntityUtility.GetComponentData<PlayerStateCD>(player.entity, player.world);
        noClipEnabled = playerState.HasAnyState(PlayerStateEnum.NoClip);
        return true;
    }

    private bool TrySetServerPlayerPosition(float3 worldPos)
    {
        World serverWorld = Manager.ecs?.ServerWorld;
        if (serverWorld == null || !serverWorld.IsCreated)
        {
            return false;
        }

        EntityManager serverEntityManager = serverWorld.EntityManager;
        if (_cachedServerPlayerEntity != Entity.Null
            && serverEntityManager.Exists(_cachedServerPlayerEntity)
            && serverEntityManager.HasComponent<PlayerGuidCD>(_cachedServerPlayerEntity))
        {
            PlayerGuidCD cachedGuid = serverEntityManager.GetComponentData<PlayerGuidCD>(_cachedServerPlayerEntity);
            if (cachedGuid.Value.Equals(_localPlayerGuid))
            {
                return TrySetEntityPosition(serverWorld, _cachedServerPlayerEntity, worldPos);
            }
        }

        _cachedServerPlayerEntity = Entity.Null;
        if (_localPlayerGuid == default)
        {
            if (!_loggedMissingServerPlayerEntity)
            {
                _loggedMissingServerPlayerEntity = true;
                Debug.LogWarning("[RevealWholeMap] Local player guid was not found. Falling back to client-only teleport.");
            }

            return false;
        }

        using EntityQuery query = serverEntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerGuidCD>(), ComponentType.ReadOnly<LocalTransform>());
        using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
        using NativeArray<PlayerGuidCD> guids = query.ToComponentDataArray<PlayerGuidCD>(Allocator.Temp);
        for (int i = 0; i < entities.Length; i++)
        {
            if (guids[i].Value.Equals(_localPlayerGuid))
            {
                _cachedServerPlayerEntity = entities[i];
                return TrySetEntityPosition(serverWorld, _cachedServerPlayerEntity, worldPos);
            }
        }

        if (!_loggedMissingServerPlayerEntity)
        {
            _loggedMissingServerPlayerEntity = true;
            Debug.LogWarning("[RevealWholeMap] Could not resolve local player entity in server world. Falling back to client-only teleport.");
        }

        return false;
    }

    private static bool TrySetEntityPosition(World world, Entity entity, float3 worldPos)
    {
        if (world == null || !world.IsCreated || entity == Entity.Null)
        {
            return false;
        }

        EntityManager entityManager = world.EntityManager;
        if (!entityManager.Exists(entity) || !entityManager.HasComponent<LocalTransform>(entity))
        {
            return false;
        }

        LocalTransform localTransform = entityManager.GetComponentData<LocalTransform>(entity);
        localTransform.Position = worldPos;
        entityManager.SetComponentData(entity, localTransform);
        return true;
    }

    private bool IsClientSubMapReady(World clientWorld, int2 targetWorldPos)
    {
        EntityManager entityManager = clientWorld.EntityManager;
        using EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ClientSubMapLayerCD>());
        if (query.IsEmptyIgnoreFilter)
        {
            return false;
        }

        using NativeArray<ClientSubMapLayerCD> layers = query.ToComponentDataArray<ClientSubMapLayerCD>(Allocator.Temp);
        if (layers.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < layers.Length; i++)
        {
            int2 delta = layers[i].data.viewPoint - targetWorldPos;
            if (math.lengthsq(delta) <= SubMapViewpointToleranceSq)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsClientTileCoverageReady(int2 targetViewPoint, out float knownRatio, out int testedSamples)
    {
        if (TryGetMapTimestampCoverageStatus(targetViewPoint, out bool timestampCoverageReady, out knownRatio, out testedSamples))
        {
            return timestampCoverageReady;
        }

        return IsClientTileCoverageReadyFromSubMapTiles(targetViewPoint, out knownRatio, out testedSamples);
    }

    private bool TryGetMapPartsForRead(out IDictionary mapParts)
    {
        mapParts = null;
        MapUI mapUI = Manager.ui?.mapUI;
        if (mapUI == null || !EnsureMapTimestampReflectionFields())
        {
            return false;
        }

        mapParts = _mapUIPartsField.GetValue(mapUI) as IDictionary;
        return mapParts != null;
    }

    private static bool IsTimestampRevealedAt(
        int2 worldPoint,
        IDictionary mapParts,
        Dictionary<Vector2Int, NativeArray<Pug.UnityExtensions.PugColorARGB32>> timestampCache)
    {
        Vector2Int mapPartKey = MapUI.WorldPositionToMapPartIndex(new float2(worldPoint.x, worldPoint.y));
        if (!timestampCache.TryGetValue(mapPartKey, out NativeArray<Pug.UnityExtensions.PugColorARGB32> timestampData))
        {
            TryGetExistingTimestampTextureData(mapParts, mapPartKey, out timestampData);
            timestampCache[mapPartKey] = timestampData;
        }

        if (!timestampData.IsCreated)
        {
            return false;
        }

        int2 partPosition = MapUI.WorldPositionToMapPartPosition(worldPoint);
        int timestampIndex = partPosition.y * 256 + partPosition.x;
        if (timestampIndex < 0 || timestampIndex >= timestampData.Length)
        {
            return false;
        }

        return !timestampData[timestampIndex].Equals(default(Pug.UnityExtensions.PugColorARGB32));
    }

    private bool TryGetMapTimestampCoverageStatus(int2 targetViewPoint, out bool coverageReady, out float knownRatio, out int testedSamples)
    {
        coverageReady = false;
        knownRatio = 0f;
        testedSamples = 0;

        if (!TryGetMapPartsForRead(out IDictionary mapParts))
        {
            return false;
        }

        Dictionary<Vector2Int, NativeArray<Pug.UnityExtensions.PugColorARGB32>> timestampCache =
            new Dictionary<Vector2Int, NativeArray<Pug.UnityExtensions.PugColorARGB32>>();
        int radius = (int)math.ceil(RevealDistanceOverride);
        int knownSamples = 0;
        for (int y = -radius; y <= radius; y += TileSampleStride)
        {
            for (int x = -radius; x <= radius; x += TileSampleStride)
            {
                if ((long)x * x + (long)y * y > (long)radius * radius)
                {
                    continue;
                }

                int2 samplePos = targetViewPoint + new int2(x, y);
                if (samplePos.x < _minWorldX || samplePos.x > _maxWorldX || samplePos.y < _minWorldY || samplePos.y > _maxWorldY)
                {
                    continue;
                }

                testedSamples++;
                if (IsTimestampRevealedAt(samplePos, mapParts, timestampCache))
                {
                    knownSamples++;
                }
            }
        }

        if (testedSamples == 0)
        {
            return false;
        }

        knownRatio = (float)knownSamples / testedSamples;
        if (testedSamples < MinTileSamples)
        {
            coverageReady = knownSamples > 0;
            return true;
        }

        coverageReady = knownRatio >= MinKnownTileRatio;
        return true;
    }

    private bool IsClientTileCoverageReadyFromSubMapTiles(int2 targetViewPoint, out float knownRatio, out int testedSamples)
    {
        knownRatio = 0f;
        testedSamples = 0;

        SinglePugMap map = Manager.multiMap;
        if (map == null)
        {
            return false;
        }

        SinglePugMap.TileLayerLookup tileLookup = map.GetTileLayerLookup();
        int radius = (int)math.ceil(RevealDistanceOverride);
        int knownSamples = 0;
        for (int y = -radius; y <= radius; y += TileSampleStride)
        {
            for (int x = -radius; x <= radius; x += TileSampleStride)
            {
                if ((long)x * x + (long)y * y > (long)radius * radius)
                {
                    continue;
                }

                int2 samplePos = targetViewPoint + new int2(x, y);
                if (samplePos.x < _minWorldX || samplePos.x > _maxWorldX || samplePos.y < _minWorldY || samplePos.y > _maxWorldY)
                {
                    continue;
                }

                testedSamples++;
                if (tileLookup.GetTopTile(samplePos).tileType != TileType.none)
                {
                    knownSamples++;
                }
            }
        }

        if (testedSamples == 0)
        {
            return false;
        }

        knownRatio = (float)knownSamples / testedSamples;
        if (testedSamples < MinTileSamples)
        {
            return knownSamples > 0;
        }

        return knownRatio >= MinKnownTileRatio;
    }

    private static bool IsSubMapWindowReady(World world, int2 targetViewPoint)
    {
        EntityManager entityManager = world.EntityManager;
        using EntityQuery registryQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<SubMapRegistry>());
        if (registryQuery.IsEmptyIgnoreFilter)
        {
            return false;
        }

        NativeParallelHashMap<int2, Entity> indexToEntity = registryQuery.GetSingleton<SubMapRegistry>().IndexToEntity;
        int2 windowMin = targetViewPoint - new int2(SubMapWindowWidth / 2, SubMapWindowHeight / 2);
        int2 windowMax = windowMin + new int2(SubMapWindowWidth - 1, SubMapWindowHeight - 1);
        int2 minIndex = (windowMin & -64) >> 6;
        int2 maxIndex = (windowMax & -64) >> 6;

        for (int y = minIndex.y; y <= maxIndex.y; y++)
        {
            for (int x = minIndex.x; x <= maxIndex.x; x++)
            {
                int2 subMapIndex = new int2(x, y);
                if (!indexToEntity.TryGetValue(subMapIndex, out Entity subMapEntity))
                {
                    return false;
                }

                if (!entityManager.Exists(subMapEntity) || !entityManager.HasBuffer<SubMapLayerBuffer>(subMapEntity))
                {
                    return false;
                }

                if (entityManager.GetBuffer<SubMapLayerBuffer>(subMapEntity).Length == 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool EnsureMapTimestampReflectionFields()
    {
        _mapUIPartsField ??= typeof(MapUI).GetField("_mapParts", BindingFlags.Instance | BindingFlags.NonPublic);
        if (_mapPartTimestampTextureField == null)
        {
            Type mapPartType = typeof(MapUI).GetNestedType("MapPart", BindingFlags.NonPublic);
            _mapPartTimestampTextureField = mapPartType?.GetField("timestampTexture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        return _mapUIPartsField != null && _mapPartTimestampTextureField != null;
    }

    private static bool TryGetExistingTimestampTextureData(
        IDictionary mapParts,
        Vector2Int mapPartKey,
        out NativeArray<Pug.UnityExtensions.PugColorARGB32> timestampData)
    {
        timestampData = default;
        if (mapParts == null)
        {
            return false;
        }

        object mapPart = mapParts[mapPartKey];
        if (mapPart == null || _mapPartTimestampTextureField == null)
        {
            return false;
        }

        Texture2D timestampTexture = _mapPartTimestampTextureField.GetValue(mapPart) as Texture2D;
        if (timestampTexture == null)
        {
            return false;
        }

        timestampData = timestampTexture.GetPixelData<Pug.UnityExtensions.PugColorARGB32>(0);
        return timestampData.IsCreated;
    }

    private bool TryCaptureAndEnableMapRevealOverride(out string message)
    {
        _mapUpdateLargeRevealDistanceField ??= typeof(MapUpdateSystem).GetField("_largeRevealDistance", BindingFlags.Static | BindingFlags.NonPublic);
        if (_mapUpdateLargeRevealDistanceField == null || _mapUpdateLargeRevealDistanceField.FieldType != typeof(bool))
        {
            message = "Could not access MapUpdateSystem._largeRevealDistance.";
            return false;
        }

        _previousLargeRevealDistance = (bool)_mapUpdateLargeRevealDistanceField.GetValue(null);
        _mapUpdateLargeRevealDistanceField.SetValue(null, true);
        _capturedMapRevealState = true;
        message = string.Empty;
        return true;
    }

    private void RestoreMapRevealOverride()
    {
        if (!_capturedMapRevealState || _mapUpdateLargeRevealDistanceField == null)
        {
            return;
        }

        _capturedMapRevealState = false;
        _mapUpdateLargeRevealDistanceField.SetValue(null, _previousLargeRevealDistance);
    }

    private void EnsureHarmonyPatches()
    {
        if (_harmonyPatched)
        {
            return;
        }

        _harmony = new Harmony("corekeeper.revealwholemap");
        _harmony.PatchAll(typeof(RevealWholeMapMod).Assembly);
        _harmonyPatched = true;
    }

    private void RemoveHarmonyPatches()
    {
        if (!_harmonyPatched)
        {
            return;
        }

        _harmony.UnpatchSelf();
        _harmonyPatched = false;
        _harmony = null;
    }

    private void RequestCancelInternal(string reason)
    {
        if (_state == RevealState.Canceling)
        {
            return;
        }

        _state = RevealState.Canceling;
        Debug.Log("[RevealWholeMap] " + reason);
    }

    private void ForceCleanup()
    {
        _hasActiveProbe = false;
        _activeProbeReadyFrames = 0;
        _activeProbeRevealFrames = 0;
        RestoreMapRevealOverride();
        RestorePlayerState();
    }

    private void ResetState()
    {
        _state = RevealState.Idle;
        _probePoints.Clear();
        _centerXs.Clear();
        _centerYs.Clear();
        _queuedBiomeOrder.Clear();
        _completedPoints.Clear();
        _attemptCounts.Clear();
        _pointToBiome.Clear();
        _primaryBatchRanges.Clear();
        _biomeMinPoints.Clear();
        _biomeMaxPoints.Clear();
        _rowMinAllowedX.Clear();
        _rowMaxAllowedX.Clear();

        _hasActiveProbe = false;
        _activeProbePoint = default;
        _activeProbeBiome = Biome.None;
        _activeProbeAttempt = 0;
        _activeProbeStartTime = 0.0;
        _activeProbeReadyFrames = 0;
        _activeProbeRevealFrames = 0;
        _activeProbeKnownRatio = 0f;
        _activeProbeKnownSamples = 0;
        _activeServerSubMapReady = false;
        _activeClientSubMapReady = false;
        _activeClientViewpointAligned = false;
        _activeTileCoverageReady = false;

        _processedProbeCount = 0;
        _skippedProbeCount = 0;
        _lastLoggedProcessed = 0;
        _startTime = 0.0;
        _currentBiomeOrderIndex = -1;
        _currentBiomeRepairPassCount = 0;
        _activeBatchStartIndex = 0;
        _activeBatchCount = 0;
        _nextBatchProbeOffset = 0;
        _activeBatchIsRepair = false;
        _activeBatchBiome = Biome.None;

        _minWorldX = 0;
        _minWorldY = 0;
        _maxWorldX = 0;
        _maxWorldY = 0;
        _hasBiomeFilter = false;
        _selectedBiomeFilter = Biome.None;
        _localPlayerGuid = default;
        _cachedServerPlayerEntity = Entity.Null;
        _loggedMissingServerPlayerEntity = false;
    }

    private static long ToKey(int2 value)
    {
        return ((long)value.x << 32) | (uint)value.y;
    }

    private static long SqDistance(int2 a, int2 b)
    {
        long dx = (long)a.x - b.x;
        long dy = (long)a.y - b.y;
        return dx * dx + dy * dy;
    }

    private void LogProgressIfNeeded()
    {
        if (_processedProbeCount - _lastLoggedProcessed >= ProgressLogStep || _processedProbeCount == _probePoints.Count)
        {
            _lastLoggedProcessed = _processedProbeCount;
            Debug.Log($"[RevealWholeMap] Progress {_processedProbeCount}/{_probePoints.Count} (skipped {_skippedProbeCount}).");
        }
    }
}

[HarmonyPatch(typeof(MapUI), "get_PauseMapUpdates")]
public static class RevealWholeMapPauseMapUpdatesPatch
{
    private static bool Prefix(ref bool __result)
    {
        if (!RevealWholeMapMod.IsRevealRunning())
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(MapUpdateSystem), "UpdateTiles")]
public static class RevealWholeMapMapUpdateSystemPatch
{
    private static void Prefix(ref float revealDistance)
    {
        if (RevealWholeMapMod.TryGetRevealDistanceOverride(out float overrideDistance))
        {
            revealDistance = overrideDistance;
        }
    }
}

public static class RevealWholeMapCommands
{
    [Preserve]
    [Command("revealmap.start", "Reveal the whole map by teleporting the player probe around the world.", QFSW.QC.Platform.AllPlatforms, MonoTargetType.Single)]
    public static string StartRevealMap()
    {
        if (RevealWholeMapMod.Instance == null)
        {
            return "RevealWholeMap mod instance is unavailable.";
        }

        RevealWholeMapMod.Instance.TryStartReveal(out string message);
        return message;
    }

    [Preserve]
    [Command("revealmap.startBiome", "Reveal only a specific biome by teleporting probes inside that biome.", QFSW.QC.Platform.AllPlatforms, MonoTargetType.Single)]
    public static string StartRevealMapBiome(Biome biome)
    {
        if (RevealWholeMapMod.Instance == null)
        {
            return "RevealWholeMap mod instance is unavailable.";
        }

        RevealWholeMapMod.Instance.TryStartRevealForBiome(biome, out string message);
        return message;
    }

    [Preserve]
    [Command("revealmap.cancel", "Cancel an ongoing teleport-based map reveal.", QFSW.QC.Platform.AllPlatforms, MonoTargetType.Single)]
    public static string CancelRevealMap()
    {
        if (RevealWholeMapMod.Instance == null)
        {
            return "RevealWholeMap mod instance is unavailable.";
        }

        RevealWholeMapMod.Instance.TryCancelReveal(out string message);
        return message;
    }

    [Preserve]
    [Command("revealmap.status", "Show current teleport-based reveal progress.", QFSW.QC.Platform.AllPlatforms, MonoTargetType.Single)]
    public static string RevealMapStatus()
    {
        if (RevealWholeMapMod.Instance == null)
        {
            return "RevealWholeMap mod instance is unavailable.";
        }

        return RevealWholeMapMod.Instance.GetStatus();
    }
}
