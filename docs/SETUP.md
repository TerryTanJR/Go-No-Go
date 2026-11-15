# GoNoGo 開發環境安裝流程

從一台乾淨的電腦，到能用 Claude Code 開發這個 Unity 專案、用 Blender 做特效素材，需要安裝的所有東西和順序。

- 第 1–11 節以 **Windows** 為主。指令預設在 **PowerShell** 執行；標明 **Bash** 的要在 Git Bash 執行。
- **Mac** 同學先看第 12 節，它列出和 Windows 不同的地方，其餘照第 1–11 節做。
- **多人協作**（加入 repo、commit、push 的規則）見第 13 節。

版本以開發機實際在用的為準。

---

## 總覽

| # | 工具 | 版本 | 用途 | 必要？ |
| --- | --- | --- | --- | --- |
| 1 | Git + Git LFS | 2.46 / 3.5 | 版本控制；`.glb`、`.png`、音訊、字型走 LFS | 必要 |
| 2 | GitHub CLI (`gh`) | 2.103 | 登入 GitHub、抓 NASA 3D 模型單檔 | 建議 |
| 3 | Python + Pillow + numpy | 3.13 / 12.0 / 2.3 | `tools/` 裡的素材與資料腳本 | 建議 |
| 4 | Claude Code | 2.1 | AI 開發助手 | 必要 |
| 5 | Unity CLI | 1.0.0-beta.17 | 從命令列安裝 Editor、建置、操作開著的 Editor | 必要 |
| 6 | Unity Editor | **6000.6.8f1** + Web Build Support | 遊戲本體 | 必要 |
| 7 | Unity Hub | 最新版 | 圖形介面管理 Editor／專案 | 選用（CLI 可取代） |
| 8 | Blender | 5.2.3 LTS | 煙霧序列圖、模型處理 | 做素材才需要 |
| 9 | uv | 0.13 | 執行 Blender MCP 伺服器 (`uvx`) | 用 Blender MCP 才需要 |
| 10 | Blender MCP（外掛 + MCP 伺服器） | — | 讓 Claude Code 操作 Blender | 用 Blender MCP 才需要 |

> 專案用 **6000.6.8f1**（見 `ProjectSettings/ProjectVersion.txt`），請裝這個版本，否則開專案時會被要求升級或降級。

建議的安裝順序就是上表的順序：先裝基礎工具，再裝 Claude Code，最後裝 Unity 和 Blender（Unity 的 skill 會自動裝進 Claude Code，所以 Claude Code 要先裝）。

---

## 1. Git + Git LFS

1. 下載安裝 Git for Windows：<https://git-scm.com/download/win>。安裝時保持預設，它會一起裝 **Git Bash** 和 **Git LFS**。
2. 啟用 LFS（每台電腦做一次）：

   ```powershell
   git lfs install
   ```

3. 設定作者資訊：

   ```powershell
   git config --global user.name  "你的名字"
   git config --global user.email "你的 email"
   ```

驗證：`git --version`、`git lfs version`。

---

## 2. GitHub CLI（建議）

```powershell
winget install --id GitHub.cli
gh auth login        # 選 GitHub.com → HTTPS → 用瀏覽器登入
```

用途：
- 推送到 `github.com/TerryTanJR/Go-No-Go` 時用它的認證。
- 從 `nasa/NASA-3D-Resources` 抓單一模型（整個 repo 約 5 GB，不要 clone）：
  `gh api "repos/nasa/NASA-3D-Resources/contents/3D%20Models/<模型資料夾>"`。
  實際下載 `.glb` 要用 `raw.githubusercontent.com/nasa/NASA-3D-Resources/master/...`，它會自動轉成 LFS 實體檔。

---

## 3. Python + 套件（建議）

1. 安裝 Python 3.13：<https://www.python.org/downloads/>（安裝時勾 **Add python.exe to PATH**），或 `winget install Python.Python.3.13`。
2. 安裝腳本會用到的套件：

   ```powershell
   python -m pip install --user pillow numpy
   ```

用到 Python 的地方：

| 腳本 | 用途 | 需要的套件 |
| --- | --- | --- |
| `tools/vfx/make_atlas.py`、`preview.py` | 把 Blender 算出的逐格 PNG 拼成 8×8 序列圖 | Pillow |
| `tools/check_strings.py` | 檢查中英文字串表沒有缺鍵 | 無 |
| `tools/data/fetch_donki.py` | 重抓 NASA DONKI 太空天氣事件 | 無 |
| 音訊裁切（NASA 倒數錄音） | 低通、降頻、淡入淡出 | numpy |

