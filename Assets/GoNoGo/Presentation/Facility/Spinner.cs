using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>慢慢自轉的裝飾（大廳控制台上方的全像地球、旋轉燈環）。</summary>
    public sealed class Spinner : MonoBehaviour
    {
        public Vector3 axis = Vector3.up;
        public float degreesPerSecond = 12f;

        void Update() => transform.Rotate(axis, degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
