using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Haptic trace CSV(frame,timeSec,segIndex,t,x,y,z,frameDurMs,activeCount,sumIntensity,m0..m31)
/// 를 Unity 안에서 그래프로 렌더링.
/// 
/// 사용법:
/// 1) 빈 GameObject에 이 스크립트 부착
/// 2) (권장) Canvas 안에 RawImage 만들고 targetImage에 할당
/// 3) traceFileRelativeToAssets = "data/haptic_trace.csv" 처럼 설정
/// 4) Play -> (옵션) Auto Load On Start 체크
/// </summary>
public class HapticTraceGraphViewer : MonoBehaviour
{
    [Header("Input CSV")]
    [Tooltip("Assets 기준 상대경로. 예: data/haptic_trace.csv")]
    public string traceFileRelativeToAssets = "data/haptic_trace.csv";

    [Tooltip("RawImage에 그래프를 그려서 표시(권장). 비워두면 OnGUI로만 표시")]
    public RawImage targetImage;

    [Header("Plot Settings")]
    public int width = 1400;
    public int height = 600;

    [Tooltip("그래프 좌우/상하 패딩(px)")]
    public int padding = 40;

    [Tooltip("그릴 모터 index들 (0~31). 예: [0, 5, 12]. 최대 3개 추천")]
    public int[] motorIndicesToPlot = new int[] { 0 };

    [Tooltip("처음부터 자동 로드")]
    public bool autoLoadOnStart = true;

    [Tooltip("로딩 후 자동 렌더")]
    public bool autoRenderOnLoad = true;

    // parsed data
    private List<float> _time = new();
    private List<int> _active = new();
    private List<int> _sum = new();
    private List<int[]> _motors = new(); // each row: 32 ints

    // texture
    private Texture2D _tex;
    private string _status = "Not loaded";

    private void Start()
    {
        if (autoLoadOnStart)
        {
            LoadFromAssetsRelativePath();
            if (autoRenderOnLoad) Render();
        }
    }

    [ContextMenu("Load CSV From Assets Path")]
    public void LoadFromAssetsRelativePath()
    {
        string fullPath = Path.Combine(Application.dataPath, traceFileRelativeToAssets);
        if (!File.Exists(fullPath))
        {
            _status = $"CSV not found: {fullPath}";
            Debug.LogError("[HapticTraceGraphViewer] " + _status);
            return;
        }

        try
        {
            ParseCsv(File.ReadAllText(fullPath));
            _status = $"Loaded: {fullPath} | rows={_time.Count}";
            Debug.Log("[HapticTraceGraphViewer] " + _status);
        }
        catch (Exception e)
        {
            _status = $"Parse error: {e.Message}";
            Debug.LogError("[HapticTraceGraphViewer] " + _status);
        }
    }

    [ContextMenu("Render Graph")]
    public void Render()
    {
        if (_time.Count == 0)
        {
            _status = "No data to render. Load CSV first.";
            Debug.LogWarning("[HapticTraceGraphViewer] " + _status);
            return;
        }

        EnsureTexture();

        // clear background
        Fill(_tex, new Color32(18, 18, 18, 255));

        // draw axes box
        DrawRect(_tex, padding, padding, width - 2 * padding, height - 2 * padding, new Color32(120, 120, 120, 255));

        // scales
        float tMin = _time[0];
        float tMax = _time[_time.Count - 1];
        if (tMax <= tMin) tMax = tMin + 0.0001f;

        // active: 0..32
        // sum: 0..(32*100)=3200 (대충)
        int activeMax = 32;
        int sumMax = 3200;

        // draw grid lines (optional light)
        DrawHGrid(_tex, 4, new Color32(50, 50, 50, 255));
        DrawVGrid(_tex, 6, new Color32(50, 50, 50, 255));

        // plot active (green-ish)
        PlotSeriesInt(
            _tex, _time, _active,
            tMin, tMax, 0, activeMax,
            new Color32(60, 220, 140, 255), 2
        );

        // plot sum (orange-ish)
        PlotSeriesInt(
            _tex, _time, _sum,
            tMin, tMax, 0, sumMax,
            new Color32(255, 170, 60, 255), 2
        );

        // plot motors
        // motor intensity: 0..100
        var motorColors = new Color32[]
        {
            new Color32(120, 180, 255, 255),
            new Color32(220, 120, 255, 255),
            new Color32(255, 120, 160, 255)
        };

        int plotted = 0;
        foreach (var m in motorIndicesToPlot ?? Array.Empty<int>())
        {
            if (m < 0 || m > 31) continue;
            if (plotted >= motorColors.Length) break;

            var series = new List<int>(_motors.Count);
            for (int i = 0; i < _motors.Count; i++)
                series.Add(_motors[i][m]);

            PlotSeriesInt(
                _tex, _time, series,
                tMin, tMax, 0, 100,
                motorColors[plotted], 2
            );

            plotted++;
        }

        _tex.Apply();

        if (targetImage != null)
        {
            targetImage.texture = _tex;
            targetImage.SetNativeSize();
        }
    }

