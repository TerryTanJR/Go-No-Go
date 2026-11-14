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

77. **倒數的聲音和畫面上的時鐘對不起來（念「六」的時候畫面寫 T-10）**
    - 原因有三個：
      1. 錄音從估計的 43 秒處再加 3 秒開始播，實際上「十」在 44.44 秒，T-10 時錄音已經念到「六」；
      2. 錄音每數一下約 0.93 秒，不是 1 秒，照遊戲時間走會越差越多；
      3. 時鐘用截斷顯示（`(int)|T|`），T 剛過 -10.0 畫面就變成 9，比念「十」早一秒。
    - 解法：
      1. 用振幅包絡量出每個數字開口的時間（十 44.44 s … 零 53.56 s），存進 `AudioBank.countdownCalls`；
      2. 倒數期間遊戲時間由錄音位置換算（`LaunchClock.TFromAudio`，每個開口對到 T = -n，中間內插），錄音停住或差超過 1.5 秒就退回用實際時間；
      3. 倒數改成無條件進位顯示（`LaunchClock.Format`）。
    - 換算與顯示寫在 Core 的純函式裡，加了 EditMode 測試。實測錄音 48.24 s 時 T = -5.96（「六」在 48.10 s）。

78. **爬升到太空是一刀硬切，看不到分離過程，天空到切換前都還是黃昏**
    - 解法：改成一個連續的長焦追蹤鏡頭，所有分離都在同一個鏡頭裡完成：
      - 助推器分離（Falcon Heavy、Delta II）；
      - 主引擎關機與滑行，接著節間分離；
      - 第二節點火，鏡頭轉去追上節並推近。
    - 天空依遙測高度（3 → 75 km）變黑：
      - 程序天空的曝光與大氣厚度、霧、環境光、反射探針都往太空的值走；
      - 陽光從黃昏暖色轉成太空白光；
      - 只改執行期的材質複本，不動資產。
    - 天空全黑後才換到太空場景，換場那一格對準同一個構圖：
      - 鏡頭方向和火箭在畫面上的大小都跟長焦一樣，只是距離近（視角 50°）；
      - 接著 3.5 秒移到上節後上方，地球才從畫面下方出現。

79. **換場要「同一格」，太空場景的方向就得跟發射台一致**
    - 原因：太空鏡頭的偏移量、太陽方向是照「火箭朝 +Z、Euler(80,0,0)」調的；發射台段的火箭卻是往長焦鏡頭的右方轉向，不然分離時兩節會沿著視線疊在一起，看不出分開。
    - 解法：
      - 飛行方位取「長焦鏡頭的右方」，太空場景整組繞 Y 轉到同一個方位（`_yaw`）；
      - 太空鏡頭的 Follow 偏移、太空陽光也乘上同一個旋轉；
      - 火箭在離塔後做滾轉程序（roll program，跟字幕「開始俯仰與滾轉程序」一致），轉向公式最後正好等於 `_yaw × Euler(80,0,0)`，原本調好的太空鏡頭全部不用重調。

80. **長焦鏡頭變焦後，程序星空的星星變成一團團模糊光斑**
    - 原因：程序星空的星點大小是用角度算的，視角 1～2° 時每顆星被放大幾十倍。
    - 解法：長焦段星空亮度維持 0（真實追蹤攝影的曝光是對準明亮的火箭，本來就拍不到星星），換到太空、鏡頭拉開後才在 4 秒內亮起來。太空場景的 `EnvironmentOverride` 會換回原本的星空資產，換場時要再指定一次執行期複本。

81. **小火箭（Electron）節間分離後，上節被甩出畫面**
    - 原因：分離出去的第一節用固定的世界速度飛，火箭本體卻沿著重力轉向一直轉彎；每秒幾度的方向差乘上約 200 m/s，幾秒內就拉開上百公尺。Falcon 9 的畫面夠大看不太出來，Electron 的畫面只有約 75 m。
    - 解法：分離物改用「相對火箭的位置與速度」，跟火箭走同一條彎曲路徑，只算相對運動（被推開的速度，加上沒跟上的那部分加速度）。

