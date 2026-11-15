using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using GoNoGo.Game;
using GoNoGo.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// 在 Play 模式裡照玩家的方式走流程：按真的 UI 按鈕、送真的按鍵、把第一人稱鏡頭對準控制台再按 E。
// unity command script run --file tools/playtest/Flow.cs --entry Flow.State
//   Flow.Click "文字"   按畫面上第一個看得到、文字包含它的按鈕或卡片
//   Flow.Key "space"   按一下鍵盤（Input System 的 Key 名稱）
//   Flow.AimAt "Screen_ops-power"   走到控制台前、準星對準它，回傳資訊框內容
//   Flow.AimFrom "Clue_wall_1" 1.6   從玩家目前那一側走近、對準（線索、大屏幕前的選件台 BigScreen、發射鈕…）
//   Flow.Fill "solar_deployable" "mass_kg" 7.2   在大屏幕填一格數值；Flow.FillAll = 把挖空的都填上真實數值
//   Flow.BuildPart "solar_deployable"   照玩家的規則組一個零件並裝上衛星（架上拿、放左側、依序裝、拿去整合台）
//   Flow.Panel         目前開著的面板文字
public static class Flow
{
    static IEnumerable<VisualElement> Elements()
    {
        foreach (var doc in Object.FindObjectsByType<UIDocument>())
        {
            if (doc.rootVisualElement == null) continue;
            if (doc.panelSettings != null && doc.panelSettings.targetTexture != null) continue; // 場景裡的螢幕
            foreach (var e in doc.rootVisualElement.Query<VisualElement>().ToList()) yield return e;
        }
    }

    static bool Visible(VisualElement e)
    {
        for (var p = e; p != null; p = p.parent)
            if (p.resolvedStyle.display == DisplayStyle.None || p.resolvedStyle.visibility == Visibility.Hidden || p.resolvedStyle.opacity < 0.02f)
                return false;
        return e.worldBound.width > 1 && e.worldBound.height > 1;
    }

    static string TextOf(VisualElement e)
    {
        if (e is TextElement t && !string.IsNullOrEmpty(t.text) && !(e is Button && e.childCount > 0)) return t.text;
        return string.Join(" ", e.Query<TextElement>().ToList().Select(x => x.text).Where(s => !string.IsNullOrEmpty(s)));
    }

    static bool Clickable(VisualElement e) => e is Button || e.ClassListContains("scn-card") || e.ClassListContains("menu-card");

    static string Screen()
    {
        var root = Object.FindAnyObjectByType<GoNoGo.UI.UIRoot>();
        var f = typeof(GoNoGo.UI.UIRoot).GetField("_current", BindingFlags.NonPublic | BindingFlags.Instance);
        return root != null && f != null ? f.GetValue(root)?.GetType().Name ?? "-" : "-";
    }

    public static string State()
    {
        var sb = new StringBuilder();
        sb.Append("scene=").Append(SceneManager.GetActiveScene().name).Append(" screen=").Append(Screen());
        if (GameSession.HasMission)
        {
            var s = GameSession.Mission.S;
            sb.Append(" phase=").Append(s.Phase).Append(" turn=").Append(s.OpsTurn);
        }
        sb.Append(" area=").Append(Hud.Area).Append(" panel=").Append(Hud.PanelOpen).Append(" loading=").Append(SceneFlow.Loading);
        if (FacilityDirector.Instance != null && FacilityDirector.Instance.Player != null)
            sb.Append(" player=").Append(FacilityDirector.Instance.Player.transform.position.ToString("F1"));
        var buttons = Elements().Where(e => Clickable(e) && Visible(e)).Select(e => TextOf(e).Replace("\n", " ")).Where(s => s.Length > 0).Distinct().Take(30);
        sb.Append("\nbuttons: ").Append(string.Join(" | ", buttons.Select(s => s.Length > 40 ? s.Substring(0, 40) : s)));
        return sb.ToString();
    }

