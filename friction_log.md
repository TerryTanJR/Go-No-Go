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

### 程式架構與 Unity API

42. **發射動畫用 Timeline 還是程式提示表（cue list）？**
    - 原因：發射有 5 種結局（正常、低軌、爆炸、整流罩故障、速度不足），每種的鏡頭和事件時間不同。Timeline 得做 5 條或用大量條件軌，改一個時間點要動好幾條。
    - 解法：`LaunchDirector` 用程式的 `Cue(t, action)` 清單，依結局組出不同清單；Cinemachine 3 負責鏡頭切換與混合。「跳過發射」直接用小步長快轉同一份清單，所有分離、展開都會正確發生。

43. **一個 .cs 檔放兩個 MonoBehaviour，場景掛上去後顯示 Missing Script**
    - 原因：Unity 要求 MonoBehaviour/ScriptableObject 的類別名稱和檔名一致，一檔一類。
    - 解法：拆成各自的檔案（純 C# 類別可以放在一起）。

44. **`new MaterialPropertyBlock()` 寫在 MonoBehaviour 欄位初始化子，進 Play 時丟 `UnityException: CreateImpl is not allowed to be called from a MonoBehaviour constructor`**
    - 解法：欄位只宣告，在 `Awake()` 裡建立。

45. **直接改 `renderer.sharedMaterial` 的顏色或參數，會改到磁碟上的材質資產（離開 Play 也不會復原），git 一直出現材質變更**
    - 解法：每個物件不同的值（推力、發光強度）一律用 `MaterialPropertyBlock`；共用材質只在建置腳本裡設定。

46. **`TextSettings.defaultFontAsset` 已標成過時（obsolete）；`FindObjectsSortMode` 參數版本的 `FindObjectsByType` 也出現棄用警告**
    - 解法：預設字型改由 Theme.uss 的 `:root { -unity-font-definition: ... }` 指定，中文字型放在 PanelTextSettings 的 `fallbackFontAssets`；`SmokeLabBuilder` 的搜尋改用不帶排序參數的 `FindObjectsByType<T>(FindObjectsInactive.Exclude)`（排序參數已被棄用），編譯保持 0 警告。

47. **WebGL 預設品質等級沒有跟著 QualitySettings 的「預設」走**
    - 原因：每個平台各自有預設品質（`m_PerPlatformDefaultQuality`），WebGL 預設是最高那一級。
    - 解法：建置腳本用 `SerializedObject` 直接把 WebGL 的預設品質設成 1（中等），PC 維持高品質。

### UI Toolkit

48. **USS 寫 `Label { white-space: normal; }`、`Button { ... }` 完全沒有作用，按鈕是灰色預設樣式、文字全部不換行**
    - 原因：自訂的 .tss 匯入了 `unity-theme://default`，預設主題用的是類別選擇器（`.unity-label`、`.unity-button`、`.unity-button:hover:enabled`），類別的權重比型別選擇器（`Label`）高，所以永遠蓋過我們的設定。
    - 解法：一律改寫成 `.unity-label`、`.unity-button`；hover/active 要同時寫 `:hover` 與 `:hover:enabled`（例如 `.btn-go:hover:enabled`），否則預設主題的 `:hover:enabled` 又贏。

49. **改成所有文字都可以換行之後，大數字（倒數時鐘、總分、Day 數）被拆成兩行**
    - 解法：在 Theme.uss 最後加一條 nowrap 清單（`.ops-day, .launch-clock, .total-num, .title-logo, .tele-v, .verdict, .chip …`），只對「必須單行」的元素關掉換行。

50. **設計室的「任務年限」數值有時是空的**
    - 原因：切到總監分頁時，`Refresh()` 先寫入年限文字、再因為軌道變了重建分類清單，重建把那個 Label 換掉了。
    - 解法：`Refresh()` 一開始先做「需要重建就重建」，之後才設定卡片狀態與文字。

51. **3D 場景的滑鼠拖曳（旋轉衛星、轉地球）在 UI 面板上也會觸發**
    - 解法：根節點與畫面層設 `pickingMode = Ignore`，`PointerHooks.OverUI` 用 `RuntimePanelUtils.ScreenToPanel` + `panel.Pick()` 判斷游標下是不是 UI（注意螢幕座標 y 要翻轉：`Screen.height - y`）。

