using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>即時反射探針：場景開始時拍一次（WebGL 也可用，不需要烘焙）。</summary>
    [RequireComponent(typeof(ReflectionProbe))]
    public sealed class ProbeOnStart : MonoBehaviour
    {
        void Start() => GetComponent<ReflectionProbe>().RenderProbe();
    }
}
