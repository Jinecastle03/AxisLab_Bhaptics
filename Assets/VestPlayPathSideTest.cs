using UnityEngine;
using Bhaptics.SDK2;

public class VestPlayPathSideTest : MonoBehaviour
{
    [Range(1,100)] public int intensity = 80;
    [Range(100,800)] public int durationMs = 300;

    [ContextMenu("Test FRONT point (x=0.2,y=0.5)")]
    public void TestFront()
    {
        // 문서 예시 형식: x[], y[], intensity[], duration :contentReference[oaicite:2]{index=2}
        BhapticsLibrary.PlayPath(
            (int)PositionType.Vest,
            new float[] { 0.2f },
            new float[] { 0.5f },
            new int[] { intensity },
            durationMs
        );
        Debug.Log("PlayPath FRONT sent");
    }

    [ContextMenu("Test BACK point (x=0.8,y=0.5)")]
    public void TestBack()
    {
        BhapticsLibrary.PlayPath(
            (int)PositionType.Vest,
            new float[] { 0.45f },
            new float[] { 0.5f },
            new int[] { intensity },
            durationMs
        );
        Debug.Log("PlayPath BACK sent");
    }
}
