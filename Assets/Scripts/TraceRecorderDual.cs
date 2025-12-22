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

        [Header("Time Handling")]
        public bool autoTime = true;
        public float dt = 0.05f;
        public float currentTime = 0f;

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

            bool isFront = p.z >= 0f;
            var layout = isFront ? frontLayout : backLayout;
            if (layout == null)
            {
                Debug.LogWarning($"[TraceRecorderDual] Missing layout for {(isFront ? "Front" : "Back")} side.");
                return;
            }

            int k = SessionSettings.Instance.blendK;
            var motors = MotorMapper.MapPointToMotors(p, layout, k);

            float t = GetNextTime();

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
                string motorStr = "";
                if (motors != null)
                {
                    for (int i = 0; i < motors.Length; i++)
                        motorStr += (i == 0 ? "" : " | ") + $"{motors[i].index}:{motors[i].weight:F3}";
                }

                Debug.Log($"[TraceRecorderDual] RECORDED t={t:F3} pos=({p.x:F3},{p.y:F3},{p.z:F3}) motors=[{motorStr}] totalPoints={traceAsset.points.Count}");
            }
        }

        private float GetNextTime()
        {
            if (!autoTime) return currentTime;
            int n = traceAsset.points.Count;
            return (n == 0) ? 0f : traceAsset.points[n - 1].time + dt;
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
    }
}