82. **長焦變焦之後追不上火箭、晃動被放大**
    - 原因：Cinemachine 的旋轉阻尼和手持雜訊都是角度。視角從 6.5° 縮到 1.6° 時，同樣的落後和晃動在畫面上大了 4 倍。
    - 解法：阻尼與雜訊強度都乘上「目前視角 ÷ 初始視角」，節間分離後阻尼歸零。視角由導演每格依距離計算（`2·atan(取景高度 / 2 / 距離)`），火箭在畫面裡維持一樣大。

83. **整流罩打開後，其中一半直直飛向側面鏡頭；或一脫離就以 30 m/s 往後飛出畫面**
    - 原因：
      - 整流罩沿上節的 X 軸分開，側面鏡頭正好在 +X 那一側；
      - 脫離後兩半沒有繼承箭體的速度，上節以 30 m/s 往前，它們等於往後飛。
    - 解法：
      - 上節繞自己的軸轉 90°，兩半改成在畫面上下方向分開；衛星的掛載點反向轉回來，展開鏡頭看到的方向不變；
      - 脫離後兩半保有 90% 的箭體位移，慢慢落後、往外飄；
      - 太空鏡頭改瞄準「上節＋整流罩」的中段，並拉遠（貼身追蹤 1.7 倍、側面 1.7 倍），整個打開、脫離、飄走的過程都在畫面裡。

---

## 安裝文件與跨平台

84. **`tools/` 的 Bash 腳本只能在這台 Windows 電腦上跑，同學和 Mac 不能用**
    - 原因：`unity-refresh.sh` 寫死了 `/c/Users/sheng/AppData/Local/Unity/bin` 和 `C:\NASA\NASA_test`；`run-core-tests.sh` 寫死了 Windows 版 Unity 內建 dotnet 的路徑；macOS 預設沒有 `timeout` 指令。
    - 解法：
      - 專案路徑從腳本位置推算，Windows 上用 `cygpath -w` 轉成 Windows 格式；Unity CLI 的位置從 `%LOCALAPPDATA%` 推算；
      - Editor 版本從 `ProjectSettings/ProjectVersion.txt` 讀，依序找 `UNITY_DOTNET` → Windows／macOS 預設安裝位置的內建 dotnet → PATH 上的 `dotnet`；
      - 有 `timeout` 用 `timeout`，有 `gtimeout` 用 `gtimeout`，都沒有就直接執行。
    - 安裝和協作流程寫在 `docs/SETUP.md`（Mac 那一節還沒在 Mac 上實測過）。

---

## 更新設計藍圖