> 處理從網路下載的檔案時用 `python -P -E script.py`（不從目前目錄載入程式碼）。不要用 `-I`，它會忽略 `--user` 裝的 Pillow／numpy。
>
> 這台機器沒有 ffmpeg，也不需要：Unity 不能匯入 `.m4a`，NASA 音訊一律抓 `.mp3`／`.wav` 原檔。

---

## 4. Claude Code

```powershell
irm https://claude.ai/install.ps1 | iex
claude               # 第一次執行會開瀏覽器登入
```

驗證：`claude --version`。

小技巧：
- 在終端機貼圖片用 **Alt+V**（不是 Ctrl+V），或直接把圖片檔拖進終端機。
- 新增 MCP 伺服器後要**重開 Claude Code** 才看得到工具；`claude --continue` 可以接續上一段對話。

---

## 5. Unity CLI

Unity CLI 可以取代 Unity Hub 的所有工作（登入、授權、安裝 Editor 和模組），而且 Claude Code 可以直接操作開著的 Editor（編譯、截圖、建置）。

### 5.1 安裝

```powershell
$env:UNITY_CLI_CHANNEL = 'beta'
irm https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 | iex
```

- 裝在 `%LOCALAPPDATA%\Unity\bin`。裝完**重開終端機**，`unity --version` 應顯示 `1.0.0-beta.17` 或更新。
- 安裝程式會**自動把 Unity skill 裝進 Claude Code**（`unity-cli`、`unity-pipeline`、`optimize-web` 等），不用另外跑 `unity skill install`。如果 Claude Code 裡看不到 Unity skill，再手動執行 `unity skill install`。

### 5.2 登入與授權

```powershell
unity auth login                                   # 開瀏覽器登入 Unity 帳號
unity license activate --personal --accept-eula    # 啟用免費 Personal 授權
unity license status                               # 確認有授權
```

> 登入帳號 ≠ 啟用授權。沒做 `license activate` 的話，Editor 開不起來。`--accept-eula` 代表你本人同意 Unity 條款，請自己決定後再執行。

### 5.3 Git Bash 裡也要找得到 `unity`

Windows PowerShell 5.1 傳參數給程式時會吃掉雙引號，所以**帶 C# 程式碼的 `unity command script eval` 一律在 Git Bash 執行**。Git Bash 預設找不到 `unity`，在 `~/.bashrc` 加一行：

```bash
export PATH="$LOCALAPPDATA/Unity/bin:$PATH"
```

（`tools/unity-refresh.sh` 會自己把 `%LOCALAPPDATA%\Unity\bin` 加進 PATH，不加這行也能跑；加了是為了自己在 Git Bash 下指令方便。）

---

## 6. Unity Editor 6000.6.8f1 + Web Build Support

專案最終輸出是 WebGL（網頁版），所以一定要裝 **Web Build Support**（模組 id：`webgl`）。

```powershell
unity install 6000.6.8f1 --module webgl --accept-eula
```