    // ---------------------------
    // CSV parsing
    // ---------------------------
    private void ParseCsv(string text)
    {
        _time.Clear();
        _active.Clear();
        _sum.Clear();
        _motors.Clear();

        using var sr = new StringReader(text);
        string line;
        bool headerSkipped = false;

        while ((line = sr.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // skip header (frame,timeSec,...)
            if (!headerSkipped)
            {
                headerSkipped = true;
                // 첫 줄이 헤더일 확률이 높으니 그냥 1줄 스킵
                // (만약 헤더가 없으면 아래 numeric check로 통과 가능)
                if (line.StartsWith("frame", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var cols = line.Split(',');
            if (cols.Length < 10 + 32) continue;

            // numeric check
            if (!int.TryParse(cols[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                continue;

            float timeSec = float.Parse(cols[1], CultureInfo.InvariantCulture);
            int activeCount = int.Parse(cols[8], CultureInfo.InvariantCulture);
            int sumIntensity = int.Parse(cols[9], CultureInfo.InvariantCulture);

            int[] motors = new int[32];
            for (int i = 0; i < 32; i++)
            {
                motors[i] = int.Parse(cols[10 + i], CultureInfo.InvariantCulture);
            }

            _time.Add(timeSec);
            _active.Add(activeCount);
            _sum.Add(sumIntensity);
            _motors.Add(motors);
        }
    }

    // ---------------------------
    // Texture helpers
    // ---------------------------
    private void EnsureTexture()
    {
        if (_tex != null && _tex.width == width && _tex.height == height) return;

        _tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        _tex.filterMode = FilterMode.Point;
        _tex.wrapMode = TextureWrapMode.Clamp;
    }

    private static void Fill(Texture2D tex, Color32 c)
    {
        var pixels = tex.GetPixels32();
        for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
        tex.SetPixels32(pixels);
    }

    private void DrawRect(Texture2D tex, int x, int y, int w, int h, Color32 c)
    {
        DrawLine(tex, x, y, x + w, y, c, 1);
        DrawLine(tex, x, y, x, y + h, c, 1);
        DrawLine(tex, x + w, y, x + w, y + h, c, 1);
        DrawLine(tex, x, y + h, x + w, y + h, c, 1);
    }

    private void DrawHGrid(Texture2D tex, int rows, Color32 c)
    {
        int x0 = padding;
        int x1 = width - padding;
        int y0 = padding;
        int y1 = height - padding;
        for (int i = 1; i < rows; i++)
        {
            int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, i / (float)rows));
            DrawLine(tex, x0, y, x1, y, c, 1);
        }
    }

    private void DrawVGrid(Texture2D tex, int cols, Color32 c)
    {
        int x0 = padding;
        int x1 = width - padding;
        int y0 = padding;
        int y1 = height - padding;
        for (int i = 1; i < cols; i++)
        {
            int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, i / (float)cols));
            DrawLine(tex, x, y0, x, y1, c, 1);
        }
    }

    private void PlotSeriesInt(
        Texture2D tex,
        List<float> xs, List<int> ys,
        float xMin, float xMax,
        float yMin, float yMax,
        Color32 color, int thickness)
    {
        int x0 = padding;
        int y0 = padding;
        int x1 = width - padding;
        int y1 = height - padding;

        int n = Mathf.Min(xs.Count, ys.Count);
        if (n < 2) return;

        int prevX = 0, prevY = 0;
        for (int i = 0; i < n; i++)
        {
            float nx = (xs[i] - xMin) / (xMax - xMin);
            float ny = (ys[i] - yMin) / (yMax - yMin);

            int px = Mathf.RoundToInt(Mathf.Lerp(x0, x1, Mathf.Clamp01(nx)));
            int py = Mathf.RoundToInt(Mathf.Lerp(y0, y1, Mathf.Clamp01(ny)));

            if (i > 0)
                DrawLine(tex, prevX, prevY, px, py, color, thickness);

            prevX = px; prevY = py;
        }
    }

    // Bresenham + thickness
    private void DrawLine(Texture2D tex, int x0, int y0, int x1, int y1, Color32 col, int thickness)
    {
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            DrawPoint(tex, x0, y0, col, thickness);

            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    private void DrawPoint(Texture2D tex, int x, int y, Color32 col, int thickness)
    {
        int r = Mathf.Max(0, thickness / 2);
        for (int oy = -r; oy <= r; oy++)
        {
            for (int ox = -r; ox <= r; ox++)
            {
                int px = x + ox;
                int py = y + oy;
                if (px < 0 || px >= tex.width || py < 0 || py >= tex.height) continue;
                tex.SetPixel(px, py, col);
            }
        }
    }

    // ---------------------------
    // Minimal UI (fallback)
    // ---------------------------
    private void OnGUI()
    {
        GUI.Label(new Rect(20, 20, 1600, 24), $"[TraceGraph] {_status}");
        GUI.Label(new Rect(20, 44, 1600, 24), $"File (Assets relative): {traceFileRelativeToAssets}");
        GUI.Label(new Rect(20, 68, 1600, 24), $"Rows: {_time.Count} | Motors plotted: {string.Join(",", motorIndicesToPlot ?? Array.Empty<int>())}");

        if (GUI.Button(new Rect(20, 95, 220, 30), "Load CSV"))
        {
            LoadFromAssetsRelativePath();
        }
        if (GUI.Button(new Rect(250, 95, 220, 30), "Render Graph"))
        {
            Render();
        }
    }
}
