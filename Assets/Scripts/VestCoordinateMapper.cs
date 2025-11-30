using UnityEngine;
using Bhaptics.SDK2;

/// <summary>
/// (x,y,z) 실수 좌표를 bHaptics Vest(32 motors) 모터 강도 배열로 바꿔주는 매퍼.
/// - x,y : 0 ~ (gridWidth-1) 실수
/// - z   : 0 = Front, 1 = Back
/// </summary>
public class VestCoordinateMapper : MonoBehaviour
{
    [Header("Grid Settings")]
    [Tooltip("앞/뒤 각각 가로 모터 개수")]
    public int gridWidth = 4;

    [Tooltip("앞/뒤 각각 세로 모터 개수")]
    public int gridHeight = 4;

    [Header("Intensity Settings")]
    [Tooltip("모터 최대 강도 (bHaptics 권장: 1~100)")]
    [Range(1, 100)]
    public int maxMotorIntensity = 100;

    [Tooltip("0~1 사이 intensity를 0~maxMotorIntensity로 스케일링할지 여부")]
    public bool intensityIs01 = true;

    // TODO: 이미 가지고 있는 integer 좌표 -> motor index 매핑을 여기에 구현하거나 연결하면 됨.
    // 예시는 단순 front: 0~15, back: 16~31 row-major 로 가정.
    private int[,,] motorIndexLut; // [z, y, x]

    private void Awake()
    {
        InitMotorIndexLut();
    }

    /// <summary>
    /// 정수 그리드 좌표를 모터 index로 매핑하는 룩업 테이블 초기화.
    /// 실험실에서 실제로 쓰는 mapping에 맞게 수정하면 됨.
    /// </summary>
    private void InitMotorIndexLut()
    {
        motorIndexLut = new int[2, gridHeight, gridWidth];

        int idx = 0;
        for (int z = 0; z < 2; z++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    motorIndexLut[z, y, x] = idx++;
                }
            }
        }

        // ★ 만약 이미 정수 좌표→index 매핑이 있다면,
        // 위 for문 대신 직접 motorIndexLut[z,y,x] = 네가 쓰는 index 로 채워버리면 됨.
    }

    /// <summary>
    /// 정수 좌표 (x,y,z) -> 모터 index 반환.
    /// </summary>
    public int GetMotorIndex(int x, int y, int z)
    {
        if (z < 0 || z > 1) return -1;
        if (x < 0 || x >= gridWidth) return -1;
        if (y < 0 || y >= gridHeight) return -1;

        return motorIndexLut[z, y, x];
    }

    /// <summary>
    /// 실수 좌표를 기반으로 bilinear interpolation으로 모터 강도 배열 생성.
    /// x, y : 0 ~ gridWidth-1 / 0 ~ gridHeight-1
    /// z    : 0 (Front), 1 (Back)
    /// intensityInput: intensityIs01이면 0~1, 아니면 0~100 스케일로 해석.
    /// </summary>
    public int[] MapPointToMotors(float x, float y, int z, float intensityInput)
    {
        // 예외 처리: z는 0 또는 1만 허용, 나머지는 clamp
        if (z != 0 && z != 1)
        {
            Debug.LogWarning($"[VestCoordinateMapper] Invalid z={z}. Clamped to 0 or 1.");
            z = Mathf.Clamp(z, 0, 1);
        }

        // x,y 범위 밖이면: 클램프 or 무시. 여기서는 클램프.
        float maxX = gridWidth - 1;
        float maxY = gridHeight - 1;
        x = Mathf.Clamp(x, 0f, maxX);
        y = Mathf.Clamp(y, 0f, maxY);

        // ✅ 여기서 y축을 한 번 뒤집어줌 (0,0을 왼쪽 아래로 쓰기 위해)
        //   외부에서 보는 좌표:  y=0 -> 아래줄
        //   실제 인덱스:        y=0 -> 위줄 이라고 가정
        y = maxY - y;

        // intensity 스케일링
        float baseIntensity;
        if (intensityIs01)
        {
            baseIntensity = Mathf.Clamp01(intensityInput) * maxMotorIntensity;
        }
        else
        {
            baseIntensity = Mathf.Clamp(intensityInput, 0f, maxMotorIntensity);
        }

        int[] motors = new int[32];  // TactSuit Pro: 32 motors

        // 정수 좌표 계산
        int x0 = Mathf.FloorToInt(x);
        int x1 = Mathf.Min(x0 + 1, (int)maxX);
        int y0 = Mathf.FloorToInt(y);
        int y1 = Mathf.Min(y0 + 1, (int)maxY);

        float tx = x - x0;   // 0~1
        float ty = y - y0;   // 0~1

        // bilinear weight
        float w00 = (1f - tx) * (1f - ty); // (x0, y0)
        float w10 = tx * (1f - ty);        // (x1, y0)
        float w01 = (1f - tx) * ty;        // (x0, y1)
        float w11 = tx * ty;               // (x1, y1)

        AddWeightedMotor(motors, x0, y0, z, baseIntensity * w00);
        AddWeightedMotor(motors, x1, y0, z, baseIntensity * w10);
        AddWeightedMotor(motors, x0, y1, z, baseIntensity * w01);
        AddWeightedMotor(motors, x1, y1, z, baseIntensity * w11);

        return motors;
    }

    private void AddWeightedMotor(int[] motors, int x, int y, int z, float intensity)
    {
        if (intensity <= 0f) return;

        int idx = GetMotorIndex(x, y, z);
        if (idx < 0 || idx >= motors.Length) return;

        int add = Mathf.RoundToInt(intensity);
        int current = motors[idx];
        int newValue = Mathf.Clamp(current + add, 0, maxMotorIntensity);

        motors[idx] = newValue;
    }
}
