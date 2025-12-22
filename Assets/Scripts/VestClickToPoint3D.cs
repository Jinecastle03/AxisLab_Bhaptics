using UnityEngine;
using UnityEngine.EventSystems;

namespace AxisLabHaptics
{
    /// <summary>
    /// UI 클릭을 Grid 좌표(연속)로 변환:
    ///  x: 0..(cols-1) float
    ///  y: 0..(rows-1) float
    ///  z: 0..1 (front/back)
    /// </summary>
    public class VestClickToPoint3D : MonoBehaviour, IPointerClickHandler
    {
        [Header("UI")]
        public RectTransform vestImageRect;

        [Header("Front / Back")]
        public bool isFront = true;

        [Header("Debug")]
        public bool logOnClick = true;

        public System.Action<Vector3> OnClickPoint3D;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (vestImageRect == null)
            {
                Debug.LogWarning("[VestClickToPoint3D] vestImageRect is null!");
                return;
            }

            var s = SessionSettings.Instance;
            if (s == null)
            {
                Debug.LogWarning("[VestClickToPoint3D] SessionSettings.Instance is null!");
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                vestImageRect, eventData.position, eventData.pressEventCamera, out Vector2 local))
                return;

            Rect r = vestImageRect.rect;

            // local -> uv(0..1)
            float u = (local.x - r.xMin) / Mathf.Max(1e-6f, r.width);
            float v = (local.y - r.yMin) / Mathf.Max(1e-6f, r.height);

            // clamp uv just in case
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            // uv -> grid continuous
            float gx = u * (s.cols - 1);
            float gy = v * (s.rows - 1);

            // z: 0..1 (front/back)
            float gz;
            if (s.frontIsZ0)
                gz = isFront ? 0f : 1f;
            else
                gz = isFront ? 1f : 0f;

            Vector3 p = new Vector3(gx, gy, gz);

            if (logOnClick)
            {
                Debug.Log($"[VestClickToPoint3D] CLICK {(isFront ? "FRONT" : "BACK")} uv=({u:F3},{v:F3}) -> grid=({p.x:F3},{p.y:F3},{p.z:F0})");
            }

            OnClickPoint3D?.Invoke(p);
        }
    }
}
