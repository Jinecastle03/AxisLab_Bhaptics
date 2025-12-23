using UnityEngine;

namespace AxisLabHaptics
{
    public class TraceRecorderDual : MonoBehaviour
    {
        [Header("Click Sources (UI panels)")]
        public VestClickToPoint3D frontClick;
        public VestClickToPoint3D backClick;

        [Header("Motor Layouts")]
        public MotorLayoutAsset frontLayout;
        public MotorLayoutAsset backLayout;

        [Header("Target Trace Asset (Project Asset)")]
        public HapticTraceAsset traceAsset;

        [Header("Point Defaults")]
        [Range(0f, 1f)] public float defaultIntensity = 1f;
        public float defaultSigma = 0.12f;

        [Header("Timing (Fixed Step)")]
        [Tooltip("When true, new points get time = (index * fixedDt) automatically.")]
        public bool useFixedDt = true;

        [Tooltip("Fixed dt between points (e.g., 0.5s).")]
        public float fixedDt = 0.5f;

        [Tooltip("If true, allow editing time later in Inspector (always possible). This only affects auto-assignment on record.")]
        public bool allowManualTimeEdit = true;

        [Header("Debug")]
        public bool logOnRecord = true;

        private void OnEnable()
        {
            if (frontClick != null) frontClick.OnClickPoint3D += HandleClick;
            if (backClick != null) backClick.OnClickPoint3D += HandleClick;
        }

        private void OnDisable()
        {
            if (frontClick != null) frontClick.OnClickPoint3D -= HandleClick;
            if (backClick != null) backClick.OnClickPoint3D -= HandleClick;
        }

        private void HandleClick(Vector3 p)
        {
            if (traceAsset == null || SessionSettings.Instance == null)
            {
                Debug.LogWarning("[TraceRecorderDual] traceAsset or SessionSettings missing");
                return;
            }

            // z(0/1) 기준 front/back 선택
            var s = SessionSettings.Instance;
            bool isFront = (s.frontIsZ0 ? p.z == 0f : p.z == 1f);
            var layout = isFront ? frontLayout : backLayout;

            if (layout == null)
            {
                Debug.LogWarning($"[TraceRecorderDual] Missing layout for {(isFront ? "Front" : "Back")} side.");
                return;
            }

            // 새 점의 time 부여: 고정 dt 기반
            float t = GetAutoTime();

            var motors = PatternGenerator.Generate(s, p, layout);

            var tp = new TracePoint
            {
                time = t,
                pos = p,
                baseIntensity = defaultIntensity,
                sigma = defaultSigma,
                motors = motors
            };

            traceAsset.points.Add(tp);

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(traceAsset);
#endif

            if (logOnRecord)
            {
                Debug.Log($"[TraceRecorderDual] +Point idx={traceAsset.points.Count - 1} time={t:F3} pos=({p.x:F3},{p.y:F3},{p.z:F0})");
            }
        }

        private float GetAutoTime()
        {
            if (!useFixedDt) return 0f;

            int idx = (traceAsset != null) ? traceAsset.points.Count : 0;
            return idx * fixedDt;
        }

        public void ClearTrace()
        {
            if (traceAsset == null) return;
            traceAsset.points.Clear();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(traceAsset);
#endif
            Debug.Log("[TraceRecorderDual] Cleared trace");
        }

        public void UndoLast()
        {
            if (traceAsset == null || traceAsset.points.Count == 0) return;
            traceAsset.points.RemoveAt(traceAsset.points.Count - 1);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(traceAsset);
#endif
            Debug.Log("[TraceRecorderDual] Undo last");
        }

        /// <summary>
        /// (옵션) 현재 points 순서대로 time을 0,dt,2dt...로 재할당
        /// </summary>
        public void RebuildTimesFixedDt()
        {
            if (traceAsset == null) return;
            for (int i = 0; i < traceAsset.points.Count; i++)
                traceAsset.points[i].time = i * fixedDt;

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(traceAsset);
#endif
            Debug.Log("[TraceRecorderDual] Rebuilt times with fixed dt");
        }
    }
}
