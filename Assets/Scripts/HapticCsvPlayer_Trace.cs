using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Bhaptics.SDK2;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// CSV 경로를 TACTILE 방식으로 재생하면서,
/// 프레임별 모터 값(32), activeCount, sumIntensity 를 CSV로 저장하는 추적용 플레이어.
/// 
/// - 기존 코드와 독립적 (새 컴포넌트)
/// - Mapper는 MonoBehaviour로 받아 Linear / Gaussian 자유 교체
/// </summary>
public class HapticCsvPlayer_Trace : MonoBehaviour
{
    // =========================
    // Input
    // =========================
    [Header("CSV Input")]
    public TextAsset csvFile;

    [Header("Mapper (Drag Here)")]
    [Tooltip("VestCoordinateMapper 또는 VestCoordinateMapper_Gaussian 컴포넌트를 가진 오브젝트")]
    public MonoBehaviour mapperBehaviour;

    // =========================
    // Playback
    // =========================
    [Header("Playback")]
    public PositionType deviceType = PositionType.Vest;

    [Tooltip("CSV durationMs가 너무 짧을 경우 하한(ms). CSV 그대로 쓰려면 0~20 권장")]
    public int minDurationMs = 20;

    public bool stopPreviousOnReplay = true;

    // =========================
    // Tactile Update
    // =========================
    [Header("Tactile Update")]
    [Tooltip("프레임 간격(ms). 10~20 권장")]
    public float sampleIntervalMs = 15f;

    [Tooltip("PlayMotors duration(ms). 보통 sampleIntervalMs 이하")]
    public int frameDurationMs = 20;

    public bool useSmoothStep = true;

    // =========================
    // Trace Export
    // =========================
    public enum TraceSaveLocation
    {
        AssetsData,            // Assets/data (Editor only)
        PersistentDataPath     // 빌드 안전
    }

    [Header("Trace Export")]
    public bool traceToCsv = true;
    public TraceSaveLocation traceSaveLocation = TraceSaveLocation.AssetsData;
    public string traceFileName = "haptic_trace.csv";
    public int traceWriteEveryNFrames = 1;

    // =========================
    // Live Metrics
    // =========================
    [Header("Live Metrics")]
    public bool showLiveMetrics = true;

    // =========================
    // Internal
    // =========================
    private Func<float, float, float, float, int[]> _mapPointToMotors;
    private Coroutine _routine;

    private StringBuilder _sb;
    private string _tracePath;
    private int _frameIndex;
    private float _globalTime;

    private int _latestActive;
    private int _latestSum;
    private string _status = "";

    [Serializable]
    private class HapticPoint
    {
        public float x, y, z;
        public int durationMs;
        public float intensity;
    }

    // =========================
    // Unity
    // =========================
    private void Awake()
    {
        BindMapper();
    }

    // =========================
    // Mapper Binding
    // =========================
    private void BindMapper()
    {
        _mapPointToMotors = null;

        if (mapperBehaviour == null)
        {
            Debug.LogError("[HapticCsvPlayer_Trace] mapperBehaviour is null.");
            return;
        }

        var method = mapperBehaviour.GetType().GetMethod(
            "MapPointToMotors",
            new Type[] { typeof(float), typeof(float), typeof(float), typeof(float) }
        );

        if (method == null)
        {
            Debug.LogError($"[HapticCsvPlayer_Trace] {mapperBehaviour.GetType().Name} does not implement MapPointToMotors(float,float,float,float)");
            return;
        }

        _mapPointToMotors = (x, y, z, intensity) =>
            (int[])method.Invoke(mapperBehaviour, new object[] { x, y, z, intensity });

        Debug.Log($"[HapticCsvPlayer_Trace] Mapper bound: {mapperBehaviour.GetType().Name}");
    }

    // =========================
    // Public Entry
    // =========================
    [ContextMenu("Play CSV Once (Trace)")]
    public void PlayCsvOnce()
    {
        if (csvFile == null)
        {
            Debug.LogError("[HapticCsvPlayer_Trace] csvFile is null.");
            return;
        }

        if (_mapPointToMotors == null)
        {
            BindMapper();
            if (_mapPointToMotors == null) return;
        }

        var points = ParseCsv(csvFile.text);
        if (points.Count < 2)
        {
            Debug.LogWarning("[HapticCsvPlayer_Trace] Need at least 2 valid points.");
            return;
        }

        if (stopPreviousOnReplay)
            BhapticsLibrary.StopAll();

        if (_routine != null)
            StopCoroutine(_routine);

        _routine = StartCoroutine(PlayRoutine(points));
    }

