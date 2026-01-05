using System.Collections;
using UnityEngine;
using Bhaptics.SDK2;

public class YawSpinMotors : MonoBehaviour
{
    public enum BandMode { Bottom1, Middle2, Top1 }

    [Header("Run")]
    public bool playOnStart = true;
    public bool isPlaying = false;

    [Header("Angular Velocity (deg/sec)")]
    public float omegaDegPerSec = 60f; // +: clockwise

    [Header("Timing")]
    [Range(0.01f, 0.05f)] public float updateIntervalSec = 0.02f;
    [Range(100, 400)] public int durationMs = 220;

    [Header("Band")]
    public BandMode bandMode = BandMode.Middle2;

    [Header("Intensity")]
    [Range(1, 100)] public int maxIntensity = 80;

    [Tooltip("기본 floor(전환 참여 모터에만 적용)")]
    [Range(0, 40)] public int floorIntensity = 15;

    [Tooltip("Side(앞<->뒤) 구간에서만 더 큰 floor 적용")]
    [Range(0, 60)] public int sideFloorIntensity = 22;

    [Header("Motion Shaping")]
    [Range(0.2f, 4f)] public float gammaFB = 1.0f;

    [Tooltip("Side 구간에서 depth(front<->back) 진행을 얼마나 '끌고 갈지'")]
    [Range(0.2f, 4f)] public float gammaSideDepth = 2.2f;

    [Tooltip("Prev(보조) 비중")]
    [Range(0.0f, 1.0f)] public float prevBoost = 0.6f;

    [Header("Side Blend Safety")]
    [Tooltip("Side에서 반대면도 최소 이 비율만은 켜지게(0이면 한쪽이 완전 0될 수 있음)")]
    [Range(0.0f, 0.3f)] public float sideMinOverlap = 0.05f;

    [Header("Mapping Fix")]
    public bool flipBackXOnly = true;
    public bool flipAllX = false;
    public bool invertDirection = false;

    // internal
    private float phase01 = 0f;
    private Coroutine loopCo;

    // ring: side를 길게 잡되, 이제 side 판단은 x==0/x==3 영역으로 함
    private readonly (int x, int z)[] ringClockwise = new (int x, int z)[]
    {
        // Front
        (0,0),(1,0),(2,0),(3,0),

        // Right side (x=3): front 영역 -> back 영역 (long)
        (3,0),(3,0),(3,0),(3,0),
        (3,1),(3,1),(3,1),(3,1),

        // Back
        (3,1),(2,1),(1,1),(0,1),

        // Left side (x=0): back 영역 -> front 영역 (long)
        (0,1),(0,1),(0,1),(0,1),
        (0,0),(0,0),(0,0),(0,0),
    };

    void Start()
    {
        if (playOnStart) StartPlay();
    }

    void OnDisable()
    {
        StopPlay();
    }

    public void StartPlay()
    {
        if (loopCo != null) StopCoroutine(loopCo);
        isPlaying = true;
        loopCo = StartCoroutine(PlayLoop());
    }

    public void StopPlay()
    {
        isPlaying = false;
        if (loopCo != null) StopCoroutine(loopCo);
        loopCo = null;

        var zeros = new int[32];
        BhapticsLibrary.PlayMotors((int)PositionType.Vest, zeros, 120);
    }

    IEnumerator PlayLoop()
    {
        var motors = new int[32];

        while (isPlaying)
        {
            System.Array.Clear(motors, 0, motors.Length);

            // phase update
            float sign = Mathf.Sign(omegaDegPerSec);
            float absOmega = Mathf.Abs(omegaDegPerSec);
            float revPerSec = absOmega / 360f;
            float dir = (invertDirection ? -1f : 1f) * sign;
            phase01 = Mathf.Repeat(phase01 + dir * revPerSec * updateIntervalSec, 1f);

            // ring index
            int N = ringClockwise.Length;
            float f = phase01 * N;
            int i = Mathf.FloorToInt(f) % N;
            int iNext = (i + 1) % N;
            int iPrev = (i - 1 + N) % N;
            float alpha = f - Mathf.Floor(f); // 0..1

            // ✅ Side는 "z change"가 아니라 "x==0 or x==3 영역"으로 정의
            bool isSide = (ringClockwise[i].x == 0 || ringClockwise[i].x == 3);

            // alpha shaping (FB는 gammaFB, side도 일단 부드럽게 cosine)
            float t = Mathf.Pow(Mathf.Clamp01(alpha), gammaFB);
            t = CosineEase(t);

            // 3-motor weights (smooth)
            float s = Mathf.Sin(0.5f * Mathf.PI * t);
            float c = Mathf.Cos(0.5f * Mathf.PI * t);

            float wI = c * c;
            float wNext = s * s;
            float wPrev = prevBoost * (2f * s * c);

            float sum = wPrev + wI + wNext;
            wPrev /= sum; wI /= sum; wNext /= sum;

            // ✅ Side에서 front/back 동시 출력용 depthT 계산
            // Right side block: indices 4..11 (길이 8)
            // Left side block:  indices 16..23 (길이 8)
            float depthT = 0f;
            if (isSide)
                depthT = ComputeSideDepthT(i, alpha, N); // 0..1

            var ys = GetBandRows(bandMode);
            foreach (int y in ys)
            {
                // 기본 3개를 "노드의 z"가 아니라,
                // Side면 동일 (x,y)에 대해 front/back을 depthT로 분배해서 동시에 울림
                AddNode(motors, ringClockwise[iPrev].x, y, ringClockwise[iPrev].z, wPrev, isSide, depthT);
                AddNode(motors, ringClockwise[i].x,     y, ringClockwise[i].z,     wI,    isSide, depthT);
                AddNode(motors, ringClockwise[iNext].x, y, ringClockwise[iNext].z, wNext, isSide, depthT);
            }

            for (int k = 0; k < 32; k++)
                motors[k] = Mathf.Clamp(motors[k], 0, 100);

            BhapticsLibrary.PlayMotors((int)PositionType.Vest, motors, durationMs);
            yield return new WaitForSeconds(updateIntervalSec);
        }
    }

