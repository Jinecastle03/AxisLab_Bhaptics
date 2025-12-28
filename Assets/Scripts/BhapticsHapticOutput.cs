using Bhaptics.SDK2;
using UnityEngine;

namespace AxisLabHaptics
{
    public class BhapticsHapticOutput : MonoBehaviour, IHapticOutput
    {
        public PositionType deviceType = PositionType.Vest;

        [Tooltip("SessionSettings.frameDurationMs를 우선 사용하려면 0으로 두세요.")]
        public int overrideFrameDurationMs = 0;

        private float[] _smooth = new float[32];
        private int[] _values = new int[32];

        public void SubmitFrame(MotorWeight[] motors, float intensity01)
        {
            var s = SessionSettings.Instance;

            int frameDurMs = overrideFrameDurationMs > 0
                ? overrideFrameDurationMs
                : (s != null ? Mathf.Max(10, s.frameDurationMs) : 20);

            // 1) MotorWeight[] -> target[32] (0..100)
            float[] target = new float[32];
            float I = Mathf.Clamp01(intensity01);

            if (motors != null)
            {
                for (int i = 0; i < motors.Length; i++)
                {
                    int idx = motors[i].index;
                    if (idx < 0 || idx >= 32) continue;

                    float w = Mathf.Clamp01(motors[i].weight);
                    target[idx] = Mathf.Clamp(target[idx] + (w * I * 100f), 0f, 100f);
                }
            }

            // 2) Temporal smoothing (EMA) + noise gate + delta clamp
            if (s != null && s.smoothingTau > 1e-6f)
            {
                float dt = Mathf.Max(0.001f, (s.sampleIntervalMs / 1000f));
                float alpha = 1f - Mathf.Exp(-dt / s.smoothingTau);

                for (int m = 0; m < 32; m++)
                {
                    float prev = _smooth[m];
                    float next = prev + (target[m] - prev) * alpha;

                    if (s.maxDeltaPerFrame > 0)
                    {
                        float d = next - prev;
                        float md = s.maxDeltaPerFrame;
                        if (d > md) next = prev + md;
                        else if (d < -md) next = prev - md;
                    }

                    int outV = Mathf.RoundToInt(next);
                    if (outV <= s.noiseGate) outV = 0;

                    _smooth[m] = next;
                    _values[m] = Mathf.Clamp(outV, 0, 100);
                }
            }
            else
            {
                // smoothing off
                for (int m = 0; m < 32; m++)
                {
                    int outV = Mathf.RoundToInt(target[m]);
                    if (s != null && outV <= s.noiseGate) outV = 0;
                    _values[m] = Mathf.Clamp(outV, 0, 100);
                }
            }

            BhapticsLibrary.PlayMotors((int)deviceType, _values, frameDurMs);
        }

        public void StopAll()
        {
            for (int i = 0; i < 32; i++) { _smooth[i] = 0f; _values[i] = 0; }
            BhapticsLibrary.StopAll();
        }
    }
}
