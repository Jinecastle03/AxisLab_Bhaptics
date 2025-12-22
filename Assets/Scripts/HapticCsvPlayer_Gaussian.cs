using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Bhaptics.SDK2;

public class HapticCsvPlayer_Gaussian : MonoBehaviour
{
    [Header("Refs")]
    public VestCoordinateMapper_Gaussian mapper;

    [Tooltip("재생할 CSV 파일 (TextAsset).")]
    public TextAsset csvFile;

    [Header("Playback Settings")]
    [Tooltip("TactSuit Pro는 PositionType.Vest 사용")]
    public PositionType deviceType = PositionType.Vest;

    [Tooltip("CSV durationMs가 너무 짧으면 이 값으로 하한을 잡음(ms). 원하면 0~20 정도로 낮춰도 됨.")]
    public int minDurationMs = 100;

    [Tooltip("CSV 재생 중에 또 재생 요청이 들어오면 이전 것을 중단할지 여부")]
    public bool stopPreviousOnReplay = true;

    [Header("Tactile Motion Settings")]
    [Tooltip("모터 업데이트 간격(ms). 작을수록 더 부드러움(권장 10~20).")]
    public float sampleIntervalMs = 15f;

    [Tooltip("프레임당 PlayMotors duration(ms). 너무 길면 끝나고도 남는(꼬리) 느낌이 생김.")]
    public int frameDurationMs = 30;

    [Tooltip("이동 보간을 smoothstep으로 할지 (가속/감속 부드러움)")]
    public bool useSmoothStep = true;

    private Coroutine _playRoutine;

    [Serializable]
    public class HapticPoint
    {
        public float x;
        public float y;
        public float z;          // ✅ 0~1 (0=Front, 1=Back). CSV에 0/1 넣어도 float로 읽힘.
        public float intensity;  // 0~1
        public int durationMs;   // "이 점에서 다음 점까지 이동 시간"
    }

    [ContextMenu("Play CSV Once")]
    public void PlayCsvOnce()
    {
        if (csvFile == null)
        {
            Debug.LogError("[HapticCsvPlayer] csvFile is null.");
            return;
        }

        List<HapticPoint> points;
        try
        {
            points = ParseCsv(csvFile.text);
        }
        catch (Exception e)
        {
            Debug.LogError($"[HapticCsvPlayer] Failed to parse CSV: {e.Message}");
            return;
        }

        if (points.Count < 2)
        {
            Debug.LogWarning("[HapticCsvPlayer] Need at least 2 valid points to play tactile motion.");
            return;
        }

        if (stopPreviousOnReplay)
        {
            // ✅ 남아있는 진동/큐를 싹 정리
            BhapticsLibrary.StopAll();
        }

        if (_playRoutine != null)
        {
            StopCoroutine(_playRoutine);
            _playRoutine = null;
        }

        _playRoutine = StartCoroutine(PlayTactileMotion(points));
    }

    private List<HapticPoint> ParseCsv(string csvText)
    {
        var result = new List<HapticPoint>();

        using (StringReader reader = new StringReader(csvText))
        {
            string line;
            int lineNo = 0;
            while ((line = reader.ReadLine()) != null)
            {
                lineNo++;
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("#")) continue;

                // ✅ 헤더 자동 스킵: 첫 컬럼이 숫자가 아니면 건너뛰기
                // 예: "x,y,z,durationMs,intensity," 같은 라인
                if (!IsLineLikelyNumeric(line))
                    continue;

                // 콤마 split (뒤에 콤마가 있어도 빈 토큰 제거)
                var rawTokens = line.Split(',');
                var tokens = new List<string>(8);
                foreach (var t in rawTokens)
                {
                    var tt = t.Trim();
                    if (!string.IsNullOrEmpty(tt)) tokens.Add(tt);
                }

                // 기대 포맷: x,y,z,durationMs,intensity
                if (tokens.Count < 5)
                {
                    Debug.LogWarning($"[HapticCsvPlayer] Line {lineNo}: not enough columns (expected >=5). Skipped.");
                    continue;
                }

                try
                {
                    float x = float.Parse(tokens[0], CultureInfo.InvariantCulture);
                    float y = float.Parse(tokens[1], CultureInfo.InvariantCulture);
                    float z = float.Parse(tokens[2], CultureInfo.InvariantCulture); // ✅ float
                    int durationMs = int.Parse(tokens[3], CultureInfo.InvariantCulture);
                    float intensity = float.Parse(tokens[4], CultureInfo.InvariantCulture);

                    durationMs = Mathf.Max(durationMs, minDurationMs);

                    result.Add(new HapticPoint
                    {
                        x = x,
                        y = y,
                        z = z,
                        intensity = intensity,
                        durationMs = durationMs
                    });
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[HapticCsvPlayer] Line {lineNo}: parse error: {e.Message}. Skipped.");
                }
            }
        }

        return result;
    }

    private static bool IsLineLikelyNumeric(string line)
    {
        // 첫 토큰이 숫자로 파싱 가능하면 numeric line으로 판단
        // (헤더 "x,y,..." 같은 건 여기서 걸러짐)
        var first = line.Split(',')[0].Trim();
        return float.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>
    /// ✅ TACTILE 방식:
    /// 점 i -> 점 i+1 사이를 durationMs 동안 pos(t)로 "이동"시키며
    /// 매 프레임 mapper로 모터 분포를 다시 계산해 PlayMotors를 쏜다.
    /// </summary>
    private IEnumerator PlayTactileMotion(List<HapticPoint> points)
    {
        if (mapper == null)
        {
            Debug.LogError("[HapticCsvPlayer] mapper is null.");
            yield break;
        }

        int posType = (int)deviceType;

        float intervalSec = Mathf.Max(sampleIntervalMs, 5f) / 1000f;
        int perFrameDurationMs = Mathf.Max(frameDurationMs, 15);

        int[] motors;

        for (int i = 0; i < points.Count - 1; i++)
        {
            var p0 = points[i];
            var p1 = points[i + 1];

            int segMs = Mathf.Max(p0.durationMs, minDurationMs);
            float segSec = segMs / 1000f;

            float elapsed = 0f;
            while (elapsed < segSec)
            {
                float t = Mathf.Clamp01(elapsed / segSec);
                if (useSmoothStep)
                {
                    // smoothstep
                    t = t * t * (3f - 2f * t);
                }

                // ✅ 좌표를 연속적으로 이동
                float x = Mathf.Lerp(p0.x, p1.x, t);
                float y = Mathf.Lerp(p0.y, p1.y, t);
                float z = Mathf.Lerp(p0.z, p1.z, t); // ✅ front/back도 부드럽게 이동
                float intensity = Mathf.Lerp(p0.intensity, p1.intensity, t);

                // ✅ 지금 순간 좌표를 바로 모터로 맵핑 (이게 tactile 느낌의 핵심)
                motors = mapper.MapPointToMotors(x, y, z, intensity);

                // ✅ 길게 쏘지 말고 짧게 덮어쓰기 (끝나고 남는 잔진동 방지)
                BhapticsLibrary.PlayMotors(posType, motors, perFrameDurationMs);

                yield return new WaitForSeconds(intervalSec);
                elapsed += intervalSec;
            }
        }

        // ✅ 끝났을 때 남는 잔여를 확실히 종료
        BhapticsLibrary.StopAll();
        _playRoutine = null;
    }
}
