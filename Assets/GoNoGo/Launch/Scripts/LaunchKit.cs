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
        [Tooltip("月球任務：月球表面（和營運畫面同一個材質）")]
        public Material moon;
        [Tooltip("月球任務：地月轉移俯瞰圖的軌道線與衛星光點（加法混合的柔和圓點）")]
        public Material line;

        static LaunchKit _cached;
        public static LaunchKit Load()
        {
            if (_cached == null) _cached = Resources.Load<LaunchKit>("Launch/LaunchKit");
            return _cached;
        }
    }
}