- 預設裝到 `C:\Program Files\Unity\Hub\Editor\6000.6.8f1\`（跟 Hub 的位置一樣，之後裝 Hub 也認得到）。
- 已經裝好 Editor、只缺模組時：`unity install-modules --editor-version 6000.6.8f1 --module webgl --accept-eula`
- 列出可裝的模組：`unity install-modules --editor-version 6000.6.8f1 --list`
- 列出已安裝的 Editor：`unity editors`

選用模組：
- `visualstudio`（Visual Studio Community，約 1.6 GB）：想在 IDE 裡寫 C# 才需要。用 Rider 或 VS Code 也可以，專案已裝好 `com.unity.ide.rider` 和 `com.unity.ide.visualstudio` 套件。
- `language-zh-hant`：Editor 介面繁體中文（Preview）。

### 第一次開 Editor

第一次開啟時會跳出 **Unity Editor 軟體條款** 視窗，必須由本人按 **Accept and Continue**。在這之前 `unity status` 會顯示 `STATUS_BLOCKED_BY_DIALOG_SOFTWARE_TERMS`，Claude Code 也無法操作 Editor。

---

## 7. Unity Hub（選用）

只用 CLI 就能完成所有開發工作，這台開發機目前也沒有裝 Hub。想要圖形介面時：

```powershell
unity hub install
```

或從 <https://unity.com/download> 下載。裝好後在 Hub 裡 **Add → Add project from disk** 選 repo 根目錄，Hub 會自動用已安裝的 6000.6.8f1 開啟。

---

## 8. Blender 5.2.3 LTS

1. 下載 Windows 安裝檔（`blender-5.2.3-windows-x64.msi`）：<https://www.blender.org/download/lts/>，或 `winget install BlenderFoundation.Blender`（裝完確認版本是 5.2.x）。
2. 安裝時保持預設，裝到 `C:\Program Files\Blender Foundation\Blender 5.2\`。

用途：`tools/vfx/blender/smoke_billow.blend` 的煙霧模擬 → 逐格算圖 → 拼成序列圖給 Unity 粒子用。重做流程見 `tools/vfx/README.md`。

> Blender 5 的動畫改成 slotted actions，舊教學裡的 `action.fcurves` 不能用了。

---

## 9. uv

Blender MCP 伺服器用 `uvx` 啟動，所以要先裝 uv：

```powershell
irm https://astral.sh/uv/install.ps1 | iex
```

裝在 `%USERPROFILE%\.local\bin`。重開終端機後 `uv --version` 確認。

---

## 10. Blender MCP（讓 Claude Code 操作 Blender）

Blender MCP 分兩半，兩邊都要裝：

| 部分 | 跑在哪裡 | 怎麼裝 |
| --- | --- | --- |
| BlenderMCP **外掛** (`addon.py`) | Blender 裡面，開一個本機 socket 等待指令 | Blender 介面安裝 |
| `blender-mcp` **MCP 伺服器** | Claude Code 啟動時用 `uvx` 執行 | `claude mcp add` |

### 10.1 安裝 Blender 外掛

1. 從 <https://github.com/ahujasid/blender-mcp> 下載 `addon.py`，存到看得到的地方（例如 `Downloads`）。
   > 不要存在 `C:\Users\<你>\AppData\...`：AppData 是隱藏資料夾，檔案選擇視窗裡找不到。
2. 開 Blender → **Edit → Preferences → Add-ons**。
3. 右上角下拉選單 → **Install from Disk…** → 選 `addon.py`。
4. 在清單裡勾選 **Interface: Blender MCP** 啟用。

> 外掛要在 Blender 介面裡**自己手動裝**。Claude Code 的自動模式會擋下「用命令列把網路下載的程式碼裝進 Blender 執行」。

### 10.2 在 Blender 裡啟動連線

1. **滑鼠移到 3D 視窗上**，按 `N` 打開側邊欄（快捷鍵只對滑鼠所在的區域有效；也可以用選單 **View → Sidebar**）。
2. 點 **BlenderMCP** 分頁 → **Connect to MCP server**（預設 port 9876）。
3. 要用素材庫時可在同一個面板勾選 Poly Haven、Sketchfab 等選項（Sketchfab 和 3D 生成需要各自的 API key）。

每次重開 Blender 都要再按一次 Connect。

### 10.3 把 MCP 伺服器加進 Claude Code

```powershell
claude mcp add blender -- uvx blender-mcp
```

- 這樣是 **local scope**（只對這台電腦的這個專案有效，不會寫進 repo）。
- 加完**重開 Claude Code**，再執行 `claude mcp list`，應看到 `blender: uvx blender-mcp - ✔ Connected`。
- 在 Claude Code 裡請它呼叫 `get_scene_info`，能回傳場景內容就代表整條線通了。

> 長時間的烘焙（煙霧模擬）可能讓 MCP 回報 `No data received`，但 Blender 裡其實還在跑。算圖分批執行，每批控制在 2 分鐘內。

---

## 11. 取得專案並驗證

### 11.1 Clone

repo 是私人的時候，要先被加成協作者才能 clone（見第 13 節）。

```powershell
git clone https://github.com/TerryTanJR/Go-No-Go.git C:\NASA\Go-No-Go
cd C:\NASA\Go-No-Go
git lfs pull                 # 確保模型、圖片、音訊、字型都是實體檔不是指標
```

### 11.2 開專案

```powershell
unity open C:\NASA\Go-No-Go
unity status --until-ready   # 第一次匯入會比較久（要建 Library/）
```

第一次開啟會自動下載 `Packages/manifest.json` 裡的套件，包括 URP 17.6、Cinemachine 3.1、glTFast 6.20 + Draco（匯入 NASA `.glb`）、Input System、Splines、Timeline、Newtonsoft JSON，以及讓 CLI 操作 Editor 的 **Pipeline 套件**（`com.unity.pipeline`）。這些**不用手動裝**。

在 Editor 按 Play 一律從標題頁（`Boot` 場景）開始，不管目前開著哪個場景；停止後回到原本的場景。想直接從目前的場景開始（例如開著 `Facility` 直接進中控室測試），把選單 **GoNoGo → Play From Title** 的勾拿掉，這個設定只存在自己的電腦。

### 11.3 驗證清單

```bash
# 以下在 Git Bash 執行
unity status                       # Editor 狀態是 ready
bash tools/unity-refresh.sh        # 匯入 + 重新編譯，沒有錯誤印 OK
bash tools/run-core-tests.sh       # 用 Unity 內建的 .NET SDK 跑核心測試（不用另外裝 .NET）
python tools/check_strings.py      # 字串表沒缺
unity command capture game_view --source camera --width 1280 --height 720 --output-path Screenshots/check.png
```

截圖會存到專案根目錄的 `Screenshots/`（不進版控）。

### 11.4 建置 WebGL（選用）

```bash
unity command build run -- --target WebGL --output-path Builds/WebGL --dry-run true   # 先驗證
unity command build run -- --target WebGL --output-path Builds/WebGL --confirm true
```

建置期間不要改 `.cs` 檔。`Builds/` 不進版控。

### 11.5 打包給別人玩（Unity Play／Windows zip）

編輯器選單 **GoNoGo → Release**：

| 選項 | 產出（在 `Builds/Release/`） |
|------|------|
| Web + Windows (zip) | 兩個都打包 |
| Web only (zip for Unity Play) | `GoNoGo_Web_日期.zip`：直接上傳 Unity Play |
| Windows only (zip) | `GoNoGo_Windows_日期.zip`：解壓縮後執行 `GoNoGo.exe` |

- 兩個都打包約 20–30 分鐘（要切換平台、重新匯入素材）。打包完會跳出視窗顯示 zip 的位置，也寫在 `Builds/Release/last_release.txt`。
- 瀏覽器版用 Gzip＋備用解壓縮，放在哪個網站都能開；專案本身的 Brotli 設定打包完會改回來。
- 命令列（編輯器開著）：`unity command script eval "return GoNoGo.EditorTools.ReleaseBuilder.BuildAllQuiet();"`（不跳視窗）。
- 上傳 Unity Play：https://play.unity.com 登入 → Upload → 拖進 Web 的 zip → 填名稱、縮圖 → 可見度選 Unlisted（只有拿到連結的人能玩）→ Publish。
- 更新版本時，房主和所有玩家都要換成同一版（網頁重新整理、Windows 版換新 zip），新舊版本可能連不進同一個房間。

### 11.6 部署到 GitHub Pages

網頁版放在 `gh-pages` 分支（只放打包結果，跟 main 分開、沒有 LFS），網址 https://terrytanjr.github.io/Go-No-Go/ 。main 更新不會自動部署，要更新網站時執行：

```bash
bash tools/deploy-pages.sh              # 用開著的編輯器打包 → 換掉 gh-pages 的內容 → commit → push
bash tools/deploy-pages.sh --skip-build # 不重新打包，部署現有的 Builds/Release/Web
bash tools/deploy-pages.sh --no-push    # 只 commit，先在本機檢查
```

- 編輯器要開著、不在 Play 模式。打包會把編輯器的平台切到 WebGL。
- 第一次執行會在專案旁邊建 `../Go-no-go-pages`（gh-pages 的 git worktree）。不要在裡面手動改檔，每次部署都會整個換掉。
- 第一次部署後要開 Pages（repo 必須是 Public）：Settings → Pages → Deploy from a branch → `gh-pages` / `(root)`。
- 推完約 1–3 分鐘生效，瀏覽器用 Ctrl+Shift+R 強制重新整理。

---

## 12. Mac 安裝（和 Windows 不同的地方）

遊戲程式碼沒有 Windows 專用的東西，Mac 上可以開 Editor、按 Play、改場景和程式碼，也可以建置 WebGL。以下是安裝步驟的差別；沒列到的照第 1–11 節做。

> 這一節的指令還沒有在 Mac 上實際跑過。遇到問題請記到 `friction_log.md`，並更新這一節。

先裝 Homebrew（<https://brew.sh>），之後很多工具都用它裝。指令都在內建的「終端機」執行，不需要 Git Bash。

| 步驟 | Mac 的做法 |
| --- | --- |
| 1. Git + LFS | `brew install git git-lfs`，然後 `git lfs install` |
| 2. GitHub CLI | `brew install gh`，然後 `gh auth login` |
| 3. Python | 用 python.org 的 macOS 安裝檔，然後 `python3 -m pip install --user pillow numpy`。指令裡的 `python` 都改成 `python3` |
| 4. Claude Code | `curl -fsSL https://claude.ai/install.sh \| bash` |
| 5. Unity CLI | `curl -fsSL https://unity.com/install.sh \| bash`，開新的終端機後 `unity --version`。登入和授權跟 5.2 節一樣 |
| 5.3 節 | 不用做。Mac 沒有 PowerShell 吃掉雙引號的問題 |
| 6. Unity Editor | 一樣是 `unity install 6000.6.8f1 --module webgl --accept-eula`。M 系列晶片的 Mac 會自動裝 arm64 版。預設裝在 `/Applications/Unity/Hub/Editor/6000.6.8f1/` |
| 8. Blender | 到 <https://www.blender.org/download/lts/> 下載 5.2 LTS 的 macOS 版（M 系列選 Apple Silicon），拖進「應用程式」 |
| 9. uv | `curl -LsSf https://astral.sh/uv/install.sh \| sh` |
| 10. Blender MCP | 外掛和 `claude mcp add blender -- uvx blender-mcp` 都跟 Windows 一樣 |
| 11.2 開專案 | `unity open ~/路徑/Go-No-Go` |

