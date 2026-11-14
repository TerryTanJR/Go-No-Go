using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 一條資源條：名稱、已用/上限、狀態顏色、「為什麼？」說明、滑鼠預覽時的白色標記與增減量。
    /// </summary>
    public sealed class ResourceBar : VisualElement
    {
        public ResourceId Id { get; }
        readonly Label _name, _value, _delta;
        readonly VisualElement _fill, _ghost, _track;
        ResourceReport _report;

        public ResourceBar(ResourceId id)
        {
            Id = id;
            AddToClassList("res");
            var head = Ui.Row("res-head").AddTo(this);
            Ui.El("res-icon").AddTo(head);
            _name = Ui.Text(Loc.T("res." + id.ToString().ToLowerInvariant()), "res-name").AddTo(head);
            var why = Ui.Btn("?", null, "res-why", "btn-ghost").AddTo(head);
            UIRoot.AttachTip(why, WhyText);
            Ui.Spacer().AddTo(head);
            _delta = Ui.Text("", "res-delta", "mono").AddTo(head);
            _value = Ui.Text("", "res-value", "mono").AddTo(head);
            _track = Ui.El("res-track").AddTo(this);
            _fill = Ui.El("res-fill").AddTo(_track);
            _ghost = Ui.El("res-ghost").AddTo(_track);
        }

        string WhyText()
        {
            string key = "res." + Id.ToString().ToLowerInvariant() + ".why";
            var text = Loc.T(key);
            if (_report == null || _report.Contributions.Count == 0) return text;
            var top = _report.Contributions.OrderByDescending(c => c.Amount).Take(4)
                .Select(c => $"• {Fmt.ContributionName(c.Key)}  {Fmt.Amount(Id, c.Amount, _report.Unit)}");
            return text + "\n\n" + string.Join("\n", top);
        }

        public void Set(ResourceReport r)
        {
            bool changed = _report != null && System.Math.Abs(_report.Used - r.Used) > 1e-6;
            _report = r;
            _value.text = Fmt.Resource(r);
            float fill = Mathf.Clamp01((float)r.Fill);
            _fill.style.width = Length.Percent(fill * 100f);
            EnableInClassList("res--tight", r.Status == ResourceStatus.Tight);
            EnableInClassList("res--over", r.Status == ResourceStatus.Over);
            if (changed)
            {
                AddToClassList("res--flash");
                schedule.Execute(() => RemoveFromClassList("res--flash")).StartingIn(260);
            }
        }

        /// <summary>預覽：顯示換了零件之後條會到哪裡，以及差多少。</summary>
        public void Preview(ResourceReport after)
        {
            if (after == null || _report == null)
            {
                _ghost.RemoveFromClassList("res-ghost--on");
                _delta.text = "";
                return;
            }
            float f = Mathf.Clamp01((float)after.Fill);
            _ghost.style.left = Length.Percent(f * 100f);
            _ghost.AddToClassList("res-ghost--on");
            double d = after.Used - _report.Used;
            if (System.Math.Abs(d) < 0.05 && System.Math.Abs(after.Capacity - _report.Capacity) < 0.05)
            {
                _delta.text = "";
                return;
            }
            // 容量改變時（例如換火箭、換電池），用餘裕的變化表示
            if (System.Math.Abs(after.Capacity - _report.Capacity) >= 0.05)
            {
                double dm = (after.Margin - _report.Margin) * 100;
                _delta.text = (dm >= 0 ? "+" : "") + dm.ToString("0") + (Loc.IsZh ? "% 餘裕" : "% margin");
                _delta.EnableInClassList("go", dm >= 0);
                _delta.EnableInClassList("over", dm < 0);
                return;
            }
            _delta.text = (d > 0 ? "+" : "-") + Fmt.Amount(Id, System.Math.Abs(d), _report.Unit);
            // 用量增加是壞事（紅），減少是好事（綠）
            _delta.EnableInClassList("over", d > 0);
            _delta.EnableInClassList("go", d < 0);
        }
    }
}
