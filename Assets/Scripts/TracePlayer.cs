using UnityEngine;

namespace AxisLabHaptics
{
    public class TracePlayer : MonoBehaviour
    {
        [Header("Data")]
        public HapticTraceAsset trace;
        public MotorLayoutAsset frontLayout;
        public MotorLayoutAsset backLayout;

        [Header("Playback")]
        public bool playOnStart = false;
        public bool loop = true;
        [Range(0.1f, 4f)] public float speed = 1f;

        [Tooltip("Each point becomes a pulse with this duration (seconds).")]
        public float pulseDuration = 0.12f;

        [Tooltip("If true, sort points by time before play.")]
        public bool sortByTimeOnPlay = true;

        [Header("Debug Output")]
        public bool useDebugOutput = true;

        private IHapticOutput output;
        private float t0;
        private bool playing;

        private void Awake()
        {
            output = useDebugOutput ? new DebugHapticOutput() : new DebugHapticOutput();
        }

        private void Start()
        {
            if (playOnStart) Play();
        }

        public void Play()
        {
            if (trace == null || trace.points == null || trace.points.Count == 0)
            {
                Debug.LogWarning("[TracePlayer] No trace points to play.");
                return;
            }

            if (SessionSettings.Instance == null)
            {
                Debug.LogWarning("[TracePlayer] SessionSettings missing.");
                return;
            }

            if (sortByTimeOnPlay)
                trace.points.Sort((a, b) => a.time.CompareTo(b.time));

            t0 = Time.time;
            playing = true;
            Debug.Log("[TracePlayer] Play");
        }

        public void Stop()
        {
            playing = false;
            output?.StopAll();
            Debug.Log("[TracePlayer] Stop");
        }

        private void Update()
        {
            if (!playing || trace == null || trace.points.Count == 0) return;

            float t = (Time.time - t0) * speed;

            // trace 끝
            float tEnd = trace.points[trace.points.Count - 1].time + pulseDuration;
            if (t > tEnd)
            {
                if (loop) { t0 = Time.time; return; }
                Stop();
                return;
            }

            // 현재 시간에 활성화되는 point 하나 찾기(간단 버전: 가장 가까운 시간대)
            // 실제로는 "동시에 여러 점"도 가능하게 확장 가능.
            TracePoint active = null;
            float best = float.MaxValue;

            for (int i = 0; i < trace.points.Count; i++)
            {
                float dt = t - trace.points[i].time;
                if (dt < 0f || dt > pulseDuration) continue;
                if (dt < best)
                {
                    best = dt;
                    active = trace.points[i];
                }
            }

            if (active == null) return;

            // 0..1 envelope x
            float envX = 1f - (best / Mathf.Max(1e-6f, pulseDuration));

            // 최종 강도 (Envelope + Perceptual)
            var s = SessionSettings.Instance;
            float intensity = HapticProfile.Evaluate(s, active.baseIntensity, envX);

            // front/back z에 따라 레이아웃 선택 (z가 0/1)
            bool isFront = (s.frontIsZ0 ? active.pos.z == 0f : active.pos.z == 1f);
            var layout = isFront ? frontLayout : backLayout;
            if (layout == null) return;

            // 패턴 생성 (Gaussian/Tactile) → 모터 가중치
            var motors = PatternGenerator.Generate(s, active.pos, layout);

            // 출력
            output.SubmitFrame(motors, intensity);
        }
    }
}
