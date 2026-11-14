# Friction Log

開發 Go/No-Go Unity 版時遇到的問題、原因和解法。每一條都寫成「下次遇到可以直接照做」的形式。

格式：**問題** → 原因 → 解法。依時間排序，最新的在最後。

---

## 環境建置

### Git

1. **推送前在 `main` 上另開了分支，使用者其實要直接上 `main`**
   - 原因：照預設流程「在預設分支上先開分支」。
   - 解法：這個 repo 一律直接在 `main` commit 和 push。

2. **`.gitattributes` 用 `docs/** -filter -diff -merge` 想排除 LFS，結果 Markdown 被當成二進位檔**
   - 原因：`-diff` 是「取消 diff 屬性」，Git 會把檔案當二進位，不是「回到預設」。
   - 解法：改用 `!filter !diff !merge`（unspecify）。用 `git check-attr -a <file>` 確認。

### Unity CLI

3. **Unity CLI 安裝**
   - 解法：`$env:UNITY_CLI_CHANNEL='beta'; irm https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 | iex`。安裝程式會自動把 Unity skill 裝進 Claude Code，不用另外跑 `unity skill install`。

4. **建專案前 `unity license status` 顯示沒有授權**
   - 原因：登入 Unity 帳號不等於啟用授權。
   - 解法：`unity license activate --personal --accept-eula`。接受條款必須由使用者本人同意。

5. **`unity projects create <name> --path <parent>` 一定會建一個新資料夾，不能直接建在已有內容的 repo 根目錄**
   - 解法：先建在暫存資料夾（`C:\NASA\_unity_tmp\GoNoGo`），再把內容整個搬進 repo 根目錄。`productName` 會是 `GoNoGo`。
   - 好處：`projects create` 會順便裝好 Pipeline 套件、Cinemachine 3，並開啟 Run In Background。

6. **第一次開 Editor 時，`unity status` 回報 `STATUS_BLOCKED_BY_DIALOG_SOFTWARE_TERMS`**
   - 原因：Unity Editor 軟體條款對話框只能由使用者本人按「Accept and Continue」。
   - 解法：請使用者在 Editor 視窗接受一次，之後就不會再出現。

7. **在 PowerShell 跑 `unity command script eval '... return "ok";'`，C# 編譯錯誤 `CS0103: The name 'ok' does not exist`**
   - 原因：Windows PowerShell 5.1 傳參數給原生程式時，會把參數裡的雙引號吃掉。
   - 解法：凡是帶 C# 程式碼的 `unity command`，一律改用 Bash 執行，並先 `export PATH="/c/Users/sheng/AppData/Local/Unity/bin:$PATH"`。

8. **用外部工具新增 `.cs` 檔後，`unity recompile` 回傳 `up_to_date`，新類別卻不存在；有時回傳 HTTP 400**
   - 原因：Editor 不在前景時不會自動偵測檔案變動；剛觸發編譯或 domain reload 時，Pipeline 伺服器會暫時回 400。
   - 解法：先 `unity command script eval 'UnityEditor.AssetDatabase.Refresh(); return "ok";'`，再 `unity status --until-ready`，最後 `unity recompile`。用 `System.Type.GetType("Namespace.Class, Assembly-CSharp-Editor")` 確認類別真的編譯進去了。

9. **Unity 截圖**
   - 解法：`unity command capture game_view --source camera --width 1280 --height 720 --output-path Screenshots/x.png`。輸出到專案根目錄的 `Screenshots/`（在 `Assets/` 外，不會產生 `.meta`，已加入 `.gitignore`）。
   - 編輯模式下粒子不會自己跑：截圖前先用 `ParticleSystem.Simulate(t, true, true, true)` 模擬到指定秒數。

### Blender MCP

10. **用 Blender 命令列自動安裝 BlenderMCP 外掛被權限擋下**
    - 原因：auto mode 分類器把「從網路下載的程式碼在 Blender 裡執行」判定為外部程式碼。
    - 解法：外掛檔交給使用者在 Blender UI 裡安裝（Edit → Preferences → Add-ons → Install from Disk）。