85. **藍圖寫的和實際做出來的對不上，照藍圖做不出現在的遊戲**
    - 原因：實作過程中很多決定改了，但藍圖沒有跟著改；另外藍圖把還沒做的功能寫得像已經存在。
    - 「理由出處」欄：**記錄**＝當時有寫在程式註解、記憶或本檔；**補記**＝當時沒寫下來，這次從程式與實測數字整理出來。

      | 項目 | 原藍圖 | 修正後 | 為什麼改 | 理由出處 |
      | --- | --- | --- | --- | --- |
      | 引擎 | Unity 6.3 LTS | Unity 6000.6.8f1 + URP 17.6 | 使用者決定沿用電腦上已裝好的版本，不另裝 6.3 | 記錄 |
      | 範圍 | 四人連線為必備賣點 | 單人版完成；線上房間只有骨架，未啟用 | 使用者指示先做個人版，四人連線先不做，但檔案與端點保留 | 記錄（使用者指示） |
      | 發射 | 一條 Timeline，約 20 秒 | 程式提示表（`LaunchDirector`），約 56 秒，長焦一鏡到底拍完各段分離；天空依高度變黑；倒數對齊錄音；5 種結果 | 5 種結局用 Timeline 要做 5 條或大量條件軌；「略過」用小步長快轉同一份清單最簡單（第 42 條）。時間拉長是因為使用者要求分離過程要完整呈現、背景要逐漸變黑、倒數要對上聲音（第 77–83 條） | 記錄 |
      | 太空船 | NASA .glb 或 sand3d | 程式生成（`SatelliteBuilder`），外型跟著零件選擇改變；NASA .glb 只用在標題畫面的 Landsat 8 | 見下方「為什麼不用 NASA .glb」 | 補記 |
      | Blender | sand3d 或 blender-skills 二選一做 3D 模型 | 只用來做發射台煙霧：Blender MCP 驅動 Mantaflow 模擬 → EEVEE 逐格算圖 → 8×8 序列圖，三組地面煙霧都用它；模型不用 Blender 做 | 體積煙霧用 Unity 粒子很難做得像，預先模擬再算成序列圖最省效能（第 16–24 條） | 記錄 |
      | 火箭 | 依等級切換尺寸 | 程式生成 5 種（`RocketBuilder`） | NASA 3D Resources 的火箭是 Saturn V、Atlas、Jupiter-C，沒有遊戲裡的 5 種；發射動畫需要可以分開的各節、兩半整流罩、助推器、引擎火焰掛點 | 補記（`RocketBuilder` 註解有寫「比例取自公開規格、不用公司塗裝」） |
      | 多語 | Unity Localization 套件 | 自寫 `Loc` + `strings.json`（323 個 key） | 核心（No Engine References）也要用同一份字串：測試檢查「核心會產生的每個代碼都有中英文」，而 Unity 外的 .NET 測試讀不到 Unity Localization 的資料表 | 補記 |
      | UI 動畫 | DOTween 或 PrimeTween | 不用外掛，USS transition + 自寫補間 | 見下方「為什麼不用 DOTween」 | 補記 |
      | 後處理 | 只開 Bloom | 每個場景一組：ACES、Bloom、色彩調整、暗角、顆粒，控制中心加景深 | 使用者要求接近 3A 的畫面品質；後處理只多 shader、不多貼圖，建置後實測 29.08 MB 仍在 30 MB 內（第 71–72 條） | 記錄（大小）、補記（動機） |
      | 音效 | Artemis 音效庫 + Freesound + 3 首音樂 | NASA IM-2 實況（倒數、發射台、海灘）+ Juno 電波；UI 音效程式合成；沒有音樂 | Artemis I 的播報提到 Artemis 和 SLS，和玩家的任務對不上（`ArtBuilder` 註解）；UI 音效用合成不必找授權素材。沒有音樂：當時沒寫理由，列為後續 | 記錄（Artemis）、未記錄（音樂） |
      | 場景 | 5 個場景，手動製作 | 6 個，全部由 `SceneBuilder` 產生，不手改場景檔 | 場景檔幾乎無法合併（藍圖原本就提醒）；用程式產生就能整個重建，命令列也能自動驗證 | 補記 |
      | 點名規則 | 沒寫清楚 | 部門要求不會造成 NO-GO，只影響團隊協作分數 | 藍圖本來就沒定義，照程式實際行為補上 | — |
      | 難度 | 比賽只開工程師 | 學員、工程師、任務主管都可選 | 難度寫成 `difficulty.json` 的參數，多開兩種幾乎沒有成本 | 補記 |
      | 歷史關卡 | Kepler 首選 | 做的是 OCO 2009 | 藍圖本來就寫「OCO 可以沿用發射演出，幾乎不用新素材」；Kepler 要另做晃動與 K2 模式 | 記錄（藍圖原文） |

    - **為什麼不用 NASA .glb 當太空船（補記）**
      - 遊戲的重點是「設計直接決定畫面」：30 個零件、太陽能板片數、天線大小、儀器外型都要跟著玩家的選擇改變。NASA 模型是一整顆特定衛星（Landsat 8、OCO-2），沒辦法拆開來換零件。
      - SatelliteKit 雖然是零件組，但只有本體、無線電、太陽翼三種，涵蓋不了 6 種儀器 × 8 類零件。尺度也只有 2–26 公分（第 39 條）。
      - 程式還需要依類別分組，才能做滑鼠移到卡片時同類零件發光；發射時太陽能板要先收起來、部署後展開。這些都要知道每個零件的位置和軸心，自己產生的模型才做得到。
      - 檔案大小：Landsat 8 一個模型匯入後網格就有 8.7 MB（25.6 萬個頂點），佔 30 MB 目標將近三成（第 71 條）。
      - 已下載、目前沒用到的 NASA 模型：OCO-2、DSN 34 m、Mobile Launcher、Saturn V、Atlas 6、Jupiter-C、SatelliteKit。之後可以放在歷史關卡、營運地面站或標題畫面。
    - **為什麼不用 DOTween（補記）**
      - 藍圖原本就寫「DOTween 或 PrimeTween，到時候再決定」。兩者都要另外從 Asset Store 或 OpenUPM 安裝，Asset Store 要使用者自己登入同意條款。
      - 實際需要的動畫只有資源條變化、閃一下、淡入淡出、印章蓋下、零件彈出：UI Toolkit 的 USS transition 加上幾行自寫補間就做得到（`Ui.cs` 有「下一幀才加 class，transition 才播得出來」的小工具）。
      - DOTween 主要針對 Transform 和 uGUI，要控制 UI Toolkit 的樣式屬性得自己寫 getter／setter，省不了多少程式。
      - 少一個第三方套件，WebGL 也少一些程式碼。
    - 教訓：這兩個決定當時沒寫下理由，事後只能從程式反推。以後偏離藍圖的決定，當下就寫進本檔。
    - 解法：
      - 先讓兩個代理分頭讀完 Core／資料和 UI／場景／Builder 的程式，發射細節直接讀 `LaunchDirector`；
      - 藍圖保留原本章節，內文改成描述現況；沒做的部分標〔未實作〕；
      - 新增「附錄：重現規格」，放公式、資料表、發射時間軸、鏡頭與火箭參數、場景與後處理數值、驗證指令。
    - 以後改了機制或數值，要同一個 commit 一起改藍圖附錄。藍圖的 PDF 版沒有自動產生，要另外手動匯出。

