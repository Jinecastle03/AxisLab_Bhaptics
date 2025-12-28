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
        [Tooltip("If false: front z=1, back z=0. If true: front z=0, back z=1.")]
        public bool frontIsZ0 = false;

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
                z01 = front ? 0f : 1f; // front=0, back=1 (이후 플래그로 뒤집음)
            }
            else if (ax >= ay && ax >= az)
            {
                // Left/Right => depth를 z로 연속 저장 (front(+Z)=0, back(-Z)=1)
                bool right = n.x > 0f;
                x01 = right ? 1f : 0f;
                z01 = 0.5f * (1f - lz); // lz(+1)=front ->0, lz(-1)=back ->1
            }
            else
            {
                // Top/Bottom => depth를 z로 사용 (front(+Z)=0, back(-Z)=1)
                x01 = (lx + 1f) * 0.5f;
                z01 = 0.5f * (1f - lz);
            }

            if (flipY) y01 = 1f - y01;

            // 저장은 기존 grid 스케일(0..gridW-1)로
            float gx = (gridW <= 1) ? 0f : x01 * (gridW - 1f);
            float gy = (gridH <= 1) ? 0f : y01 * (gridH - 1f);

            // Apply global convention if available
            bool frontZ0 = frontIsZ0;
            if (SessionSettings.Instance != null)
                frontZ0 = SessionSettings.Instance.frontIsZ0;

            if (!frontZ0)
                z01 = 1f - z01; // front=z=1, back=z=0

            Vector3 pos = new Vector3(gx, gy, z01);

            if (recorder != null) recorder.RecordPoint(pos);
            else Debug.LogWarning("[CubeClickToPoint3D] recorder is null");
        }
    }
}
