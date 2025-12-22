using UnityEngine;

namespace AxisLabHaptics
{
    public class TraceRecorder : MonoBehaviour
    {
        [Header("Wiring")]
        public VestClickToPoint3D clickSource;
        public MotorLayoutAsset frontLayout;
        public MotorLayoutAsset backLayout;

        [Header("Target Trace Asset (in Project)")]
        public HapticTraceAsset traceAsset;

        [Header("Point Defaults")]
        [Range(0f, 1f)] public float defaultIntensity = 1f;
        public float defaultSigma = 0.12f;

        [Header("Time Handling")]
        [Tooltip("If true, auto-append time using (lastTime + dt). Otherwise use currentTime field.")]
        public bool autoTime = true;

        public float dt = 0.05f;
        public float currentTime = 0f;

        private void OnEnable()
        {
            if (clickSource != null)
                clickSource.OnClickPoint3D += HandleClick;
        }

        private void OnDisable()
        {
            if (clickSource != null)
                clickSource.OnClickPoint3D -= HandleClick;
        }

        private void HandleClick(Vector3 p)
        {
            if (traceAsset == null || SessionSettings.Instance == null) return;

            bool isFront = p.z >= 0f;
            MotorLayoutAsset layout = isFront ? frontLayout : backLayout;
            if (layout == null)
            {
                Debug.LogWarning("Missing MotorLayoutAsset for this side.");
                return;
            }

            int k = SessionSettings.Instance.blendK;
            MotorWeight[] motors = MotorMapper.MapPointToMotors(p, layout, k);

            var tp = new TracePoint
            {
                time = GetNextTime(),
                pos = p,
                baseIntensity = defaultIntensity,
                sigma = defaultSigma,
                motors = motors
            };

            traceAsset.points.Add(tp);

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(traceAsset);
#endif
        }

        private float GetNextTime()
        {
            if (!autoTime) return currentTime;

            float t = 0f;
            int n = traceAsset.points.Count;
            if (n > 0) t = traceAsset.points[n - 1].time + dt;
            return t;
        }

        public void ClearTrace()
        {
            if (traceAsset == null) return;
            traceAsset.points.Clear();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(traceAsset);
#endif
        }

        public void UndoLast()
        {
            if (traceAsset == null || traceAsset.points.Count == 0) return;
            traceAsset.points.RemoveAt(traceAsset.points.Count - 1);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(traceAsset);
#endif
        }
    }
}
