using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>鏡頭緩慢漂移，像手持或在太空中飄浮，讓靜態畫面有生命感。</summary>
    public sealed class CameraDrift : MonoBehaviour
    {
        public Vector3 positionAmplitude = new Vector3(0.15f, 0.08f, 0.1f);
        public Vector3 rotationAmplitude = new Vector3(0.6f, 0.9f, 0.3f);
        public float speed = 0.12f;
        Vector3 _p0, _r0;

        void Start()
        {
            _p0 = transform.localPosition;
            _r0 = transform.localEulerAngles;
        }

        void LateUpdate()
        {
            float t = Time.time * speed;
            transform.localPosition = _p0 + new Vector3(
                (Mathf.PerlinNoise(t, 0.1f) - 0.5f) * 2f * positionAmplitude.x,
                (Mathf.PerlinNoise(0.3f, t) - 0.5f) * 2f * positionAmplitude.y,
                (Mathf.PerlinNoise(t, 0.7f) - 0.5f) * 2f * positionAmplitude.z);
            transform.localEulerAngles = _r0 + new Vector3(
                (Mathf.PerlinNoise(t * 0.8f, 2.1f) - 0.5f) * 2f * rotationAmplitude.x,
                (Mathf.PerlinNoise(3.3f, t * 0.8f) - 0.5f) * 2f * rotationAmplitude.y,
                (Mathf.PerlinNoise(t * 0.6f, 4.7f) - 0.5f) * 2f * rotationAmplitude.z);
        }
    }
}
