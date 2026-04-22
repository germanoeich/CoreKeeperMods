internal readonly struct DebugFpsSnapshot
{
    internal readonly float LatestFps;
    internal readonly float MeanFps;
    internal readonly float MinFps;
    internal readonly float MaxFps;
    internal readonly float LatestFrameTimeMs;
    internal readonly float MeanFrameTimeMs;
    internal readonly float MinFrameTimeMs;
    internal readonly float MaxFrameTimeMs;

    internal DebugFpsSnapshot(
        float latestFps,
        float meanFps,
        float minFps,
        float maxFps,
        float latestFrameTimeMs,
        float meanFrameTimeMs,
        float minFrameTimeMs,
        float maxFrameTimeMs)
    {
        LatestFps = latestFps;
        MeanFps = meanFps;
        MinFps = minFps;
        MaxFps = maxFps;
        LatestFrameTimeMs = latestFrameTimeMs;
        MeanFrameTimeMs = meanFrameTimeMs;
        MinFrameTimeMs = minFrameTimeMs;
        MaxFrameTimeMs = maxFrameTimeMs;
    }
}