86. **更新藍圖時把 Blender 煙霧寫成「實驗中、正式發射未使用」，其實三組地面煙霧都在用**
    - 原因：
      - 煙霧序列圖和材質放在 `Launch/Experiments/`，資料夾名稱看起來像沒在用。
      - 我照代理的摘要寫，沒有回頭查 `ArtBuilder` 把 `M_Smoke_Billow` 指定給 `LaunchKit.smoke`、`SceneBuilder` 用它建三組地面煙霧。
    - 解法：
      - 藍圖改正四個地方：修訂表、發射特效、美術資源、可用工具。另外在專案結構註明「Experiments/ 正式發射也在用」，附錄 C 補上煙霧材質。
      - 本檔第 85 條的修正表也補上 Blender 這一列。
    - 教訓：判斷「有沒有用到」要查引用（材質、GUID 被誰載入），不能看資料夾名稱。之後可以考慮把序列圖搬出 `Experiments/`。

## 3D 版（單人迴圈：設施、avatar、主選單）

87. **場景裡的名牌大多沒出現：`WorldLabel`、`WorldScreen` 寫在 `Hud.cs` 裡，存進場景後變成 Missing Script**
    - 原因：Unity 只能序列化「類別名稱和檔名相同」的 MonoBehaviour。執行時 `AddComponent<WorldLabel>()`（零件編號）正常，但 SceneBuilder 存進場景的名牌、看板一載入就是 Missing Script。`ConsoleTarget` 寫在 `Interactable.cs` 裡也一樣。
    - 解法：拆成 `Game/WorldLabel.cs`、`Game/WorldScreen.cs`、`Presentation/Facility/ConsoleTarget.cs`。用小腳本掃過所有 `.cs`：繼承 MonoBehaviour／Interactable／ScriptableObject 的類別名稱都要等於檔名。
    - 怎麼發現的：Play 時 `GameObject.Find("Label_ui.fac.stand")` 找得到，但 `GetComponents` 回來是 `MISSING`。

