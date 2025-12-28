using UnityEngine;

namespace AxisLabHaptics
{
    public static class PatternGenerator
    {
        /// <summary>
        /// 기본 Generate (기존 호출 호환)
        /// </summary>
        public static MotorWeight[] Generate(SessionSettings s, Vector3 pos, MotorLayoutAsset layout)
        {
            return Generate(s, pos, layout, sigmaOverride: 0.7f);
        }

        /// <summary>
        /// ✅ sigmaOverride를 받아서 Gaussian의 퍼짐 정도를 제어 (TracePoint.sigma 지원)
        /// </summary>
        public static MotorWeight[] Generate(SessionSettings s, Vector3 pos, MotorLayoutAsset layout, float sigmaOverride)
        {
            if (layout == null || layout.motorPos == null || layout.motorPos.Length == 0)
                return System.Array.Empty<MotorWeight>();

            // 거리 계산은 x,y만 사용 (z는 front/back 믹스용)
            Vector2 p = new Vector2(pos.x, pos.y);

            switch (s.strokeType)
            {
                case StrokeType.Gaussian:
                    return GaussianWeights(p, layout, sigma: Mathf.Max(1e-4f, sigmaOverride));
                case StrokeType.TactileBrush:
                    return InverseDistanceKNN(p, layout, s.blendK);
                default:
                    return InverseDistanceKNN(p, layout, s.blendK);
            }
        }

        // 가까운 K개 inverse-distance
        private static MotorWeight[] InverseDistanceKNN(Vector2 p, MotorLayoutAsset layout, int k)
        {
            int count = layout.motorPos.Length;
            k = Mathf.Clamp(k, 1, Mathf.Min(8, count));

            int[] idx = new int[count];
            float[] d2 = new float[count];

            for (int i = 0; i < count; i++)
            {
                idx[i] = i;
                Vector2 m = new Vector2(layout.motorPos[i].x, layout.motorPos[i].y);
                d2[i] = (m - p).sqrMagnitude;
            }

            System.Array.Sort(d2, idx);

            var outW = new MotorWeight[k];

            if (k == 1)
            {
                outW[0] = new MotorWeight { index = idx[0], weight = 1f };
                return outW;
            }

            float sum = 0f;
            for (int n = 0; n < k; n++)
            {
                float w = 1f / Mathf.Max(1e-6f, d2[n]);
                sum += w;
                outW[n] = new MotorWeight { index = idx[n], weight = w };
            }
            for (int n = 0; n < k; n++)
                outW[n].weight /= Mathf.Max(1e-6f, sum);

            return outW;
        }

        // Gaussian: 모든 모터에 가우시안 분포 주고 상위 k개만 사용
        private static MotorWeight[] GaussianWeights(Vector2 p, MotorLayoutAsset layout, float sigma)
        {
            int count = layout.motorPos.Length;
            int k = Mathf.Clamp(SessionSettings.Instance != null ? SessionSettings.Instance.blendK : 4, 1, Mathf.Min(12, count));

            float inv2s2 = 1f / (2f * sigma * sigma);

            int[] idx = new int[count];
            float[] w = new float[count];

            for (int i = 0; i < count; i++)
            {
                idx[i] = i;
                Vector2 m = new Vector2(layout.motorPos[i].x, layout.motorPos[i].y);
                float d2 = (m - p).sqrMagnitude;
                w[i] = Mathf.Exp(-d2 * inv2s2);
            }

            // weight 큰 순서대로 k개 뽑기
            System.Array.Sort(w, idx); // 오름차순
            System.Array.Reverse(w);
            System.Array.Reverse(idx);

            var outW = new MotorWeight[k];
            float sum = 0f;

            for (int i = 0; i < k; i++)
            {
                sum += w[i];
                outW[i] = new MotorWeight { index = idx[i], weight = w[i] };
            }

            for (int i = 0; i < k; i++)
                outW[i].weight /= Mathf.Max(1e-6f, sum);

            return outW;
        }
    }
}
