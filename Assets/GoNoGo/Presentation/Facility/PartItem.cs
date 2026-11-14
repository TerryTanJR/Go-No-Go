using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 零件架上的一個實體零件。只標編號；準星對到時顯示編號、類別和一句白話功能（不顯示任何數值）。
    /// 被拿走或裝上船時隱藏（由 <see cref="FacilityDirector"/> 依狀態切換）。
    /// </summary>
    public sealed class PartItem : Interactable
    {
        public string partId;
        public PartDef Def { get; private set; }
        public Transform Model { get; private set; }

        SatPart[] _glow;
        WorldLabel _label;

        public static PartItem Create(PartDef def, GameData data, Transform parent, Vector3 localPos, float size)
        {
            var go = new GameObject("Item_" + def.Id);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var item = go.AddComponent<PartItem>();
            item.partId = def.Id;
            item.Def = def;

            var model = SatelliteBuilder.BuildPart(def.Id, data, go.transform, size);
            item.Model = model.transform;
            // 薄板狀的零件（電池、電腦板、太陽能板…）側面朝外時幾乎看不到：把最薄的軸轉向架子的深度方向，大面朝外、往後仰一點
            var b = SatelliteRig.ComputeBounds(model);
            var sz = go.transform.InverseTransformVector(b.size);
            sz = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));
            float thin = Mathf.Min(sz.x, sz.y, sz.z), big = Mathf.Max(sz.x, sz.y, sz.z);
            if (thin < big * 0.3f)
            {
                var pivot = b.center;
                Quaternion turn = Quaternion.identity;
                if (thin == sz.x) turn = Quaternion.Euler(0, 90, 0);
                else if (thin == sz.y) turn = Quaternion.Euler(-90, 0, 0);
                turn = Quaternion.Euler(-15, 0, 0) * turn;
                model.transform.localRotation = turn * model.transform.localRotation;
                var nb = SatelliteRig.ComputeBounds(model);
                model.transform.position += pivot - nb.center;
            }
            b = SatelliteRig.ComputeBounds(model);
            model.transform.position += new Vector3(0, go.transform.position.y - b.min.y + 0.02f, 0);
            b = SatelliteRig.ComputeBounds(model);

            var col = go.AddComponent<BoxCollider>();
            col.center = go.transform.InverseTransformPoint(b.center);
            var ls = go.transform.InverseTransformVector(b.size);
            col.size = Vector3.Max(new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z)), Vector3.one * 0.25f);

            // 用 SatPart 的發光（模型裡每一組都有）
            item._glow = go.GetComponentsInChildren<SatPart>(true);

            item._label = go.AddComponent<WorldLabel>();
            item._label.text = def.Code;
            item._label.style = "code";
            item._label.offset = new Vector3(0, b.max.y - go.transform.position.y + 0.12f, 0);
            item._label.maxDistance = 5f;
            item._label.color = def.Color;
            return item;
        }

        public static HudTarget Info(PartDef def)
        {
            return new HudTarget
            {
                Title = def.Code + "｜" + Loc.T("ui.cat." + def.Category),
                Body = Loc.T(def.Function),
                Color = def.Color,
            };
        }

        public override HudTarget Describe(Interactor who)
        {
            var t = Info(Def);
            if (who.Holding != null) t.Subtitle = Loc.T("ui.hint.hands_full");
            else t.Keys.Add(new KeyHint("C", Loc.T("ui.hint.pick")));
            return t;
        }

        public override void OnKey(Interactor who, char key)
        {
            if (key != 'C') return;
            if (who.Holding != null) { Hud.Toast(Loc.T("err.hands_full")); return; }
            GameSession.Send(CommandType.Pick, partId);
            Sfx.Pick();
            Hud.Signal("pick:" + partId);
        }

        public override void SetHighlight(bool on)
        {
            foreach (var g in _glow) if (g != null) g.SetGlow(on ? 0.8f : 0f);
        }

        public void SetVisible(bool v)
        {
            if (gameObject.activeSelf != v) gameObject.SetActive(v);
        }
    }
}
