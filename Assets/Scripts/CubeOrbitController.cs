using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace AxisLabHaptics
{
    public class CubeOrbitController : MonoBehaviour
    {
        [Header("Target")]
        public Transform target; // 보통 이 스크립트가 붙은 오브젝트(큐브)면 비워도 됨

        [Header("Rotation")]
        public float rotateSpeed = 250f;
        public bool invertY = false;

        [Header("Zoom (optional)")]
        public Camera cam;
        public float zoomSpeed = 8f;
        public float minDistance = 1.5f;
        public float maxDistance = 6f;

        private Vector3 _lastMouse;
        private float _distance = 3f;

        void Awake()
        {
            if (target == null) target = transform;
            if (cam == null) cam = Camera.main;

            if (cam != null)
            {
                _distance = Vector3.Distance(cam.transform.position, target.position);
            }
        }

        void Update()
        {
            if (Mouse.current == null) return;

            // UI 위에서 드래그 중이면 회전 막기
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            // 마우스 드래그로 회전
            if (Mouse.current.rightButton.isPressed || Mouse.current.middleButton.isPressed)
            {
                Vector2 delta = Mouse.current.delta.ReadValue();

                float yaw = delta.x * rotateSpeed * Time.deltaTime;
                float pitch = delta.y * rotateSpeed * Time.deltaTime * (invertY ? 1f : -1f);

                // 월드 Y축으로 yaw, 로컬 X축으로 pitch
                target.Rotate(Vector3.up, yaw, Space.World);
                target.Rotate(Vector3.right, pitch, Space.Self);
            }

            // 휠 줌 (카메라가 있으면)
            if (cam != null)
            {
                float scroll = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    _distance -= scroll * 0.001f * zoomSpeed;
                    _distance = Mathf.Clamp(_distance, minDistance, maxDistance);

                    // 카메라를 target 기준으로 앞쪽(-forward) 방향으로 유지
                    cam.transform.position = target.position - cam.transform.forward * _distance;
                }
            }
        }
    }
}