88. **avatar 的頭盔整個蓋住面罩、耳朵、鰭，從正面看像後腦勺**
    - 原因：Unity 6.6 的 `Resources.GetBuiltinResource<Mesh>("Sphere.fbx")` 回傳直徑 2 的網格（膠囊 2×4、圓柱 2×2），不是 `CreatePrimitive` 用的直徑 1。程式照直徑 1 設縮放，球形零件全部大一倍。
    - 解法：`AvatarBuilder.Builtin` 改用 `GameObject.CreatePrimitive(...)` 取網格再刪掉物件。面罩往前移到頭盔前緣外。
    - 沒改的：`SatelliteBuilder`、`RocketBuilder` 也用 `GetBuiltinResource`，但它們的尺寸是 v1 照畫面調過的，維持原樣。

89. **零件架看起來是空的**
    - 原因：
      - 很多零件是薄板（電池、電腦、太陽能板），從整顆衛星拆出來時是貼在本體側面的方向，放到架上剛好側面朝外，只看到 1 公分厚的邊。
      - 架子是黑色、零件也偏暗，十幾公尺外完全看不出來。
    - 解法：`PartItem.Create` 偵測最薄的軸，把大面轉向玩家、往後仰 15°；架子改淺色、每層加發光邊條、每座架子一盞聚光燈；零件尺寸 0.42 → 0.5 m。

90. **整合台上的衛星很小，而且懸在台座上方**
    - 原因：`SatelliteRig` 只有「太大才縮小」（maxSize），預設設計只有 0.9 m；模型以中心對齊，中心放在 1.6 m 高。
    - 解法：`SatelliteRig` 加 `fixedScale`（整合台用 2 倍）和 `bottomAnchor`（底部貼台面）。

91. **設施裡太暗，天花板是黑的**
    - 原因：環境光用三色漸層，朝下的面吃 ground 色（0.1），天花板內面全黑；太陽被當成室內主光但只照到地板。
    - 解法：環境光調亮（ground 0.32）、太陽 1.3、後處理曝光 +0.35；拿掉一盞補光避免 URP 每個物件的燈數上限。

92. **`.git/index.lock` 殘留，`git rm` 失敗**
    - 原因：之前某個 git 指令中斷（0 位元組、沒有 git 行程在跑）。
    - 解法：確認沒有 git 行程後刪掉 lock 再執行。

93. **在 Bash 用 heredoc 把一大段 USS 接到 `Theme.uss` 失敗：`unexpected EOF while looking for matching '''`**
    - 原因：同一個指令前面的 `grep` 參數有未成對的引號，整段被當成一個字串。
    - 解法：大段文字先用 Write 寫到暫存檔，再 `cat 暫存檔 >> Theme.uss`。

## 3D 版（多人連線）

94. **房間碼的連線服務即使選「直連」也要雲端專案**
    - 原因：Multiplayer Services 的 session（`CreateSessionAsync(...).WithDirectNetwork()`）一樣要先登入、在雲端建立房間；專案的 `cloudProjectId` 是空的（還沒連結 Unity Cloud）。
    - 解法：分兩條路。房間碼走 session＋轉送（`WithRelayNetwork`，WebGL 自動用 WSS）；「同一個網路」不經雲端，直接用 Netcode＋`UnityTransport.SetConnectionData`。沒連結雲端時建房會顯示 `ui.net.err.cloud`，引導改用同網路。

95. **`SessionState` 名稱衝突**
    - 原因：`Unity.Services.Multiplayer` 也有一個 `SessionState` 列舉，和 `GoNoGo.Core.SessionState` 同時 `using` 就編不過。
    - 解法：`NetLink.cs` 加 `using SessionState = GoNoGo.Core.SessionState;`。

