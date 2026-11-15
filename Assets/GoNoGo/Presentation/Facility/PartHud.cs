using GoNoGo.Core;
using GoNoGo.Game;

namespace GoNoGo.Presentation
{
    /// <summary>準星對到一個零件時的資訊框：編號、類別和一句白話功能（不顯示任何數值）。</summary>
    public static class PartHud
    {
        public static HudTarget Info(PartDef def)
        {
            return new HudTarget
            {
                Title = def.Code + "｜" + Loc.T("ui.cat." + def.Category),
                Body = Loc.T(def.Function),
                Color = def.Color,
            };
        }
    }
}