    // Side depth 진행도 계산:
    // - Right side(x=3)에서는 front->back (0->1)
    // - Left side(x=0)에서는 back->front (1->0) 가 되어야 함
    float ComputeSideDepthT(int i, float alpha, int N)
    {
        // ring 구성 고정 기준:
        // Front(0..3), RightSide(4..11), Back(12..15), LeftSide(16..23)
        // (현재 ringClockwise 배열과 동일)
        int rightStart = 4, rightLen = 8;
        int leftStart = 16, leftLen = 8;

        float raw;
        if (i >= rightStart && i < rightStart + rightLen)
        {
            // 0..1
            raw = ((i - rightStart) + alpha) / rightLen;
        }
        else if (i >= leftStart && i < leftStart + leftLen)
        {
            // 0..1 이지만 방향은 back->front 이므로 뒤집기
            raw = ((i - leftStart) + alpha) / leftLen;
            raw = 1f - raw;
        }
        else
        {
            // 혹시 boundary에서 살짝 걸리면, 해당 노드의 z로 결정
            raw = ringClockwise[i].z == 0 ? 0f : 1f;
        }

        // depth를 더 "끌고" 가게 (감마 + cosine)
        float d = Mathf.Pow(Mathf.Clamp01(raw), gammaSideDepth);
        d = CosineEase(d);
        return d;
    }

    static float CosineEase(float t)
    {
        t = Mathf.Clamp01(t);
        return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t);
    }

    static int[] GetBandRows(BandMode mode)
    {
        switch (mode)
        {
            case BandMode.Bottom1: return new[] { 0 };
            case BandMode.Top1:    return new[] { 3 };
            default:               return new[] { 1, 2 };
        }
    }

    void AddNode(int[] motors, int x, int y, int zHint, float w01, bool isSide, float depthT)
    {
        if (!isSide)
        {
            AddMotor(motors, x, y, zHint, w01, false);
            return;
        }

        // ✅ Side에서는 front/back을 동시에 출력 (같은 x,y)
        // depthT: 0이면 front, 1이면 back
        float backW = Mathf.Clamp01(depthT);
        float frontW = 1f - backW;

        // 최소 겹침 보장(완전 0 방지)
        frontW = Mathf.Max(frontW, sideMinOverlap);
        backW  = Mathf.Max(backW,  sideMinOverlap);

        // 다시 정규화
        float s = frontW + backW;
        frontW /= s; backW /= s;

        AddMotor(motors, x, y, 0, w01 * frontW, true);
        AddMotor(motors, x, y, 1, w01 * backW,  true);
    }

    void AddMotor(int[] motors, int x, int y, int z, float w01, bool isSide)
    {
        int v = StrengthFromWeight(w01, isSide);
        if (v <= 0) return;

        int idx = MotorIndex(x, y, z);
        motors[idx] = Mathf.Max(motors[idx], v);
    }

    int StrengthFromWeight(float w01, bool isSide)
    {
        w01 = Mathf.Clamp01(w01);
        if (w01 < 0.02f) return 0;

        int floor = isSide ? sideFloorIntensity : floorIntensity;
        int v = Mathf.RoundToInt(floor + (maxIntensity - floor) * w01);
        return Mathf.Clamp(v, 0, 100);
    }

    int MotorIndex(int x, int y, int z)
    {
        x = Mathf.Clamp(x, 0, 3);
        y = Mathf.Clamp(y, 0, 3);

        if (flipAllX) x = 3 - x;
        if (flipBackXOnly && z == 1) x = 3 - x;

        if (z == 0)
        {
            int[,] front = new int[4, 4] {
                {15,14,13,12},
                {11,10, 9, 8},
                { 7, 6, 5, 4},
                { 3, 2, 1, 0},
            };
            return front[y, x];
        }
        else
        {
            int[,] back = new int[4, 4] {
                {28,29,30,31},
                {24,25,26,27},
                {20,21,22,23},
                {16,17,18,19},
            };
            return back[y, x];
        }
    }

    // ---------- UI Hooks (그대로 둬도 됨) ----------
    public void UI_SetOmegaText(string s)
    {
        if (float.TryParse(s, out var v)) omegaDegPerSec = v;
    }

    public void UI_SetGammaFBText(string s)
    {
        if (float.TryParse(s, out var v)) gammaFB = Mathf.Clamp(v, 0.2f, 4f);
    }

    public void UI_SetGammaSideText(string s)
    {
        if (float.TryParse(s, out var v)) gammaSideDepth = Mathf.Clamp(v, 0.2f, 4f);
    }

    public void UI_SetBandMode(int index) => bandMode = (BandMode)index;

    public void UI_Start() => StartPlay();
    public void UI_Stop()  => StopPlay();
}
