using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace AxisLabHaptics
{
    public class CubeClickToPoint3D : MonoBehaviour
    {
        [Header("Dependencies")]
        public TraceRecorderDual recorder;

        [Header("Grid Scale (store like UI grid)")]
        public int gridW = 4;
        public int gridH = 4;
        public bool flipY = true;

        [Header("Cube Size (local)")]
        public Vector3 cubeLocalSize = Vector3.one;

        [Header("Raycast")]
        public Camera rayCamera;

        void Awake()
        {
            if (rayCamera == null) rayCamera = Camera.main;
        }

        void Update()
        {
            // ✅ New Input System: left mouse pressed this frame
            if (Mouse.current == null) return;
            if (!Mouse.current.leftButton.wasPressedThisFrame) return;

            // UI 위 클릭이면 무시
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            if (rayCamera == null) return;

            Vector2 screenPos = Mouse.current.position.ReadValue();
            Ray ray = rayCamera.ScreenPointToRay(screenPos);

            if (!Physics.Raycast(ray, out RaycastHit hit, 500f)) return;
            if (hit.collider.gameObject != gameObject) return;

            Vector3 local = transform.InverseTransformPoint(hit.point);

            Vector3 half = cubeLocalSize * 0.5f;
            float lx = Mathf.Clamp(local.x / half.x, -1f, 1f);
            float ly = Mathf.Clamp(local.y / half.y, -1f, 1f);
            float lz = Mathf.Clamp(local.z / half.z, -1f, 1f);

            float x01 = 0.5f;
            float y01 = (ly + 1f) * 0.5f;
            float z01 = 0.5f;

            Vector3 n = transform.InverseTransformDirection(hit.normal).normalized;

            float ax = Mathf.Abs(n.x);
            float ay = Mathf.Abs(n.y);
            float az = Mathf.Abs(n.z);

            if (az >= ax && az >= ay)
            {
                // Front(+Z) / Back(-Z)
                bool front = n.z > 0f;
                x01 = (lx + 1f) * 0.5f;
                z01 = front ? 0f : 1f;
            }
            else if (ax >= ay && ax >= az)
            {
                // Left/Right => depth를 z로 연속 저장
                bool right = n.x > 0f;
                x01 = right ? 1f : 0f;
                z01 = (lz + 1f) * 0.5f;
            }
            else
            {
                // Top/Bottom (선택)
                x01 = (lx + 1f) * 0.5f;
                z01 = (lz + 1f) * 0.5f;
            }

            if (flipY) y01 = 1f - y01;

            // 저장은 기존 grid 스케일(0..gridW-1)로
            float gx = (gridW <= 1) ? 0f : x01 * (gridW - 1f);
            float gy = (gridH <= 1) ? 0f : y01 * (gridH - 1f);

            Vector3 pos = new Vector3(gx, gy, z01);

            if (recorder != null) recorder.RecordPoint(pos);
            else Debug.LogWarning("[CubeClickToPoint3D] recorder is null");
        }
    }
}
