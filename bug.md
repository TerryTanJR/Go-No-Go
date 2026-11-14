# Bug List

已知但還沒修的問題。修好後把狀態改成「已修」，並寫上 commit。

格式：現象 → 怎麼重現 → 原因 → 可能的修法 → 狀態。

---

## BUG-002：沒有指定難度時，預設難度跟著清單順序變成學員

- **嚴重度**：中（評價頁的預算數字錯；兩個核心測試失敗）
- **狀態**：未修

### 現象

- 評價頁「花費 / 預算」那一行，不管這一局選哪個難度，預算都用學員的倍率（×1.5）算。例如工程師難度的「看顧地球」應該是 140 M$，會顯示 210 M$。（依程式推斷，還沒在遊戲裡實際看過。）
- 核心測試失敗：
  - `EvaluatorTests.BudgetIsDevelopmentLaunchAndOperations`：預期預算 140，實際 210。
  - `SimulatorTests.CrossRuntimeFingerprint`：指紋從 `92B23DD720C0BC75` 變成別的值。
  - `BalanceTests.HistoryRepeatsThenCanBeRewritten` 原本也因此失敗（預期 300.74，實際 451.11）；`feature/historical-missions` 讓歷史關卡的預算不乘難度倍率之後，這個已經通過。

### 怎麼重現

1. 跑 `tools/run-core-tests.sh`，上面兩個測試失敗。
2. 遊戲裡：選工程師或任務主管難度玩完一局，到評價頁看「花費 / 預算」。

### 驗證結果

用 git worktree 在兩個版本各跑一次核心測試：

| 版本 | 結果 |
| --- | --- |
| `87e8822`（`373320e` 的前一個） | 116 個全部通過 |
| `373320e` feat: order difficulty menu easiest to hardest, recommend Cadet | 3 個失敗（就是上面三個） |

### 原因

- 核心程式在呼叫端沒有指定難度時，拿 `difficulty.json` 的**第一個難度**當預設：
  - `Evaluator.EvaluateDesign`、`FixFinder`、`Scoring`、`Simulator`：`data.Difficulties.FirstOrDefault()`
  - `SessionRules`：難度 id 找不到時用 `data.SelectableDifficulties.First()`
- `373320e` 為了讓選單由易到難，把學員移到清單第一個，預設難度就從工程師變成學員。
- 遊戲裡沒有傳難度的呼叫：
  - `ReportScreen.cs`（評價頁的花費 / 預算）：**數字錯**。
  - `OpsPanels.cs`（營運控制台的讀數）：讀數用到評估結果，有沒有受影響還沒確認。
  - `LaunchDirector.cs`、`IntegrationStand.cs`、`OpsView.cs`：只拿評估結果去組衛星的外觀，應該不受影響。
- 這其實是更早就有的問題：`373320e` 之前，評價頁一直用工程師的預算，學員和任務主管本來就顯示錯，只是剛好和測試用的預設難度一樣，所以沒被發現。

### 可能的修法

1. 預設難度改成固定的 id（例如 `GameData.DefaultDifficultyId = "engineer"`），不要依賴清單順序。選單怎麼排都不影響規則。
2. `ReportScreen`、`OpsPanels` 改成傳入這一局的難度（`M.S.DifficultyId`）。
3. 修完 `main` 上的指紋應該回到 `92B23DD720C0BC75`；之後合併 `feature/lunar-mapping` 時，天數捨入的修正會刻意改變模擬結果，要再更新一次指紋（並在 Unity 裡比對）。

---

## BUG-001：月球測繪任務在學員難度完全無法發射

- **嚴重度**：高（學員難度下這一關無法過關）
- **狀態**：未修

### 現象

月球測繪（`lunar_mapping`）情境的 Go/No-Go 點名，「通訊與營運」席位一直喊 NO-GO，只能回設計室，發射按鈕不會出現。

### 怎麼重現

1. 標題畫面選「月球測繪」、難度選「學員」。
2. 直接進點名：通訊席位 NO-GO，理由是下傳量不足（`nogo.comms`）。
3. 回設計室，不管怎麼換零件都過不了。設計室的下傳警告（`warn.downlink`）沒有任何一鍵修正按鈕。

### 驗證結果（在 Editor 用 `script eval` 跑核心規則）

月球預設設計（Falcon 9、X 頻段天線、1 個地面站、大容量儲存電腦、可見光相機）：

| 難度 | 預設設計 | 一鍵修正 | 窮舉後能全員 GO 的組合 |
| --- | --- | --- | --- |
| 學員 `cadet` | 通訊超標 → NO-GO | **0 個** | **0 / 1,536** |
| 工程師 `engineer` | 通訊超標 → NO-GO | 2 個 | 1,728 / 39,366 |
| 任務主管 `director` | 通訊超標 → NO-GO | 只顯示問題數 | 1,728 / 39,366 |

窮舉範圍：8 類零件在該難度可用的所有等級 × 3 種火箭（Electron、Falcon 9、Falcon Heavy）× 2 種單一儀器（可見光、多光譜），任務年數 2 年，不抽部門要求（部門要求本來就不會造成 NO-GO）。

學員難度下 NO-GO 理由的分布：資料 768、通訊 768、電力 698、質量 432、壽命 230（同一組設計可能同時有多個席位 NO-GO）。

### 原因

- 月球軌道 `llo_100` 的下傳條件很差：`contact_fraction` 0.167、`downlink_rate_factor` 0.05（`orbits.json`）。
- 工程師和任務主管難度能過關的組合，全部都用「X 頻段高速天線」（`ant_xband_hr`）。
- 這支天線是**第 3 級**零件。學員難度的 `tiers_per_category` 是 2（`difficulty.json`），設計室不會列出它，`FixFinder` 也不會拿它當修正方案。所以學員難度下，月球任務沒有任何一組設計能過點名。
- 測試沒有抓到：`BalanceTests.LunarScenarioIsWinnable` 只測工程師難度，而且直接指定 `ant_xband_hr`。

### 相關的次要問題

- **三種難度的月球預設設計一開局都是 NO-GO。** 地球觀測的預設設計刻意「只有一個教得會的問題」，而且有測試保證（`DefaultEarthDesignStartsWithOneTeachableProblem`）；月球沒有對應的設計與測試。
- **任務主管難度只顯示問題數量**，玩家很難猜到要換天線（這是設計上的選擇，但月球關卡因此特別難）。
- **發射動畫沒有月球版**：`LaunchDirector` 沒有月球分支，畫面是一般的地球入軌，沒有飛往月球那一段。不影響遊戲進行，只是表現上的缺口。
- **未驗證**：只驗證到「點名能不能全員 GO」，還沒在遊戲裡從頭實際玩一次月球任務（工程師難度全員 GO 之後，按發射到營運、報告是否正常）。

### 可能的修法（待決定）

1. **情境層級開放零件**：在 `scenarios.json` 加一個欄位（例如 `always_available_parts: ["ant_xband_hr"]`），讓月球情境在學員難度也能用高速天線。核心改 `GameState` 的零件清單和 `FixFinder` 的等級篩選。
2. **調整月球軌道參數**：提高 `llo_100` 的下傳條件，讓第 2 級的 X 頻段天線剛好夠用。要注意工程師、任務主管難度會因此變簡單，而且這兩個數值要有來源依據。
3. **不論選哪一種**：
   - 月球預設設計改成和地球觀測一樣「只有一個教得會的問題」，且該問題要有一鍵修正；
   - 新增測試：三種難度都至少有一組設計能全員 GO，而且入軌後至少拿 B；
   - 同一個 commit 更新藍圖附錄 B 的數值。
