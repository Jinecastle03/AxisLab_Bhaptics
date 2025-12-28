using UnityEngine;

namespace AxisLabHaptics
{
    public class TracePlayer : MonoBehaviour
    {
        [Header("Data")]
        public HapticTraceAsset trace;
        public MotorLayoutAsset frontLayout;
        public MotorLayoutAsset backLayout;

        [Header("Output")]
        public BhapticsMotorOutput bhapticsOutput;
        public bool useDebugOutput = false;

        [Header("Playback")]
        public bool loop = false;
        [Range(0.1f, 4f)] public float speed = 1f;

        [Tooltip("How often to submit a frame (seconds). 0 = every Update.")]
        [Range(0f, 0.05f)] public float frameInterval = 0.016f; // 60fps

        private IHapticOutput output;
        private bool playing;
        private float t0;
        private int seg;              // current segment index
        private float nextFrameTime;  // throttling

        private void Awake()
        {
            if (!useDebugOutput && bhapticsOutput != null) output = bhapticsOutput;
            else output = new DebugHapticOutput();
        }

        public void Play()
        {
            if (trace == null || trace.points == null || trace.points.Count < 1)
            {
                Debug.LogWarning("[TracePlayer] No trace points.");
                return;
            }

            // time 정렬 보장(안 돼 있으면 문제 생김)
            trace.points.Sort((a, b) => a.time.CompareTo(b.time));

            playing = true;
            t0 = Time.time;
            seg = 0;
            nextFrameTime = 0f;

            Debug.Log($"[TracePlayer] Play. points={trace.points.Count}");
        }

        public void Stop()
        {
            playing = false;
            output?.StopAll();
            Debug.Log("[TracePlayer] Stop.");
        }

        private void Update()
        {
            if (!playing || trace == null || trace.points == null || trace.points.Count == 0) return;

            if (frameInterval > 0f && Time.time < nextFrameTime) return;
            nextFrameTime = Time.time + frameInterval;

            float t = (Time.time - t0) * speed;

            float endT = trace.points[trace.points.Count - 1].time;
            if (t > endT)
            {
                if (loop)
                {
                    t0 = Time.time;
                    t = 0f;
                    seg = 0;
                }
                else
                {
                    Stop();
                    return;
                }
            }

            // segment 찾기: points[seg] <= t <= points[seg+1]
            while (seg < trace.points.Count - 2 && trace.points[seg + 1].time < t)
                seg++;

            TracePoint p0 = trace.points[seg];
            TracePoint p1 = trace.points[Mathf.Min(seg + 1, trace.points.Count - 1)];

            float dt = Mathf.Max(1e-6f, (p1.time - p0.time));
            float u = Mathf.Clamp01((t - p0.time) / dt);

            // ✅ 시간 기반 보간: 위치/강도/시그마
            Vector3 pos = Vector3.Lerp(p0.pos, p1.pos, u);
            float baseI = Mathf.Lerp(p0.baseIntensity, p1.baseIntensity, u);
            float sigma = Mathf.Lerp(p0.sigma, p1.sigma, u);

            // profile(감각 맵/감마 등) 적용 (있으면)
            var s = SessionSettings.Instance;
            float intensity01 = (s != null) ? HapticProfile.Evaluate(s, baseI, 1f) : baseI;
            intensity01 = Mathf.Clamp01(intensity01);

            SubmitMixedFrame(pos, sigma, intensity01);
        }

        private void SubmitMixedFrame(Vector3 pos, float sigma, float intensity01)
        {
            var s = SessionSettings.Instance;

            // z -> front/back weight
            float z = Mathf.Clamp01(pos.z);

            float wFront, wBack;
            if (s != null && s.frontIsZ0)
            {
                wFront = 1f - z;
                wBack = z;
            }
            else
            {
                // frontIsZ0=false면 반대
                wFront = z;
                wBack = 1f - z;
            }

            // front/back 각각 패턴 생성
            MotorWeight[] front = (frontLayout != null) ? PatternGenerator.Generate(s, pos, frontLayout, sigma) : System.Array.Empty<MotorWeight>();
            MotorWeight[] back  = (backLayout  != null) ? PatternGenerator.Generate(s, pos, backLayout,  sigma) : System.Array.Empty<MotorWeight>();

            // ✅ 합치기 (가중치 적용)
            var mixed = MixWeights(front, wFront, back, wBack, frontLayout, backLayout);

            output.SubmitFrame(mixed, intensity01);
        }

        /// <summary>
        /// front/back weight를 섞어 MotorWeight[] 하나로 만든다.
        /// backLayout이 0..15만 쓰는 경우를 대비해 16 오프셋 자동 적용.
        /// </summary>
        private MotorWeight[] MixWeights(
            MotorWeight[] front, float wFront,
            MotorWeight[] back,  float wBack,
            MotorLayoutAsset fLayout, MotorLayoutAsset bLayout)
        {
            // back index가 0..15로 들어오는 경우 자동 +16
            int backOffset = GuessBackOffset(bLayout);

            // 합칠 때는 간단히 같은 index는 더해줌
            System.Collections.Generic.Dictionary<int, float> map = new System.Collections.Generic.Dictionary<int, float>(64);

            if (front != null)
            {
                for (int i = 0; i < front.Length; i++)
                {
                    int idx = front[i].index;
                    float w = front[i].weight * wFront;
                    if (idx < 0) continue;
                    map[idx] = map.TryGetValue(idx, out float old) ? (old + w) : w;
                }
            }

            if (back != null)
            {
                for (int i = 0; i < back.Length; i++)
                {
                    int idx = back[i].index + backOffset;
                    float w = back[i].weight * wBack;
                    if (idx < 0) continue;
                    map[idx] = map.TryGetValue(idx, out float old) ? (old + w) : w;
                }
            }

            // 정규화(합이 1이 되도록) — bHaptics 출력이 안정적
            float sum = 0f;
            foreach (var kv in map) sum += Mathf.Max(0f, kv.Value);
            if (sum <= 1e-6f) sum = 1f;

            var outW = new MotorWeight[map.Count];
            int k = 0;
            foreach (var kv in map)
            {
                outW[k++] = new MotorWeight { index = kv.Key, weight = Mathf.Clamp01(kv.Value / sum) };
            }

            return outW;
        }

        /// <summary>
        /// backLayout이 인덱스 0..15만 사용하는 스타일이면 +16 해준다.
        /// 이미 16..31 형태면 0.
        /// </summary>
        private int GuessBackOffset(MotorLayoutAsset backLayout)
        {
            if (backLayout == null || backLayout.motorPos == null) return 0;

            // backLayout 모터 개수가 16이면 대부분 0..15로 쓰는 경우가 많음
            // 하지만 이미 16..31로 맵핑했을 수도 있으니, "index 범위"는 MotorWeight에서만 알 수 있음.
            // 여기서는 보수적으로: backLayout.Count==16이면 +16 적용
            if (backLayout.Count == 16) return 16;

            return 0;
        }
    }
}
