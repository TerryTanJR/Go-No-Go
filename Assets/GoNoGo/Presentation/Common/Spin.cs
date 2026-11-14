using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>繞自身某軸持續旋轉（地球自轉、雲層、展示台）。</summary>
    public sealed class Spin : MonoBehaviour
    {
        public Vector3 axis = Vector3.up;
        public float degreesPerSecond = 2f;

        void Update() => transform.Rotate(axis, degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
