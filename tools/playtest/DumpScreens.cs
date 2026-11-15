// 把場景裡每一面螢幕（WorldScreen：設計主螢幕、兩側的需求與組裝清單、組裝區儀表板、組裝台螢幕、大廳看板…）
// 現在畫的內容存成 Screenshots/ws_<id>.png。遊戲視窗的截圖裡這些螢幕又小又斜，排版問題要看這裡。
// 只讀畫面，不改遊戲狀態。用法（Play 模式中）：tools/playtest/pt.sh evalf tools/playtest/DumpScreens.cs
using System.IO;
using GoNoGo.Game;
using UnityEngine;
var sb = new System.Text.StringBuilder();
foreach (var ws in WorldScreen.All)
{
    if (ws.target == null) continue;
    var rt = ws.target.sharedMaterial.GetTexture("_BaseMap") as RenderTexture;
    if (rt == null) { sb.Append(ws.id).Append(": no texture; "); continue; }
    var prev = RenderTexture.active;
    RenderTexture.active = rt;
    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
    tex.Apply();
    RenderTexture.active = prev;
    Directory.CreateDirectory("Screenshots");
    File.WriteAllBytes("Screenshots/ws_" + ws.id.Replace(':', '-') + ".png", tex.EncodeToPNG());
    Object.Destroy(tex);
    sb.Append(ws.id).Append(' ').Append(rt.width).Append('x').Append(rt.height).Append("; ");
}
return sb.ToString();
