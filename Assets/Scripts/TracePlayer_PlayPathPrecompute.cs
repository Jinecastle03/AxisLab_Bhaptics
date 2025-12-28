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
        [Range(5, 1000)] public int dtMs = 10;

        [Tooltip("Each stamp lasts this long. dtMs보다 크게(50~90ms) => overlap으로 부드러움")]
        [Range(20, 160)] public int stampDurationMs = 70;

        [Header("Temporal spacing")]
        [Tooltip("각 trace 구간(두 점 사이)당 최소 stamp 개수. 1이면 원본 시간 간격 유지, 3~5면 더 부드러움")]
        [Range(1, 12)] public int stampsPerSegment = 4;

        [Header("Depth Split")]
        [Range(0f, 0.5f)] public float zFrontThreshold = 0.15f;
        [Range(0.5f, 1f)] public float zBackThreshold = 0.85f;
        [Tooltip("Trace 좌표계에서 front가 z=1이면 true, z=0이면 false")]
        public bool traceFrontIsZ1 = true;
        [Tooltip("필요시 z(0=front,1=back) 반전. front/back이 뒤집혀 시작할 때 켜기")]
        public bool invertZ = false;
        [Tooltip("trace가 0~1이 아닌 좁은 구간(예: 0.8~1)으로 저장됐을 때 0~1로 리스케일")]
        public bool normalizeZByTraceRange = true;

        [Header("Side face assist")]
        [Tooltip("trace의 x 변화가 거의 없으면 측면으로 간주하고 z를 한 번 더 반전")]
        public bool autoFlipSideZWhenXFlat = false;
        [Tooltip("x 변화 허용 오차(그리드 좌표계)")]
        [Range(0f, 1f)] public float sideXFlatEps = 0.05f;

        [Header("TactSuit coordinate X ranges")]
        public Vector2 frontXRange = new Vector2(0.05f, 0.95f);
        public Vector2 backXRange  = new Vector2(0.02f, 0.98f);
        [Tooltip("true면 좌우를 뒤집음(0→오른쪽, 1→왼쪽). 원하는 좌→우=0→1이면 false")]
        public bool flipFrontX = false;
        [Tooltip("Back 면을 front와 맞추기 위해 기본값을 true로 설정 (좌→우 매핑을 뒤집어 정렬)")]
        public bool flipBackX = true;
        [Tooltip("입력된 x 이동을 더 크게 느끼게 할 배율")]
        [Range(0.5f, 2f)] public float xMotionGain = 1.6f;

        [Header("Seam / leak control")]
        [Range(0f, 0.25f)] public float seamMargin = 0.02f;

        [Header("Edge reach (taper)")]
        [Tooltip("안전 범위를 넘어갈 때 에지 방향으로 얼마나 부드럽게 감쇠할지(0이면 바로 컷)")]
        [Range(0f, 0.1f)] public float edgeSoftness = 0.03f;

        [Header("Gaussian Brush (stamp)")]
        [Range(0.01f, 0.15f)] public float radius01 = 0.04f;
        [Range(0.1f, 1f)] public float sigmaRatio = 0.55f;
        [Range(5, 13)] public int brushSamples = 9;
        [Range(0f, 0.2f)] public float weightCutoff = 0.05f;

        [Header("Middle wrap feeling")]
        [Range(0f, 1f)] public float sidePullStrength = 0.9f;

        [Header("Blend Mode (path time blend)")]
        public PathBlendMode pathBlendMode = PathBlendMode.Linear;
        [Tooltip("Gaussian 시간 블렌딩 sigma (초)")]
        [Range(0.01f, 1f)] public float pathGaussianSigma = 0.2f;

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
        private float _traceZMin = 0f;
        private float _traceZMax = 1f;

        // trace change detect
        private int _lastCount = -1;
        private float _lastEndTime = -999f;
        private Vector3 _lastEndPos = Vector3.zero;
        private int _lastConfigHash = 0;

        // brush precomputed
        private List<Vector2> _offsets;
        private List<float> _weights;

        // shape params (may come from SessionSettings)
        private float _shapeThreshold;
        private float _shapeGamma;
        private float _shapeGain;
        private bool _useSessionProfile;

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

            int cfgHash = ComputeConfigHash();

            int c = trace.points.Count;
            var last = trace.points[c - 1];

            bool changed =
                c != _lastCount ||
                !Mathf.Approximately(last.time, _lastEndTime) ||
                (last.pos - _lastEndPos).sqrMagnitude > 1e-6f ||
                cfgHash != _lastConfigHash;

            if (changed || _samples.Count == 0)
            {
                BuildSamples();
                _lastCount = c;
                _lastEndTime = last.time;
                _lastEndPos = last.pos;
                _lastConfigHash = cfgHash;
            }

            return _samples.Count > 0;
        }

        private void BuildSamples()
        {
            _samples.Clear();
            trace.points.Sort((a, b) => a.time.CompareTo(b.time));

            float spd = Mathf.Max(0.0001f, speed);
            float dtUser = Mathf.Max(0.001f, sampleDt);

            float tStart = trace.points[0].time;
            float t = trace.points[0].time;
            int seg = 0;
            float endT = trace.points[trace.points.Count - 1].time;
            float totalDuration = Mathf.Max(1e-6f, endT - tStart);

            // trace 구간 중 가장 짧은 시간 간격 파악(원본 시간 간격을 유지할 때 사용)
            float minSegDt = float.MaxValue;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < trace.points.Count - 1; i++)
            {
                float d = Mathf.Max(1e-6f, trace.points[i + 1].time - trace.points[i].time);
                if (d < minSegDt) minSegDt = d;

                minX = Mathf.Min(minX, trace.points[i].pos.x);
                maxX = Mathf.Max(maxX, trace.points[i].pos.x);
                minZ = Mathf.Min(minZ, trace.points[i].pos.z);
                maxZ = Mathf.Max(maxZ, trace.points[i].pos.z);
            }
            // include last point for range
            if (trace.points.Count > 0)
            {
                minX = Mathf.Min(minX, trace.points[^1].pos.x);
                maxX = Mathf.Max(maxX, trace.points[^1].pos.x);
                minZ = Mathf.Min(minZ, trace.points[^1].pos.z);
                maxZ = Mathf.Max(maxZ, trace.points[^1].pos.z);
            }
            if (minSegDt == float.MaxValue) minSegDt = dtUser;
            if (minZ == float.MaxValue || maxZ == float.MinValue)
            {
                _traceZMin = 0f;
                _traceZMax = 1f;
            }
            else
            {
                _traceZMin = minZ;
                _traceZMax = maxZ;
            }

            var s = SessionSettings.Instance;
            PathBlendMode mode = pathBlendMode;
            float gaussSigma = pathGaussianSigma;
            _shapeThreshold = threshold;
            _shapeGamma = gamma;
            _shapeGain = gain;
            _useSessionProfile = false;

            if (s != null)
            {
                mode = (s.strokeType == StrokeType.Gaussian) ? PathBlendMode.Gaussian : PathBlendMode.Linear;
                gaussSigma = Mathf.Max(0.01f, s.pathGaussianSigma);
                // SessionSettings의 envelope/perceptual을 직접 적용할 것이므로 여기서는 추가 shaping을 끔
                _shapeThreshold = 0f;
                _shapeGamma = 1f;
                _shapeGain = 1f;
                _useSessionProfile = true;
            }
            bool useGauss = (mode == PathBlendMode.Gaussian);

            // 측면 trace( x 변화 거의 없고 z 변화가 있는 경우 )면 z를 한 번 더 반전해서 front->back 순서를 맞춤
            bool sideFlip = autoFlipSideZWhenXFlat &&
                            (maxX - minX) <= sideXFlatEps &&
                            (maxZ - minZ) > 0.05f;

            while (t <= endT + 1e-6f)
            {
                while (seg < trace.points.Count - 2 && trace.points[seg + 1].time < t)
                    seg++;

                var p0 = trace.points[seg];
                var p1 = trace.points[Mathf.Min(seg + 1, trace.points.Count - 1)];

                float t0 = p0.time;
                float t1 = Mathf.Max(t0 + 1e-6f, p1.time);
                float u = Mathf.Clamp01((t - t0) / (t1 - t0));

                Vector3 pos;
                float baseI;

                if (useGauss)
                {
                    (pos, baseI) = GaussianBlendTrace(t, gaussSigma);
                }
                else
                {
                    pos = Vector3.Lerp(p0.pos, p1.pos, u);
                    baseI = Mathf.Lerp(p0.baseIntensity, p1.baseIntensity, u);
                }

                if (s != null && _useSessionProfile)
                {
                    float envX = Mathf.Clamp01((t - tStart) / totalDuration);
                    baseI = HapticProfile.Evaluate(s, baseI, envX);
                }

                float gx01 = (gridW <= 1) ? 0.5f : Mathf.Clamp01(pos.x / (gridW - 1f));
                float y01  = (gridH <= 1) ? 0.5f : Mathf.Clamp01(pos.y / (gridH - 1f));
                if (flipY) y01 = 1f - y01;

                float z01 = NormalizeZ(Mathf.Clamp01(pos.z), sideFlip);

                baseI = Mathf.Clamp01(baseI * _shapeGain);
                float i01 = ShapeIntensity(baseI);

                _samples.Add(new Sample
                {
                    t = (t - tStart) / spd,
                    gx01 = gx01,
                    y01 = y01,
                    z01 = z01,
                    i01 = i01
                });

                // 구간 길이 대비 최소 stamp 수를 고려해 step을 결정(원본 dt나 더 촘촘히)
                float segDt = Mathf.Max(1e-6f, (t1 - t0) / Mathf.Max(1, stampsPerSegment));
                float step = Mathf.Min(dtUser, segDt);

                t += step * spd;
            }

            // dtMs sync 권장: 실제 스텝과 맞추되 최소/최대 클램프
            float effectiveDt = Mathf.Min(dtUser, minSegDt / Mathf.Max(1, stampsPerSegment));
            dtMs = Mathf.Clamp(Mathf.RoundToInt(effectiveDt * 1000f), 5, 1000);
        }

        private float ShapeIntensity(float i)
        {
            i = Mathf.Clamp01(i);
            if (i <= _shapeThreshold) return 0f;
            float x = (i - _shapeThreshold) / Mathf.Max(1e-6f, 1f - _shapeThreshold);
            x = Mathf.Pow(x, _shapeGamma);
            return Mathf.Clamp01(x);
        }

        private float ScaleX01(float gx01)
        {
            // 중심(0.5) 기준으로 이동 폭을 키워 좌우 차이를 더 크게 전달
            float c = 0.5f;
            float d = (gx01 - c) * xMotionGain;
            return Mathf.Clamp01(c + d);
        }

        private float MapFrontX(float gx01)
        {
            float u = flipFrontX ? (1f - gx01) : gx01;
            return Mathf.Lerp(frontXRange.x, frontXRange.y, ScaleX01(u));
        }

        private float MapBackX(float gx01)
        {
            float u = flipBackX ? (1f - gx01) : gx01;
            return Mathf.Lerp(backXRange.x, backXRange.y, ScaleX01(u));
        }

        private float NormalizeZ(float zRaw, bool forceFlipSide = false)
        {
            // 0) trace 자체 분포를 0~1로 재정규화 (예: 0.8~1 -> 0~1)
            if (normalizeZByTraceRange)
            {
                float range = _traceZMax - _traceZMin;
                if (range > 1e-4f)
                    zRaw = Mathf.Clamp01((zRaw - _traceZMin) / range);
            }

            // 1) Trace 자체 좌표계(front가 z=1인지 여부)
            if (traceFrontIsZ1)
                zRaw = 1f - zRaw;

            // 2) 수동 반전 옵션
            if (invertZ)
                zRaw = 1f - zRaw; // 강제 반전 옵션

            // 3) 측면 자동 보정
            if (forceFlipSide)
                zRaw = 1f - zRaw;

            // 결과: 내부적으로 front=0, back=1
            return zRaw;
        }

        private int ComputeConfigHash()
        {
            var s = SessionSettings.Instance;
            int h = 17;
            unchecked
            {
                h = h * 31 + Mathf.RoundToInt(speed * 1000f);
                h = h * 31 + Mathf.RoundToInt(sampleDt * 1000f);
                h = h * 31 + gridW;
                h = h * 31 + gridH;
                h = h * 31 + (flipY ? 1 : 0);
                h = h * 31 + (traceFrontIsZ1 ? 1 : 0);
                h = h * 31 + (invertZ ? 1 : 0);
                h = h * 31 + (normalizeZByTraceRange ? 1 : 0);
                h = h * 31 + (autoFlipSideZWhenXFlat ? 1 : 0);
                h = h * 31 + Mathf.RoundToInt(sideXFlatEps * 1000f);
                h = h * 31 + stampsPerSegment;
                h = h * 31 + (int)pathBlendMode;
                h = h * 31 + Mathf.RoundToInt(pathGaussianSigma * 1000f);
                h = h * 31 + Mathf.RoundToInt(gain * 1000f);
                h = h * 31 + Mathf.RoundToInt(threshold * 1000f);
                h = h * 31 + Mathf.RoundToInt(gamma * 1000f);

                if (s != null)
                {
                    h = h * 31 + (s.frontIsZ0 ? 1 : 0);
                    h = h * 31 + (int)s.strokeType;
                    h = h * 31 + Mathf.RoundToInt(s.pathGaussianSigma * 1000f);
                    h = h * 31 + Mathf.RoundToInt(s.threshold * 1000f);
                    h = h * 31 + Mathf.RoundToInt(s.gamma * 1000f);
                    h = h * 31 + Mathf.RoundToInt(s.globalIntensityGain * 1000f);
                }
            }
            return h;
        }

        private (Vector3 pos, float baseI) GaussianBlendTrace(float t, float sigma)
        {
            if (trace == null || trace.points == null || trace.points.Count == 0)
                return (Vector3.zero, 0f);

            float inv2s2 = 1f / (2f * sigma * sigma);
            Vector3 accPos = Vector3.zero;
            float accI = 0f;
            float wsum = 0f;

            for (int i = 0; i < trace.points.Count; i++)
            {
                float dt = t - trace.points[i].time;
                float w = Mathf.Exp(-dt * dt * inv2s2);
                accPos += trace.points[i].pos * w;
                accI += trace.points[i].baseIntensity * w;
                wsum += w;
            }

            if (wsum <= 1e-6f)
                return (trace.points[0].pos, trace.points[0].baseIntensity);

            return (accPos / wsum, accI / wsum);
        }

        private (float minX, float maxX) SafeRange(Vector2 range)
        {
            // Ensure seam + brush radius cannot collapse the usable width.
            float width = Mathf.Max(1e-4f, range.y - range.x);
            float usable = Mathf.Max(1e-4f, width - 2f * radius01);
            float margin = Mathf.Min(seamMargin, usable * 0.25f);

            float minX = range.x + radius01 + margin;
            float maxX = range.y - radius01 - margin;

            if (minX >= maxX)
            {
                float mid = (range.x + range.y) * 0.5f;
                minX = mid - 0.001f;
                maxX = mid + 0.001f;
            }

            return (minX, maxX);
        }

        private (float minX, float maxX) SafeFront() => SafeRange(frontXRange);
        private (float minX, float maxX) SafeBack()  => SafeRange(backXRange);

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

                // 마지막 샘플은 일정 시간(스탬프 지속+여유) 이후에는 반복하지 않고 종료
                if (idx >= _samples.Count - 1)
                {
                    float tail = stampDurationMs * 0.001f + 0.05f; // 50ms 여유
                    if (now - s.t > tail) break;
                }

                if (s.i01 > 1e-4f)
                {
                    EmitStampBlended(s);
                }

                // dtMs만큼 텀을 두고 다음 stamp
                yield return new WaitForSeconds(dtMs / 1000f);
            }

            Stop();
        }

        private void EmitStampBlended(Sample s)
        {
            // z 전체 구간을 부드럽게 front/back로 나눔 (0~1에서 선형 블렌딩)
            float z = Mathf.Clamp01(s.z01);
            float tBlend = Mathf.Clamp01((z - zFrontThreshold) / Mathf.Max(1e-6f, zBackThreshold - zFrontThreshold));
            float wFront = 1f - tBlend;
            float wBack  = tBlend;

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

            int iFront = Mathf.Clamp(Mathf.RoundToInt(baseI * wFront), 0, 100);
            int iBack  = Mathf.Clamp(Mathf.RoundToInt(baseI * wBack), 0, 100);

            if (iFront > 0) EmitGaussianStamp(fx, y, iFront, fMin, fMax, stampDurationMs);
            if (iBack  > 0) EmitGaussianStamp(bx, y, iBack,  bMin, bMax, stampDurationMs);
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

                // seam leak 방지 + 에지 도달감: 범위를 넘으면 에지로 붙여주되 가중 감쇠
                float wEdge = 1f;
                if (x < minX)
                {
                    float d = minX - x;
                    if (edgeSoftness <= 1e-6f) continue;
                    wEdge = Mathf.Exp(-d / Mathf.Max(1e-6f, edgeSoftness));
                    x = minX;
                }
                else if (x > maxX)
                {
                    float d = x - maxX;
                    if (edgeSoftness <= 1e-6f) continue;
                    wEdge = Mathf.Exp(-d / Mathf.Max(1e-6f, edgeSoftness));
                    x = maxX;
                }

                int Ii = Mathf.Clamp(Mathf.RoundToInt(baseI * w * wEdge), 0, 100);
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
