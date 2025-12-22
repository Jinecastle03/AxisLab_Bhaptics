using UnityEngine;
using UnityEngine.UI;

namespace AxisLabHaptics
{
    /// <summary>
    /// Front/Back 패널 위에 4x4 모터 마커(동그라미)를 자동 생성한다.
    /// - 시각화용 (클릭은 VestClickToPoint3D가 받음)
    /// - 마커는 Raycast Target을 꺼서 클릭 방해 안하게 함
    /// </summary>
    public class UiMotorGridSpawner : MonoBehaviour
    {
        [Header("Target Panels")]
        public RectTransform frontPanel;
        public RectTransform backPanel;

        [Header("Grid")]
        [Range(1, 16)] public int rows = 4;
        [Range(1, 16)] public int cols = 4;

        [Header("Marker")]
        public float markerSize = 18f;
        public Sprite circleSprite; // 없으면 기본 Image(사각형)로 뜸

        [Tooltip("If true, spawn automatically on Start.")]
        public bool spawnOnStart = true;

        [Tooltip("Clear existing markers under panel before spawning.")]
        public bool clearExisting = true;

        private const string MARKER_ROOT_NAME = "__MotorMarkers";

        private void Start()
        {
            if (!spawnOnStart) return;
            Spawn();
        }

        [ContextMenu("Spawn Markers Now")]
        public void Spawn()
        {
            if (frontPanel != null) SpawnOnPanel(frontPanel, "Front");
            if (backPanel != null) SpawnOnPanel(backPanel, "Back");
        }

        private void SpawnOnPanel(RectTransform panel, string label)
        {
            if (rows <= 0 || cols <= 0) return;

            // Root object
            Transform root = panel.Find(MARKER_ROOT_NAME);
            if (root != null && clearExisting)
            {
                DestroyImmediate(root.gameObject);
                root = null;
            }

            if (root == null)
            {
                var rootGO = new GameObject(MARKER_ROOT_NAME, typeof(RectTransform));
                rootGO.transform.SetParent(panel, false);
                root = rootGO.transform;

                var rt = (RectTransform)root;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            Rect r = panel.rect;
            float w = r.width;
            float h = r.height;

            for (int rr = 0; rr < rows; rr++)
            {
                float vy = (rows == 1) ? 0.5f : (float)rr / (rows - 1); // 0..1
                float y = Mathf.Lerp(-h * 0.5f, +h * 0.5f, vy);

                for (int cc = 0; cc < cols; cc++)
                {
                    float vx = (cols == 1) ? 0.5f : (float)cc / (cols - 1); // 0..1
                    float x = Mathf.Lerp(-w * 0.5f, +w * 0.5f, vx);

                    int index = rr * cols + cc;

                    var go = new GameObject($"M_{label}_{index}", typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(root, false);

                    var img = go.GetComponent<Image>();
                    img.sprite = circleSprite;
                    img.raycastTarget = false; // 클릭 방해 금지
                    img.preserveAspect = true;

                    var rt = go.GetComponent<RectTransform>();
                    rt.sizeDelta = new Vector2(markerSize, markerSize);
                    rt.anchoredPosition = new Vector2(x, y);
                }
            }

            Debug.Log($"Spawned UI markers on {label} panel: {rows * cols}");
        }
    }
}