    // =========================
    // Core Routine
    // =========================
    private IEnumerator PlayRoutine(List<HapticPoint> points)
    {
        TraceBegin();
        _globalTime = 0f;
        _frameIndex = 0;

        int posType = (int)deviceType;
        float intervalSec = Mathf.Max(sampleIntervalMs, 5f) / 1000f;
        int perFrameDurMs = Mathf.Max(frameDurationMs, 10);

        _status = $"Playing | interval={sampleIntervalMs}ms frameDur={perFrameDurMs}ms";

        for (int i = 0; i < points.Count - 1; i++)
        {
            var p0 = points[i];
            var p1 = points[i + 1];

            int segMs = Mathf.Max(p0.durationMs, minDurationMs);
            float segSec = segMs / 1000f;

            float elapsed = 0f;
            while (elapsed < segSec)
            {
                float t = Mathf.Clamp01(elapsed / segSec);
                if (useSmoothStep)
                    t = t * t * (3f - 2f * t);

                float x = Mathf.Lerp(p0.x, p1.x, t);
                float y = Mathf.Lerp(p0.y, p1.y, t);
                float z = Mathf.Lerp(p0.z, p1.z, t);
                float intensity = Mathf.Lerp(p0.intensity, p1.intensity, t);

                int[] motors = _mapPointToMotors(x, y, z, intensity);
                ComputeMetrics(motors, out _latestActive, out _latestSum);

                TraceAppend(motors, _globalTime, i, t, x, y, z, perFrameDurMs, _latestActive, _latestSum);

                BhapticsLibrary.PlayMotors(posType, motors, perFrameDurMs);

                yield return new WaitForSeconds(intervalSec);
                elapsed += intervalSec;
                _globalTime += intervalSec;
                _frameIndex++;
            }
        }

        BhapticsLibrary.StopAll();
        _status = "Finished (StopAll called)";
        TraceEnd();
        _routine = null;
    }

    // =========================
    // Metrics
    // =========================
    private static void ComputeMetrics(int[] motors, out int active, out int sum)
    {
        active = 0;
        sum = 0;
        for (int i = 0; i < 32; i++)
        {
            int v = motors[i];
            if (v > 0) active++;
            sum += v;
        }
    }

    // =========================
    // Trace CSV
    // =========================
    private void TraceBegin()
    {
        if (!traceToCsv)
        {
            _sb = null;
            return;
        }

        _sb = new StringBuilder(1024 * 64);

#if UNITY_EDITOR
        if (traceSaveLocation == TraceSaveLocation.AssetsData)
        {
            string dir = Path.Combine(Application.dataPath, "data");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            _tracePath = Path.Combine(dir, traceFileName);
        }
        else
        {
            _tracePath = Path.Combine(Application.persistentDataPath, traceFileName);
        }
#else
        _tracePath = Path.Combine(Application.persistentDataPath, traceFileName);
#endif

        _sb.Append("frame,timeSec,segIndex,t,x,y,z,frameDurMs,activeCount,sumIntensity");
        for (int i = 0; i < 32; i++) _sb.Append($",m{i}");
        _sb.AppendLine();
    }

    private void TraceAppend(
        int[] motors,
        float timeSec,
        int segIndex,
        float t,
        float x, float y, float z,
        int frameDurMs,
        int activeCount,
        int sumIntensity)
    {
        if (!traceToCsv || _sb == null) return;
        if (traceWriteEveryNFrames > 1 && (_frameIndex % traceWriteEveryNFrames != 0)) return;

        _sb.Append(_frameIndex).Append(',')
           .Append(timeSec.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
           .Append(segIndex).Append(',')
           .Append(t.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
           .Append(x.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
           .Append(y.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
           .Append(z.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
           .Append(frameDurMs).Append(',')
           .Append(activeCount).Append(',')
           .Append(sumIntensity);

        for (int i = 0; i < 32; i++)
            _sb.Append(',').Append(motors[i]);

        _sb.AppendLine();
    }

    private void TraceEnd()
    {
        if (!traceToCsv || _sb == null) return;

        File.WriteAllText(_tracePath, _sb.ToString());
        Debug.Log($"[HAPTIC TRACE] Saved: {_tracePath}");

#if UNITY_EDITOR
        if (traceSaveLocation == TraceSaveLocation.AssetsData)
            AssetDatabase.Refresh();
#endif

        _sb = null;
    }

    // =========================
    // CSV Parser
    // =========================
    private List<HapticPoint> ParseCsv(string csv)
    {
        var list = new List<HapticPoint>();
        using (var sr = new StringReader(csv))
        {
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (!IsNumericLine(line)) continue;

                var raw = line.Split(',');
                if (raw.Length < 5) continue;

                try
                {
                    list.Add(new HapticPoint
                    {
                        x = float.Parse(raw[0], CultureInfo.InvariantCulture),
                        y = float.Parse(raw[1], CultureInfo.InvariantCulture),
                        z = float.Parse(raw[2], CultureInfo.InvariantCulture),
                        durationMs = int.Parse(raw[3], CultureInfo.InvariantCulture),
                        intensity = float.Parse(raw[4], CultureInfo.InvariantCulture)
                    });
                }
                catch { }
            }
        }
        return list;
    }

    private static bool IsNumericLine(string line)
    {
        var first = line.Split(',')[0];
        return float.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    // =========================
    // UI
    // =========================
    private void OnGUI()
    {
        if (!showLiveMetrics) return;

        GUI.Label(new Rect(20, 20, 600, 24), $"Active Motors: {_latestActive}/32");
        GUI.Label(new Rect(20, 44, 600, 24), $"Sum Intensity: {_latestSum}");
        GUI.Label(new Rect(20, 68, 1200, 24), _status);
        if (traceToCsv)
            GUI.Label(new Rect(20, 92, 1200, 24), $"Trace: {_tracePath}");
    }
}