`tools/` 的腳本在 Mac 上的情況：

- `tools/unity-refresh.sh`：可以直接跑。macOS 沒有 `timeout` 指令，腳本會改成直接執行；想要逾時保護可以 `brew install coreutils`。
- `tools/run-core-tests.sh`：會先找 Unity 內建的 dotnet（`Unity.app/Contents/DotNetSdk/dotnet`）。找不到時改用系統的 `dotnet`，這時要 `brew install --cask dotnet-sdk`（需 .NET 8 以上）。也可以設定 `UNITY_DOTNET` 環境變數，直接指定 dotnet 的位置。

---

## 13. 多人協作

### 13.1 加入 repo

要 push 的同學都要先被加成協作者（repo 是私人的時候，clone 也需要）：

1. **repo 擁有者**（`TerryTanJR`）邀請同學，用網頁或指令都可以：
   - 網頁：repo → **Settings → Collaborators → Add people**。
   - 指令：`gh api -X PUT repos/TerryTanJR/Go-No-Go/collaborators/<同學的 GitHub 帳號>`
2. **同學**到 email 或 <https://github.com/TerryTanJR/Go-No-Go/invitations> 接受邀請。
3. 同學照第 1、2、11 節安裝工具並 clone。

被加進來的協作者可以直接 push 到 `main`。

