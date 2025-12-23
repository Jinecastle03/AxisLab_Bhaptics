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
        public bool loop = false;
        [Range(0.1f, 4f)] public float speed = 1f;

        [Tooltip("Each point is a single-shot pulse with this duration (seconds).")]
        public float pulseDuration = 0.12f;

        [Tooltip("Sort points by time before play.")]
        public bool sortByTimeOnPlay = true;

        [Header("Output")]
        public bool useDebugOutput = true;
        public BhapticsOutput bhapticsOutput; // ✅ 실제 장비 출력용(아래에서 만들 코드)

        private IHapticOutput output;
        private float t0;
        private bool playing;
        private int lastFiredIndex = -1;

        private void Awake()
        {
            // Output 선택
            if (!useDebugOutput && bhapticsOutput != null) output = bhapticsOutput;
            else output = new DebugHapticOutput();
        }

        public void Play()
        {
            if (trace == null || trace.points == null || trace.points.Count == 0)
            {
                Debug.LogWarning("[TracePlayer] No trace points.");
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
            lastFiredIndex = -1;

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

            // 다음에 쏠 index 찾기: lastFiredIndex+1부터 time이 t를 넘지 않는 가장 마지막
            int nextIndex = lastFiredIndex + 1;
            if (nextIndex >= trace.points.Count)
            {
                // 끝 처리
                if (loop)
                {
                    t0 = Time.time;
                    lastFiredIndex = -1;
                }
                else
                {
                    Stop();
                }
                return;
            }

            // 아직 다음 점 시간이 안 됐으면 대기
            if (t < trace.points[nextIndex].time) return;

            // ✅ 점 1개 "단발" 실행
            FirePoint(nextIndex);
            lastFiredIndex = nextIndex;
        }

        private void FirePoint(int index)
        {
            var s = SessionSettings.Instance;
            var p = trace.points[index];

            // intensity: baseIntensity에 profile(Envelope/Perceptual)을 한 번 적용
            // 단발이므로 envX=1로 넣음(최대치)
            float intensity = HapticProfile.Evaluate(s, p.baseIntensity, 1f);

            // front/back 선택: z=0/1 규칙
            bool isFront = (s.frontIsZ0 ? p.pos.z == 0f : p.pos.z == 1f);
            var layout = isFront ? frontLayout : backLayout;
            if (layout == null) return;

            // 패턴 생성 (Gaussian/Tactile)
            var motors = PatternGenerator.Generate(s, p.pos, layout);

            // 출력
            output.SubmitFrame(motors, intensity);

            Debug.Log($"[TracePlayer] Fired #{index} t={p.time:F3} pos=({p.pos.x:F3},{p.pos.y:F3},{p.pos.z:F0}) intensity={intensity:F3}");
        }
    }
}
