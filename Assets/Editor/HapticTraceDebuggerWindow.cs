#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Haptic trace CSV 디버거 (EditorWindow)
/// - Load/Reload
/// - 3-panel plot: Active / Sum / Motors
/// - Zoom (mouse wheel), Pan (drag)
/// - Hover tooltip: frame info + topK motors
/// - Motor selection (0~31 multi-toggle)
/// - Optional Heatmap (time x motor)
///
/// CSV expected header:
/// frame,timeSec,segIndex,t,x,y,z,frameDurMs,activeCount,sumIntensity,m0..m31
/// </summary>
public class HapticTraceDebuggerWindow : EditorWindow
{
    // -------- Data model --------
    private class Row
    {
        public int frame;
        public float timeSec;
        public int segIndex;
        public float t;
        public float x, y, z;
        public int frameDurMs;
        public int activeCount;
        public int sumIntensity;
        public int[] motors; // length 32
    }

    private List<Row> _rows = new List<Row>();
    private string _csvPath = "";
    private Vector2 _scroll;

    // -------- View state --------
    private float _viewT0 = 0f;
    private float _viewT1 = 1f;
    private bool _autoFitX = true;

    private bool _showHeatmap = true;
    private bool _lockYActive = true;  // 0..32
    private bool _lockYMotor = true;   // 0..100
    private bool _lockYSum = false;    // auto or fixed
    private int _sumYMax = 3200;

    private int _topK = 6;

    // motor selection
    private bool[] _motorSel = new bool[32];
    private int _motorQuickPick = 0;

    // interaction
    private bool _isPanning;
    private Vector2 _panStartMouse;
    private float _panStartT0, _panStartT1;

    // tooltip
    private int _hoverIndex = -1;
    private Vector2 _hoverMouse;

    // cached min/max
    private float _dataTMin = 0f, _dataTMax = 1f;

    [MenuItem("Tools/Haptics/Haptic Trace Debugger")]
    public static void Open()
    {
        var w = GetWindow<HapticTraceDebuggerWindow>();
        w.titleContent = new GUIContent("Haptic Trace Debugger");
        w.minSize = new Vector2(1100, 700);
        w.Show();
    }

    private void OnEnable()
    {
        // default select motor 0
        Array.Clear(_motorSel, 0, _motorSel.Length);
        _motorSel[0] = true;
    }

    private void OnGUI()
    {
        DrawTopBar();

        if (_rows.Count == 0)
        {
            EditorGUILayout.HelpBox("No data loaded. Pick a CSV file and click Load.", MessageType.Info);
            return;
        }

        // Layout
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        // range controls
        DrawRangeControls();

        // panels
        Rect rActive = GUILayoutUtility.GetRect(position.width - 20, 160);
        Rect rSum    = GUILayoutUtility.GetRect(position.width - 20, 160);
        Rect rMotor  = GUILayoutUtility.GetRect(position.width - 20, 220);

        DrawPanel(rActive, "ActiveCount (0..32)", PlotActive);
        DrawPanel(rSum,    "SumIntensity",        PlotSum);
        DrawPanel(rMotor,  "Motors (selected)",   PlotMotors);

        if (_showHeatmap)
        {
            Rect rHeat = GUILayoutUtility.GetRect(position.width - 20, 260);
            DrawPanel(rHeat, "Heatmap (time × motor index)", DrawHeatmap);
        }

        EditorGUILayout.EndScrollView();

        // tooltip overlay (outside scroll content too)
        DrawTooltipOverlay();
        HandleMouseEvents(rActive, rSum, rMotor);
    }

    // ---------------- UI ----------------
    private void DrawTopBar()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Haptic Trace Debugger (Editor Tool)", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("CSV Path", GUILayout.Width(70));

        EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(_csvPath) ? "(none)" : _csvPath,
            GUILayout.Height(18));

        if (GUILayout.Button("Pick...", GUILayout.Width(80)))
        {
            string picked = EditorUtility.OpenFilePanel("Pick haptic_trace.csv", Application.dataPath, "csv");
            if (!string.IsNullOrEmpty(picked))
            {
                _csvPath = picked;
                Repaint();
            }
        }

        if (GUILayout.Button("Load", GUILayout.Width(80)))
            LoadCsv(_csvPath);

        if (GUILayout.Button("Reload", GUILayout.Width(80)))
            LoadCsv(_csvPath);

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        _autoFitX = GUILayout.Toggle(_autoFitX, "Auto Fit X on Load", GUILayout.Width(150));
        _showHeatmap = GUILayout.Toggle(_showHeatmap, "Heatmap", GUILayout.Width(90));

        GUILayout.Space(10);
        _lockYActive = GUILayout.Toggle(_lockYActive, "Lock Active Y (0..32)", GUILayout.Width(170));
        _lockYMotor  = GUILayout.Toggle(_lockYMotor,  "Lock Motor Y (0..100)", GUILayout.Width(170));
        _lockYSum    = GUILayout.Toggle(_lockYSum,    "Lock Sum Y", GUILayout.Width(110));
        using (new EditorGUI.DisabledScope(!_lockYSum))
        {
            _sumYMax = EditorGUILayout.IntField("Sum Y Max", _sumYMax, GUILayout.Width(180));
            _sumYMax = Mathf.Max(100, _sumYMax);
        }

        GUILayout.FlexibleSpace();
        _topK = EditorGUILayout.IntSlider("TopK", _topK, 1, 12, GUILayout.Width(260));
        EditorGUILayout.EndHorizontal();

        // motor selection bar
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Motor selection", GUILayout.Width(110));

        _motorQuickPick = EditorGUILayout.IntSlider(_motorQuickPick, 0, 31, GUILayout.Width(260));
        if (GUILayout.Button("Solo", GUILayout.Width(60)))
        {
            for (int i = 0; i < 32; i++) _motorSel[i] = false;
            _motorSel[_motorQuickPick] = true;
        }
        if (GUILayout.Button("Add", GUILayout.Width(60)))
        {
            _motorSel[_motorQuickPick] = true;
        }
        if (GUILayout.Button("Clear", GUILayout.Width(60)))
        {
            for (int i = 0; i < 32; i++) _motorSel[i] = false;
        }
        if (GUILayout.Button("All", GUILayout.Width(60)))
        {
            for (int i = 0; i < 32; i++) _motorSel[i] = true;
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // motor toggle grid (compact)
        DrawMotorToggleGrid();

        EditorGUILayout.EndVertical();
    }

    private void DrawMotorToggleGrid()
    {
        const int perRow = 16;
        for (int row = 0; row < 2; row++)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(14);
            for (int i = 0; i < perRow; i++)
            {
                int idx = row * perRow + i;
                bool v = _motorSel[idx];
                bool nv = GUILayout.Toggle(v, idx.ToString(), "Button", GUILayout.Width(52));
                if (nv != v) _motorSel[idx] = nv;
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawRangeControls()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Time Window", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Data range", GUILayout.Width(80));
        EditorGUILayout.LabelField($"{_dataTMin:F3}s  →  {_dataTMax:F3}s", GUILayout.Width(220));

        GUILayout.Space(10);
        if (GUILayout.Button("Fit to Data", GUILayout.Width(110)))
        {
            _viewT0 = _dataTMin;
            _viewT1 = _dataTMax;
            Repaint();
        }

        if (GUILayout.Button("Zoom In (×2)", GUILayout.Width(110)))
            Zoom(0.5f);

        if (GUILayout.Button("Zoom Out (×2)", GUILayout.Width(110)))
            Zoom(2f);

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // sliders
        float t0 = _viewT0;
        float t1 = _viewT1;
        EditorGUILayout.MinMaxSlider(new GUIContent("View"), ref t0, ref t1, _dataTMin, _dataTMax);
        // prevent collapse
        if (t1 - t0 < 0.01f) t1 = t0 + 0.01f;
        _viewT0 = t0; _viewT1 = t1;

        EditorGUILayout.BeginHorizontal();
        _viewT0 = EditorGUILayout.FloatField("Start", _viewT0, GUILayout.Width(220));
        _viewT1 = EditorGUILayout.FloatField("End", _viewT1, GUILayout.Width(220));
        _viewT0 = Mathf.Clamp(_viewT0, _dataTMin, _dataTMax);
        _viewT1 = Mathf.Clamp(_viewT1, _dataTMin, _dataTMax);
        if (_viewT1 - _viewT0 < 0.01f) _viewT1 = Mathf.Min(_dataTMax, _viewT0 + 0.01f);

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox(
            "Mouse: Wheel = Zoom at cursor | Left-drag = Pan | Hover = Tooltip (frame + top motors).",
            MessageType.None);

        EditorGUILayout.EndVertical();
    }

    private void DrawPanel(Rect rect, string title, Action<Rect> drawPlot)
    {
        GUI.Box(rect, GUIContent.none);
        Rect inner = new Rect(rect.x + 10, rect.y + 22, rect.width - 20, rect.height - 32);

        GUI.Label(new Rect(rect.x + 10, rect.y + 4, rect.width - 20, 18), title, EditorStyles.boldLabel);

        // background
        EditorGUI.DrawRect(inner, new Color(0.08f, 0.08f, 0.08f, 1f));
        // border
        Handles.color = new Color(0.35f, 0.35f, 0.35f, 1f);
        Handles.DrawAAPolyLine(1f, new Vector3(inner.x, inner.y), new Vector3(inner.xMax, inner.y));
        Handles.DrawAAPolyLine(1f, new Vector3(inner.x, inner.yMax), new Vector3(inner.xMax, inner.yMax));
        Handles.DrawAAPolyLine(1f, new Vector3(inner.x, inner.y), new Vector3(inner.x, inner.yMax));
        Handles.DrawAAPolyLine(1f, new Vector3(inner.xMax, inner.y), new Vector3(inner.xMax, inner.yMax));

        // grid
        DrawGrid(inner, vLines: 8, hLines: 4);

        // plot
        drawPlot(inner);
    }

    private void DrawGrid(Rect r, int vLines, int hLines)
    {
        Handles.color = new Color(0.18f, 0.18f, 0.18f, 1f);
        for (int i = 1; i < vLines; i++)
        {
            float x = Mathf.Lerp(r.x, r.xMax, i / (float)vLines);
            Handles.DrawAAPolyLine(1f, new Vector3(x, r.y), new Vector3(x, r.yMax));
        }
        for (int j = 1; j < hLines; j++)
        {
            float y = Mathf.Lerp(r.y, r.yMax, j / (float)hLines);
            Handles.DrawAAPolyLine(1f, new Vector3(r.x, y), new Vector3(r.xMax, y));
        }
    }

    // ---------------- Plots ----------------
    private void PlotActive(Rect r)
    {
        var idxs = GetVisibleIndices();
        if (idxs.Count < 2) return;

        float yMin = 0f;
        float yMax = _lockYActive ? 32f : idxs.Select(i => (float)_rows[i].activeCount).Max();

        DrawLineSeries(
            r, idxs,
            xSelector: i => _rows[i].timeSec,
            ySelector: i => _rows[i].activeCount,
            _viewT0, _viewT1, yMin, Mathf.Max(yMin + 1f, yMax),
            new Color(0.2f, 0.9f, 0.6f, 1f), 2.0f);

        DrawYAxisLabels(r, yMin, yMax, 32, "active");
    }

    private void PlotSum(Rect r)
    {
        var idxs = GetVisibleIndices();
        if (idxs.Count < 2) return;

        float yMin = 0f;
        float yMax = _lockYSum ? _sumYMax : idxs.Select(i => (float)_rows[i].sumIntensity).Max();
        yMax = Mathf.Max(10f, yMax);

        DrawLineSeries(
            r, idxs,
            xSelector: i => _rows[i].timeSec,
            ySelector: i => _rows[i].sumIntensity,
            _viewT0, _viewT1, yMin, yMax,
            new Color(1.0f, 0.7f, 0.2f, 1f), 2.0f);

        DrawYAxisLabels(r, yMin, yMax, (int)yMax, "sum");
    }

    private void PlotMotors(Rect r)
    {
        var idxs = GetVisibleIndices();
        if (idxs.Count < 2) return;

        float yMin = 0f;
        float yMax = _lockYMotor ? 100f : idxs.Select(i => _rows[i].motors.Max()).Max();
        yMax = Mathf.Max(10f, yMax);

        // draw selected motors (up to 6 for readability)
        int[] selected = Enumerable.Range(0, 32).Where(i => _motorSel[i]).Take(6).ToArray();
        if (selected.Length == 0)
        {
            GUI.Label(new Rect(r.x + 8, r.y + 8, r.width - 16, 20), "No motors selected.", EditorStyles.miniLabel);
            return;
        }

        Color[] palette = new[]
        {
            new Color(0.45f, 0.70f, 1f, 1f),
            new Color(0.95f, 0.55f, 1f, 1f),
            new Color(1f, 0.55f, 0.60f, 1f),
            new Color(0.70f, 1f, 0.55f, 1f),
            new Color(1f, 0.90f, 0.45f, 1f),
            new Color(0.65f, 0.95f, 0.95f, 1f),
        };

        for (int k = 0; k < selected.Length; k++)
        {
            int m = selected[k];
            Color c = palette[k % palette.Length];

            DrawLineSeries(
                r, idxs,
                xSelector: i => _rows[i].timeSec,
                ySelector: i => _rows[i].motors[m],
                _viewT0, _viewT1, yMin, yMax,
                c, 2.0f);

            // legend
            GUI.Label(new Rect(r.x + 8 + (k * 90), r.y + 4, 88, 18), $"m{m}", EditorStyles.miniBoldLabel);
        }

        DrawYAxisLabels(r, yMin, yMax, 100, "motor");
    }

    private void DrawHeatmap(Rect r)
    {
        var idxs = GetVisibleIndices();
        if (idxs.Count < 2) return;

        // heatmap grid: time axis horizontally, motor 0..31 vertically
        // We'll sample columns to match pixel width for performance.
        int cols = Mathf.Clamp((int)r.width, 200, 1400);
        int rows = 32;

        // build a lookup from time -> nearest index
        float t0 = _viewT0, t1 = _viewT1;
        float dt = (t1 - t0) / (cols - 1);

        // draw rectangles
        for (int col = 0; col < cols; col++)
        {
            float tt = t0 + dt * col;
            int i = FindNearestIndexByTime(tt);
            if (i < 0) continue;

            for (int m = 0; m < rows; m++)
            {
                float v = _rows[i].motors[m] / 100f; // normalize
                // color: dark -> bright
                Color c = Color.Lerp(new Color(0.10f, 0.10f, 0.10f, 1f), new Color(0.95f, 0.35f, 0.15f, 1f), Mathf.Clamp01(v));
                float cellW = r.width / cols;
                float cellH = r.height / rows;

                Rect cell = new Rect(r.x + col * cellW, r.y + (rows - 1 - m) * cellH, cellW + 1, cellH + 1);
                EditorGUI.DrawRect(cell, c);
            }
        }

        // motor index labels
        GUI.Label(new Rect(r.x + 6, r.y + 4, 140, 18), "m31", EditorStyles.miniLabel);
        GUI.Label(new Rect(r.x + 6, r.yMax - 18, 140, 18), "m0", EditorStyles.miniLabel);
    }

    // ---------------- Drawing helpers ----------------
    private void DrawLineSeries(
        Rect r,
        List<int> indices,
        Func<int, float> xSelector,
        Func<int, int> ySelector,
        float xMin, float xMax,
        float yMin, float yMax,
        Color color,
        float thickness)
    {
        Handles.color = color;

        Vector3 prev = Vector3.zero;
        bool hasPrev = false;

        for (int k = 0; k < indices.Count; k++)
        {
            int i = indices[k];
            float x = xSelector(i);
            int yv = ySelector(i);

            float nx = Mathf.InverseLerp(xMin, xMax, x);
            float ny = Mathf.InverseLerp(yMin, yMax, yv);

            float px = Mathf.Lerp(r.x, r.xMax, nx);
            float py = Mathf.Lerp(r.yMax, r.y, ny);

            Vector3 p = new Vector3(px, py, 0);
            if (hasPrev)
            {
                Handles.DrawAAPolyLine(thickness, prev, p);
            }
            prev = p;
            hasPrev = true;
        }
    }

    private void DrawYAxisLabels(Rect r, float yMin, float yMax, int suggestedMax, string tag)
    {
        // small labels for sanity
        GUI.Label(new Rect(r.x + 6, r.y + 4, 220, 16), $"{tag}: {yMax:F0}", EditorStyles.miniLabel);
        GUI.Label(new Rect(r.x + 6, r.yMax - 18, 220, 16), $"{tag}: {yMin:F0}", EditorStyles.miniLabel);
    }

    // ---------------- Mouse / tooltip ----------------
    private void HandleMouseEvents(Rect rActive, Rect rSum, Rect rMotor)
    {
        Event e = Event.current;
        if (e == null) return;

        // determine if mouse is over any plot area (inner rects are inside each panel)
        // We used inner rect in DrawPanel. Here, accept the whole panel region for interaction.
        bool overPlot = rActive.Contains(e.mousePosition) || rSum.Contains(e.mousePosition) || rMotor.Contains(e.mousePosition);
        if (!overPlot) return;

        _hoverMouse = e.mousePosition;

        // Zoom with wheel
        if (e.type == EventType.ScrollWheel)
        {
            float zoomFactor = (e.delta.y > 0) ? 1.2f : 0.8f;
            ZoomAtMouse(zoomFactor);
            e.Use();
            return;
        }

        // Pan with left drag
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            _isPanning = true;
            _panStartMouse = e.mousePosition;
            _panStartT0 = _viewT0;
            _panStartT1 = _viewT1;
            e.Use();
            return;
        }

        if (e.type == EventType.MouseUp && e.button == 0)
        {
            _isPanning = false;
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDrag && _isPanning)
        {
            // drag -> shift time window
            float dx = (e.mousePosition.x - _panStartMouse.x);
            float w = position.width;
            float span = _panStartT1 - _panStartT0;
            float shift = -(dx / Mathf.Max(300f, w)) * span;

            _viewT0 = Mathf.Clamp(_panStartT0 + shift, _dataTMin, _dataTMax);
            _viewT1 = Mathf.Clamp(_panStartT1 + shift, _dataTMin, _dataTMax);
            if (_viewT1 - _viewT0 < 0.01f) _viewT1 = Mathf.Min(_dataTMax, _viewT0 + 0.01f);

            Repaint();
            e.Use();
            return;
        }

        // Hover: find nearest index by time
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
        {
            float t = MouseToTime(e.mousePosition.x);
            _hoverIndex = FindNearestIndexByTime(t);
            Repaint();
        }
    }

    private float MouseToTime(float mouseX)
    {
        // map mouse x over window width -> view time
        float nx = Mathf.InverseLerp(0f, position.width, mouseX);
        return Mathf.Lerp(_viewT0, _viewT1, nx);
    }

    private void Zoom(float factor)
    {
        float mid = (_viewT0 + _viewT1) * 0.5f;
        float span = (_viewT1 - _viewT0) * factor;
        span = Mathf.Clamp(span, 0.02f, (_dataTMax - _dataTMin));

        _viewT0 = Mathf.Clamp(mid - span * 0.5f, _dataTMin, _dataTMax);
        _viewT1 = Mathf.Clamp(mid + span * 0.5f, _dataTMin, _dataTMax);
        Repaint();
    }

    private void ZoomAtMouse(float factor)
    {
        float mouseT = MouseToTime(_hoverMouse.x);
        float span = (_viewT1 - _viewT0) * factor;
        span = Mathf.Clamp(span, 0.02f, (_dataTMax - _dataTMin));

        // keep mouseT anchored
        float leftRatio = Mathf.InverseLerp(_viewT0, _viewT1, mouseT);
        _viewT0 = mouseT - span * leftRatio;
        _viewT1 = _viewT0 + span;

        // clamp to data range
        if (_viewT0 < _dataTMin)
        {
            float d = _dataTMin - _viewT0;
            _viewT0 += d; _viewT1 += d;
        }
        if (_viewT1 > _dataTMax)
        {
            float d = _viewT1 - _dataTMax;
            _viewT0 -= d; _viewT1 -= d;
        }

        _viewT0 = Mathf.Clamp(_viewT0, _dataTMin, _dataTMax);
        _viewT1 = Mathf.Clamp(_viewT1, _dataTMin, _dataTMax);
        if (_viewT1 - _viewT0 < 0.01f) _viewT1 = Mathf.Min(_dataTMax, _viewT0 + 0.01f);

        Repaint();
    }

    private void DrawTooltipOverlay()
    {
        if (_hoverIndex < 0 || _hoverIndex >= _rows.Count) return;

        Row r = _rows[_hoverIndex];

        // topK motors
        var top = r.motors
            .Select((v, idx) => (idx, v))
            .OrderByDescending(p => p.v)
            .Take(_topK)
            .Where(p => p.v > 0)
            .ToArray();

        string topStr = (top.Length == 0)
            ? "(none)"
            : string.Join(", ", top.Select(p => $"m{p.idx}:{p.v}"));

        string text =
            $"frame={r.frame}  time={r.timeSec:F3}s  seg={r.segIndex}  t={r.t:F3}\n" +
            $"pos=({r.x:F2},{r.y:F2},{r.z:F2})  dur={r.frameDurMs}ms\n" +
            $"active={r.activeCount}  sum={r.sumIntensity}\n" +
            $"top{_topK}: {topStr}";

        Vector2 size = EditorStyles.helpBox.CalcSize(new GUIContent(text));
        float w = Mathf.Clamp(size.x + 20, 420, 760);
        float h = 72;

        Rect box = new Rect(
            Mathf.Clamp(_hoverMouse.x + 16, 10, position.width - w - 10),
            Mathf.Clamp(_hoverMouse.y + 16, 10, position.height - h - 10),
            w, h);

        EditorGUI.DrawRect(box, new Color(0f, 0f, 0f, 0.75f));
        GUI.Label(new Rect(box.x + 8, box.y + 6, box.width - 16, box.height - 12), text, EditorStyles.whiteLabel);
    }

    // ---------------- Visible indices helpers ----------------
    private List<int> GetVisibleIndices()
    {
        if (_rows.Count == 0) return new List<int>();

        // binary search to get range
        int lo = LowerBoundTime(_viewT0);
        int hi = UpperBoundTime(_viewT1);
        hi = Mathf.Clamp(hi, 0, _rows.Count);

        var list = new List<int>(Mathf.Max(0, hi - lo));
        for (int i = lo; i < hi; i++) list.Add(i);

        // downsample if too many points (perf)
        int maxPoints = 4000;
        if (list.Count > maxPoints)
        {
            int step = Mathf.CeilToInt(list.Count / (float)maxPoints);
            var ds = new List<int>(maxPoints);
            for (int k = 0; k < list.Count; k += step) ds.Add(list[k]);
            return ds;
        }
        return list;
    }

    private int FindNearestIndexByTime(float t)
    {
        if (_rows.Count == 0) return -1;
        int idx = LowerBoundTime(t);
        if (idx <= 0) return 0;
        if (idx >= _rows.Count) return _rows.Count - 1;

        float a = _rows[idx - 1].timeSec;
        float b = _rows[idx].timeSec;
        return (Mathf.Abs(t - a) <= Mathf.Abs(t - b)) ? (idx - 1) : idx;
    }

    private int LowerBoundTime(float t)
    {
        int lo = 0, hi = _rows.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_rows[mid].timeSec < t) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private int UpperBoundTime(float t)
    {
        int lo = 0, hi = _rows.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_rows[mid].timeSec <= t) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    // ---------------- Load CSV ----------------
    private void LoadCsv(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            EditorUtility.DisplayDialog("Load CSV", "Invalid path. Pick a valid CSV file.", "OK");
            return;
        }

        try
        {
            string text = File.ReadAllText(path);
            ParseCsvText(text);
            _csvPath = path;

            _dataTMin = _rows.First().timeSec;
            _dataTMax = _rows.Last().timeSec;
            if (_dataTMax <= _dataTMin) _dataTMax = _dataTMin + 0.0001f;

            if (_autoFitX)
            {
                _viewT0 = _dataTMin;
                _viewT1 = _dataTMax;
            }
            else
            {
                _viewT0 = Mathf.Clamp(_viewT0, _dataTMin, _dataTMax);
                _viewT1 = Mathf.Clamp(_viewT1, _dataTMin, _dataTMax);
            }

            Repaint();
        }
        catch (Exception e)
        {
            Debug.LogError($"[HapticTraceDebugger] Failed to load: {e.Message}");
            EditorUtility.DisplayDialog("Load CSV Error", e.Message, "OK");
        }
    }

    private void ParseCsvText(string text)
    {
        _rows.Clear();

        using (var sr = new StringReader(text))
        {
            string line;
            bool headerSeen = false;

            while ((line = sr.ReadLine()) != null)
            {
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (!headerSeen)
                {
                    headerSeen = true;
                    if (line.StartsWith("frame", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                var cols = line.Split(',');
                if (cols.Length < (10 + 32)) continue;

                if (!int.TryParse(cols[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int frame))
                    continue;

                var r = new Row();
                r.frame = frame;
                r.timeSec = float.Parse(cols[1], CultureInfo.InvariantCulture);
                r.segIndex = int.Parse(cols[2], CultureInfo.InvariantCulture);
                r.t = float.Parse(cols[3], CultureInfo.InvariantCulture);
                r.x = float.Parse(cols[4], CultureInfo.InvariantCulture);
                r.y = float.Parse(cols[5], CultureInfo.InvariantCulture);
                r.z = float.Parse(cols[6], CultureInfo.InvariantCulture);
                r.frameDurMs = int.Parse(cols[7], CultureInfo.InvariantCulture);
                r.activeCount = int.Parse(cols[8], CultureInfo.InvariantCulture);
                r.sumIntensity = int.Parse(cols[9], CultureInfo.InvariantCulture);

                r.motors = new int[32];
                for (int i = 0; i < 32; i++)
                    r.motors[i] = int.Parse(cols[10 + i], CultureInfo.InvariantCulture);

                _rows.Add(r);
            }
        }

        if (_rows.Count == 0)
            throw new Exception("Parsed 0 rows. Check CSV format/header.");
    }
}
#endif