### 燈光、材質與畫面

52. **發射台整個過曝，而且高光偏青綠色**
    - 原因：三盞探照燈強度 9000、顏色偏藍；ACES 色調映射會把過曝的偏藍高光推向青色。
    - 解法：強度降到 1200、改成暖白色 (1, 0.94, 0.85)。

53. **程序天空（Skybox/Procedural）黃昏時天空中段變成橄欖綠**
    - 原因：`_SkyTint` 偏藍加上大氣厚度 1.35，太陽仰角低時散射算出來偏綠。
    - 解法：SkyTint 用中性灰 (0.5, 0.5, 0.5)、大氣厚度 1.12、曝光 1.2。

54. **設計室畫面整片偏青色、底座過曝**
    - 原因：底座上方 0.4 m 有一盞點光源，離表面太近。
    - 解法：拿掉點光源，改成從地面往上打的聚光燈（強度 3、角度 50°）。

55. **任務控制中心地板上有一條垂直的光柱**
    - 原因：光滑地板（smoothness 高）把天花板燈的鏡面反射拉成一長條。
    - 解法：地板和階梯改用霧面材質 `M_FloorMatte`（smoothness 0.35 + 格線貼圖），鏡頭也稍微抬高。

56. **底座的圓環看得出多邊形稜角**
    - 原因：Unity 內建 Cylinder 只有 24 段。
    - 解法：建置腳本自己產生 96 段的圓柱（`HiCylinder`）和 160 段的圓環（`Annulus`），存成 Mesh 資產。

57. **太空鏡頭裡地球有一條細細的亮線，地平線邊緣看得到稜角**
    - 原因：火箭剛好在北極上方，經度 ±180° 的貼圖接縫和所有經線都在極點收斂；而且 192 段的整顆球放大到半徑 6000 後，地平線每段約 100 單位，近看就是折線。
    - 解法：只產生看得到的那一塊球冠（`SphereCap`：以北緯 26°、西經 74° 為中心、半角 34°、160×256 段），再旋轉讓它朝上。頂點集中在畫面裡，物件空間方向仍是經緯度，所以行星 shader 不用改。

58. **支柱的混凝土貼圖看起來像軟木塞**
    - 解法：改用金屬飾條材質 `art.trim`。

### 發射鏡頭（Cinemachine 3）

59. **切到太空場景的前一秒只看得到星空**
    - 原因：火箭瞬間移動了約 2 萬公尺，跟隨鏡頭的阻尼還在從發射台「飛」上來。
    - 解法：切換前對 `camSpace / camSide / camDeploy` 設 `PreviousStateIsValid = false`，阻尼狀態作廢。

60. **從側面鏡頭混合（blend）到衛星展開鏡頭時，鏡頭路徑穿過上節箭體**
    - 解法：改成硬切（`Cut`）。

61. **硬切之後，展開鏡頭前三秒畫面一半是白色上節**
    - 原因：展開鏡頭在衛星後側方 5.5 m，剛好貼著上節側面。把鏡頭移到衛星前方往回拍也不行：30 m 外的上節正面變成畫面左邊一大片米色圓盤。
    - 解法：鏡頭維持在後側方，硬切的同一個影格把上節往後移 `6 + 8k` 公尺（k = 火箭高度 / 70），退到鏡頭後面；已經飄走的整流罩也一起隱藏。硬切看不出瞬移，畫面就像分離已經完成。

62. **塔邊鏡頭被導焰槽的煙整個蓋住；往上移之後又太貼近箭身，畫面只剩一根白柱子**
    - 解法：放在塔的斜後方 (-16, 14, 24) 低角度仰拍，`RotationComposer.TargetOffset` 由導演依火箭高度瞄準箭身中段（高度 × 0.35）。

