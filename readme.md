# Boren.StockLottery

## 專案概述
自動抓取台灣股票公開申購資料，依溢價比例篩選，並將重要日期寫入 Google 日曆的排程服務

## 專案目標
1. 使用Claude Code 體驗自動化開發
2. 每天自動從 ibfs.com.tw 抓取申購中的股票清單（含溢價率）
3. 依設定門檻過濾，只關注值得申購的股票
4. 自動在 Google 日曆建立提醒，不再漏買
5. 最後仍由人類操作申購，最後把關，避免自動化錯誤造成損失

## 提供給Claude Code 的需求說明
我想要做一個小小的side project, 利用claude code 幫我產生程式碼

一個自動的排程，每天抓股票公開申購資料，依照我設定的溢價比例(%)，篩選出符合的股票，將該股票的相關日程自動安排到我的行事曆上。

功能跟流程如下:

1.事先設定溢價成數

2.每天固定時間去抓取台灣最新的股票抽籤清單(https://www.twse.com.tw/zh/announcement/public.html)。需要的欄位有(抽籤日期、證券名稱、證券代號、申購結束日、申購價格、申購股數)。抓取條件: 限定在股票代碼會是四碼，當下日期小於申購結束日兩天以內，且沒有抓過的(已經抓過的會記錄在localDB 裡)

3.當每天從網站抓取到新資料，用股票代碼去GET呼叫API，XXXX填入股票代碼(http://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_XXXX.tw) 
取得回傳json，解析出裡面y 的數值就是該股票目前價格。

json 範例:
    ```
    {
        "msgArray": [
            {
                "y": "1840.0000",
            }
        ]
    }
    ```

4.取得目前價格，與申購價格做計算，算出"溢價比例"。公式: 溢價比例 = 100.00*(目前價格-申購價格)/申購價格。比對超過事先設定溢價成數的股票，就自動到google 日曆，在申購結束日前一天新增項目，抽籤日期也新增一個項目，兩個項目都要載明 $"{證券代號}{證券名稱} {申購價格*申購股數}:{溢價比例}"。如果沒超過事先設定溢價成數，就跳過不處理。

5.只要有經過2,3 步驟的股票資料，就將 $"{證券代號}-{抽籤日期:yyyyMMdd}" append寫到一個文字檔

## 開發過程

**開發方式**：先規劃需求與資料來源，寫出完整 prompt 後交給 Claude Code 生成，約花 8 分鐘完成初版。後續依實際問題（TWSE 新股查不到市價、ibfs 已提供溢價率）調整資料來源架構。總耗時大概 4 小時

**遇到問題**：
- 從 TWSE JSON API + MIS 即時價格，改為直接爬 ibfs.com.tw（已含溢價率，省去獨立計算）
- 抓回並解析完的資料不正確，整合測試直連外部網站，解析出來的資料跟HTML 實際頁面一致

**事後檢討心得**：
- 指定技術棧：先前的prompt 中，沒有提到 1. 是用甚麼語言 2. 沒說架構要用Worker Service (Claude Code 很聰明自己用了) 3. 沒指定Google Calendar 介接方式 ... 等等
- 需要完全明確：跟上面那一點也有重疊，prompt 裡面第五點說要寫到文字檔，但沒說寫入的位置。以上兩點，容易造成不必要的修改：自動化生成之後發現不符合自己的想像，又要叫Claude Code 修改
- 修改亂做：只有指出錯誤，讓Claude Code 自己改容易改不到真正錯誤的點上，也造成要來來回回不斷修改。如果要讓它改，最好明確指出要它抓取的關鍵字或錯誤點。這次也有利用單元測試，讓它自己先寫了一個HTML Parse邏輯，再跟現有的function 輸出比對，用最精準(assert) 的方式驗證結果是否正確。

## Tech Stack

| 層次 | 技術 |
|------|------|
| Runtime | .NET 8.0 (C#) |
| 執行模式 | Run-and-exit console app（排程由 GitHub Actions 負責） |
| 資料來源 | ibfs.com.tw HTML（`HtmlAgilityPack` 解析） |
| 行事曆整合 | Google Calendar API v3（OAuth 2.0） |
| 本地儲存 | SQLite（`Microsoft.Data.Sqlite`，無 EF Core） |
| DI / 設定 | `Microsoft.Extensions.*` |
| 測試 | NUnit 3（整合測試，直連 ibfs.com.tw） |

---

## 運作流程

```
每日 15:10
    │
    ▼
IbfsService.GetActiveSubscriptionsAsync()
    └─ GET https://www.ibfs.com.tw/rank/default6.aspx?xy=6&xt=1
    └─ 解析 HTML 表格，過濾 status = 申購
    └─ 回傳：StockCode, StockName, SubscriptionPrice(扣款金額),
             ReferencePrice, PremiumRatioPercent, LotteryDate, SubscriptionEndDate
    │
    ▼
SqliteStockRepository.IsProcessedAsync(StockCode, LotteryDate)
    └─ 已處理 → 跳過（idempotent）
    │
    ▼
PremiumRatioPercent >= PremiumThresholdPercent ?
    ├─ 否 → 略過
    └─ 是 → GoogleCalendarService.CreateEventsAsync()
                ├─ 事件 1：申購截止日前一天（提醒去申購）
                └─ 事件 2：抽籤日（提醒查結果）
                   標題格式：{StockCode}{StockName} {扣款金額}:{溢價率}%
    │
    ▼
SqliteStockRepository.MarkProcessedAsync(StockCode, LotteryDate)
```

---

## 專案結構

```
● Boren.StockLottery/
  ├── .github/
  │   └── workflows/
  │       └── lottery.yml                 # GitHub Actions，每日 03:00 UTC 執行
  │
  ├── Boren.StockLottery/                 # 主專案
  │   ├── Configuration/
  │   │   └── AppSettings.cs             # 設定 POCO
  │   ├── Models/
  │   │   └── StockSubscription.cs       # 資料模型
  │   ├── Services/
  │   │   ├── IIbfsService.cs
  │   │   ├── IbfsService.cs             # ibfs.com.tw HTML 爬蟲
  │   │   ├── ICalendarService.cs
  │   │   ├── GoogleCalendarService.cs   # Google Calendar OAuth + 建立事件
  │   │   ├── ILotteryOrchestrator.cs
  │   │   ├── LotteryOrchestrator.cs     # 主流程編排
  │   │   ├── IStockRepository.cs
  │   │   └── SqliteStockRepository.cs   # SQLite 持久化（冪等）
  │   ├── Program.cs                     # 進入點，DI 設定，直接執行後退出
  │   ├── appsettings.json
  │   ├── credentials.json               # Google OAuth（不進 git）
  │   └── Boren.StockLottery.csproj
  │
  ├── Boren.StockLottery.Tests/
  │   ├── IbfsServiceIntegrationTest.cs  # NUnit，打真實 ibfs.com.tw
  │   └── Boren.StockLottery.Tests.csproj
  │
  └── Boren.StockLottery.sln
```

---