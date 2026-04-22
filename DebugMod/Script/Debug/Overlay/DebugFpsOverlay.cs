using Pug.RP;
using UnityEngine;

internal sealed class DebugFpsOverlay : MonoBehaviour
{
    private const float StatsWindowSeconds = 1f;
    private const float GraphSampleIntervalSeconds = 1f / 30f;

    private const float Padding = 16f;
    private const float PanelWidth = 380f;
    private const float PanelHeight = 356f;
    private const float InnerPadding = 12f;
    private const float RowHeight = 22f;
    private const float SectionSpacing = 12f;
    private const float GraphTitleSpacing = 4f;
    private const float GraphSpacing = 8f;
    private const float GraphHeight = 88f;
    private const float LabelWidth = 92f;
    private const float ValueWidth = 120f;
    private const float LegendCircleSize = 14f;
    private const string LegendCircle = "●";

    private static readonly Color PanelColor = new(0f, 0f, 0f, 215f / 255f);
    private static readonly Color InstantGraphColor = new(1f, 210f / 255f, 64f / 255f);
    private static readonly Color MeanGraphColor = new(64f / 255f, 220f / 255f, 1f);

    private static DebugFpsOverlay _instance;

    private readonly DebugFpsStatsWindow _statsWindow = new(StatsWindowSeconds);
    private readonly DebugFpsGraph _graph = new(320, 88, GraphSampleIntervalSeconds);

    private DebugFpsSnapshot _displayedStats;
    private GUIStyle _headerStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _valueStyle;
    private GUIStyle _legendStyle;
    private GUIStyle _circleStyle;

    internal static string Toggle()
    {
        DebugFpsOverlay overlay = EnsureInstance();
        overlay.enabled = !overlay.enabled;
        return overlay.enabled ? "Debug FPS overlay enabled." : "Debug FPS overlay disabled.";
    }

    internal static string SetVisible(bool isVisible)
    {
        DebugFpsOverlay overlay = EnsureInstance();
        overlay.enabled = isVisible;
        return overlay.enabled ? "Debug FPS overlay enabled." : "Debug FPS overlay disabled.";
    }

    private static DebugFpsOverlay EnsureInstance()
    {
        if (_instance != null)
        {
            return _instance;
        }

        GameObject overlayObject = new("[DebugMod] FPS Overlay");
        DontDestroyOnLoad(overlayObject);

        _instance = overlayObject.AddComponent<DebugFpsOverlay>();
        _instance.enabled = false;
        return _instance;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        float now = Time.unscaledTime;
        _statsWindow.Clear();
        _graph.Reset(now);
        _displayedStats = new DebugFpsSnapshot(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }

        _graph.Dispose();
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        float frameTimeMs = GetLatestFrameTimeMs();

        _displayedStats = _statsWindow.AddSample(now, frameTimeMs);
        _graph.AddSample(now, _displayedStats.LatestFps, _displayedStats.MeanFps);
    }

    private void OnGUI()
    {
        EnsureStyles();

        Rect panelRect = new(Padding, Padding, PanelWidth, PanelHeight);
        DrawPanel(panelRect);

        float contentX = panelRect.x + InnerPadding;
        float contentWidth = panelRect.width - InnerPadding * 2f;
        float y = panelRect.y + InnerPadding;

        y = DrawStatsSection(
            contentX,
            y,
            contentWidth,
            "FPS",
            FormatFps(_displayedStats.LatestFps),
            FormatFps(_displayedStats.MeanFps),
            FormatFps(_displayedStats.MinFps),
            FormatFps(_displayedStats.MaxFps));

        y += SectionSpacing;

        y = DrawStatsSection(
            contentX,
            y,
            contentWidth,
            "Frame",
            FormatFrameTime(_displayedStats.LatestFrameTimeMs),
            FormatFrameTime(_displayedStats.MeanFrameTimeMs),
            FormatFrameTime(_displayedStats.MinFrameTimeMs),
            FormatFrameTime(_displayedStats.MaxFrameTimeMs));

        y += SectionSpacing;
        GUI.Label(new Rect(contentX, y, contentWidth, RowHeight), "FPS Graph", _headerStyle);

        y += RowHeight + GraphTitleSpacing;
        Rect graphRect = new(contentX, y, contentWidth, GraphHeight);
        GUI.DrawTexture(graphRect, _graph.GetTexture(), ScaleMode.StretchToFill, alphaBlend: true);

        y = graphRect.yMax + GraphSpacing;
        DrawLegend(contentX, y);
    }

    private void EnsureStyles()
    {
        if (_headerStyle != null)
        {
            return;
        }

        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };
        _headerStyle.normal.textColor = Color.white;

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14
        };
        _labelStyle.normal.textColor = Color.white;

        _valueStyle = new GUIStyle(_labelStyle)
        {
            alignment = TextAnchor.UpperRight
        };

        _legendStyle = new GUIStyle(_labelStyle);
        _circleStyle = new GUIStyle(_labelStyle)
        {
            fontSize = 12
        };
    }

    private float DrawStatsSection(
        float x,
        float y,
        float width,
        string title,
        string instantValue,
        string meanValue,
        string minValue,
        string maxValue)
    {
        GUI.Label(new Rect(x, y, width, RowHeight), $"{title} {instantValue}", _headerStyle);
        y += RowHeight;

        DrawStatRow(x, y, "Mean 1s:", meanValue);
        y += RowHeight;

        DrawStatRow(x, y, "Min 1s:", minValue);
        y += RowHeight;

        DrawStatRow(x, y, "Max 1s:", maxValue);
        return y + RowHeight;
    }

    private void DrawStatRow(float x, float y, string label, string value)
    {
        GUI.Label(new Rect(x, y, LabelWidth, RowHeight), label, _labelStyle);
        GUI.Label(new Rect(x + LabelWidth, y, ValueWidth, RowHeight), value, _valueStyle);
    }

    private void DrawLegend(float x, float y)
    {
        DrawLegendItem(x, y, InstantGraphColor, "instant");
        DrawLegendItem(x + 96f, y, MeanGraphColor, "1s mean");
    }

    private void DrawLegendItem(float x, float y, Color color, string label)
    {
        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.Label(new Rect(x, y, LegendCircleSize, RowHeight), LegendCircle, _circleStyle);
        GUI.color = previousColor;

        GUI.Label(new Rect(x + LegendCircleSize + 2f, y, 72f, RowHeight), label, _legendStyle);
    }

    private static string FormatFps(float value)
    {
        return value.ToString("0.#");
    }

    private static string FormatFrameTime(float value)
    {
        return $"{value:0.00} ms";
    }

    private static void DrawPanel(Rect panelRect)
    {
        Color previousColor = GUI.color;
        GUI.color = PanelColor;
        GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
        GUI.color = previousColor;
    }

    private static float GetLatestFrameTimeMs()
    {
        if (PugRP.frametimeMS > 0f)
        {
            return PugRP.frametimeMS;
        }

        return Time.unscaledDeltaTime * 1000f;
    }
}
