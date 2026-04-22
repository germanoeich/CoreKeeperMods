using UnityEngine;

internal sealed class DebugFpsStatsWindow
{
    private readonly float _windowSeconds;

    private bool _hasCurrentWindow;
    private float _currentWindowStartTime;
    private int _currentSampleCount;
    private float _currentFpsSum;
    private float _currentMinFps;
    private float _currentMaxFps;
    private float _currentFrameTimeSum;
    private float _currentMinFrameTimeMs;
    private float _currentMaxFrameTimeMs;

    private bool _hasPublishedWindow;
    private float _publishedMeanFps;
    private float _publishedMinFps;
    private float _publishedMaxFps;
    private float _publishedMeanFrameTimeMs;
    private float _publishedMinFrameTimeMs;
    private float _publishedMaxFrameTimeMs;

    internal DebugFpsStatsWindow(float windowSeconds)
    {
        _windowSeconds = Mathf.Max(0.01f, windowSeconds);
    }

    internal void Clear()
    {
        _hasCurrentWindow = false;
        _currentWindowStartTime = 0f;
        _currentSampleCount = 0;
        _currentFpsSum = 0f;
        _currentMinFps = 0f;
        _currentMaxFps = 0f;
        _currentFrameTimeSum = 0f;
        _currentMinFrameTimeMs = 0f;
        _currentMaxFrameTimeMs = 0f;

        _hasPublishedWindow = false;
        _publishedMeanFps = 0f;
        _publishedMinFps = 0f;
        _publishedMaxFps = 0f;
        _publishedMeanFrameTimeMs = 0f;
        _publishedMinFrameTimeMs = 0f;
        _publishedMaxFrameTimeMs = 0f;
    }

    internal DebugFpsSnapshot AddSample(float now, float frameTimeMs)
    {
        float fps = ToFps(frameTimeMs);

        if (!_hasCurrentWindow)
        {
            StartWindow(now, fps, frameTimeMs);
            return BuildSnapshot(fps, frameTimeMs);
        }

        if (now - _currentWindowStartTime >= _windowSeconds)
        {
            PublishCurrentWindow();
            StartWindow(now, fps, frameTimeMs);
            return BuildSnapshot(fps, frameTimeMs);
        }

        AddToCurrentWindow(fps, frameTimeMs);
        return BuildSnapshot(fps, frameTimeMs);
    }

    private void StartWindow(float now, float fps, float frameTimeMs)
    {
        _hasCurrentWindow = true;
        _currentWindowStartTime = now;
        _currentSampleCount = 1;
        _currentFpsSum = fps;
        _currentMinFps = fps;
        _currentMaxFps = fps;
        _currentFrameTimeSum = frameTimeMs;
        _currentMinFrameTimeMs = frameTimeMs;
        _currentMaxFrameTimeMs = frameTimeMs;
    }

    private void AddToCurrentWindow(float fps, float frameTimeMs)
    {
        _currentSampleCount++;
        _currentFpsSum += fps;
        _currentMinFps = Mathf.Min(_currentMinFps, fps);
        _currentMaxFps = Mathf.Max(_currentMaxFps, fps);
        _currentFrameTimeSum += frameTimeMs;
        _currentMinFrameTimeMs = Mathf.Min(_currentMinFrameTimeMs, frameTimeMs);
        _currentMaxFrameTimeMs = Mathf.Max(_currentMaxFrameTimeMs, frameTimeMs);
    }

    private void PublishCurrentWindow()
    {
        if (_currentSampleCount <= 0)
        {
            return;
        }

        float sampleCount = _currentSampleCount;
        _hasPublishedWindow = true;
        _publishedMeanFps = _currentFpsSum / sampleCount;
        _publishedMinFps = _currentMinFps;
        _publishedMaxFps = _currentMaxFps;
        _publishedMeanFrameTimeMs = _currentFrameTimeSum / sampleCount;
        _publishedMinFrameTimeMs = _currentMinFrameTimeMs;
        _publishedMaxFrameTimeMs = _currentMaxFrameTimeMs;
    }

    private DebugFpsSnapshot BuildSnapshot(float latestFps, float latestFrameTimeMs)
    {
        if (_hasPublishedWindow)
        {
            return new DebugFpsSnapshot(
                latestFps,
                _publishedMeanFps,
                _publishedMinFps,
                _publishedMaxFps,
                latestFrameTimeMs,
                _publishedMeanFrameTimeMs,
                _publishedMinFrameTimeMs,
                _publishedMaxFrameTimeMs);
        }

        if (_currentSampleCount <= 0)
        {
            return new DebugFpsSnapshot(
                latestFps,
                latestFps,
                latestFps,
                latestFps,
                latestFrameTimeMs,
                latestFrameTimeMs,
                latestFrameTimeMs,
                latestFrameTimeMs);
        }

        float sampleCount = _currentSampleCount;
        return new DebugFpsSnapshot(
            latestFps,
            _currentFpsSum / sampleCount,
            _currentMinFps,
            _currentMaxFps,
            latestFrameTimeMs,
            _currentFrameTimeSum / sampleCount,
            _currentMinFrameTimeMs,
            _currentMaxFrameTimeMs);
    }

    private static float ToFps(float frameTimeMs)
    {
        if (frameTimeMs <= 0f)
        {
            return 0f;
        }

        return 1000f / frameTimeMs;
    }
}
