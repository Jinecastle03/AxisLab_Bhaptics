using UnityEngine;

namespace AxisLabHaptics
{
    /// <summary>
    /// MotorLayoutAsset(Front/Back)에 4x4 "그리드 좌표"를 채운다.
    /// motorPos[index] = (col, row, z)
    ///  col: 0..cols-1
    ///  row: 0..rows-1
    ///  z  : front/back -> 0/1
    /// </summary>
    public class LayoutAutoBuilder : MonoBehaviour
    {
        [Header("Assets to fill")]
        public MotorLayoutAsset frontLayout;
        public MotorLayoutAsset backLayout;

        [Header("Options")]
        public bool generateOnStart = true;

        private void Start()
        {
            if (generateOnStart) Generate();
        }

        [ContextMenu("Generate Layouts Now")]
        public void Generate()
        {
            var s = SessionSettings.Instance;
            if (s == null)
            {
                Debug.LogError("[LayoutAutoBuilder] SessionSettings.Instance is null");
                return;
            }

            if (frontLayout == null || backLayout == null)
            {
                Debug.LogError("[LayoutAutoBuilder] Assign both frontLayout and backLayout assets.");
                return;
            }

            int rows = s.rows;
            int cols = s.cols;

            var front = new Vector3[rows * cols];
            var back = new Vector3[rows * cols];

            float frontZ = s.frontIsZ0 ? 0f : 1f;
            float backZ  = s.frontIsZ0 ? 1f : 0f;

            // index = row*cols + col  (row: 0(bottom)~3(top) 라고 할지, UI 방향 맞추려면 여기서 통일해야 함)
            // 지금은 y가 v*(rows-1)라서 "아래=0, 위=rows-1"로 자연스럽게 맞음.
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    front[i] = new Vector3(c, r, frontZ);
                    back[i]  = new Vector3(c, r, backZ);
                }
            }

            frontLayout.motorPos = front;
            backLayout.motorPos = back;

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(frontLayout);
            UnityEditor.EditorUtility.SetDirty(backLayout);
            UnityEditor.AssetDatabase.SaveAssets();
#endif

            Debug.Log($"[LayoutAutoBuilder] Generated grid layouts rows={rows} cols={cols} frontZ={frontZ} backZ={backZ}");
        }
    }
}