63. **長焦追蹤鏡頭裡，火箭加速時衝出畫面上緣；天空中沒有排氣尾跡，畫面很空**
    - 原因：瞄準點是火箭底部，卻放在畫面中線偏上（ScreenPosition y = -0.08，Cinemachine 3 的 y 正值是往下），阻尼 0.6 又追不上加速度。
    - 解法：ScreenPosition 改成 (0, 0.2)（中線偏下）、阻尼 0.25；升空時在第一節底部掛 `TrailRenderer`（9 秒、寬度由 1.3 倍到 10 倍直徑、寬度方向柔和淡出的程序貼圖），爆炸時停止發射但保留在空中慢慢散掉；切到太空前先 `Clear()`，否則火箭瞬移會畫出一條橫跨天空的直線。Electron（18 m）在 1.6 km 外只剩幾個像素，長焦 FOV 依火箭高度乘 `clamp(k × 1.3, 0.4, 1)`。

64. **速度不足／整流罩故障的墜落段：上節甩出畫面，有時鏡頭轉到只拍到星空、看不到地球**
    - 原因：翻滾是繞火箭底部（root）旋轉，上節離支點約 40 m，甩出一個大弧；鏡頭又跟著上節的軸心（會跟著翻滾的那一端）走。
    - 解法：開始下墜時算出上節 Renderer 的幾何中心，改用 `RotateAround(中心)` 翻滾；鏡頭改追一個不旋轉的 `FallFocus` 點（每格放在上節中心），並在 3 秒內把跟隨距離拉遠到 1.8 倍。

65. **整流罩打開後一直鉸在上節上，上節翻滾時把它甩到鏡頭前；正常入軌時也會飄進展開鏡頭**
    - 解法：打開動畫結束（2.5 秒）後把兩半改掛到 `SpaceAnchor`，保留往外與稍微往後的速度並慢慢翻轉；展開鏡頭硬切時隱藏。

66. **墜落時遙測高度從 397 km 跳到 628 km 再往下掉，速度直接歸零**
    - 原因：失敗結局的高度曲線終點還是目標軌道高度（700 km）；下墜時每個影格減一個固定量（跟影格率有關），速度每格減 0.01 很快就到 0。
    - 解法：失敗結局的曲線終點改成次軌道頂點 150 km；下墜用開始下墜那一刻的高度與速度當起點，`alt = h0 − 1.6·t²`、`vel = v0 + 0.04·t`，跟影格率無關。

### 命令列工具（Unity CLI / Bash）

67. **`unity command script eval` 的各種小陷阱**
    - `-- --code "..."` 會回「Give either code or file」：程式碼要當位置參數直接傳。
    - 擷圖參數是 `--output-path`，不是 `--save_path`。
    - eval 裡 `.Q()`、`.Query()` 擴充方法解析不到：要寫 `UQueryExtensions.Q(root, ...)`。
    - `objectReferenceInstanceIDValue` 已過時：用 `objectReferenceEntityIdValue != EntityId.None`。

68. **`unity command job wait` 的條件寫 `"op":"gt"` 會失敗**
    - 原因：只接受 `equals, notEquals, greaterThan, lessThan, contains, changed`。錯誤只寫在 Editor console，命令列這邊看起來像「立刻等到了」，結果擷圖時間全部錯開。
    - 解法：改用 `greaterThan`／`lessThan`。另外，等靜態欄位（例如 `LaunchFeed.T`）前要先等它被新場景重設（先等 `lessThan 0`），否則讀到的是上一次發射留下的值。

69. **Bash heredoc 內容有大量引號時出現 `unexpected EOF`（又發生一次）**
    - 解法：較長的修改一律先用 Write 寫成暫存資料夾裡的 Python 腳本，再用 `python -P -E script.py <專案路徑>` 執行；腳本先檢查每段原文在檔案裡恰好出現一次，否則中止，不會改到一半。

### WebGL 建置

70. **第一次 WebGL 建置花了約 15 分鐘**
    - 原因：從 Windows 切到 WebGL 平台，所有貼圖、shader 都要用 WebGL 的格式重新匯入和編譯；之後 Editor 的目標平台就停在 WebGL。
    - 解法：用 `unity command build run -- --target WebGL --output-path Builds/WebGL --confirm true`（先 `--dry-run true` 驗證），在背景每 20 秒查一次 `build status`。建置期間不改 .cs，避免 Player 建置讀到改到一半的程式碼。`Builds/` 已在 .gitignore。

