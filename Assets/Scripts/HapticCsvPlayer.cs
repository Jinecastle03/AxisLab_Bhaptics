using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Bhaptics.SDK2;

public class HapticCsvPlayer : MonoBehaviour
{
    [Header("Refs")]
    public VestCoordinateMapper mapper;

    [Tooltip("재생할 CSV 파일 (TextAsset). 또는 경로를 따로 입력할 수도 있음.")]
    public TextAsset csvFile;

    [Header("Playback Settings")]
    [Tooltip("TactSuit Pro는 PositionType.Vest 사용")]
    public PositionType deviceType = PositionType.Vest;

    [Tooltip("한 점 진동 최소 duration (ms). 너무 짧으면 전달이 잘 안됨.")]
    public int minDurationMs = 100;

    [Tooltip("CSV 재생 중에 또 재생 요청이 들어오면 이전 것을 중단할지 여부")]
    public bool stopPreviousOnReplay = true;

    private Coroutine _playRoutine;

    [Serializable]
    public class HapticPoint
    {
        public float x;
        public float y;
        public int z;           // 0 = Front, 1 = Back
        public float intensity; // 0~1
        public int durationMs;
    }

    // ----------------- CSV 파싱 & 재생 -----------------

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

        if (points.Count == 0)
        {
            Debug.LogWarning("[HapticCsvPlayer] No valid haptic points in CSV.");
            return;
        }

        if (stopPreviousOnReplay && _playRoutine != null)
        {
            StopCoroutine(_playRoutine);
            _playRoutine = null;
        }

        _playRoutine = StartCoroutine(PlaySequence(points));
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

                // 빈 줄, 주석(#로 시작) 스킵
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("#")) continue;

                string[] tokens = line.Split(',');
                if (tokens.Length < 5)
                {
                    Debug.LogWarning($"[HapticCsvPlayer] Line {lineNo}: not enough columns (expected 5). Skipped.");
                    continue;
                }

                // float parsing 시 invariant culture 사용 (점/콤마 문제 회피)
                try
                {
                    float x = float.Parse(tokens[0].Trim(), CultureInfo.InvariantCulture);
                    float y = float.Parse(tokens[1].Trim(), CultureInfo.InvariantCulture);
                    int z = int.Parse(tokens[2].Trim(), CultureInfo.InvariantCulture);
                    float intensity = float.Parse(tokens[3].Trim(), CultureInfo.InvariantCulture);
                    int durationMs = int.Parse(tokens[4].Trim(), CultureInfo.InvariantCulture);

                    if (durationMs < minDurationMs)
                    {
                        Debug.LogWarning($"[HapticCsvPlayer] Line {lineNo}: duration {durationMs}ms < min {minDurationMs}ms. Clamped.");
                        durationMs = minDurationMs;
                    }

                    var point = new HapticPoint
                    {
                        x = x,
                        y = y,
                        z = z,
                        intensity = intensity,
                        durationMs = durationMs
                    };
                    result.Add(point);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[HapticCsvPlayer] Line {lineNo}: parse error: {e.Message}. Skipped.");
                }
            }
        }

        return result;
    }

    private IEnumerator PlaySequence(List<HapticPoint> points)
    {
        if (mapper == null)
        {
            Debug.LogError("[HapticCsvPlayer] mapper is null.");
            yield break;
        }

        foreach (var p in points)
        {
            int[] motors = mapper.MapPointToMotors(p.x, p.y, p.z, p.intensity);

            if (!IsAllZero(motors))
            {
                int pos = (int)deviceType;
                int durationMs = Mathf.Max(p.durationMs, minDurationMs);
                BhapticsLibrary.PlayMotors(pos, motors, durationMs);
            }
            else
            {
                Debug.LogWarning($"[HapticCsvPlayer] Point ({p.x},{p.y},z={p.z}) produced all-zero motors. Skipping PlayMotors.");
            }

            // duration 만큼 기다린 뒤 다음 점 실행 (겹치지 않는 단순 시퀀스)
            yield return new WaitForSeconds(p.durationMs / 1000f);
        }

        _playRoutine = null;
    }

    private bool IsAllZero(int[] arr)
    {
        for (int i = 0; i < arr.Length; i++)
        {
            if (arr[i] != 0) return false;
        }
        return true;
    }
}
