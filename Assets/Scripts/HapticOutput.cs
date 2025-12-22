using UnityEngine;

namespace AxisLabHaptics
{
    public interface IHapticOutput
    {
        void SubmitFrame(MotorWeight[] motors, float intensity01);
        void StopAll();
    }

    /// <summary>
    /// 디버그용 출력: 콘솔로 어떤 모터가 얼마나 울리는지 찍어줌
    /// </summary>
    public class DebugHapticOutput : IHapticOutput
    {
        public void SubmitFrame(MotorWeight[] motors, float intensity01)
        {
            if (motors == null || motors.Length == 0) return;

            string s = "";
            for (int i = 0; i < motors.Length; i++)
                s += (i == 0 ? "" : " | ") + $"{motors[i].index}:{motors[i].weight:F3}";

            Debug.Log($"[DebugHapticOutput] intensity={intensity01:F3} motors=[{s}]");
        }

        public void StopAll()
        {
            Debug.Log("[DebugHapticOutput] StopAll");
        }
    }
}