71. **WebGL 首次載入 33.6 MB，超過藍圖的 30 MB 目標**
    - 原因：建置報告依資產加總（未壓縮 50 MB）：Landsat 8 模型網格 8.7 MB（.glb 是 Draco 壓縮的 780 KB，匯入後解成 25.6 萬個頂點）、中文字型 5.7 MB、地球日間貼圖 5.6 MB、煙霧序列圖 5.6 MB、Unity 啟動畫面 Logo 2.8 MB（Personal 授權不能拿掉）。
    - 解法：建置腳本統一套用藍圖的發布設定（`BuildUtil.ApplyWebPublishSettings`）：Brotli、關掉 Decompression Fallback、Code Optimization = Disk Size with LTO、WebAssembly 2023、剝除沒用到的網格通道；大貼圖只對 WebGL 平台覆寫成 DXT Crunch（品質 80，煙霧 85），桌機版不變。結果：data 24.3 → 20.5 MB、wasm 9.2 → 8.4 MB，總計 **29.08 MB**，太空鏡頭擷圖比對看不出畫質差異。
    - 還可以再省：Landsat 8 只在標題畫面以 0.55 單位大小出現，可以先在 Blender 減面到約 25%（約省 6 MB 未壓縮）。

72. **建置時出現 3 個 `UAC1015` 警告：`RoomSnapshot` 的 Dictionary 欄位沒有 `[SerializeField]`**
    - 原因：Unity 6.6 的序列化分析器開始檢查 Dictionary 欄位。這個類別是線上版用 Json.NET 寫進 Firebase 的，不走 Unity 序列化。
    - 解法：加 `[UnityEngine.SerializeField]` 明確表態。**不能**用 `[NonSerialized]`：Json.NET 也會跳過有 `NonSerialized` 的欄位，線上版的成員、投票、選擇就同步不到了。第二次建置 0 警告。

73. **WebGL 建置後專案根目錄多了 `Data/Plugins/lib_burst_generated.{cpp,wasm}`，被 `git add -A` 一起 commit 了**
    - 原因：Burst 在 WebGL 建置時把產生的程式碼放在專案根目錄的 `Data/`，Unity 的標準 .gitignore 沒有涵蓋。
    - 解法：`.gitignore` 加 `/Data/`，`git rm -r --cached Data` 從版控移除（檔案留在本機）。以後 commit 前先看 `git status`，不要直接 `add -A`。另外建置也會更新 URP 資產的 shader 剝除旗標（`m_Prefilter*`）、在 `preloadedAssets` 加入 Input System 的全專案動作表，這兩個是正常變動。

74. **標題畫面的 Landsat 8 幾乎看不到**
    - 原因：軌道一圈 112 秒，起始相位 200° 剛好在地球背面，之後又轉到畫面右緣外（viewport x 1.11），玩家停在標題的前一分鐘都看不到它。
    - 解法：用 eval 每 15° 取樣一次軌道位置的 viewport 座標與前後關係，找出在地球正面、畫面裡的那一段（相位 345°→100°）；起始相位改成 350°、速度降到 2.4°/s，進標題後約 48 秒都看得到它從地球前面飛過。

### 試玩回饋

75. **開局的紅燈寫「只有 100% 的資料能傳回」，玩家看不懂為什麼不能 GO**
    - 原因：記錄器的需求有 1.5 倍漏接餘裕（`storage_safety_factor`）。開局設計實際放得下（真的能傳回 100%），但沒有餘裕所以判定超標，文字卻只寫了「溢出」那一種情況。通訊席位的 NO-GO 理由「會爆滿」也有同樣的誤導。
    - 解法：警告改成同時寫出「需要多少（含 50% 漏接餘裕）／裝了多少／目前能傳回多少」；NO-GO 理由改成「漏接一次過境就會爆滿」。警告代碼不變，測試不用改。

76. **橘燈（餘裕不足、還沒超標）出現「還差 -7 Wh」這種負數**
    - 原因：橘燈沿用了超標的句子（超出／還差多少），還沒超標時差額是負的；質量、預算、電力都會這樣。
    - 解法：`DesignScreen.WarningText` 對有 `margin_pct` 的橘燈一律改用 `warn.tight`：「{資源}餘裕只剩 N%：建議至少留 10%」。
