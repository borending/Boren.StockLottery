# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Boren.StockLottery is a .NET 8.0 C# console application that runs as a long-lived background service. It scrapes Taiwan stock public subscription data from ibfs.com.tw daily, and creates Google Calendar reminders for stocks whose premium ratio meets the configured threshold.

## Commands

```bash
# Restore packages
dotnet restore

# Build
dotnet build

# Run (stays alive, waits for scheduled time; in DEBUG mode runs immediately)
dotnet run --project Boren.StockLottery/Boren.StockLottery.csproj

# Build release
dotnet build -c Release

# Run tests (NUnit integration tests — hit live ibfs.com.tw)
dotnet test
```

## Architecture

**Entry point:** `Program.cs` — Generic Host (`IHostedService`) with a named `HttpClient` ("ibfs") and DI setup.

**Background worker:** `Workers/SchedulerHostedService.cs` — `BackgroundService` that sleeps until the configured daily time, then invokes the orchestrator. In `#if DEBUG` mode the delay is skipped and it runs immediately. Initializes DB and Google Calendar OAuth on startup.

**Core workflow:** `Services/LotteryOrchestrator.cs`
1. Fetch active subscriptions (status = 申購) from ibfs.com.tw via `IbfsService`
2. Skip already-processed stocks (SQLite, keyed on `StockCode + LotteryDate`)
3. If premium ratio >= threshold → create 2 Google Calendar events
4. Mark processed in SQLite

**Services:**
- `IbfsService` — scrapes HTML from `https://www.ibfs.com.tw/rank/default6.aspx?xy=6&xt=1` using `HtmlAgilityPack`. Parses table columns by header keyword matching. Returns list of `StockSubscription` with premium ratio pre-calculated by ibfs.
- `SqliteStockRepository` — uses `Microsoft.Data.Sqlite` directly (no EF Core)
- `GoogleCalendarService` — OAuth2 via `GoogleWebAuthorizationBroker`; **first run opens browser for consent**, saves token to `data/token/`

**Model:** `Models/StockSubscription.cs`
- `SubscriptionPrice` — total subscription cost (扣款金額, e.g. 72070), used in calendar event title
- `ReferencePrice` — 參考價 (market reference price)
- `PremiumRatioPercent` — 報酬率 (%), pre-calculated by ibfs.com.tw
- `SubscriptionShares` — 申購股數
- `LotteryDate` / `SubscriptionEndDate` — `"yyyy-MM-dd"` strings

**Calendar event title format:** `{StockCode}{StockName} {SubscriptionPrice:0}:{premiumRatio:F2}%`
Two all-day events are created: one on the day before subscription end date, one on the lottery date.

**Configuration:** `appsettings.json` → `Configuration/AppSettings.cs` (section key: `"AppSettings"`)

## Tests

`Boren.StockLottery.Tests/` — NUnit integration tests that hit live ibfs.com.tw:
- `DumpHeaders` — dumps raw table header columns to console (diagnostic)
- `GetActiveSubscriptionsAsync_ShouldMatchWebsite` — fetches raw HTML independently and compares field-by-field against `IbfsService` output

## Setup Required Before Running

1. **Google Calendar API credentials:**
   - Go to Google Cloud Console → APIs & Services → Enable "Google Calendar API"
   - Create OAuth 2.0 Client ID (type: **Desktop app**)
   - Download as `credentials.json` and place it next to the executable (or in the project root for `dotnet run`)
   - First run will open a browser for OAuth consent; token saved to `data/token/`

2. **Configuration** (`appsettings.json`):
   - `PremiumThresholdPercent` — minimum premium ratio (%) to trigger calendar events (default: 30.0)
   - `ScheduleHour` / `ScheduleMinute` — daily run time in local time (default: 15:10)
   - `CalendarId` — Google Calendar ID (default: `"primary"`)
   - `DbPath` — SQLite DB path (default: `"data/lottery.db"`)
   - `GoogleCredentialsPath` — path to `credentials.json` (default: `"credentials.json"`)
   - `GoogleTokenFolder` — OAuth token storage folder (default: `"data/token"`)

## Key Gotchas

- **ibfs.com.tw HTML scraping:** Requires `Referer: https://www.ibfs.com.tw/` header or requests may be blocked. Column indices are resolved dynamically from header row keywords — if the site changes column order, `IbfsService` handles it gracefully via `IndexOf`.
- **Cell layout quirks:** The first column uses `<th>` (not `<td>`), causing a count offset between header row and data rows. `IbfsService` calculates this offset from the first data row.
- **Price cell:** Contains both 承銷價 (line 0) and 參考價 (line 1) in one cell.
- **Shares cell:** Contains 承銷張數 (line 0) and 申購股數 (line 1) — last line is used.
- **Date cells:** 截止日 cell has two dates (start/end) — last is used. 抽籤日 cell has three dates (抽籤/扣款/領券) — first is used.
- **Dates format:** ibfs.com.tw already provides dates as `yyyy-MM-dd` (no ROC calendar conversion needed).
- **Idempotency:** SQLite `UNIQUE(StockCode, LotteryDate)` prevents double-processing.
- **Google token:** `GoogleWebAuthorizationBroker` requires a browser on first run. On headless machines, run interactively once to generate the token, then copy `data/token/` to the server.
- **`ProcessedFilePath` in AppSettings** — config key exists but `ProcessedFileService` has been removed; the field is currently unused.