96. **一台電腦、一個編輯器，沒辦法同時當房主和客戶端測試**
    - 解法：建一個 Windows 開發版 Player（`Builds/NetTest`，約 2–4 分鐘），加測試用命令列參數：`-gng-host` 開起來就當房主，`-gng-join 127.0.0.1 -gng-name 名字 -gng-control` 自動加入並送一個操作。編輯器當另一端，用 script eval 送操作、查狀態、截圖。兩個方向都測過：大廳 → 設計（拿取、安裝、回報）→ 發射 → 營運（結束回合、下一回合）→ 房主斷線回主選單。

97. **區網 IP 顯示成 VMware 虛擬網卡的 192.168.152.1**
    - 原因：`Dns.GetHostEntry` 回傳的第一個 IPv4 不一定是實體網卡。
    - 解法：改用 `NetworkInterface`，優先選有預設閘道、狀態是 Up 的網卡。

98. **客戶端當機或直接關掉視窗，房主要 30 秒才把他移出房間**
    - 原因：`UnityTransport.DisconnectTimeoutMS` 預設 30000。
    - 解法：改成 10 秒；正常按「離開」會立刻斷線。

99. **客戶端還沒註冊訊息處理就收到房主的狀態，訊息會被丟掉**
    - 原因：用房間碼加入時，`JoinSessionByCodeAsync` 回來時 Netcode 已經連上；房主如果在「有人連線」時就推狀態，可能早於客戶端註冊 `gng.state`。
    - 解法：房主連線時不推；客戶端註冊好處理器後送 `Join`，房主套用後廣播整包狀態（`Join` 重送也會回 Ok，所以一定會收到）。房間滿了回 `err.room_full`，客戶端顯示 `ui.net.err.full`。

100. **Windows Player 開場一直報 `ScreenSpaceAmbientOcclusion` 資源找不到、`Blitter is already initialized`（一萬多次）**
    - 原因：PC_Renderer 有啟用 SSAO，但 URP 全域設定（`UniversalRenderPipelineGlobalSettings.asset`）裡還沒登記 SSAO 的資源，建置時被剝除，URP 每幀重建管線都失敗。
    - 解法：第一次建置時 URP 自己把 SSAO 的兩筆資源登記進全域設定（已 commit）；之後的 Player 記錄 0 筆 SSAO／Blitter 錯誤。

### WebGL 建置（3D 版）

101. **WebGL 首次載入 32.2 MB，超過藍圖寫的 30 MB 目標**
    - 原因：多人套件（Netcode、Transport、Multiplayer Services）讓 wasm 從 8.4 MB 變 9.3 MB；ISS 模型 17 萬個三角形（網格約 3.5 MB，未壓縮）。
    - 解法（一項一項建置量測）：
      - 四段 NASA 長音檔改 22.05 kHz、品質 0.45（低頻轟鳴和無線電語音聽不出差別）：−1.5 MB。
      - 關掉 Unity 啟動畫面（Unity 6 的 Personal 授權可以關，第 71 條當時的說法已不成立）：−0.1 MB（Logo 本來就很好壓）。
      - ISS 在 Blender 減面到 25%（4.4 萬個三角形），標題畫面上看不出差別。
      - 雲層改 WebGL 專用的灰階 DXT1 Crunch；發射場景的地球球冠從 34° 縮到 24°（看得到的範圍最多約 21°）。
      - ~~WebGL 程式碼剝除 Minimal → Low~~：試過，但有剝掉 Json.NET 反射用到的程式碼的風險。
    - 後來決定（使用者）：**30 MB 只是經驗值，不設上限**，所以程式碼剝除改回 Minimal、刪掉 link.xml。上面其他幾項本身就是改善（雲和海面反光的修正、ISS 減面、音檔聽不出差別、少等啟動畫面），都保留。之後每次建置量一次記在藍圖。

102. **`AudioImporter.SetOverrideSampleSettings` 對每個平台名稱都回傳 false**
    - 原因：Unity 6000.6 的行為（試過 "WebGL"、"Web"、`BuildTargetGroup.WebGL`，連不改任何值也是 false）。
    - 解法：直接改預設值（`defaultSampleSettings`）；遊戲主要發布在 WebGL，桌機版一起降也沒關係。

