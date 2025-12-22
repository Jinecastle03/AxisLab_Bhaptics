using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AxisLabHaptics
{
    public class TraceFilePanel : MonoBehaviour
    {
        public HapticTraceAsset trace;

        [Header("File Name (Assets/data/)")]
        public string fileName = "trace_export.csv";

        /// <summary>
        /// Assets/data/ 아래로 CSV 저장 (Editor 전용)
        /// </summary>
        public void ExportToAssetsData()
        {
#if UNITY_EDITOR
            if (trace == null)
            {
                Debug.LogWarning("[TraceFilePanel] trace is null");
                return;
            }

            string dataDir = Path.Combine(Application.dataPath, "data");
            if (!Directory.Exists(dataDir))
                Directory.CreateDirectory(dataDir);

            string path = Path.Combine(dataDir, fileName);

            CsvIO.ExportTraceToCsv(trace, path);

            // Unity가 새 파일을 인식하게 함
            AssetDatabase.Refresh();

            Debug.Log($"[TraceFilePanel] Exported CSV to: {path}");
#else
            Debug.LogWarning("[TraceFilePanel] ExportToAssetsData works only in Unity Editor.");
#endif
        }

        /// <summary>
        /// Assets/data/ 에서 CSV 불러오기 (Editor 전용)
        /// </summary>
        public void ImportFromAssetsData(bool clearFirst = true)
        {
#if UNITY_EDITOR
            if (trace == null)
            {
                Debug.LogWarning("[TraceFilePanel] trace is null");
                return;
            }

            string path = Path.Combine(Application.dataPath, "data", fileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[TraceFilePanel] CSV not found: {path}");
                return;
            }

            CsvIO.ImportTraceFromCsv(trace, path, clearFirst);

            Debug.Log($"[TraceFilePanel] Imported CSV from: {path}");
#else
            Debug.LogWarning("[TraceFilePanel] ImportFromAssetsData works only in Unity Editor.");
#endif
        }

        /// <summary>
        /// 저장 위치 콘솔 출력
        /// </summary>
        public void PrintAssetsDataPath()
        {
#if UNITY_EDITOR
            string path = Path.Combine(Application.dataPath, "data");
            Debug.Log($"[TraceFilePanel] Assets/data path = {path}");
#else
            Debug.LogWarning("[TraceFilePanel] Editor only.");
#endif
        }
    }
}
