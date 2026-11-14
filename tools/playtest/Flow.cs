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
//   Flow.AimAt "Screen_dashboard"   走到控制台前、準星對準它，回傳資訊框內容
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
        if (s.backgroundBehavior != InputSettings.BackgroundBehavior.IgnoreFocus
            || s.editorInputBehaviorInPlayMode != InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView)
        {
            var copy = Object.Instantiate(s);
            copy.name = "PlaytestInputSettings";
            copy.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            copy.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = copy;
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
        var outward = name == "LaunchButton" ? Vector3.left : new Vector3(c.x - 28f, 0, c.z).normalized; // 中控台圓心 = 世界 (28, 0, 0)
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
}
