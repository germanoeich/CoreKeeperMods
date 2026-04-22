using UnityEngine;

internal sealed class DebugFpsGraph
{
    private const int GraphPaddingPixels = 4;

    private static readonly Color32 BackgroundColor = new(14, 18, 22, 255);
    private static readonly Color32 GridColor = new(55, 62, 68, 255);
    private static readonly Color32 InstantLineColor = new(255, 210, 64, 255);
    private static readonly Color32 MeanLineColor = new(64, 220, 255, 255);

    private readonly int _width;
    private readonly int _height;
    private readonly float _sampleIntervalSeconds;
    private readonly float[] _instantSamples;
    private readonly float[] _meanSamples;
    private readonly Color32[] _pixels;

    private Texture2D _texture;
    private int _count;
    private int _writeIndex;
    private float _nextSampleTime;
    private bool _isDirty;

    internal DebugFpsGraph(int width, int height, float sampleIntervalSeconds)
    {
        _width = width;
        _height = height;
        _sampleIntervalSeconds = Mathf.Max(0.01f, sampleIntervalSeconds);
        _instantSamples = new float[width];
        _meanSamples = new float[width];
        _pixels = new Color32[width * height];
    }

    internal void Reset(float now)
    {
        _count = 0;
        _writeIndex = 0;
        _nextSampleTime = now;
        _isDirty = true;
    }

    internal void AddSample(float now, float instantFps, float meanFps)
    {
        if (now < _nextSampleTime)
        {
            return;
        }

        _instantSamples[_writeIndex] = instantFps;
        _meanSamples[_writeIndex] = meanFps;

        _writeIndex = (_writeIndex + 1) % _width;
        if (_count < _width)
        {
            _count++;
        }

        _nextSampleTime = now + _sampleIntervalSeconds;
        _isDirty = true;
    }

    internal Texture2D GetTexture()
    {
        EnsureTexture();

        if (_isDirty)
        {
            RebuildTexture();
            _isDirty = false;
        }

        return _texture;
    }

    internal void Dispose()
    {
        if (_texture == null)
        {
            return;
        }

        Object.Destroy(_texture);
        _texture = null;
    }

    private void EnsureTexture()
    {
        if (_texture != null)
        {
            return;
        }

        _texture = new Texture2D(_width, _height, TextureFormat.RGBA32, mipChain: false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        _isDirty = true;
    }

    private void RebuildTexture()
    {
        FillBackground();

        if (_count >= 2)
        {
            float maxFps = GetGraphMaxFps();
            DrawSeries(_instantSamples, maxFps, InstantLineColor);
            DrawSeries(_meanSamples, maxFps, MeanLineColor);
        }

        _texture.SetPixels32(_pixels);
        _texture.Apply(updateMipmaps: false);
    }

    private void FillBackground()
    {
        for (int i = 0; i < _pixels.Length; i++)
        {
            _pixels[i] = BackgroundColor;
        }

        DrawHorizontalLine(_height - 1 - GraphPaddingPixels, GridColor);
        DrawHorizontalLine(_height / 2, GridColor);
        DrawHorizontalLine(GraphPaddingPixels, GridColor);
    }

    private void DrawSeries(float[] samples, float maxFps, Color32 color)
    {
        float maxIndex = Mathf.Max(1, _count - 1);

        for (int i = 1; i < _count; i++)
        {
            int x0 = Mathf.RoundToInt((i - 1) / maxIndex * (_width - 1));
            int x1 = Mathf.RoundToInt(i / maxIndex * (_width - 1));
            int y0 = ToGraphY(GetSample(samples, i - 1), maxFps);
            int y1 = ToGraphY(GetSample(samples, i), maxFps);

            DrawLine(x0, y0, x1, y1, color);
        }
    }

    private float GetGraphMaxFps()
    {
        float maxFps = 60f;

        for (int i = 0; i < _count; i++)
        {
            maxFps = Mathf.Max(maxFps, GetSample(_instantSamples, i));
            maxFps = Mathf.Max(maxFps, GetSample(_meanSamples, i));
        }

        return Mathf.Ceil(maxFps / 30f) * 30f;
    }

    private float GetSample(float[] samples, int chronologicalIndex)
    {
        int firstSampleIndex = _writeIndex - _count;
        if (firstSampleIndex < 0)
        {
            firstSampleIndex += _width;
        }

        int sampleIndex = (firstSampleIndex + chronologicalIndex) % _width;
        return samples[sampleIndex];
    }

    private int ToGraphY(float fps, float maxFps)
    {
        float normalized = maxFps <= 0f ? 0f : Mathf.Clamp01(fps / maxFps);
        float usableHeight = _height - 1 - GraphPaddingPixels * 2f;
        float y = _height - 1 - GraphPaddingPixels - normalized * usableHeight;
        return Mathf.RoundToInt(y);
    }

    private void DrawHorizontalLine(int y, Color32 color)
    {
        for (int x = 0; x < _width; x++)
        {
            SetPixel(x, y, color);
        }
    }

    private void DrawLine(int x0, int y0, int x1, int y1, Color32 color)
    {
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int stepX = x0 < x1 ? 1 : -1;
        int stepY = y0 < y1 ? 1 : -1;
        int error = dx - dy;

        while (true)
        {
            SetPixel(x0, y0, color);

            if (x0 == x1 && y0 == y1)
            {
                return;
            }

            int doubleError = error * 2;
            if (doubleError > -dy)
            {
                error -= dy;
                x0 += stepX;
            }

            if (doubleError < dx)
            {
                error += dx;
                y0 += stepY;
            }
        }
    }

    private void SetPixel(int x, int y, Color32 color)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height)
        {
            return;
        }

        _pixels[y * _width + x] = color;
    }
}