11. **使用者在檔案總管找不到 `C:\Users\<user>\AppData\...` 下的外掛檔**
    - 原因：`AppData` 是隱藏資料夾。
    - 解法：把檔案複製到 `Downloads` 等看得到的位置再給路徑。

12. **在 Blender 按 `N` 開側邊欄沒有反應**
    - 原因：Blender 的快捷鍵只對滑鼠所在的區域有效。
    - 解法：滑鼠移到 3D 視窗上再按 `N`，或用選單 View → Sidebar。

13. **Blender MCP 需要 `uv`，但電腦上沒有**
    - 解法：`irm https://astral.sh/uv/install.ps1 | iex`，裝在 `~/.local/bin`；再 `claude mcp add blender -- uvx blender-mcp`。

14. **新加的 MCP server 在目前的對話裡看不到工具**
    - 原因：MCP 工具在 Claude Code 啟動時載入。
    - 解法：重開 Claude Code（`claude --continue` 可接續對話）。

15. **在終端機裡用 Ctrl+V 貼不了圖片**
    - 解法：Windows 上用 `Alt+V`，或直接把圖片檔拖進終端機，或給圖片路徑。

### Blender 煙霧序列圖

16. **Blender 5 清除關鍵格時 `AttributeError: 'Action' object has no attribute 'fcurves'`**
    - 原因：Blender 5 改成 slotted actions，F-Curve 移到 `action.layers[].strips[].channelbags` 底下。
    - 解法：物件只有這組動畫時，直接 `obj.animation_data_clear()` 再重設關鍵格。

17. **EEVEE 算出來的煙霧整團是黑的**
    - 原因：EEVEE 在**正交攝影機**下，燈光不會打進體積（volume）。同一格用透視攝影機算就正常。
    - 解法：改用 200 mm 長焦透視攝影機，距離 = 框住寬度 × 焦距 ÷ 36（感光元件寬度），看起來幾乎和正交一樣。

18. **煙往上衝得太快，撞到模擬範圍頂部變成香菇狀**
    - 解法：`use_collision_border_top = False`（頂部開放），降低 `alpha` 和 `beta` 浮力，降低初速。

19. **加了亂流之後，煙充滿整個模擬範圍，在格子邊緣被切成方形**
    - 解法：模擬範圍從 3 m 放大到 4 m、解析度 80 → 96、亂流減弱、開啟 `use_dissolve_smoke`，讓煙自然淡出。

20. **Blender MCP 長時間烘焙時回報 `Communication error with Blender: No data received`**
    - 原因：MCP 等待逾時，但 Blender 裡的烘焙其實還在跑，最後也成功了。
    - 解法：用快取資料夾的檔案數確認是否完成（`data/`、`noise/` 各有 N 個檔）。算圖分批執行，每批控制在 2 分鐘內。

21. **`python -I script.py` 找不到 Pillow**
    - 原因：`-I`（isolated mode）會忽略使用者 site-packages，Pillow 裝在那裡。
    - 解法：自己寫、只處理自己產生的檔案的腳本，不用 `-I`。處理下載來的不明檔案時才用 `-I`。

### Unity 粒子

22. **Unity 裡煙霧底部出現水平的硬切線**
    - 原因 1：軟粒子沒有生效，因為攝影機沒有深度貼圖。
    - 原因 2：序列圖裡的煙團底部被模擬範圍的地板切平。
    - 解法 1：只在需要的攝影機上設 `UniversalAdditionalCameraData.requiresDepthTexture = true`，不改專案全域設定。
    - 解法 2：`make_atlas.py` 對每格底部 22% 做 smoothstep alpha 淡出。

23. **鏡像發射器缺了顏色、尺寸、序列圖等設定**
    - 原因：`Object.Instantiate` 在後面的模組設定之前就執行了，複製到的是半成品。
    - 解法：所有模組設定完之後才複製。

24. **`main.randomSeed` 編譯錯誤**
    - 原因：`randomSeed` 是 `ParticleSystem` 本身的屬性，不在 `MainModule`。而且只能在粒子停止時設定。
    - 解法：`ps.Stop(true, StopEmittingAndClear); ps.useAutoRandomSeed = false; ps.randomSeed = 1234; ps.Play();`