    public static string Click(string text)
    {
        var hits = Elements().Where(e => Clickable(e) && Visible(e) && TextOf(e).Contains(text)).ToList();
        if (hits.Count == 0) return "NOT FOUND: " + text + "\n" + State();
        var e = hits[0];
        if (e is Button)
        {
            using var ev = NavigationSubmitEvent.GetPooled();
            ev.target = e;
            e.SendEvent(ev);
        }
        else
        {
            using var ev = ClickEvent.GetPooled();
            ev.target = e;
            e.SendEvent(ev);
        }
        return "clicked: " + TextOf(e).Replace("\n", " ");
    }

    static async Task Frames(int n)
    {
        int end = Time.frameCount + n;
        var t = Time.realtimeSinceStartup + 5f;
        while (Time.frameCount < end && Time.realtimeSinceStartup < t) await Task.Yield();
    }

    /// <summary>Editor 視窗沒有焦點時 Input System 會停用鍵盤：換一份「一律送進遊戲」的設定（只在這次 Play 有效，不存檔）。</summary>
    static void LetInputThrough()
    {
        var s = InputSystem.settings;
        // 上一次 Play 留下來的那份設定在離開 Play 時被銷毀了：重新建一份（否則一碰就丟例外）
        if (s == null)
        {
            s = ScriptableObject.CreateInstance<InputSettings>();
            s.hideFlags = HideFlags.HideAndDontSave;
            InputSystem.settings = s;
        }
        if (s.backgroundBehavior != InputSettings.BackgroundBehavior.IgnoreFocus
            || s.editorInputBehaviorInPlayMode != InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView)
        {
            var copy = Object.Instantiate(s);
            copy.name = "PlaytestInputSettings";
            // 離開 Play 時不要被銷毀（Input System 還拿著它）
            copy.hideFlags = HideFlags.HideAndDontSave;
            copy.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            copy.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = copy;
        }
        // 設定物件沒換（上一次 Play 留下來的那一份）時 Input System 不會重新套用，鍵盤會停在「背景停用」：
        // 換一份新的設定物件，逼它重新套用一次
        if (Keyboard.current != null && !Keyboard.current.enabled)
        {
            var again = Object.Instantiate(InputSystem.settings);
            again.name = "PlaytestInputSettings";
            again.hideFlags = HideFlags.HideAndDontSave;
            again.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            again.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = again;
        }
        foreach (var d in InputSystem.devices)
            if (!d.enabled) InputSystem.EnableDevice(d);
    }

    public static async Task<string> Key(string key)
    {
        LetInputThrough();
        await Frames(2);
        var kb = Keyboard.current;
        if (kb == null) return "no keyboard";
        // 重新套用設定後，鍵盤要過幾幀才會啟用
        for (int i = 0; i < 20 && !kb.enabled; i++)
        {
            InputSystem.EnableDevice(kb);
            await Frames(2);
        }
        if (!kb.enabled) return "keyboard still disabled";
        var k = (Key)System.Enum.Parse(typeof(Key), key, true);
        InputSystem.QueueStateEvent(kb, new KeyboardState(k));
        await Frames(3);
        InputSystem.QueueStateEvent(kb, new KeyboardState());
        await Frames(3);
        return "pressed " + k;
    }

    /// <summary>按住一個鍵 frames 幀（走路）。</summary>
    public static async Task<string> Hold(string key, int frames)
    {
        LetInputThrough();
        var kb = Keyboard.current;
        var k = (Key)System.Enum.Parse(typeof(Key), key, true);
        for (int i = 0; i < frames; i++)
        {
            InputSystem.QueueStateEvent(kb, new KeyboardState(k));
            await Frames(1);
        }
        InputSystem.QueueStateEvent(kb, new KeyboardState());
        await Frames(2);
        return "held " + k + " " + frames + " frames. " + State();
    }

