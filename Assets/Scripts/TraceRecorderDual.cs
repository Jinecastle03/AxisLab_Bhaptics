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

        [Header("Recording Defaults")]
        [Range(0f, 1f)] public float defaultIntensity = 1f;
        [Range(0.01f, 2f)] public float defaultSigma = 0.12f;

        [Header("Time Assignment")]
        [Tooltip("Auto time step between recorded points (e.g., 0.5s).")]
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

        // ✅ NEW: Cube/외부 입력에서도 기록할 수 있는 public API
        public void RecordPoint(Vector3 p)
        {
            HandleClick(p);
        }

        // 기존 UI 클릭 처리 로직 (내부에서 공통 사용)
        private void HandleClick(Vector3 p)
        {
            if (traceAsset == null || SessionSettings.Instance == null)
            {
                Debug.LogWarning("[TraceRecorderDual] traceAsset or SessionSettings missing");
                return;
            }

            var s = SessionSettings.Instance;

            // ✅ z=0/1뿐 아니라 연속 z도 허용: 0.5 기준으로 면 판정
            bool isFront;
            if (s.frontIsZ0)
                isFront = (p.z <= 0.5f);   // z=0 쪽이 front
            else
                isFront = (p.z > 0.5f);    // z=1 쪽이 front

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
                Debug.Log($"[TraceRecorderDual] Recorded t={t:F3} pos=({p.x:F3},{p.y:F3},{p.z:F3}) side={(isFront ? "Front" : "Back")} points={traceAsset.points.Count}");
            }
        }

        private float GetAutoTime()
        {
            if (traceAsset == null) return 0f;
            int n = traceAsset.points.Count;
            return n * fixedDt;
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
