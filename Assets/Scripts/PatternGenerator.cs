using UnityEngine;

namespace AxisLabHaptics
{
    public static class PatternGenerator
    {
        /// <summary>
        /// 입력: 포인트 pos (grid x,y,z), 레이아웃(모터 grid pos), k
        /// 출력: 모터 가중치(스냅/블렌딩/가우시안 등)
        /// </summary>
        public static MotorWeight[] Generate(SessionSettings s, Vector3 pos, MotorLayoutAsset layout)
        {
            if (layout == null || layout.motorPos == null || layout.motorPos.Length == 0)
                return System.Array.Empty<MotorWeight>();

            // z는 front/back 구분 용도이므로 거리 계산에 포함할지 말지 선택 가능.
            // 네 스펙은 z=0/1이라 "면"만 구분하면 되니, 거리 계산은 XY 중심이 자연스러움.
            // -> 여기서는 XY 거리로만 계산.
            Vector2 p = new Vector2(pos.x, pos.y);

            switch (s.strokeType)
            {
                case StrokeType.Gaussian:
                    return GaussianWeights(p, layout, sigma: 0.7f); // sigma는 나중에 TracePoint.sigma로 대체
                case StrokeType.TactileBrush:
                    return InverseDistanceKNN(p, layout, s.blendK);
                default:
                    return InverseDistanceKNN(p, layout, s.blendK);
            }
        }

        // Tactile 느낌: 가까운 K개에 inverse-distance
        private static MotorWeight[] InverseDistanceKNN(Vector2 p, MotorLayoutAsset layout, int k)
        {
            k = Mathf.Clamp(k, 1, 4);

            // MotorMapper가 Vector3 기준이라 XY만 쓰는 간단 버전 구현
            var idx = new int[layout.motorPos.Length];
            var d2 = new float[layout.motorPos.Length];

            for (int i = 0; i < layout.motorPos.Length; i++)
            {
                idx[i] = i;
                Vector2 m = new Vector2(layout.motorPos[i].x, layout.motorPos[i].y);
                d2[i] = (m - p).sqrMagnitude;
            }

            System.Array.Sort(d2, idx); // d2 오름차순에 맞춰 idx 정렬

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

        // Gaussian: 모든 모터에 가우시안 분포로 weight 주고 상위 몇 개만 컷
        private static MotorWeight[] GaussianWeights(Vector2 p, MotorLayoutAsset layout, float sigma)
        {
            sigma = Mathf.Max(1e-4f, sigma);
            float inv2s2 = 1f / (2f * sigma * sigma);

            float[] w = new float[layout.motorPos.Length];
            int[] idx = new int[layout.motorPos.Length];

            for (int i = 0; i < layout.motorPos.Length; i++)
            {
                idx[i] = i;
                Vector2 m = new Vector2(layout.motorPos[i].x, layout.motorPos[i].y);
                float d2 = (m - p).sqrMagnitude;
                w[i] = Mathf.Exp(-d2 * inv2s2);
            }

            // 상위 4개만 사용
            // weight 기준으로 내림차순 정렬
            System.Array.Sort(w, idx); // 오름차순
            System.Array.Reverse(w);
            System.Array.Reverse(idx);

            int k = Mathf.Min(4, layout.motorPos.Length);
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