103. **瀏覽器版加不進「同一個網路」的房間**
    - 原因：直連預設走 UDP，瀏覽器只能用 WebSocket；桌面版房主沒有開 WebSocket。
    - 解法：「同一個網路」一律用 WebSocket（房主和客戶端都是），房間碼（轉送）才依平台分 WSS／DTLS。瀏覽器版沒有命令列，測試參數改從網址讀：`?gng-join=192.168.1.20&gng-name=名字`。

104. **標題的地球一直沒有雲，海面也沒有反光（舊問題，這次才發現）**
    - 原因：`ConfigureTexture` 把雲和水域遮罩設成單通道貼圖，Unity 預設取 **Alpha** 通道（JPG 沒有透明度，整張是 255）存成 Alpha8；shader 讀 `.r` 拿到 0。之前做雲的 Crunch 實驗時畫面突然有雲才發現。
    - 解法：單通道一律取 Red（存成 R8）。海面反光第一次看得到，強度原本沒調過，在 Bloom 下是一團白光：指數 60 → 110、強度 0.6 → 0.22。
    - 連帶：先前「雲改 Crunch 反而變大」是假象：Alpha8 全是 255，Brotli 壓到幾乎 0；換成真的雲資料後 Crunch 才是對的。

105. **刪掉沒用到的字串時誤刪 `ui.hud.controls_solo`，畫面左下角顯示字串鍵**
    - 原因：程式是 `"ui.hud.controls" + (多人 ? "" : "_solo")`，`check_strings.py` 只認得完整字串，把它列成 unused。
    - 解法：加回去，並在 `check_strings.py` 的 families 加 `"ui.hud.controls": ["", "_solo"]`；刪除前再全專案搜一次（另外兩個命中是 `science_bonus`、`redesigns` 的子字串，不算）。

106. **換掉 ISS 的 .glb 之後，標題畫面的 ISS 不見了（0 個 Renderer）**
    - 原因：Boot 場景存的是模型的 prefab 實例，重新匯入後模型內部物件的 fileID 變了。
    - 解法：重跑 `GoNoGo/Build/All Scenes`。換模型檔一律要重建場景。

107. **在使用者開著的 Blender 裡減面，匯出時多帶了三個不相干的物件（Cube.001 等）**
    - 原因：`use_selection` 看的是目前 view layer 的選取，使用者檔案裡其他場景的物件也是選取狀態。
    - 解法：在暫存場景匯入，匯出前把 view layer 裡不是 ISS 的物件都取消選取，加 `use_active_scene`；做完把暫存場景、物件、網格、材質全部刪掉，確認使用者的檔案回到原本的 2 個場景、14 個物件。

108. **編輯器裡的中文變成亂碼（瀏覽器版正常）**
    - 原因：字型是動態 SDF，執行時會把用到的字畫進圖集、寫回 `CJK.asset` 等檔案。之前為了不把這些變動 commit 進去，在編輯器開著時用 `git checkout` 還原字型檔；磁碟上的圖集被換掉，編輯器記憶體裡的字形表還是舊的，對不上就把別的字的圖貼上來。瀏覽器版是從乾淨的檔案建置的，所以沒事。
    - 解法：在編輯器裡對四個字型呼叫 `FontAsset.ClearFontAssetData(true)` 再存檔（圖集 1×1、沒有字形，執行時重新產生），並 commit 這個真正清空的版本。**之後不要用 `git checkout` 還原字型檔**，commit 前改用同樣的方式清空。

109. **雲的貼圖警告：`RGB Crunched DXT1` 不能用在 SingleChannel 類型**
    - 原因：第 101 條把雲設成 WebGL 用 DXT1 Crunch，但雲的貼圖類型是單通道，Unity 不支援這個組合（雖然還是有套用）。
    - 解法：雲改用一般貼圖類型（shader 只讀 `.r`，一樣能用），WebGL 照樣用 DXT1 Crunch；水域遮罩維持單通道 R8。

