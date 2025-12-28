using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Bhaptics.SDK2;

namespace AxisLabHaptics
{
    /// <summary>
    /// FINAL: Stamped Brush Streaming (works even if PlayPath doesn't animate over time)
    /// - Every dt, send a "brush stamp" using PlayPath with multiple points at once.
    /// - durationMs > dtMs so stamps overlap => continuous stroke 느낌.
    /// - z:
    ///   - near 0: front only
    ///   - near 1: back only
    ///   - middle: front+back blended + seam pull
    /// </summary>
    public class TracePlayer_PlayPathPrecompute : MonoBehaviour
    {
        [Header("Trace")]
        public HapticTraceAsset trace;

        [Header("Grid Space (trace x,y)")]
        public int gridW = 4;
        public int gridH = 4;
        public bool flipY = true;

        [Header("Playback")]
        [Range(0.1f, 5f)] public float speed = 1f;

        [Tooltip("Resample dt (sec). 0.008~0.012 추천")]
        [Range(0.003f, 0.03f)] public float sampleDt = 0.01f;

        [Header("Stamp Timing")]
        [Tooltip("Stamp interval in ms. 보통 sampleDt*1000과 같게 둠")]
        [Range(5, 30)] public int dtMs = 10;

        [Tooltip("Each stamp lasts this long. dtMs보다 크게(50~90ms) => overlap으로 부드러움")]
        [Range(20, 160)] public int stampDurationMs = 70;

        [Header("Depth Split")]
        [Range(0f, 0.5f)] public float zFrontThreshold = 0.15f;
        [Range(0.5f, 1f)] public float zBackThreshold = 0.85f;

        [Header("TactSuit coordinate X ranges")]
        public Vector2 frontXRange = new Vector2(0.00f, 0.40f);
        public Vector2 backXRange  = new Vector2(0.55f, 0.95f);
        public bool flipBackX = true;

        [Header("Seam / leak control")]
        [Range(0f, 0.25f)] public float seamMargin = 0.14f;

        [Header("Gaussian Brush (stamp)")]
        [Range(0.01f, 0.15f)] public float radius01 = 0.06f;
        [Range(0.1f, 1f)] public float sigmaRatio = 0.55f;
        [Range(5, 13)] public int brushSamples = 9;
        [Range(0f, 0.2f)] public float weightCutoff = 0.02f;

        [Header("Middle wrap feeling")]
        [Range(0f, 1f)] public float sidePullStrength = 0.9f;

        [Header("Intensity shaping")]
        [Range(0f, 2f)] public float gain = 1.0f;
        [Range(0f, 1f)] public float threshold = 0.15f;
        [Range(0.5f, 4f)] public float gamma = 1.6f;

        private struct Sample
        {
            public float t;    // sec from start (speed applied)
            public float gx01;
            public float y01;
            public float z01;
            public float i01;
        }

        private readonly List<Sample> _samples = new List<Sample>(20000);
        private Coroutine _routine;

        // trace change detect
        private int _lastCount = -1;
        private float _lastEndTime = -999f;
        private Vector3 _lastEndPos = Vector3.zero;

        // brush precomputed
        private List<Vector2> _offsets;
        private List<float> _weights;

        [ContextMenu("Play")]
        public void Play()
        {
            if (_routine != null) StopCoroutine(_routine);

            if (!EnsureUpToDateSamples()) return;

            if (_offsets == null || _weights == null)
            {
                _offsets = BuildBrushOffsets(brushSamples);
                _weights = BuildGaussianWeights(_offsets, radius01, sigmaRatio);
            }

            _routine = StartCoroutine(PlayStream());
        }

        [ContextMenu("Stop")]
        public void Stop()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            BhapticsLibrary.StopAll();
        }

        private bool EnsureUpToDateSamples()
        {
            if (trace == null || trace.points == null || trace.points.Count < 2)
            {
                Debug.LogError("[TracePlayer] trace points need >=2");
                return false;
            }

            int c = trace.points.Count;
            var last = trace.points[c - 1];

            bool changed =
                c != _lastCount ||
                !Mathf.Approximately(last.time, _lastEndTime) ||
                (last.pos - _lastEndPos).sqrMagnitude > 1e-6f;

            if (changed || _samples.Count == 0)
            {
                BuildSamples();
                _lastCount = c;
                _lastEndTime = last.time;
                _lastEndPos = last.pos;
            }

            return _samples.Count > 0;
        }

