using UnityEngine;

namespace AxisLabHaptics
{
    public enum StrokeType { Gaussian, TactileBrush }
    public enum EnvelopeType { Lerp, SmoothStep, ExpIn, ExpOut, Curve }
    public enum PerceptualMapType { None, ThresholdGamma, Logistic }
    public enum PathBlendMode { Linear, Gaussian }

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
        [Range(1, 16)] public int rows = 4;
        [Range(1, 16)] public int cols = 4;
        public bool snapXYToGrid = true;

        [Header("Z Convention")]
        [Tooltip("If true: Front z=0, Back z=1. If false: Front z=1, Back z=0.")]
        public bool frontIsZ0 = false;

        [Header("Motor Mapping")]
        [Range(1, 4)] public int blendK = 1;

        [Header("PlayPath Precompute (TracePlayer_PlayPathPrecompute)")]
        public PathBlendMode pathBlendMode = PathBlendMode.Linear;
        [Tooltip("Gaussian 블렌딩 선택 시 시간 축 sigma(초). 0.1~0.4 권장")]
        [Range(0.01f, 1f)] public float pathGaussianSigma = 0.2f;

        // =========================
        // ✅ 연결감(Continuous stroke) 핵심 파라미터
        // =========================
        [Header("Continuous Playback (Connected Feel)")]
        [Tooltip("점 사이를 이 주기(ms)로 샘플링해서 연속으로 재생. 10~20 권장")]
        [Range(5f, 50f)] public float sampleIntervalMs = 15f;

        [Tooltip("PlayMotors duration(ms). sampleIntervalMs보다 같거나 조금 크게(겹치게) 15~30 권장")]
        [Range(10, 80)] public int frameDurationMs = 20;

        [Tooltip("재생 속도 배율. 1=원래 trace 시간")]
        [Range(0.1f, 5f)] public float playSpeed = 1f;

        [Tooltip("세그먼트(점-점) 보간 시 SmoothStep 적용(연결감↑)")]
        public bool useSmoothStepOnSegments = true;

        [Tooltip("전체 강도 게인(0.0~2.0). 연결감 약하면 1.1~1.3 추천")]
        [Range(0f, 2f)] public float globalIntensityGain = 1.0f;

        [Header("Temporal Smoothing (per-motor)")]
        [Tooltip("모터 값(0..100)을 시간적으로 부드럽게(EMA). 0.04~0.12 추천. 0이면 끔")]
        [Range(0f, 0.3f)] public float smoothingTau = 0.08f;

        [Tooltip("노이즈 제거용 최소 컷(0..100). 2~6 추천")]
        [Range(0, 20)] public int noiseGate = 2;

        [Tooltip("프레임당 변화량 제한(갑자기 튀는 느낌 억제). 0이면 끔. 8~15 추천")]
        [Range(0, 50)] public int maxDeltaPerFrame = 12;

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
