using System;
using System.Collections.Generic;
using UnityEngine;

namespace AxisLabHaptics
{
    [Serializable]
    public struct MotorWeight
    {
        public int index;
        public float weight; // 0..1
    }

    [Serializable]
    public class TracePoint
    {
        public float time;            // seconds
        public Vector3 pos;           // (x,y,z) where z indicates front/back (+/-)
        [Range(0f, 1f)] public float baseIntensity = 1f;
        public float sigma = 0.12f;   // gaussian radius or brush size
        public MotorWeight[] motors;  // snap:1, blend:2~4
    }

    [CreateAssetMenu(menuName = "AxisLabHaptics/Haptic Trace Asset")]
    public class HapticTraceAsset : ScriptableObject
    {
        public List<TracePoint> points = new List<TracePoint>();
    }
}