        private void BuildSamples()
        {
            _samples.Clear();
            trace.points.Sort((a, b) => a.time.CompareTo(b.time));

            float spd = Mathf.Max(0.0001f, speed);
            float dt = Mathf.Max(0.001f, sampleDt);

            float tStart = trace.points[0].time;
            float t = trace.points[0].time;
            int seg = 0;
            float endT = trace.points[trace.points.Count - 1].time;

            while (t <= endT + 1e-6f)
            {
                while (seg < trace.points.Count - 2 && trace.points[seg + 1].time < t)
                    seg++;

                var p0 = trace.points[seg];
                var p1 = trace.points[Mathf.Min(seg + 1, trace.points.Count - 1)];

                float t0 = p0.time;
                float t1 = Mathf.Max(t0 + 1e-6f, p1.time);
                float u = Mathf.Clamp01((t - t0) / (t1 - t0));

                Vector3 pos = Vector3.Lerp(p0.pos, p1.pos, u);

                float gx01 = (gridW <= 1) ? 0.5f : Mathf.Clamp01(pos.x / (gridW - 1f));
                float y01  = (gridH <= 1) ? 0.5f : Mathf.Clamp01(pos.y / (gridH - 1f));
                if (flipY) y01 = 1f - y01;

                float z01 = Mathf.Clamp01(pos.z);

                float baseI = Mathf.Clamp01(Mathf.Lerp(p0.baseIntensity, p1.baseIntensity, u) * gain);
                float i01 = ShapeIntensity(baseI);

                _samples.Add(new Sample
                {
                    t = (t - tStart) / spd,
                    gx01 = gx01,
                    y01 = y01,
                    z01 = z01,
                    i01 = i01
                });

                t += dt * spd;
            }

            // dtMs sync 권장
            dtMs = Mathf.Clamp(Mathf.RoundToInt(sampleDt * 1000f), 5, 30);
        }

        private float ShapeIntensity(float i)
        {
            i = Mathf.Clamp01(i);
            if (i <= threshold) return 0f;
            float x = (i - threshold) / Mathf.Max(1e-6f, 1f - threshold);
            x = Mathf.Pow(x, gamma);
            return Mathf.Clamp01(x);
        }

        private float MapFrontX(float gx01) => Mathf.Lerp(frontXRange.x, frontXRange.y, gx01);

        private float MapBackX(float gx01)
        {
            float u = flipBackX ? (1f - gx01) : gx01;
            return Mathf.Lerp(backXRange.x, backXRange.y, u);
        }

        private (float minX, float maxX) SafeFront()
        {
            float minX = frontXRange.x + seamMargin + radius01;
            float maxX = frontXRange.y - seamMargin - radius01;
            if (minX >= maxX) { minX = frontXRange.x + radius01; maxX = frontXRange.y - radius01; }
            return (minX, maxX);
        }

        private (float minX, float maxX) SafeBack()
        {
            float minX = backXRange.x + seamMargin + radius01;
            float maxX = backXRange.y - seamMargin - radius01;
            if (minX >= maxX) { minX = backXRange.x + radius01; maxX = backXRange.y - radius01; }
            return (minX, maxX);
        }

        private IEnumerator PlayStream()
        {
            float start = Time.time;
            int idx = 0;

            while (idx < _samples.Count)
            {
                float now = Time.time - start;

                // 현재 시간에 해당하는 sample로 이동
                while (idx < _samples.Count - 1 && _samples[idx].t < now)
                    idx++;

                var s = _samples[idx];
                if (s.i01 > 1e-4f)
                {
                    int mode = DepthMode(s.z01);

                    if (mode == 0)
                        EmitStampFront(s);
                    else if (mode == 1)
                        EmitStampBack(s);
                    else
                        EmitStampMiddle(s);
                }

                // dtMs만큼 텀을 두고 다음 stamp
                yield return new WaitForSeconds(dtMs / 1000f);
            }

            Stop();
        }

        private int DepthMode(float z01)
        {
            if (z01 <= zFrontThreshold) return 0;
            if (z01 >= zBackThreshold) return 1;
            return 2;
        }

        private void EmitStampFront(Sample s)
        {
            var (minX, maxX) = SafeFront();

            float cx = Mathf.Clamp(MapFrontX(s.gx01), minX, maxX);
            float cy = Mathf.Clamp01(s.y01);
            int baseI = Mathf.Clamp(Mathf.RoundToInt(s.i01 * 100f), 0, 100);

            EmitGaussianStamp(cx, cy, baseI, minX, maxX, stampDurationMs);
        }

        private void EmitStampBack(Sample s)
        {
            var (minX, maxX) = SafeBack();

            float cx = Mathf.Clamp(MapBackX(s.gx01), minX, maxX);
            float cy = Mathf.Clamp01(s.y01);
            int baseI = Mathf.Clamp(Mathf.RoundToInt(s.i01 * 100f), 0, 100);

            EmitGaussianStamp(cx, cy, baseI, minX, maxX, stampDurationMs);
        }

