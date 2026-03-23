# Boren.StockLottery

自動抓取台灣股票公開申購資料，依溢價比例篩選，並將重要日期寫入 Google 日曆的排程服務。

## 解決的問題

台灣股票公開申購有三個關鍵日期需要追蹤：**申購截止日**、**扣款日**、**抽籤日**。
傳統流程需要自己開 App 看清單、手動計算溢價、自己記日期——忘記就錯過了。

本專案做的事：
1. 每天自動從 ibfs.com.tw 抓取申購中的股票清單（含溢價率）
2. 依設定門檻過濾，只關注值得申購的股票
3. 自動在 Google 日曆建立提醒，不再漏買

---

## Tech Stack

| 層次 | 技術 |
|------|------|
| Runtime | .NET 8.0 (C#) |
| 執行模式 | `IHostedService` / `BackgroundService` (Generic Host) |
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
Boren.StockLottery/
├── Program.cs                          # Generic Host 入口，DI 註冊
├── appsettings.json                    # 設定檔
├── credentials.json                    # Google OAuth 憑證（不入 git）
├── Configuration/
│   └── AppSettings.cs                  # 設定 model
├── Models/
│   └── StockSubscription.cs            # 申購資料 model
├── Services/
│   ├── IbfsService.cs / IIbfsService   # ibfs.com.tw HTML 爬蟲
│   ├── GoogleCalendarService.cs        # Google 日曆 OAuth + 事件建立
│   ├── SqliteStockRepository.cs        # 已處理紀錄（防重複）
│   └── LotteryOrchestrator.cs          # 主流程協調
└── Workers/
    └── SchedulerHostedService.cs       # 排程 BackgroundService

Boren.StockLottery.Tests/
└── IbfsServiceIntegrationTest.cs       # ibfs 解析正確性驗證
```

---

## 環境設定

### 1. Google Calendar API 憑證

1. Google Cloud Console → APIs & Services → 啟用 **Google Calendar API**
2. 建立 OAuth 2.0 用戶端 ID（類型：**Desktop app**）
3. 下載為 `credentials.json`，放在專案根目錄（`dotnet run` 用）或執行檔旁
4. 第一次執行會開啟瀏覽器要求授權，Token 存於 `data/token/`（之後免重新授權）

> 無頭機器（headless server）：本機授權一次後，將 `data/token/` 複製到伺服器。

### 2. appsettings.json

```json
{
  "AppSettings": {
    "PremiumThresholdPercent": 30.0,
    "ScheduleHour": 15,
    "ScheduleMinute": 10,
    "CalendarId": "primary",
    "DbPath": "data/lottery.db",
    "GoogleCredentialsPath": "credentials.json",
    "GoogleTokenFolder": "data/token"
  }
}
```

| 設定 | 說明 | 預設 |
|------|------|------|
| `PremiumThresholdPercent` | 觸發行事曆的最低溢價率 (%) | `30.0` |
| `ScheduleHour` / `ScheduleMinute` | 每日執行時間（本地時間） | `15:10` |
| `CalendarId` | Google 日曆 ID | `"primary"` |
| `DbPath` | SQLite 資料庫路徑 | `"data/lottery.db"` |

---

## 執行

```bash
# 還原套件
dotnet restore

# 建置
dotnet build

# 執行（Release：等到 15:10 才跑；Debug：立即執行一次）
dotnet run --project Boren.StockLottery/Boren.StockLottery.csproj

# 執行測試（會直連 ibfs.com.tw）
dotnet test
```

---

## 注意事項

- **ibfs.com.tw 需要 `Referer` header**，否則可能被 403。已在 HttpClient 設定中處理。
- **重複保護**：SQLite 的 `UNIQUE(StockCode, LotteryDate)` 確保同一檔期不會重複建立行事曆事件。
- **Debug vs Release**：`SchedulerHostedService` 以 `#if !DEBUG` 控制排程等待，Debug 模式下程式啟動即立刻執行一次，方便測試。
- **欄位解析**：ibfs 表格中部分欄位一格多行（承銷價/參考價同格、抽籤日/扣款日/領券日同格），`IbfsService` 依行序取值。

---

## 開發背景

**痛點**：每次公開申購都要自己開 App 查、自己算溢價、自己記日期，忘記就錯過了。

**開發方式**：先規劃需求與資料來源，寫出完整 prompt 後交給 Claude Code 生成，約花 8 分鐘完成初版。後續依實際問題（TWSE 新股查不到市價、ibfs 已提供溢價率）調整資料來源架構。

**主要設計決策**：
- 從 TWSE JSON API + MIS 即時價格，改為直接爬 ibfs.com.tw（已含溢價率，省去獨立計算）
- 不引入 EF Core，直接用 `Microsoft.Data.Sqlite` 保持輕量
- 整合測試直連外部網站，確保 HTML 解析邏輯與實際頁面一致
