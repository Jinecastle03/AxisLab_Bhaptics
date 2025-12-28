using UnityEngine;

namespace AxisLabHaptics
{
    public class FaceLabelBillboard : MonoBehaviour
    {
        public Camera cam;

        void Awake()
        {
            if (cam == null) cam = Camera.main;
        }

        void LateUpdate()
        {
            if (cam == null) return;
            // 카메라가 보는 방향으로 글자가 정면을 향하게
            transform.forward = cam.transform.forward;
        }
    }
}
