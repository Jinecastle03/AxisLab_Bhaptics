using UnityEngine;

/// <summary>
/// (x, y, z) 실수 좌표를 bHaptics Vest(32 motors) 모터 강도 배열로 바꿔주는
/// Gaussian Brush 버전 매퍼.
/// - x, y : 0 ~ (gridWidth - 1) 실수
/// - z    : 0 ~ 1 (0 = Front, 1 = Back)
/// 
/// Bilinear 대신 Gaussian kernel로 주변 모터에 weight를 분포시켜
/// 더 자연스럽고 부드러운 Tactile 느낌을 만든다.
/// </summary>
public class VestCoordinateMapper_Gaussian : MonoBehaviour
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

    [Header("Gaussian Brush Settings")]
    [Tooltip("가우시안 브러시의 σ (sigma). 값이 클수록 더 넓게 퍼짐.")]
    [Min(0.01f)]
    public float sigma = 0.6f;

    [Tooltip("weight가 이 값보다 작으면 계산 생략 (성능용 cutoff)")]
    [Range(0f, 0.1f)]
    public float weightCutoff = 0.001f;

    /// <summary>
    /// 총 모터 개수 (앞/뒤 2면)
    /// </summary>
    public int MotorCount => gridWidth * gridHeight * 2;

    /// <summary>
    /// (x,y,z,intensity01) 입력에 대해, 길이 MotorCount 의 모터 강도 배열을 리턴.
    /// intensity01 은 0~1 로 가정.
    /// </summary>
    public int[] MapPoint(float x, float y, float z, float intensity01)
    {
        int[] motors = new int[MotorCount];
        WritePointToArray(x, y, z, intensity01, motors);
        return motors;
    }

    /// <summary>
    /// 기존 코드에서 쓰던 이름과도 호환되도록 래퍼 추가
    /// </summary>
    public int[] MapPointToMotors(float x, float y, float z, float intensity01)
    {
        return MapPoint(x, y, z, intensity01);
    }

    /// <summary>
    /// outArray 에 모터 강도 값을 써 넣는 버전 (GC 줄이려면 이거 사용)
    /// </summary>
    public void WritePointToArray(float x, float y, float z, float intensity01, int[] outArray)
    {
        if (outArray == null || outArray.Length != MotorCount)
        {
            Debug.LogError($"VestCoordinateMapper_Gaussian: outArray 길이가 잘못됨. 필요: {MotorCount}");
            return;
        }

        // 초기화
        for (int i = 0; i < outArray.Length; i++)
        {
            outArray[i] = 0;
        }

        // 좌표 클램핑
        x = Mathf.Clamp(x, 0f, gridWidth - 1f);
        y = Mathf.Clamp(y, 0f, gridHeight - 1f);
        z = Mathf.Clamp01(z);

        // y축 방향 반전 (y=0 을 "아래", y=최대 를 "위"로 쓰고 싶어서)
        y = (gridHeight - 1) - y;

        // intensity 스케일링
        float baseIntensity = intensityIs01
            ? Mathf.Clamp01(intensity01) * maxMotorIntensity
            : Mathf.Clamp(intensity01, 0f, maxMotorIntensity);

        if (baseIntensity <= 0f)
            return;

        int planeSize = gridWidth * gridHeight;

        // Gaussian kernel 준비: exp( -d^2 / (2σ^2) )
        float s = Mathf.Max(sigma, 0.01f);
        float invTwoSigma2 = 1f / (2f * s * s);

        // z에 따른 앞/뒤 weight (선형)
        float wFront = 1f - z;   // z=0 -> front 100%
        float wBack  = z;        // z=1 -> back 100%

        // 모든 셀(i,j)에 대해 가우시안 weight 계산
        for (int jy = 0; jy < gridHeight; jy++)
        {
            for (int ix = 0; ix < gridWidth; ix++)
            {
                float dx = x - ix;
                float dy = y - jy;
                float dist2 = dx * dx + dy * dy;

                float wXY = Mathf.Exp(-dist2 * invTwoSigma2); // 0 ~ 1
                if (wXY < weightCutoff)
                    continue; // 너무 작은 값은 생략 (성능 + 노이즈 감소)

                int idxFront = jy * gridWidth + ix;
                int idxBack  = idxFront + planeSize;

                float addFront = baseIntensity * wXY * wFront;
                float addBack  = baseIntensity * wXY * wBack;

                int valF = outArray[idxFront] + Mathf.RoundToInt(addFront);
                int valB = outArray[idxBack]  + Mathf.RoundToInt(addBack);

                outArray[idxFront] = Mathf.Clamp(valF, 0, maxMotorIntensity);
                outArray[idxBack]  = Mathf.Clamp(valB, 0, maxMotorIntensity);
            }
        }
    }
}
