using System;
using UnityEngine;

namespace GoNoGo.Game
{
    /// <summary>
    /// 採購終端左邊的零件 3D 預覽。UI 層不認得 3D 模型，由場景（Presentation 的 PartPreviewStage）掛上實作：
    /// Show 回傳一張畫著零件、會慢慢轉的 RenderTexture；Hide 關掉預覽相機。場景裡沒有預覽台時兩者都是 null。
    /// </summary>
    public static class PartPreview
    {
        public static Func<string, Texture> Show;
        public static Action Hide;
    }
}
