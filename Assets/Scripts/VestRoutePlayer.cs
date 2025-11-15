using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Bhaptics.SDK2;

public class VestRoutePlayer : MonoBehaviour
{
    public enum VestSide { Front, Back }

    public enum StrokeMode
    {
        Step,       // 점프식: 한 모터씩 순서대로
        CrossFade   // 부드럽게: 앞에서 뒤로 점점 페이드
    }

    [System.Serializable]
    public class HapticWaypoint
    {
        public VestSide side = VestSide.Front;
        [Range(0, 3)] public int row = 0;
        [Range(0, 3)] public int col = 0;
    }

    [Header("Route Settings")]
    public StrokeMode strokeMode = StrokeMode.CrossFade;
    public List<HapticWaypoint> waypoints = new List<HapticWaypoint>();

    [Tooltip("각 구간(waypoint→다음 waypoint) 재생 시간(초)")]
    public float segmentDuration = 0.3f;

    [Tooltip("진동 업데이트 간격(초). 작을수록 부드럽지만 호출 많아짐")]
    public float tickInterval = 0.03f;

    [Header("Haptic Params")]
    [Range(1, 100)] public int baseIntensity = 80;
    public int durationMs = 100;   // 각 tick당 진동 길이

    Coroutine playRoutine;

    // --- 좌표 <-> 인덱스 매핑 ---

    int CoordToIndex(VestSide s, int r, int c)
    {
        int local = r * 4 + c;                // 0~15
        int baseIndex = (s == VestSide.Front) ? 0 : 16;
        return baseIndex + local;             // Front:0~15, Back:16~31
    }

    void IndexToCoord(int idx, out VestSide s, out int r, out int c)
    {
        s = (idx < 16) ? VestSide.Front : VestSide.Back;
        int local = idx % 16;
        r = local / 4;
        c = local % 4;
    }

    // --- 외부에서 호출할 API ---

    [ContextMenu("Play Route Once")]
    public void PlayRouteOnce()
    {
        if (playRoutine != null) StopCoroutine(playRoutine);
        playRoutine = StartCoroutine(PlayRouteCoroutine());
    }

    IEnumerator PlayRouteCoroutine()
    {
        if (waypoints == null || waypoints.Count == 0)
            yield break;

        // waypoint → index로 미리 변환
        int count = waypoints.Count;
        int[] indices = new int[count];
        for (int i = 0; i < count; i++)
        {
            var wp = waypoints[i];
            indices[i] = CoordToIndex(wp.side, wp.row, wp.col);
        }

        // 구간별 재생
        if (strokeMode == StrokeMode.Step)
        {
            // 1) STEP 모드: 한 점씩 툭툭
            for (int i = 0; i < count; i++)
            {
                PlaySingleIndex(indices[i], baseIntensity);
                yield return new WaitForSeconds(segmentDuration);
            }
        }
        else
        {
            // 2) CROSS_FADE 모드: 이전→다음 선형 페이드
            for (int i = 0; i < count - 1; i++)
            {
                int from = indices[i];
                int to   = indices[i + 1];

                float elapsed = 0f;
                while (elapsed < segmentDuration)
                {
                    float t = Mathf.Clamp01(elapsed / segmentDuration);

                    int iFrom = Mathf.RoundToInt(baseIntensity * (1f - t));
                    int iTo   = Mathf.RoundToInt(baseIntensity * t);

                    PlayBlend(from, to, iFrom, iTo);

                    yield return new WaitForSeconds(tickInterval);
                    elapsed += tickInterval;
                }
            }
        }

        // 마지막에 다 꺼주기
        StopAllMotors();
        playRoutine = null;
    }

    // --- Low-level 동작 함수들 ---

    void PlaySingleIndex(int idx, int intensity)
    {
        int[] motors = new int[32];
        motors[idx] = intensity;

        BhapticsLibrary.PlayMotors(
            (int)PositionType.Vest,
            motors,
            durationMs
        );
    }

    void PlayBlend(int idxA, int idxB, int intensityA, int intensityB)
    {
        int[] motors = new int[32];
        if (idxA >= 0 && idxA < motors.Length) motors[idxA] = intensityA;
        if (idxB >= 0 && idxB < motors.Length) motors[idxB] = intensityB;

        BhapticsLibrary.PlayMotors(
            (int)PositionType.Vest,
            motors,
            durationMs
        );
    }

    void StopAllMotors()
    {
        int[] motors = new int[32]; // 전부 0
        BhapticsLibrary.PlayMotors(
            (int)PositionType.Vest,
            motors,
            50
        );
    }
}