### 13.2 每天的流程

```bash
git pull --rebase            # 開始工作前先拉最新版
# …在 Unity 裡工作…
git status                   # 確認要 commit 哪些檔案
git add <檔案>               # .cs/.prefab/.unity 一定要連同它的 .meta 一起加
git commit -m "說明改了什麼"
git pull --rebase            # push 前再拉一次
git push
```

- 小改動直接 push 到 `main`。
- 大改動（新功能、大量改場景）開分支，再發 PR 合併：
  `git switch -c <分支名>` → commit → `git push -u origin <分支名>` → `gh pr create`

### 13.3 Unity 專案的規則

1. **同一個場景（`.unity`）或 prefab，同一時間只有一個人改。** 這兩種檔案幾乎沒辦法手動合併。開始改之前先在群組說一聲「我在改 XXX 場景」。
2. **`.meta` 檔要跟著原檔一起 commit、一起刪除。** 少了 `.meta`，別人那邊的引用會斷掉（Inspector 裡出現 Missing）。
3. **所有人都用 Unity 6000.6.8f1。** Editor 問要不要升級專案時，一律選不要。
4. **不要 commit `Library/`、`Temp/`、`Logs/`、`UserSettings/`、`Builds/`。** `.gitignore` 已經排除了，只要不要用 `git add -f` 就好。
5. **大檔案會自動走 LFS**（`.glb`、`.png`、`.wav`、`.mp3`、字型等）。commit 後可以用 `git lfs ls-files` 確認新檔案有在清單裡。
6. **pull 之後回到 Unity 視窗**，等它匯入完、右下角沒有轉圈，再繼續工作。
7. **commit 前先看 `git status`，只 add 自己真的有改的檔案**，不要直接 `git add -A`。Unity 可能會自己改寫下面這些檔案，看到了用 `git restore <檔案>` 丟掉，不要 commit：
   - `Assets/GoNoGo/UI/Resources/UI/Fonts/` 底下 4 個字型 `.asset`：repo 裡存的是**清空的**動態字型（圖集 1×1），字是執行時才畫進去的；關閉 Unity 時也會清回這個狀態，所以平常不會有 diff。如果 Play 之後存檔，畫好字的版本會暫時寫進檔案（diff 是大量新增），丟掉就好。
   - `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset`：`m_RuntimeSettings` 被清空，是 URP 在全新 `Library/` 上做的設定遷移。
