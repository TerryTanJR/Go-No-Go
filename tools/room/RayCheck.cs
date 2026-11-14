using System.Text;
using GoNoGo.Presentation;
using UnityEngine;

// 從每個席位操作員的眼睛（距螢幕約 1.3 m、高 1.6 m）往螢幕中心打射線，確認打到的是那台螢幕的 ConsoleTarget。
// unity command script run --file tools/room/RayCheck.cs --entry RayCheck.Run
public static class RayCheck
{
    public static string Run()
    {
        var sb = new StringBuilder();
        int ok = 0, n = 0;
        foreach (var ct in Object.FindObjectsByType<ConsoleTarget>(FindObjectsSortMode.None))
        {
            if (!ct.name.StartsWith("Screen_") && ct.name != "LaunchButton") continue;
            n++;
            var col = ct.GetComponent<Collider>();
            var c = col.bounds.center;
            var outward = new Vector3(c.x - 28f, 0, c.z).normalized;   // 中控台圓心 = 世界 (28, 0, 0)
            float lift = ct.name == "Screen_ops_desk" ? 0.2f : 0f;
            var eye = new Vector3(c.x, 0, c.z) + outward * 1.3f + Vector3.up * (1.6f + lift);
            bool hit = Physics.Raycast(eye, (c - eye).normalized, out var h, 4f, ~(1 << 2), QueryTriggerInteraction.Collide);
            var it = hit ? h.collider.GetComponentInParent<Interactable>() : null;
            bool good = it == ct && Vector3.Distance(eye - Vector3.up * 0.6f, h.point) <= ct.Range;
            if (good) ok++;
            else sb.Append(ct.name).Append(" -> ").Append(hit ? h.collider.name : "nothing").Append("; ");
        }
        return $"{ok}/{n} reachable. {sb}";
    }
}
