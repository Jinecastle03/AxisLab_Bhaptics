using UnityEngine;

namespace AxisLabHaptics
{
    public static class HapticProfile
    {
        public static float ApplyEnvelope(EnvelopeType type, float x01, AnimationCurve curve)
        {
            x01 = Mathf.Clamp01(x01);
            switch (type)
            {
                case EnvelopeType.Lerp:
                    return x01;
                case EnvelopeType.SmoothStep:
                    return x01 * x01 * (3f - 2f * x01);
                case EnvelopeType.ExpIn:
                    return Mathf.Pow(x01, 3f);
                case EnvelopeType.ExpOut:
                    return 1f - Mathf.Pow(1f - x01, 3f);
                case EnvelopeType.Curve:
                    return curve != null ? curve.Evaluate(x01) : x01;
                default:
                    return x01;
            }
        }

        public static float ApplyPerceptual(PerceptualMapType type, float v01, float threshold, float gamma, float k)
        {
            v01 = Mathf.Clamp01(v01);

            switch (type)
            {
                case PerceptualMapType.None:
                    return v01;

                case PerceptualMapType.ThresholdGamma:
                    if (v01 <= threshold) return 0f;
                    float t = (v01 - threshold) / Mathf.Max(1e-6f, (1f - threshold));
                    return Mathf.Pow(Mathf.Clamp01(t), Mathf.Max(1e-6f, gamma));

                case PerceptualMapType.Logistic:
                    // threshold를 중심으로 S-curve
                    float x = (v01 - threshold) * k;
                    return 1f / (1f + Mathf.Exp(-x));

                default:
                    return v01;
            }
        }

        /// <summary>
        /// 최종 강도 = Envelope(시간) * baseIntensity 후 Perceptual mapping
        /// </summary>
        public static float Evaluate(SessionSettings s, float baseIntensity01, float envelopeX01)
        {
            float env = ApplyEnvelope(s.envelopeType, envelopeX01, s.envelopeCurve);
            float raw = Mathf.Clamp01(baseIntensity01) * env;
            return ApplyPerceptual(s.perceptualMapType, raw, s.threshold, s.gamma, s.logisticK);
        }
    }
}
