using UnityEngine;

namespace AxisLabHaptics
{
    [CreateAssetMenu(menuName = "AxisLabHaptics/Motor Layout Asset")]
    public class MotorLayoutAsset : ScriptableObject
    {
        [Tooltip("Motor center positions in Vest Local Space (x,y,z). z should be +depth for front motors, -depth for back motors.")]
        public Vector3[] motorPos;

        [Header("Optional")]
        [Tooltip("If true, mirror X when mapping (useful if your back image is mirrored).")]
        public bool mirrorX = false;

        public Vector3 GetMotorPos(int index)
        {
            if (motorPos == null || index < 0 || index >= motorPos.Length) return Vector3.zero;
            return motorPos[index];
        }

        public int Count => motorPos != null ? motorPos.Length : 0;
    }
}
