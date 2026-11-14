using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>太空船程序化模型用的材質組（Resources/Presentation/SatMaterials，由編輯器腳本建立）。</summary>
    [CreateAssetMenu(menuName = "GoNoGo/Sat Materials")]
    public sealed class SatMaterials : ScriptableObject
    {
        public Material mliGold;
        public Material mliSilver;
        public Material paintWhite;
        public Material frameDark;
        public Material solarCell;
        public Material dish;
        public Material lens;
        public Material metal;
        public Material accent;

        static SatMaterials _cached;
        public static SatMaterials Load()
        {
            if (_cached == null) _cached = Resources.Load<SatMaterials>("Presentation/SatMaterials");
            return _cached;
        }
    }
}