110. **遊戲中選單（Tab）：字串鍵和 USS class 撞名**
    - 原因：主選單已經用了 `ui.menu.title`（回到標題）、`ui.menu.settings`、`ui.menu.back`，也有 `.menu-col`（主選單左欄，絕對定位）。新選單沿用同樣的名字會改到主選單的文字和版面。
    - 解法：遊戲中選單的字串用 `ui.gmenu.*`、樣式用 `gm-*`。新增 key 或 class 前先搜一次整個專案。

111. **測試按鍵時，輸入框焦點檢查看起來沒作用**
    - 原因：場景裡有 5 個 UIDocument（中控室的世界螢幕也是），測試用的輸入框被加到別的文件，`UIRoot.IsTyping` 只看自己的面板，結果當然是 false。
    - 解法：測試時用 `UIRoot.Instance` 的 UIDocument。實際遊戲裡的輸入框（筆記、房間碼）都在 UIRoot 裡，檢查是對的。另外：`unity command scene open` 只會選取場景資產，不會開啟，要用 `EditorSceneManager.OpenScene`；直接開 Launch 場景會被測試用的任務切回 Facility，要從 Facility 送 `Launch` 指令進去。

112. **多人時別人按發射，自己還鎖著游標走路，進到發射畫面後滑鼠點不到按鈕（舊問題）**
    - 原因：只有設施畫面管游標，換場景時沒有人把游標放開。
    - 解法：`UIRoot` 換到設施以外的畫面時一律放開游標。

113. **倒數的數字和錄音對不起來（有時快、有時慢半秒）**
    - 原因：倒數時間是用 `Time.deltaTime` 自己累加，錄音是另外播的。剛進場景時錄音檔還在解壓縮，`AudioSource.time` 卡在 0 好幾格，兩邊從一開始就差一截，之後還會越差越多。
    - 解法：
      - 倒數錄音改成 `DecompressOnLoad`。
      - 先等錄音真的開始往前走，再把時間直接設成 `T = max(上一格, 錄音位置換算的 T)`，不再自己累加。
      - 實測 T-5 到 T+6 每一格都對得上。

114. **改成真實時間後，有一個火箭的高度在整流罩分離前往下掉**
    - 原因：整流罩分離的高度是固定值（110 km），但 Delta II 的二節點火已經比這還高，內插時高度倒退。
    - 解法：整流罩的高度改成 `max(二節點火高度 + 8, 110)`。內插改用單調三次（Fritsch–Butland），測試每 5 秒檢查一次高度不倒退。

115. **發射時火箭看起來像在往下栽（頭朝右下）**
    - 原因：
      - 真實時間下，火箭 1 分鐘就飛到十幾公里高。長焦鏡頭只在 1.6 km 外，越拍越往頭頂仰（約 76°）。
      - 透視之下，往右上飛的火箭看起來像往下掉。
    - 解法：
      - 俯仰提早做完。
      - 場景裡的速度比例 35 秒內縮到 0.1。
      - 長焦鏡頭移到 4 km 外，視角跟著收窄，開頭構圖不變。
      - 現在 T+1:32 看得到火箭往右上爬。

116. **用 `script eval` 測其他火箭，結果發射出去的是 Electron**
    - 原因：`SetLauncher` 只接受目前情境允許的火箭（`LaunchersFor(scenario)`），回 `err.not_allowed`，設計沒變。另外 `SessionState` 沒有 `Design` 屬性，要改 `Bench` 和 `Reported`。
    - 解法：測試腳本直接改 `m.S.Bench` 和 `m.S.Reported` 的 `LauncherId`（`OrbitId`），再送 `RunTest`、`Launch`。

117. **Taurus XL 長時間滑行時，狀態一直顯示「節間分離」**
    - 原因：`StageKey` 只看最後一個事件，`upper_sep` 之後下一個事件要等好幾分鐘。
    - 解法：分離後 12 秒內顯示「節間分離」，之後依推力和後面有沒有點火，顯示「上節燃燒」「滑行」或「入軌」，並加測試。直接入軌的 Falcon 9 關機後到分離之間，播報改成「入軌滑行中，準備衛星分離」，不再說「等待下一次點火」。
