using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>沿圓形軌道繞著某個中心點移動（標題畫面飛過的衛星）。</summary>
    public sealed class OrbitAround : MonoBehaviour
    {
        public Transform center;
        public float radius = 1.3f;
        public float degreesPerSecond = 3f;
        public Vector3 axis = new Vector3(0.2f, 1f, 0.1f);
        public float phase;

        void Update()
        {
            if (center == null) return;
            phase += degreesPerSecond * Time.deltaTime;
            var ax = axis.normalized;
            var start = Vector3.Cross(ax, Vector3.right).sqrMagnitude > 1e-4f ? Vector3.Cross(ax, Vector3.right).normalized : Vector3.forward;
            var rot = Quaternion.AngleAxis(phase, ax);
            var pos = rot * start * radius;
            var vel = Vector3.Cross(ax, pos).normalized;
            transform.position = center.position + pos;
            transform.rotation = Quaternion.LookRotation(vel, pos.normalized);
        }
    }
}
