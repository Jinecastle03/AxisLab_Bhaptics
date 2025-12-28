using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

namespace AxisLabHaptics
{
    public class CubeFaceDebugger : MonoBehaviour
    {
        public Camera rayCamera;
        public TMP_Text hudText;

        [Header("Cube Size (local)")]
        public Vector3 cubeLocalSize = Vector3.one;

        [Header("Fix for mirrored / flipped transforms")]
        public bool flipLeftRight = true;   // ✅ 지금 너 케이스는 true가 맞을 확률 큼
        public bool flipFrontBack = false;
        public bool flipTopBottom = false;

        void Awake()
        {
            if (rayCamera == null) rayCamera = Camera.main;
        }

        void Update()
        {
            if (rayCamera == null || Mouse.current == null) return;

            Vector2 screenPos = Mouse.current.position.ReadValue();
            Ray ray = rayCamera.ScreenPointToRay(screenPos);

            if (!Physics.Raycast(ray, out RaycastHit hit, 500f) || hit.collider == null)
            {
                if (hudText) hudText.text = "";
                return;
            }

            if (hit.collider.gameObject != gameObject)
            {
                if (hudText) hudText.text = "";
                return;
            }

            // local normal
            Vector3 n = transform.InverseTransformDirection(hit.normal).normalized;

            // ✅ optional flips
            if (flipLeftRight) n.x *= -1f;
            if (flipTopBottom) n.y *= -1f;
            if (flipFrontBack) n.z *= -1f;

            string face = FaceName(n);

            // 0..1 coords
            Vector3 local = transform.InverseTransformPoint(hit.point);
            Vector3 half = cubeLocalSize * 0.5f;

            float lx = Mathf.Clamp(local.x / half.x, -1f, 1f);
            float ly = Mathf.Clamp(local.y / half.y, -1f, 1f);
            float lz = Mathf.Clamp(local.z / half.z, -1f, 1f);

            float x01 = (lx + 1f) * 0.5f;
            float y01 = (ly + 1f) * 0.5f;
            float z01 = (lz + 1f) * 0.5f;

            if (hudText)
            {
                hudText.text =
                    $"Face: {face}\n" +
                    $"Normal(local): {n.x:F2}, {n.y:F2}, {n.z:F2}\n" +
                    $"Hit 0..1: x={x01:F2}, y={y01:F2}, z={z01:F2}";
            }
        }

        private string FaceName(Vector3 n)
        {
            float ax = Mathf.Abs(n.x);
            float ay = Mathf.Abs(n.y);
            float az = Mathf.Abs(n.z);

            if (az >= ax && az >= ay) return (n.z > 0f) ? "FRONT (+Z)" : "BACK (-Z)";
            if (ax >= ay && ax >= az) return (n.x > 0f) ? "RIGHT (+X)" : "LEFT (-X)";
            return (n.y > 0f) ? "TOP (+Y)" : "BOTTOM (-Y)";
        }
    }
}
