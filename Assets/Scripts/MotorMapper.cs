using System.Collections.Generic;
using UnityEngine;

namespace AxisLabHaptics
{
    public static class MotorMapper
    {
        public static MotorWeight[] MapPointToMotors(Vector3 p, MotorLayoutAsset layout, int k)
        {
            if (layout == null || layout.motorPos == null || layout.motorPos.Length == 0)
                return new MotorWeight[0];

            // mirror option
            if (layout.mirrorX) p.x = -p.x;

            k = Mathf.Clamp(k, 1, 4);

            // find k nearest
            var list = new List<(int idx, float d2)>(layout.motorPos.Length);
            for (int i = 0; i < layout.motorPos.Length; i++)
            {
                float d2 = (layout.motorPos[i] - p).sqrMagnitude;
                list.Add((i, d2));
            }
            list.Sort((a, b) => a.d2.CompareTo(b.d2));

            var outW = new MotorWeight[k];
            if (k == 1)
            {
                outW[0] = new MotorWeight { index = list[0].idx, weight = 1f };
                return outW;
            }

            // inverse-distance weights
            float sum = 0f;
            for (int n = 0; n < k; n++)
            {
                float w = 1f / Mathf.Max(1e-8f, list[n].d2);
                sum += w;
                outW[n] = new MotorWeight { index = list[n].idx, weight = w };
            }
            for (int n = 0; n < k; n++)
                outW[n].weight /= Mathf.Max(1e-8f, sum);

            return outW;
        }
    }
}
