using UnityEngine;
using Bhaptics.SDK2;

namespace AxisLabHaptics
{
    /// <summary>
    /// MotorWeight[] -> bHaptics PlayMotors(Vest, int[32])로 출력
    /// </summary>
    public class BhapticsMotorOutput : MonoBehaviour, IHapticOutput
    {
        [Header("Output")]
        [Range(10, 200)] public int durationMs = 60;
        [Range(0, 100)] public int maxIntensity = 100;

        public void SubmitFrame(MotorWeight[] motors, float intensity01)
        {
            if (motors == null || motors.Length == 0) return;

            int[] arr = new int[32];
            int baseI = Mathf.Clamp(Mathf.RoundToInt(intensity01 * maxIntensity), 0, maxIntensity);

            for (int i = 0; i < motors.Length; i++)
            {
                int idx = motors[i].index;
                float w = Mathf.Clamp01(motors[i].weight);

                if (idx < 0 || idx >= 32) continue;

                int v = Mathf.Clamp(Mathf.RoundToInt(baseI * w), 0, maxIntensity);
                if (v > arr[idx]) arr[idx] = v; // 겹치면 더 큰 값 유지
            }

            BhapticsLibrary.PlayMotors((int)PositionType.Vest, arr, durationMs);
        }

        public void StopAll()
        {
            int[] zeros = new int[32];
            BhapticsLibrary.PlayMotors((int)PositionType.Vest, zeros, 50);
        }
    }
}