8. **Windows：第一次 pull 到 `.gitattributes` 加了 `*.mat` 那一版之後，執行一次 `git restore -- "*.mat"`。** 在這之前 clone 的材質是 CRLF 換行，Unity 存檔時一律寫 LF，git 就會把內容沒變的材質列成已修改。執行前先確認自己沒有還沒 commit 的材質修改，這個指令會把它們丟掉。

### 13.4 場景衝突了怎麼辦

`git pull --rebase` 時如果 `.unity` 或 `.prefab` 衝突，不要手動改檔案內容，直接選一個版本：

```bash
git checkout --theirs Assets/路徑/場景.unity   # 保留自己的版本（rebase 時 theirs = 自己的 commit）
git checkout --ours   Assets/路徑/場景.unity   # 改用遠端的版本
git add Assets/路徑/場景.unity
git rebase --continue
```

被捨棄的那一邊，修改要在 Unity 裡重做一次。rebase 時 `--ours`／`--theirs` 的意思跟平常相反，很容易搞混，不確定的話先 `git rebase --abort` 回到原狀再問人。

---

## 常見問題速查

| 症狀 | 解法 |
| --- | --- |
| `unity license status` 沒有授權 | `unity license activate --personal --accept-eula` |
| `unity status` 卡在 `STATUS_BLOCKED_BY_DIALOG_SOFTWARE_TERMS` | 到 Editor 視窗按 Accept and Continue |
| PowerShell 跑 `script eval` 出現 `CS0103: The name 'ok' does not exist` | 改在 Git Bash 執行（PowerShell 吃掉了雙引號） |
| 新增 `.cs` 後 `unity recompile` 說 `up_to_date`，類別卻不存在 | 跑 `bash tools/unity-refresh.sh`（Editor 不在前景時不會自動偵測檔案變動） |
| `unity command test run` 逾時、Editor 卡死 | 核心測試改用 `bash tools/run-core-tests.sh`；Editor 卡死用 `unity close <專案> --force` 再 `unity open` |
| 沒動材質，`git status` 卻出現一堆 `.mat` 已修改，`git diff` 是空的 | 換行字元的問題，見 13.3 第 8 點：`git restore -- "*.mat"` |
| 衛星零件 hover／安全模式／事件閃燈不亮 | 材質的 `_EMISSION` 被拿掉了。`git diff` 看 `.mat` 的 `m_ValidKeywords` 是不是少了 `_EMISSION`、`m_LightmapFlags` 變回 0；是的話 `git restore` 那些材質，不要 commit |
| Claude Code 看不到 Blender 工具 | 重開 Claude Code；確認 Blender 裡按了 Connect；`claude mcp list` |
| `claude mcp add` 後啟動失敗、找不到 `uvx` | 重開終端機讓 `%USERPROFILE%\.local\bin` 進 PATH |
| 模型或圖片打開是幾百 bytes 的文字檔 | 沒裝 LFS：`git lfs install` 後 `git lfs pull` |
| `python -I` 找不到 Pillow | 改用 `python -P -E` |
| clone 時出現 `Repository not found` | repo 是私人的：確認已接受協作邀請，且 `gh auth status` 登入的是被邀請的帳號 |
| push 被拒絕（`rejected … fetch first`） | 別人先 push 了：`git pull --rebase` 再 `git push` |
| Inspector 出現 Missing 的引用 | 有人 commit 時漏了 `.meta`，請他補上 |

更多問題和原因見 repo 根目錄的 `friction_log.md`。
