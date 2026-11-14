using UnityEngine;

namespace GoNoGo.Launch
{
    /// <summary>發射場景用的材質與特效（Resources/Launch/LaunchKit，由編輯器腳本建立）。</summary>
    [CreateAssetMenu(menuName = "GoNoGo/Launch Kit")]
    public sealed class LaunchKit : ScriptableObject
    {
        public Material paintWhite;
        public Material paintBlack;
        public Material paintTeal;
        public Material metal;
        public Material interstage;
        public Material plume;
        public Material smoke;
        public Material fireball;
        public Material debris;
        [Tooltip("爬升時拖在火箭後面的排氣尾跡")]
        public Material trail;

        static LaunchKit _cached;
        public static LaunchKit Load()
        {
            if (_cached == null) _cached = Resources.Load<LaunchKit>("Launch/LaunchKit");
            return _cached;
        }
    }
}
