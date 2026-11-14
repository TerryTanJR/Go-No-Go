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

---

## 建置：單人版

### 核心（Core）與測試

25. **`unity command test run` 跑到 600 秒逾時，之後整個 Editor 主執行緒卡死（`script eval` 150 秒都沒回應）**
    - 原因：Editor 不在前景時，測試執行器會停住；`unity test` 也拒絕在已開啟的專案上執行。
    - 解法：核心是純 C#（No Engine References），改在 Unity 外用 Unity 內建的 .NET SDK 跑：`bash tools/run-core-tests.sh`。Editor 卡死時用 `unity close <專案> --force` 再 `unity open <專案>`（場景要先存檔）。

26. **dotnet 測試全過，Unity 卻進 Safe Mode：`'ConstraintExpression' does not contain a definition for 'AnyOf'`**
    - 原因：Unity 的 `com.unity.ext.nunit` 是以 NUnit 3.5 為基礎，測試器用的 NuGet NUnit 3.14 多了 `Has.All.AnyOf` 等 API。直接把 NuGet 降到 3.5 也不行，它在 .NET 8 載不起來（`System.Runtime.Remoting` 型別載入失敗）。
    - 解法：多一個只編譯不執行的 `tools/UnityApiCheck/UnityApiCheck.csproj`（net472 + `Library/PackageCache` 裡 Unity 實際的 `nunit.framework.dll`），`run-core-tests.sh` 先跑它。測試裡改用 `Assert.That(list.All(...))`、`Has.No.Member(...)`。

27. **`*.csproj` 被 Unity 的 .gitignore 規則忽略，測試器專案沒進版控**
    - 解法：在 `.gitignore` 加 `!/tools/CoreTests/CoreTests.csproj` 和 `!/tools/UnityApiCheck/UnityApiCheck.csproj`。用 `git check-ignore -v <file>` 檢查。

28. **同一個模擬結果，Unity 和 .NET 8 序列化後的 SHA 不同（長度 929,544 vs 919,855）**
    - 原因：數值本身一樣，但 double 轉字串的規則不同（Mono 輸出 17 位數，.NET Core 3.0+ 輸出最短往返字串）。
    - 解法：比對確定性時，用固定小數位（`ToString("F6", InvariantCulture)`）組指紋。`SimulatorTests.Fingerprint()` 兩邊都是 `9370C5F071BA88B4`，證明 `System.Random(seed)` 在 Mono 和 .NET 8 結果一致。

29. **`unity command package search --name` 不是有效參數**
    - 解法：用 `--query`。改完 `Packages/manifest.json` 後跑 `unity command package resolve`。

30. **`unity status` 偶爾回報 `STATUS_ALL_UNREACHABLE`，但 Unity.exe 還在**
    - 原因：Editor 正在匯入或編譯，或主執行緒被卡住。
    - 解法：先 `unity status --until-ready`；如果 `script eval --timeout 150` 也沒回應，就當作卡死，照第 25 條重開。

### 資料

31. **DONKI API（api.nasa.gov 和 kauai.ccmc.gsfc.nasa.gov）回 503 / 連線逾時**
    - 原因：NASA 上游服務當時故障，`DEMO_KEY` 或直連 CCMC 都一樣。
    - 解法：先用 NASA/NOAA 公開報告整理 12 個著名事件，寫進 `donki_events.json` 當備援快照，連結指到 DONKI 查詢頁或 NASA 文章。服務恢復後跑 `python tools/data/fetch_donki.py` 重抓（會依 Kp 和閃焰等級自動換算嚴重度 1–3）。

32. **藍圖裡的歷史關卡 JSON 範例用 camelCase（`presetDesign`），資料契約範例用 snake_case（`mass_kg`）**
    - 解法：統一用 snake_case（`SnakeCaseNamingStrategy`），搭配 `MissingMemberHandling.Error`，欄位拼錯就直接丟例外，不會悄悄變成 0。

33. **科學分數線性計算時，便宜小衛星只拿到 13 分，幾乎等於失敗；高科學型又遠超 100**
    - 解法：改成 `100 · √(科學點數 / 參考值)`，上限 100。預算剩越多效率分越高（25% 封頂），質量與電力餘裕 10–50% 最好。調完的中位數：便宜型 71（B）、均衡型 91、高科學型 97，三組都在不同分項領先（`BalanceTests`）。

### 素材下載與匯入

34. **eoimages.gsfc.nasa.gov（Blue Marble、Black Marble 原始網址）連線逾時**
    - 原因：NASA Earth Observatory 圖庫已搬家。
    - 解法：改從 `assets.science.nasa.gov/content/dam/science/esd/eo/images/...` 下載同一批檔案；雲層圖改用 SVS 4439 的全球雲圖。

35. **google/fonts 的 Noto Sans TC 可變字型匯入後預設是 Thin（字重 100）**
    - 原因：可變字型的預設軸值是最細的那一端，Unity 的 FontAsset 不會自動選字重。
    - 解法：改用 noto-cjk 倉庫的靜態 OTF（`NotoSansTC-Regular.otf` / `-Bold.otf`）。

36. **media.githubusercontent.com 抓 NASA-3D-Resources 的 .glb 回 404**
    - 解法：改用 `raw.githubusercontent.com/nasa/NASA-3D-Resources/master/...`，它會自動轉 LFS 物件。

37. **glTFast 匯入 NASA .glb 失敗：需要 `KHR_draco_mesh_compression`**
    - 原因：NASA 大部分模型用 Draco 壓縮，而且列為 required extension。glTFast 本身不含解碼器。
    - 解法：`unity command package add --name com.unity.cloud.draco@5.6.0 --confirm true --wait true`（不加 `--confirm true` 會被拒絕）。加完重新匯入 → 16 個模型都正常。

38. **Sat_Terra.glb 匯入失敗（Failed to import ... see inspector），而且單位是毫米（26 km 大）、11.7 萬面**
    - 解法：Terra 不是必要模型（地球觀測用 Landsat 8、碳監測用 OCO-2），直接移除，CREDITS 一起更新。Atlas 6（16.5 萬面）只放在發射場景遠景，不放進 WebGL 常駐場景。

39. **NASA 模型尺度不一：SatelliteKit 零件只有 2–26 公分、Terra 是毫米、DSN 天線 34 m**
    - 解法：場景建置腳本依 Renderer bounds 自動縮放到目標尺寸（`FitToSize`），不手調 Transform。

40. **Python `-I`（隔離模式）找不到 Pillow；機器上沒有 ffmpeg 可以剪音訊；Unity 不能匯入 m4a**
    - 原因：`-I` 會忽略使用者 site-packages。
    - 解法：處理下載檔改用 `python -P -E`（一樣不從目前目錄載入程式碼）。音訊只抓 NASA 的 wav/mp3 原檔（不用 m4a），用 numpy 裁切：windowed-sinc 低通 → 降頻到 24 kHz → 淡入淡出 → 峰值正規化到 -1 dBFS。

41. **IM-2 發射台麥克風原始錄音本身就削波（clipping）**
    - 解法：峰值正規化到 -1 dBFS 後，在遊戲裡只用點火後那幾秒，AudioSource 音量壓低，並和遠方海灘的低頻轟鳴疊在一起蓋過削波的刺耳感。
