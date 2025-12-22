using UnityEngine;

/// <summary>
/// (x,y,z) 실수 좌표를 bHaptics Vest(32 motors) 모터 강도 배열로 바꿔주는 매퍼.
/// - x,y : 0 ~ (gridWidth-1) 실수
/// - z01 : 0 ~ 1 (0 = Front, 1 = Back)  ✅ 실수 지원
/// </summary>
public class VestCoordinateMapper : MonoBehaviour
{
    [Header("Grid Settings")]
    public int gridWidth = 4;
    public int gridHeight = 4;

    [Header("Intensity Settings")]
    [Range(1, 100)]
    public int maxMotorIntensity = 100;

    public bool intensityIs01 = true;

    // [z, y, x]  where z=0(front), z=1(back)
    private int[,,] motorIndexLut;

    private void Awake()
    {
        InitMotorIndexLut();
    }

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
        // 실제 실험실 매핑이 있다면 여기 LUT를 교체해서 쓰면 됨.
    }

    public int GetMotorIndex(int x, int y, int z)
    {
        if (z < 0 || z > 1) return -1;
        if (x < 0 || x >= gridWidth) return -1;
        if (y < 0 || y >= gridHeight) return -1;
        return motorIndexLut[z, y, x];
    }

    /// <summary>
    /// ✅ 권장: z01(0~1) 실수 입력을 받아 front/back을 블렌딩
    /// </summary>
    public int[] MapPointToMotors(float x, float y, float z01, float intensityInput)
    {
        // clamp
        float maxX = gridWidth - 1;
        float maxY = gridHeight - 1;
        x = Mathf.Clamp(x, 0f, maxX);
        y = Mathf.Clamp(y, 0f, maxY);
        z01 = Mathf.Clamp01(z01);

        // ✅ y축 뒤집기 (y=0이 아래)
        y = maxY - y;

        // intensity scaling
        float baseIntensity = intensityIs01
            ? Mathf.Clamp01(intensityInput) * maxMotorIntensity
            : Mathf.Clamp(intensityInput, 0f, maxMotorIntensity);

        int[] motors = new int[32];

        // bilinear in (x,y)
        int x0 = Mathf.FloorToInt(x);
        int x1 = Mathf.Min(x0 + 1, (int)maxX);
        int y0 = Mathf.FloorToInt(y);
        int y1 = Mathf.Min(y0 + 1, (int)maxY);

        float tx = x - x0;
        float ty = y - y0;

        float w00 = (1f - tx) * (1f - ty);
        float w10 = tx * (1f - ty);
        float w01 = (1f - tx) * ty;
        float w11 = tx * ty;

        // ✅ z-blend: front/back weights
        float wFront = 1f - z01;
        float wBack = z01;

        // front(z=0)
        AddWeightedMotor(motors, x0, y0, 0, baseIntensity * w00 * wFront);
        AddWeightedMotor(motors, x1, y0, 0, baseIntensity * w10 * wFront);
        AddWeightedMotor(motors, x0, y1, 0, baseIntensity * w01 * wFront);
        AddWeightedMotor(motors, x1, y1, 0, baseIntensity * w11 * wFront);

        // back(z=1)
        AddWeightedMotor(motors, x0, y0, 1, baseIntensity * w00 * wBack);
        AddWeightedMotor(motors, x1, y0, 1, baseIntensity * w10 * wBack);
        AddWeightedMotor(motors, x0, y1, 1, baseIntensity * w01 * wBack);
        AddWeightedMotor(motors, x1, y1, 1, baseIntensity * w11 * wBack);

        return motors;
    }

    /// <summary>
    /// ✅ 기존 코드 호환용: z=0/1 int 입력
    /// </summary>
    public int[] MapPointToMotors(float x, float y, int z, float intensityInput)
    {
        float z01 = (z <= 0) ? 0f : 1f;
        return MapPointToMotors(x, y, z01, intensityInput);
    }

    private void AddWeightedMotor(int[] motors, int x, int y, int z, float intensity)
    {
        if (intensity <= 0f) return;

        int idx = GetMotorIndex(x, y, z);
        if (idx < 0 || idx >= motors.Length) return;

        int add = Mathf.RoundToInt(intensity);
        int newValue = Mathf.Clamp(motors[idx] + add, 0, maxMotorIntensity);
        motors[idx] = newValue;
    }
}
