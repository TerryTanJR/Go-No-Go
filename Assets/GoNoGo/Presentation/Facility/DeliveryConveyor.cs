using System.Collections.Generic;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 組裝區的收貨輸送帶：買到（或放回去）的零件放在皮帶上，從末端擋板往窗口方向排隊。
    /// 新到貨的零件一個一個從收貨窗口的簾子後面出來，沿皮帶滑到隊伍最後面；前面的零件被拿走時，後面的往前補位。
    /// 零件由 <see cref="FacilityDirector"/> 建立並交給這裡（<see cref="Add"/> / <see cref="Remove"/>）。
    /// </summary>
    public sealed class DeliveryConveyor : MonoBehaviour
    {
        [Tooltip("零件的父物件：局部 +Z 朝房間（PartItem 會把零件的大面轉向 +Z）")]
        public Transform items;
        [Tooltip("皮帶上零件底部的路徑（items 的局部座標）：start 在窗口簾子後面，end 在末端擋板前")]
        public Vector3 start, end;
        [Tooltip("從 start 走多遠才出了窗口（之前的部分藏在簾子後面）")]
        public float entry = 1.2f;
        public float spacing = 0.62f, minSpacing = 0.4f;
        [Tooltip("皮帶速度（公尺/秒）")]
        public float speed = 1.6f;
        [Tooltip("連續出貨時每件的間隔（秒）")]
        public float releaseGap = 0.7f;
        public float itemSize = 0.5f;

        [Header("外觀")]
        public Renderer belt;
        [Tooltip("皮帶貼圖一格的長度（公尺）：捲動速度 = speed / 這個值")]
        public float beltTile = 0.25f;
        [Tooltip("簾子條（樞紐在上緣，局部 -Y 往下垂）：零件經過時繞 +Z 往皮帶前進方向掀")]
        public Transform[] flaps;
        [Tooltip("出貨時旋轉的警示燈（含聚光燈），閒置時關掉")]
        public Transform beacon;
        public Light beaconLight;
        [Tooltip("窗口上方的狀態燈：閒置綠、出貨中琥珀色（Unlit 材質的 _BaseColor）")]
        public Renderer statusLamp;
        public Color idleColor = new Color(0.2f, 1.6f, 0.5f), busyColor = new Color(2.6f, 1.2f, 0.1f);

        sealed class Slot
        {
            public PartItem item;
            public float d;
            public float wait; // > 0：還在倉庫裡排隊，看不到
            public bool riding;
        }

        readonly List<Slot> _slots = new List<Slot>();
        Material _beltMat;
        float _scroll, _flapOpen, _beaconAngle;
        MaterialPropertyBlock _mpb;

        float Length => Vector3.Distance(start, end);
        public bool Busy { get; private set; }

        void Awake()
        {
            if (belt != null) _beltMat = belt.material;
            _mpb = new MaterialPropertyBlock();
            SetBusy(false);
        }

        void OnDestroy()
        {
            if (_beltMat != null) Destroy(_beltMat);
        }

        public bool Has(PartItem item) => _slots.Exists(s => s.item == item);

        /// <summary>放上皮帶。fromWindow：從收貨窗口送出來（新到貨）；否則直接出現在隊伍最後面（例如放回去的零件）。</summary>
        public void Add(PartItem item, bool fromWindow)
        {
            if (item == null || Has(item)) return;
            var s = new Slot { item = item };
            if (fromWindow)
            {
                // 排在所有還沒出來的後面
                float lastWait = 0;
                foreach (var o in _slots) if (o.d <= 0.01f) lastWait = Mathf.Max(lastWait, o.wait);
                bool anyWaiting = _slots.Exists(o => o.d <= 0.01f);
                s.d = 0;
                s.wait = anyWaiting ? lastWait + releaseGap : 0.4f;
                s.riding = true;
                if (!Busy) Sfx.Alert();
            }
            else
            {
                float back = Length;
                int visible = 0;
                foreach (var o in _slots) if (o.wait <= 0) { back = Mathf.Min(back, o.d); visible++; }
                float sp = Spacing(visible + 1);
                s.d = visible == 0 ? Length - sp * 0.5f : Mathf.Max(entry, back - sp);
            }
            _slots.Add(s);
            Place(s);
            item.SetVisible(s.wait <= 0);
        }

        public void Remove(PartItem item)
        {
            int i = _slots.FindIndex(s => s.item == item);
            if (i >= 0) _slots.RemoveAt(i);
        }

        float Spacing(int n) => Mathf.Max(minSpacing, Mathf.Min(spacing, (Length - entry) / Mathf.Max(1, n)));

        void Update()
        {
            float dt = Time.deltaTime;
            float L = Length;
            // 實際順序：已經在皮帶上的照位置（越前面 d 越大），還在倉庫裡的照出貨順序
            _slots.Sort((a, b) =>
            {
                bool aw = a.wait > 0, bw = b.wait > 0;
                if (aw != bw) return aw ? 1 : -1;
                if (aw) return a.wait.CompareTo(b.wait);
                return b.d.CompareTo(a.d);
            });
            float sp = Spacing(_slots.Count);
            bool moving = false, waiting = false;
            float flap = 0;
            float ahead = float.MaxValue;
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (s.wait > 0)
                {
                    waiting = true;
                    s.wait -= dt;
                    // 窗口前面要有空位才出貨
                    if (s.wait <= 0 && ahead < sp * 0.95f) s.wait = 0.05f;
                    if (s.wait > 0) continue;
                    s.d = 0;
                    s.item.SetVisible(true);
                    Sfx.Tick();
                }
                float target = Mathf.Max(0, L - sp * (i + 0.5f));
                if (i > 0 && _slots[i - 1].wait <= 0) target = Mathf.Min(target, _slots[i - 1].d - sp * 0.95f);
                // 只往前走（往末端），不倒退
                float before = s.d;
                s.d = Mathf.MoveTowards(s.d, Mathf.Max(s.d, target), speed * dt);
                if (s.d - before > 1e-4f) moving = true;
                if (s.riding && s.d >= L - sp * (i + 0.5f) - 0.01f) { s.riding = false; Sfx.Drop(); }
                // 簾子在 entry 前面一點：零件穿過時掀開
                flap = Mathf.Max(flap, 1f - Mathf.Clamp01(Mathf.Abs(s.d - (entry - 0.35f)) / 0.45f));
                ahead = s.d;
                Place(s);
            }

            if (moving && _beltMat != null)
            {
                _scroll = Mathf.Repeat(_scroll - speed * dt / beltTile, 1f);
                _beltMat.SetTextureOffset("_BaseMap", new Vector2(_scroll, 0));
            }
            _flapOpen = Mathf.MoveTowards(_flapOpen, flap, dt * 4f);
            if (flaps != null)
                foreach (var f in flaps)
                    if (f != null) f.localRotation = Quaternion.AngleAxis(55f * _flapOpen, Vector3.forward);
            SetBusy(moving || waiting);
            if (Busy && beacon != null)
            {
                _beaconAngle = Mathf.Repeat(_beaconAngle + 300f * dt, 360f);
                beacon.localRotation = Quaternion.Euler(0, _beaconAngle, 0);
            }
        }

        void SetBusy(bool on)
        {
            if (_busySet && on == Busy) return;
            _busySet = true;
            Busy = on;
            if (beaconLight != null) beaconLight.enabled = on;
            if (beacon != null) beacon.gameObject.SetActive(on);
            if (statusLamp != null)
            {
                statusLamp.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", on ? busyColor : idleColor);
                statusLamp.SetPropertyBlock(_mpb);
            }
        }
        bool _busySet;

        void Place(Slot s)
        {
            float L = Length;
            var p = L > 0 ? Vector3.Lerp(start, end, Mathf.Clamp01(s.d / L)) : start;
            s.item.transform.localPosition = p;
        }
    }
}
