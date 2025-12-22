using UnityEngine;

namespace AxisLabHaptics
{
    public enum StrokeType { Gaussian, TactileBrush }
    public enum EnvelopeType { Lerp, SmoothStep, ExpIn, ExpOut, Curve }
    public enum PerceptualMapType { None, ThresholdGamma, Logistic }

    public class SessionSettings : MonoBehaviour
    {
        public static SessionSettings Instance { get; private set; }

        [Header("Profile Selection")]
        public StrokeType strokeType = StrokeType.Gaussian;
        public EnvelopeType envelopeType = EnvelopeType.Lerp;
        public PerceptualMapType perceptualMapType = PerceptualMapType.None;

        [Header("Perceptual Params")]
        [Range(0f, 1f)] public float threshold = 0.2f;
        public float gamma = 2.0f;
        public float logisticK = 10.0f;

        [Header("Envelope Curve (when EnvelopeType=Curve)")]
        public AnimationCurve envelopeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Grid Coordinate System (SAVE FORMAT)")]
        [Tooltip("x range becomes 0..(cols-1), y range becomes 0..(rows-1)")]
        [Range(1, 16)] public int rows = 4;
        [Range(1, 16)] public int cols = 4;

        [Tooltip("If true, save x,y as integer grid indices (0..3). If false, save continuous (0..3 float).")]
        public bool snapXYToGrid = true;

        [Header("Z Convention")]
        [Tooltip("If true: Front z=0, Back z=1. If false: Front z=1, Back z=0.")]
        public bool frontIsZ0 = true;

        [Header("Motor Mapping")]
        [Range(1, 4)] public int blendK = 1;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;

            var existing = Object.FindFirstObjectByType<SessionSettings>();
            if (existing != null)
            {
                Instance = existing;
                Object.DontDestroyOnLoad(existing.gameObject);
                return;
            }

            var go = new GameObject("SessionSettings (Auto)");
            go.AddComponent<SessionSettings>();
            Object.DontDestroyOnLoad(go);
        }
    }
}
