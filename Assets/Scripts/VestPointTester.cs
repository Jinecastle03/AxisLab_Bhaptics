using UnityEngine;
using Bhaptics.SDK2;

public class VestPointTester : MonoBehaviour
{
    public enum VestSide
    {
        Front,
        Back
    }

    [Header("Logical Coordinate")]
    public VestSide side = VestSide.Front;

    // 4x4 그리드 좌표
    [Range(0, 3)] public int row = 0;   // 0 = 위, 3 = 아래
    [Range(0, 3)] public int col = 0;   // 0 = 왼쪽, 3 = 오른쪽

    [Header("Haptic Params")]
    [Range(1, 100)] public int intensity = 80;
    public int durationMs = 300;

    // (side, row, col) -> motor index(0~31)
    int CoordToIndex(VestSide s, int r, int c)
    {
        int local = r * 4 + c;          // 0~15
        int baseIndex = (s == VestSide.Front) ? 0 : 16;
        return baseIndex + local;       // Front: 0~15, Back: 16~31
    }

    [ContextMenu("Play Haptic Once")]
    public void PlayOnce()
    {
        int idx = CoordToIndex(side, row, col);

        // 32개 모터 배열 준비
        int[] motors = new int[32];
        for (int i = 0; i < motors.Length; i++)
            motors[i] = 0;

        // 목표 인덱스만 진동
        motors[idx] = intensity;

        // TactSuit Pro = PositionType.Vest
        BhapticsLibrary.PlayMotors(
            (int)PositionType.Vest,
            motors,
            durationMs
        );

        Debug.Log($"[PlayMotors] side={side}, row={row}, col={col}, index={idx}, I={intensity}, D={durationMs}ms");
    }
}
