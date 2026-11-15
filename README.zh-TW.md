<p align="center">
  <img src=".github/assets/logo.png" alt="Houston, We Have a Trade-off 隊伍 logo" width="260">
</p>

<h1 align="center">Go/No-Go</h1>

<p align="center">
  <b>用 NASA 真實數據做的多人合作衛星任務遊戲。</b><br>
  找數值、選零件、組衛星、喊 Go 或 No-Go、發射，然後在中控室撐完整個任務。
</p>

<p align="center">
  NASA Space Apps Challenge · 隊伍 <b>Houston, We Have a Trade-off</b>
</p>

<p align="center">
  <b><a href="https://terrytanjr.github.io/Go-No-Go/">在瀏覽器上玩</a></b>
</p>

<p align="center">
  <a href="README.md">English</a> · <b>繁體中文</b>
</p>

---

## 簡報

<!--
  TODO：把簡報放在這裡，三選一：
  - 連結：[看簡報](https://...)
  - PDF：檔案放到 .github/assets/slides.pdf，再寫 [下載簡報（PDF）](.github/assets/slides.pdf)
  - 封面圖：![簡報封面](.github/assets/slides-cover.png)
-->

> 簡報即將放上。

## 這是什麼遊戲

真實的任務設計就是一連串取捨：電池加大，質量就變重；質量變重，就要換大火箭；火箭變大，預算就不夠。**Go/No-Go** 讓 1–4 位玩家走進一座衛星設施，分工合作，一起做這些取捨。

**系統不會告訴你設計行不行。** 需求要自己讀，數字要自己算，發不發射要自己決定。判斷錯了，發射結果會告訴你，失敗原因看板會寫出真實的數值。

## 一局的流程

| 步驟 | 地點 | 玩家做什麼 |
| --- | --- | --- |
| 1. 任務簡報 | 中控室 | 飛行主任交代任務目標。 |
| 2. 選件 | 中控室大屏幕 | 選酬載儀器、電力、通訊、航電、軌道、火箭和地面站。部分零件的數值是挖空的，要到設施各處的線索（研究牆、白板、書、設計圖）找出來，自己填回去。儀表板只算你填了的數字。 |
| 3. 組裝 | 組裝區 | 每個零件是一包小零件。到架子上拿，在組裝台照順序組起來，再裝到衛星上。 |
| 4. Go/No-Go | 中控室發射台 | 沒有人會告訴你設計好了沒。個人模式由自己決定；多人時按下發射就開始點名，每個人喊 GO 或 NO-GO，有人喊 NO-GO 就 HOLD，全員 GO 才發射。 |
| 5. 發射 | 發射畫面 | 入軌，或是失敗回去重新設計。 |
| 6. 營運 | 中控室 | 四個回合的事件：來自 DONKI 真實紀錄的太陽風暴、零件故障、電力異常、科學觀測機會，野火關卡還有火警。看遙測、查手冊，用小遊戲處理每個事件。 |
| 7. 評價 | 評價頁 | 分數拆成科學、壽命、效率、任務需求、營運處置五項，逐一檢討每個事件的判斷，並和真實任務（Landsat 9、PRISMA、Planet Dove）對照。 |

## 遊戲畫面

<table>
  <tr>
    <td width="50%"><img src=".github/assets/screenshots/assembly-bay.png" alt="組裝區"></td>
    <td width="50%"><img src=".github/assets/screenshots/control-room.png" alt="中控室大屏幕"></td>
  </tr>
  <tr>
    <td align="center">自己的太空人在組裝區</td>
    <td align="center">在中控室大屏幕上選零件</td>
  </tr>
  <tr>
    <td><img src=".github/assets/screenshots/clue.png" alt="寫著零件數值的線索"></td>
    <td><img src=".github/assets/screenshots/bench.png" alt="在組裝台組零件"></td>
  </tr>
  <tr>
    <td align="center">線索裡藏著被挖空的零件數值</td>
    <td align="center">用小零件組出一台相機</td>
  </tr>
  <tr>
    <td><img src=".github/assets/screenshots/launch.png" alt="發射倒數"></td>
    <td><img src=".github/assets/screenshots/report.png" alt="任務報告"></td>
  </tr>
  <tr>
    <td align="center">最後倒數</td>
    <td align="center">任務報告和真實任務對照</td>
  </tr>
</table>

<sub>截圖是英文介面，遊戲裡可以切換英文和繁體中文。</sub>

## 模式與關卡

| 模式 | 人數 | 說明 |
| --- | --- | --- |
| 新手訓練 | 1 | 有引導的第一個任務。 |
| 個人任務 | 1 | 一個人跑完整個流程，用平板看儀表板和手冊。 |
| 多人房間 | 2–4 | 輸入房間碼加入，內建語音聊天，分頭找線索、組裝。 |
| 沙盒模式 | 1 | 沒有預算上限、沒有任務需求，所有零件、軌道、火箭都開放。 |

| 關卡 | 難度 | 預算 | 說明 |
| --- | --- | --- | --- |
| 看顧地球 | 學員 | 2.1 億美元 | 地球觀測 |
| 繪製月球地圖 | 工程師 | 2.5 億美元 | 月球測繪，有轉移軌道和入軌的演出 |
| 野火偵測 | 任務主管 | 4,000 萬美元 | 小衛星預算；選哪條軌道決定地影多長、電池要多大 |
| 改寫歷史：軌道碳觀測衛星（2009） | 工程師 | 2.734 億美元 | 重演 OCO 發射失敗，再揭曉 NASA 怎麼用 OCO-2 補救 |

每個關卡的難度是固定的。**學員**的儀表板有顏色提示，營運事件直接告訴你名稱；**工程師**的儀表板只有數字，營運每回合 3 分鐘；**任務主管**的數值全部挖空，每回合 90 秒。

## 用到的 NASA 資料

遊戲資料裡的每個數字都附來源（標題、網址，以及是查到的、推算的還是估計的）。

| NASA 資源 | 用在哪裡 |
| --- | --- |
| DONKI 太空天氣資料庫 | 營運階段的太陽閃焰和地磁暴事件 |
| 小型太空船技術（SST）報告 | 零件規格（質量、功率、下傳、壽命），以及每個零件由哪些小零件組成 |
| 任務新聞資料包（OCO、OCO-2）、JPL、NSSDC、NTRS | 歷史關卡、軌道與地影常數 |
| LRO 任務資料、NSSDCA 月球資料表 | 月球轉移的時間 |
| Earth Observatory、SVS（Blue Marble、Black Marble、LROC） | 地球和月球貼圖 |
| NASA 3D Resources | 標題畫面的國際太空站和太空梭模型 |
| NASA 錄音（CLPS IM-2 發射錄音、Juno 飛掠木衛三） | 發射畫面的倒數、發射台和升空聲音；營運時的太空背景音 |
| JPL 概念成熟度（CML） | 新手訓練結尾的說明（CML 1–4：草圖、可行性、取捨、點設計） |

完整清單和使用條款見 [docs/DATA_SOURCES.md](docs/DATA_SOURCES.md)（英文）。每個素材的出處見 [Assets/GoNoGo/Art/CREDITS.md](Assets/GoNoGo/Art/CREDITS.md)。

## 操作

| 按鍵 | 動作 |
| --- | --- |
| W A S D／Shift／滑鼠 | 移動／跑／轉視角 |
| Q | 切換第一／第三人稱 |
| E | 使用大屏幕、線索、控制台 |
| C | 拿取、放回 |
| V（長按） | 安裝零件、裝上小零件 |
| B | 任務簡報 |
| K／L | 知識卡／設計庫（存檔、分享、比較） |
| T | 平板（只有個人模式） |
| X（按住） | 表情輪盤：揮手、歡呼、大笑、點頭 |
| M | 麥克風靜音／取消靜音（多人） |
| G／N | Go/No-Go 點名投票（多人） |
| H | 展開／收起新手提示（只有新手訓練） |
| Tab／Esc | 選單 |

## 技術

| | |
| --- | --- |
| 引擎 | Unity 6000.6.8f1、URP |
| 多人連線 | Netcode for GameObjects、Unity Multiplayer Services（房間碼）、Vivox 語音 |
| 素材 | glTFast + Draco 載入 NASA `.glb` 模型；設施和煙霧特效用 Blender 製作 |
| 平台 | WebGL（瀏覽器）和 Windows |
| 語言 | 英文、繁體中文，遊戲中可切換 |

## 專案結構

```
Assets/GoNoGo/
  Core/           不依賴 Unity 的遊戲規則：評估、模擬、計分、軌道計算、小遊戲
  Core.Tests/     Core 的單元測試
  Data/           JSON 遊戲資料（零件、組裝套件、任務、軌道、火箭、DONKI 事件、字串）
  Game/           遊戲流程、設定、音效、多語系
  Net/            多人連線和語音
  Presentation/   3D 設施、玩家、avatar、衛星組裝、營運畫面
  UI/             UI Toolkit 畫面和面板
  Launch/         發射演出
  Editor/         場景產生器、打包
  Art/、Audio/    模型、貼圖、字型和 NASA 錄音（出處見 Art/CREDITS.md）
  Resources/      小遊戲美術
tools/            資料、美術、測試腳本（抓 DONKI、小遊戲美術、Blender 場景）
docs/             安裝流程和資料來源
```

## 在本機執行

需要 Git LFS 和 Unity **6000.6.8f1**（含 Web Build Support）。

```bash
git clone https://github.com/TerryTanJR/Go-No-Go.git
cd Go-No-Go
git lfs pull
```

用 Unity 開這個資料夾，按 Play 就會從標題頁開始。核心測試用 `bash tools/run-core-tests.sh` 跑。要打包時用編輯器選單 **GoNoGo → Release**。

完整的開發環境安裝（Unity CLI、Blender、Claude Code、多人協作規則）見 [docs/SETUP.md](docs/SETUP.md)。

## AI 工具的使用

大部分程式、資料整理和文件是在 Claude Code（Anthropic）協助下完成的，有它參與的 commit 都帶有 `Co-Authored-By: Claude`。遊戲資料的數值來自各筆 `source` 標示的出處，音效是 NASA 的原始錄音。

## 聲明

粉絲製作的教育遊戲，與 NASA 無背書關係。遊戲中沒有使用 NASA 徽章。火箭和公司名稱只用來指認真實產品。