    /// <summary>移動滑鼠（轉視角）。</summary>
    public static async Task<string> Look(float dx, float dy, int frames)
    {
        LetInputThrough();
        var mouse = Mouse.current;
        for (int i = 0; i < frames; i++)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(dx, dy) });
            await Frames(1);
        }
        return "looked";
    }

    public static async Task<string> Wait(int frames)
    {
        await Frames(frames);
        return State();
    }

    /// <summary>走到控制台前 1.25 m、第一人稱、準星對準它的中心，回傳資訊框。</summary>
    public static Task<string> AimAt(string name)
    {
        var ct = Object.FindObjectsByType<Interactable>().FirstOrDefault(c => c.name == name);
        if (ct == null) return Task.FromResult("no target " + name);
        var c = CenterOf(ct);
        var outward = name == "LaunchButton" ? Vector3.left : new Vector3(c.x - 36.8f, 0, c.z).normalized; // 中控台圓心 = 世界 (36.8, 0, 0)
        return AimAtTarget(ct, outward, 1.25f);
    }

    /// <summary>名稱開頭符合的第一個可互動物件：從玩家目前所在那一側走近 dist 公尺、對準它。</summary>
    public static Task<string> AimFrom(string prefix, float dist)
    {
        var pc = FacilityDirector.Instance.Player;
        var ct = Object.FindObjectsByType<Interactable>()
            .Where(c => c.name.StartsWith(prefix) && c.isActiveAndEnabled && c.GetComponentsInChildren<Renderer>().Any(r => r.enabled && r.gameObject.activeInHierarchy))
            .OrderBy(c => Vector3.Distance(c.transform.position, pc.transform.position)).FirstOrDefault();
        if (ct == null) return Task.FromResult("no target " + prefix);
        var c = CenterOf(ct);
        var outward = pc.transform.position - c;
        outward.y = 0;
        return AimAtTarget(ct, outward.normalized, dist);
    }

    static Vector3 CenterOf(Interactable ct)
    {
        var col = ct.GetComponent<Collider>();
        if (col == null) col = ct.GetComponentInChildren<Collider>();
        return col.bounds.center;
    }

    static async Task<string> AimAtTarget(Interactable ct, Vector3 outward, float dist)
    {
        string name = ct.name;
        var c = CenterOf(ct);
        var stand = new Vector3(c.x, 0, c.z) + outward * dist;
        float y = Physics.Raycast(stand + Vector3.up * 3f, Vector3.down, out var gh, 6f) ? gh.point.y : 0f;
        var pc = FacilityDirector.Instance.Player;
        Hud.FirstPerson = true;
        pc.Teleport(new Vector3(stand.x, y + 0.05f, stand.z), Quaternion.LookRotation(-outward).eulerAngles.y);
        await Frames(4);
        var eye = pc.Eye;
        var d = c - eye;
        float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
        var t = typeof(PlayerController);
        t.GetField("_yaw", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pc, yaw);
        t.GetField("_pitch", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pc, pitch);
        await Frames(5);
        var h = Hud.Target;
        if (h == null) return "aim " + name + ": nothing targeted";
        return "aim " + name + ": " + h.Title + (string.IsNullOrEmpty(h.Subtitle) ? "" : " (" + h.Subtitle + ")") + " keys=" + string.Join(",", h.Keys.Select(k => k.Key + ":" + k.Label));
    }

    /// <summary>新手訓練右上角提示框的文字。</summary>
    public static string Coach()
    {
        var c = Elements().FirstOrDefault(e => e.ClassListContains("coach") && Visible(e));
        return c == null ? "no coach" : "coach: " + TextOf(c).Replace("\n", " ");
    }

    public static string Panel()
    {
        var modal = Elements().FirstOrDefault(e => e.ClassListContains("modal") && Visible(e));
        if (modal == null) return "no panel. " + State();
        var s = TextOf(modal).Replace("\n", " ");
        return "panel: " + (s.Length > 500 ? s.Substring(0, 500) + "…" : s);
    }

    /// <summary>直接送一個指令（組裝區的拿、裝這些不在這次檢查範圍時用）。</summary>
    public static string Send(string type, string a, int n)
    {
        var t = (GoNoGo.Core.CommandType)System.Enum.Parse(typeof(GoNoGo.Core.CommandType), type);
        GameSession.Send(t, a, n);
        return "sent " + t + " " + a + " " + n + "  " + State();
    }

    // ---------------- 直接開局、整合台上的零件 ----------------

    static async Task<bool> WaitFor(System.Func<bool> ok, float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            if (ok()) return true;
            await Task.Yield();
        }
        return ok();
    }

    /// <summary>不經選單直接開一局個人任務（"sandbox" = 沙盒），等設施場景載好、簡報關掉、進設計階段。</summary>
    public static async Task<string> Start(string scenario)
    {
        if (scenario == "sandbox") GameSession.StartSandbox();
        else if (scenario == "tutorial") GameSession.StartSolo("earth_observation", "tutorial", true);
        else GameSession.StartSolo(scenario, null);
        bool ok = await WaitFor(() => SceneManager.GetActiveScene().name == "Facility" && FacilityDirector.Instance != null
                                      && FacilityDirector.Instance.Player != null && !SceneFlow.Loading && !Hud.Covered, 30f);
        await Frames(20);
        return (ok ? "" : "TIMEOUT ") + State();
    }

    /// <summary>整合台上的每一組零件：類別、id、有幾個模型、幾個碰撞體。</summary>
    public static string Parts()
    {
        var stand = IntegrationStand.Instance;
        if (stand == null) return "no stand";
        var sb = new StringBuilder();
        foreach (var p in stand.rig.Parts)
            sb.Append(p.category).Append('=').Append(string.IsNullOrEmpty(p.partId) ? "-" : p.partId)
              .Append(" r").Append(p.GetComponentsInChildren<Renderer>().Length)
              .Append(" c").Append(p.GetComponentsInChildren<Collider>().Length).Append("; ");
        return sb.ToString();
    }

    static Bounds BoundsOf(SatPart p)
    {
        var rs = p.GetComponentsInChildren<Renderer>();
        var b = rs.Length > 0 ? rs[0].bounds : new Bounds(p.transform.position, Vector3.zero);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>
    /// 站在整合台旁（零件那一側）、看向船上的某個零件，回傳資訊框內容。firstPerson = false 測第三人稱（預設視角）。
    /// 第三人稱只轉向零件的水平方向，俯仰用預設值（玩家平常就是這樣走過去的，不會特地低頭對準）。
    /// </summary>
    public static async Task<string> AimPart(string partId, bool firstPerson)
    {
        var stand = IntegrationStand.Instance;
        var part = stand.rig.Parts.FirstOrDefault(x => x.partId == partId);
        if (part == null) return "no part " + partId + " on the stand";
        if (part.GetComponentsInChildren<Renderer>().Length == 0) return partId + ": NO MODEL";
        // 瞄這個零件最大的那一塊（整組的中心可能在船身裡面，例如四面都有的貼片太陽能板）
        var biggest = part.GetComponentsInChildren<Renderer>().OrderByDescending(r => r.bounds.size.x * r.bounds.size.y + r.bounds.size.y * r.bounds.size.z + r.bounds.size.x * r.bounds.size.z).First();
        var c = biggest.bounds.center;
        var center = stand.transform.position;
        var outward = new Vector3(c.x - center.x, 0, c.z - center.z);
        if (outward.magnitude < 0.15f) outward = Vector3.back;
        outward.Normalize();
        // 整合台的台座半徑 3.1 m：站在台座邊緣外一點
        var at = new Vector3(center.x, 0, center.z) + outward * 3.45f;
        var pc = FacilityDirector.Instance.Player;
        Hud.FirstPerson = firstPerson;
        pc.Teleport(new Vector3(at.x, 0.05f, at.z), Quaternion.LookRotation(-outward).eulerAngles.y);
        await Frames(4);
        var t = typeof(PlayerController);
        var yawF = t.GetField("_yaw", BindingFlags.NonPublic | BindingFlags.Instance);
        var pitchF = t.GetField("_pitch", BindingFlags.NonPublic | BindingFlags.Instance);
        // 把畫面中央轉到零件上（第三人稱的鏡頭位置會跟著視角動，多轉幾次才對得準）：就是玩家用滑鼠做的事
        for (int i = 0; i < (firstPerson ? 2 : 8); i++)
        {
            var from = Camera.main.transform.position;
            var d = c - from;
            yawF.SetValue(pc, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
            pitchF.SetValue(pc, Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -70f, 75f));
            await Frames(firstPerson ? 3 : 12);
        }
        await Frames(4);
        var h = Hud.Target;
        var def = GameSession.Data.Part(partId);
        if (h == null) return partId + ": FAIL nothing targeted";
        // 整合台上的零件不能手動拆（要換零件是回大屏幕重新開啟設計）：對得到、名稱對就算過
        bool right = def != null && h.Title.StartsWith(def.Code + "｜");
        return partId + ": " + (right ? "OK" : "FAIL") + " [" + h.Title + (string.IsNullOrEmpty(h.Subtitle) ? "" : " / " + h.Subtitle) + "] keys=" + string.Join(",", h.Keys.Select(k => k.Key));
    }

    /// <summary>
    /// 第一人稱：零件可能被別的零件擋住（大太陽能翼後面的電腦、被別台儀器擋住的儀器）。
    /// 玩家會繞著船走：這裡繞一圈每 30° 站一次、再加上零件自己那一側，任何一個位置對得到就算看得到。
    /// </summary>
    public static async Task<string> AimPartAround(string partId)
    {
        var stand = IntegrationStand.Instance;
        var part = stand.rig.Parts.FirstOrDefault(x => x.partId == partId);
        if (part == null) return "no part " + partId;
        var renderers = part.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return partId + ": NO MODEL";
        var def = GameSession.Data.Part(partId);
        var pc = FacilityDirector.Instance.Player;
        var t = typeof(PlayerController);
        var yawF = t.GetField("_yaw", BindingFlags.NonPublic | BindingFlags.Instance);
        var pitchF = t.GetField("_pitch", BindingFlags.NonPublic | BindingFlags.Instance);
        var center = stand.transform.position;
        Hud.FirstPerson = true;
        string last = "nothing targeted";
        for (int a = 0; a < 360; a += 30)
        {
            var outward = Quaternion.Euler(0, a, 0) * Vector3.forward;
            var at = new Vector3(center.x, 0, center.z) + outward * 3.45f;
            pc.Teleport(new Vector3(at.x, 0.05f, at.z), Quaternion.LookRotation(-outward).eulerAngles.y);
            await Frames(3);
            foreach (var r in renderers)
            {
                var d = r.bounds.center - Camera.main.transform.position;
                yawF.SetValue(pc, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
                pitchF.SetValue(pc, Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -70f, 75f));
                await Frames(4);
                var h = Hud.Target;
                if (h == null) continue;
                last = h.Title;
                if (h.Title.StartsWith(def.Code + "｜")) return partId + ": OK from " + a + "°";
            }
        }
        return partId + ": FAIL from every side, last=[" + last + "]";
    }

    /// <summary>第一人稱繞著船走，船上每個零件是不是至少有一個位置對得到。</summary>
    public static async Task<string> AimAllAround()
    {
        var ids = IntegrationStand.Instance.rig.Parts.Where(p => !string.IsNullOrEmpty(p.partId)).Select(p => p.partId).Distinct().ToList();
        var sb = new StringBuilder("first person, walking around; ");
        int ok = 0;
        foreach (var id in ids)
        {
            var r = await AimPartAround(id);
            if (r.Contains(": OK")) ok++;
            sb.Append(r).Append("; ");
        }
        sb.Append(ok).Append('/').Append(ids.Count).Append(" targetable");
        return sb.ToString();
    }

    /// <summary>船上每一個零件都瞄一次（見 <see cref="AimPart"/>）。</summary>
    public static async Task<string> AimAllParts(bool firstPerson)
    {
        var ids = IntegrationStand.Instance.rig.Parts.Where(p => !string.IsNullOrEmpty(p.partId)).Select(p => p.partId).Distinct().ToList();
        var sb = new StringBuilder(firstPerson ? "first person\n" : "third person\n");
        int ok = 0;
        foreach (var id in ids)
        {
            var r = await AimPart(id, firstPerson);
            if (r.Contains(": OK")) ok++;
            sb.Append(r).Append('\n');
        }
        sb.Append(ok).Append('/').Append(ids.Count).Append(" targetable");
        return sb.ToString();
    }

    /// <summary>目前這一局的狀態摘要：手上、勾選的設計、是否確定、填了幾格、組好的、裝上衛星的。</summary>
    public static string Mission()
    {
        if (!GameSession.HasMission) return "no mission";
        var s = GameSession.Mission.S;
        var d = GameSession.Data;
        var cells = GoNoGo.Core.Blanks.Cells(d, s);
        return "scenario=" + s.ScenarioId + " diff=" + s.DifficultyId + " phase=" + s.Phase + " solo=" + s.Solo + " confirmed=" + s.Confirmed
               + " held=" + (s.HeldBy(GameSession.LocalPlayer) ?? "-") + " comps=" + string.Join(",", s.CompsOf(GameSession.LocalPlayer))
               + "\ndesign=" + string.Join(",", s.Bench.AllPartIds()) + " orbit=" + s.Bench.OrbitId + " launcher=" + s.Bench.LauncherId + " years=" + s.Bench.MissionYears
               + "\nblanks=" + cells.Count(c => GoNoGo.Core.Blanks.Filled(s, c.part, c.field) != null) + "/" + cells.Count
               + " built=" + string.Join(",", s.Built) + "\ninstalled=" + string.Join(",", s.Installed)
               + "\nnot installed=" + string.Join(",", GoNoGo.Core.SessionRules.NotInstalled(s, d))
               + "\nblockers=" + string.Join(",", GoNoGo.Core.SessionRules.LaunchBlockers(s, d))
               + "\nlaunches=" + s.Launches.Count + " restarts=" + s.Restarts + " mistakes=" + s.Mistakes;
    }

    /// <summary>畫面上所有看得到的文字（檢查有沒有漏掉的字串鍵、沒轉換的 [[ ]] 記號）。</summary>
    public static string Texts()
    {
        var list = Elements().OfType<TextElement>().Where(e => Visible(e) && !string.IsNullOrEmpty(e.text)).Select(e => e.text.Replace("\n", " ")).Distinct().ToList();
        var s = string.Join(" | ", list);
        return s.Length > 3000 ? s.Substring(0, 3000) + "…" : s;
    }

    /// <summary>看得到的文字裡，像是沒翻到的字串鍵（ui.xxx.yyy）或沒轉換的記號。沒有就回傳 clean。</summary>
    public static string Leaks()
    {
        var bad = Elements().OfType<TextElement>().Where(e => Visible(e) && !string.IsNullOrEmpty(e.text))
            .Select(e => e.text).Where(t => t.Contains("[[") || t.Contains("]]") || System.Text.RegularExpressions.Regex.IsMatch(t, @"^(ui|err|res|warn|event|ops|mini|cause|suggest|objective)\.[a-z0-9_.]+$")
                                            || System.Text.RegularExpressions.Regex.IsMatch(t, @"\{[a-z_]+(:[^}]*)?\}")).Distinct().ToList();
        return bad.Count == 0 ? "clean" : "LEAKS: " + string.Join(" | ", bad);
    }

    // ---------------- 選件：填數值、確定設計 ----------------

    /// <summary>挖空的每一格：零件/欄位 = 玩家填的值（沒填是 ?）。</summary>
    public static string BlankCells()
    {
        var s = GameSession.Mission.S;
        var d = GameSession.Data;
        return string.Join(" ", GoNoGo.Core.Blanks.Cells(d, s).Select(c =>
        {
            var f = GoNoGo.Core.Blanks.Filled(s, c.part, c.field);
            return c.part + "/" + c.field + "=" + (f == null ? "?" : f.V.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }));
    }

    /// <summary>在大屏幕填一格數值（和面板送的是同一個指令）。</summary>
    public static string Fill(string partId, string field, double value)
    {
        GameSession.Send(GoNoGo.Core.CommandType.Fill, GoNoGo.Core.Blanks.FillArg(partId, field, value));
        return BlankCells();
    }

    /// <summary>把挖空的每一格都填上真實數值（等於把線索都找齊了）。</summary>
    public static async Task<string> FillAll()
    {
        var s = GameSession.Mission.S;
        var d = GameSession.Data;
        foreach (var c in GoNoGo.Core.Blanks.Cells(d, s))
            GameSession.Send(GoNoGo.Core.CommandType.Fill, GoNoGo.Core.Blanks.FillArg(c.part, c.field, GoNoGo.Core.PartFields.Get(d.Part(c.part), c.field)));
        await Frames(5);
        return BlankCells();
    }

    /// <summary>這一局每個位置放了哪些線索（零件、欄位、呈現方式）。</summary>
    public static string ClueMap()
    {
        var m = GameSession.Mission;
        var sb = new StringBuilder();
        foreach (var spot in GoNoGo.Core.Clues.Spots)
        {
            var list = m.CluesAt(spot.Id);
            sb.Append(spot.Id).Append(": ").Append(list.Count == 0 ? "-" : string.Join(" + ", list.Select(c => c.PartId + "[" + string.Join(",", c.Fields) + "]" + c.Kind))).Append('\n');
        }
        return sb.ToString();
    }

    // ---------------- 組裝：小零件架、兩座組裝台 ----------------

    /// <summary>兩座組裝台的狀態、手上的東西、最近的通知。</summary>
    public static string Bench()
    {
        var s = GameSession.Mission.S;
        var d = GameSession.Data;
        var sb = new StringBuilder();
        for (int i = 0; i < s.Benches.Count; i++)
        {
            var b = s.Benches[i];
            var job = b.Job;
            sb.Append("bench").Append(i).Append(": ").Append(GoNoGo.Core.SessionRules.BenchStatusOf(s, d, i))
              .Append(" left=[").Append(string.Join(",", b.Left)).Append("]")
              .Append(" job=").Append(job == null ? "-" : job.PartId + " step " + job.Step + " mistakes " + job.Mistakes + " done " + job.Done + " lastOk " + job.LastOk);
            var want = GoNoGo.Core.SessionRules.BenchWants(s, d, i);
            sb.Append(" wants=").Append(want == null ? "-" : want.Id).Append('\n');
        }
        sb.Append("held=").Append(s.HeldBy(GameSession.LocalPlayer) ?? "-").Append(" comps=").Append(string.Join(",", s.CompsOf(GameSession.LocalPlayer)));
        var toast = Elements().FirstOrDefault(e => e.ClassListContains("toast--on"));
        sb.Append("\ntoast: ").Append(toast == null ? "-" : TextOf(toast).Replace("\n", " / "));
        sb.Append("\nlearned=").Append(string.Join(",", s.Learned)).Append(" flawless=").Append(string.Join(",", s.Flawless));
        return sb.ToString();
    }

    /// <summary>架上放著這個小零件的那一格（CompBin 的物件名稱；沒有 = 空字串）。</summary>
    public static string BinOf(string compId)
    {
        var b = CompBin.Bins.FirstOrDefault(x => x.Component != null && x.Component.Id == compId);
        return b == null ? "" : b.name + " #" + b.index;
    }

    static async Task<string> Do(GoNoGo.Core.CommandType t, string a = "", int n = 0)
    {
        string before = GameSession.Mission.S.Phase.ToString();
        GameSession.Send(t, a, n);
        await Frames(4);
        return before;
    }

    /// <summary>
    /// 照規則把一個零件組好並裝上衛星（送的是玩家按鍵會送的同一批指令）：架上拿小零件（單人一籃四個）→ 放到組裝台左側 →
    /// 照提示的順序從左側拿起、在右側裝上 → 拿起成品 → 裝上衛星。回傳每一步之後的狀態，方便看哪一步被擋。
    /// </summary>
    public static async Task<string> BuildPart(string partId, int bench)
    {
        var s = GameSession.Mission.S;
        var d = GameSession.Data;
        var part = d.Part(partId);
        if (part == null) return "no part " + partId;
        if (!GoNoGo.Core.SessionRules.NeedsAssembly(s, d, partId)) return partId + ": no assembly needed (installed=" + s.Installed.Contains(partId) + ")";
        var recipe = d.Recipe(part);
        var sb = new StringBuilder(partId + ": ");
        int me = GameSession.LocalPlayer;
        foreach (var c in recipe.Components)
        {
            await Do(GoNoGo.Core.CommandType.CompPick, c.Id);
            // 多人一次只能拿一個：拿一個放一個
            if (!GoNoGo.Core.SessionRules.Alone(s)) await Do(GoNoGo.Core.CommandType.BenchPut, "", bench);
        }
        if (GoNoGo.Core.SessionRules.Alone(s)) await Do(GoNoGo.Core.CommandType.BenchPut, "", bench);
        sb.Append("status=").Append(GoNoGo.Core.SessionRules.BenchStatusOf(s, d, bench));
        for (int guard = 0; guard < 12; guard++)
        {
            var want = GoNoGo.Core.SessionRules.BenchWants(s, d, bench);
            if (want == null) break;
            await Do(GoNoGo.Core.CommandType.BenchTake, want.Id, bench);
            await Do(GoNoGo.Core.CommandType.CompFit, "", bench);
        }
        var job = s.BenchAt(bench)?.Job;
        sb.Append(" built=").Append(job != null && job.Done);
        await Do(GoNoGo.Core.CommandType.Pick, partId);
        sb.Append(" held=").Append(s.HeldBy(me) ?? "-");
        await Do(GoNoGo.Core.CommandType.Install, partId);
        sb.Append(" installed=").Append(s.Installed.Contains(partId));
        return sb.ToString();
    }

    /// <summary>設計裡還沒裝上衛星的零件全部組好、裝上（見 <see cref="BuildPart"/>）。</summary>
    public static async Task<string> BuildAll()
    {
        var s = GameSession.Mission.S;
        var d = GameSession.Data;
        var sb = new StringBuilder();
        foreach (var id in GoNoGo.Core.SessionRules.NotInstalled(s, d).ToList())
            sb.Append(await BuildPart(id, 0)).Append('\n');
        sb.Append("not installed: ").Append(string.Join(",", GoNoGo.Core.SessionRules.NotInstalled(s, d)));
        return sb.ToString();
    }

    /// <summary>準星／身邊對到的東西（資訊框全文）。</summary>
    public static string Target()
    {
        var h = Hud.Target;
        if (h == null) return "nothing targeted";
        return h.Title + (string.IsNullOrEmpty(h.Subtitle) ? "" : " (" + h.Subtitle + ")") + (string.IsNullOrEmpty(h.Body) ? "" : " [" + h.Body.Replace("\n", " / ") + "]")
               + " keys=" + string.Join(",", h.Keys.Select(k => k.Key + ":" + k.Label));
    }

    /// <summary>
    /// 走到某個可互動物件的正面（它的根物件局部 -Z 那一側）dist 公尺、面向它，回傳對到什麼。
    /// name = 物件名稱：架上的一格（Bin0…）、組裝台左側的一格或右側的組裝墊要加第幾座，例如 "AssemblyBench0/Slot2"、"AssemblyBench1/Jig"。
    /// 第三人稱只轉水平方向，俯仰不動（玩家平常走過去的樣子）。
    /// </summary>
    public static async Task<string> AimFront(string name, float dist, bool firstPerson)
    {
        Interactable ct;
        Transform root;
        int slash = name.IndexOf('/');
        if (slash > 0)
        {
            var bench = GameObject.Find(name.Substring(0, slash));
            if (bench == null) return "no target " + name;
            ct = bench.GetComponentsInChildren<Interactable>().FirstOrDefault(c => c.name == name.Substring(slash + 1));
            root = bench.transform;
        }
        else
        {
            ct = Object.FindObjectsByType<Interactable>().FirstOrDefault(c => c.name == name);
            root = ct is CompBin ? ct.transform.parent : ct != null ? ct.transform : null;
        }
        if (ct == null) return "no target " + name;
        var outward = -root.forward;
        outward.y = 0;
        outward.Normalize();
        Vector3 c = CenterOf(ct);
        var stand = new Vector3(c.x, 0, c.z) + outward * dist;
        var pc = FacilityDirector.Instance.Player;
        Hud.FirstPerson = firstPerson;
        pc.Teleport(new Vector3(stand.x, 0.05f, stand.z), Quaternion.LookRotation(-outward).eulerAngles.y);
        await Frames(4);
        var t = typeof(PlayerController);
        var eye = pc.Eye;
        var dir = c - eye;
        t.GetField("_yaw", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pc, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
        if (firstPerson)
            t.GetField("_pitch", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pc, -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg);
        await Frames(8);
        return "aim " + name + (firstPerson ? " (1st)" : " (3rd)") + ": " + Target();
    }
}