        private void EmitStampMiddle(Sample s)
        {
            // z를 wrap(앞->옆->뒤)로 해석해서 front/back 동시
            float z = Mathf.Clamp01(s.z01);
            float theta = z * Mathf.PI;
            float wFront = Mathf.Pow(Mathf.Cos(theta * 0.5f), 2f);
            float wBack  = Mathf.Pow(Mathf.Sin(theta * 0.5f), 2f);

            float side = 1f - Mathf.Clamp01(Mathf.Abs(z - 0.5f) / 0.5f);
            float pull = side * sidePullStrength;

            var (fMin, fMax) = SafeFront();
            var (bMin, bMax) = SafeBack();

            float fx = MapFrontX(s.gx01);
            float bx = MapBackX(s.gx01);

            // seam쪽으로 당기기(옆 느낌)
            fx = Mathf.Lerp(fx, fMax, pull);
            bx = Mathf.Lerp(bx, bMin, pull);

            fx = Mathf.Clamp(fx, fMin, fMax);
            bx = Mathf.Clamp(bx, bMin, bMax);

            float y = Mathf.Clamp01(s.y01);
            int baseI = Mathf.Clamp(Mathf.RoundToInt(s.i01 * 100f), 0, 100);

            EmitGaussianStamp(fx, y, Mathf.Clamp(Mathf.RoundToInt(baseI * wFront), 0, 100), fMin, fMax, stampDurationMs);
            EmitGaussianStamp(bx, y, Mathf.Clamp(Mathf.RoundToInt(baseI * wBack), 0, 100), bMin, bMax, stampDurationMs);
        }

        /// <summary>
        /// A single stamp = one PlayPath call with multiple points (brush).
        /// durationMs overlaps next stamps => continuous stroke.
        /// </summary>
        private void EmitGaussianStamp(float cx, float cy, int baseI, float minX, float maxX, int durationMs)
        {
            if (baseI <= 0) return;

            // stamp points arrays
            List<float> xs = new List<float>(_offsets.Count);
            List<float> ys = new List<float>(_offsets.Count);
            List<int> Is   = new List<int>(_offsets.Count);

            for (int i = 0; i < _offsets.Count; i++)
            {
                float w = _weights[i];
                if (w < weightCutoff) continue;

                float x = cx + _offsets[i].x * radius01;
                float y = cy + _offsets[i].y * radius01;

                // seam leak 방지: 범위 밖은 버림
                if (x < minX || x > maxX) continue;

                int Ii = Mathf.Clamp(Mathf.RoundToInt(baseI * w), 0, 100);
                if (Ii <= 0) continue;

                xs.Add(Mathf.Clamp(x, minX, maxX));
                ys.Add(Mathf.Clamp01(y));
                Is.Add(Ii);
            }

            if (xs.Count == 0) return;

            BhapticsLibrary.PlayPath(
                (int)PositionType.Vest,
                xs.ToArray(),
                ys.ToArray(),
                Is.ToArray(),
                durationMs
            );
        }

        private List<Vector2> BuildBrushOffsets(int count)
        {
            var o = new List<Vector2>(16)
            {
                Vector2.zero,
                new Vector2(1, 0), new Vector2(-1, 0),
                new Vector2(0, 1), new Vector2(0, -1),
                new Vector2(1, 1), new Vector2(1, -1),
                new Vector2(-1, 1), new Vector2(-1, -1)
            };

            if (count >= 13)
            {
                o.Add(new Vector2(0.5f, 0));
                o.Add(new Vector2(-0.5f, 0));
                o.Add(new Vector2(0, 0.5f));
                o.Add(new Vector2(0, -0.5f));
            }
            return o;
        }

        private List<float> BuildGaussianWeights(List<Vector2> offsets, float radius, float sigmaRatioLocal)
        {
            float sigma = Mathf.Max(1e-4f, radius * sigmaRatioLocal);
            float inv2s2 = 1f / (2f * sigma * sigma);

            var w = new List<float>(offsets.Count);
            float sum = 0f;

            for (int i = 0; i < offsets.Count; i++)
            {
                Vector2 p = offsets[i] * radius;
                float ww = Mathf.Exp(-p.sqrMagnitude * inv2s2);
                w.Add(ww);
                sum += ww;
            }

            if (sum <= 1e-6f) sum = 1f;
            for (int i = 0; i < w.Count; i++) w[i] /= sum;
            return w;
        }
    }
}
