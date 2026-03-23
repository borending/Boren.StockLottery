using System.Globalization;
using Boren.StockLottery.Models;
using Boren.StockLottery.Services;
using HtmlAgilityPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Boren.StockLottery.Tests;

[TestFixture]
public class IbfsServiceIntegrationTest
{
    private IIbfsService _service = null!;
    private HttpClient _rawClient = null!;

    [OneTimeTearDown]
    public void TearDown() => _rawClient.Dispose();

    [OneTimeSetUp]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.AddHttpClient("ibfs", c =>
        {
            c.BaseAddress = new Uri("https://www.ibfs.com.tw");
            c.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            c.DefaultRequestHeaders.Add("Accept-Language", "zh-TW,zh;q=0.9,en;q=0.8");
            c.DefaultRequestHeaders.Add("Referer", "https://www.ibfs.com.tw/");
            c.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        });
        services.AddSingleton<IIbfsService, IbfsService>();

        var sp = services.BuildServiceProvider();
        _service = sp.GetRequiredService<IIbfsService>();

        var factory = sp.GetRequiredService<IHttpClientFactory>();
        _rawClient = factory.CreateClient("ibfs");
    }

    [Test]
    public async Task DumpHeaders()
    {
        var html = await _rawClient.GetStringAsync("rank/default6.aspx?xy=6&xt=1");
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var rows = doc.DocumentNode.SelectNodes("//table//tr");
        var headers = rows[0].SelectNodes(".//th | .//td");
        Console.WriteLine($"Header count: {headers.Count}");
        for (int i = 0; i < headers.Count; i++)
            Console.WriteLine($"  headers[{i}] = \"{headers[i].InnerText.Trim()}\"");
        Assert.Pass();
    }

    [Test]
    public async Task GetActiveSubscriptionsAsync_ShouldMatchWebsite()
    {
        // ── Step 1: 直接抓網站原始資料 ──────────────────────────────────────
        Console.WriteLine("========== Step 1: 直接從網站抓取原始申購清單 ==========");
        var rawHtml = await _rawClient.GetStringAsync("rank/default6.aspx?xy=6&xt=1");
        var rawItems = ParseRawSubscriptions(rawHtml);

        Console.WriteLine($"網站原始資料 共 {rawItems.Count} 筆 (status=申購):");
        foreach (var r in rawItems)
            Console.WriteLine($"  [{r.StockCode}] {r.StockName}  承銷價:{r.SubscriptionPrice}  參考價:{r.ReferencePrice}  報酬率:{r.PremiumRatioPercent}%  截止:{r.SubscriptionEndDate}  抽籤:{r.LotteryDate}");

        // ── Step 2: 呼叫 GetActiveSubscriptionsAsync ─────────────────────────
        Console.WriteLine();
        Console.WriteLine("========== Step 2: 呼叫 GetActiveSubscriptionsAsync ==========");
        var serviceItems = await _service.GetActiveSubscriptionsAsync();

        Console.WriteLine($"Service 回傳 共 {serviceItems.Count} 筆:");
        foreach (var s in serviceItems)
            Console.WriteLine($"  [{s.StockCode}] {s.StockName}  承銷價:{s.SubscriptionPrice}  參考價:{s.ReferencePrice}  報酬率:{s.PremiumRatioPercent}%  截止:{s.SubscriptionEndDate}  抽籤:{s.LotteryDate}");

        // ── Step 3: 比對 ──────────────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("========== Step 3: 比對結果 ==========");

        var rawCodes    = rawItems.Select(r => r.StockCode).ToHashSet();
        var serviceCodes = serviceItems.Select(s => s.StockCode).ToHashSet();

        var onlyInRaw     = rawCodes.Except(serviceCodes).ToList();
        var onlyInService = serviceCodes.Except(rawCodes).ToList();
        var inBoth        = rawCodes.Intersect(serviceCodes).ToList();

        Console.WriteLine($"兩邊都有: {inBoth.Count} 筆 → {string.Join(", ", inBoth)}");

        if (onlyInRaw.Count > 0)
            Console.WriteLine($"[警告] 只在網站有 (Service 漏抓): {string.Join(", ", onlyInRaw)}");
        else
            Console.WriteLine("✓ Service 沒有漏抓任何網站上的申購");

        if (onlyInService.Count > 0)
            Console.WriteLine($"[警告] 只在 Service 有 (多抓): {string.Join(", ", onlyInService)}");
        else
            Console.WriteLine("✓ Service 沒有多抓");

        // 欄位細節比對
        Console.WriteLine();
        Console.WriteLine("── 欄位細節比對 ──");
        bool allMatch = true;
        foreach (var code in inBoth)
        {
            var r = rawItems.First(x => x.StockCode == code);
            var s = serviceItems.First(x => x.StockCode == code);

            var diffs = new List<string>();
            if (r.StockName           != s.StockName)           diffs.Add($"名稱: 網站={r.StockName} / Service={s.StockName}");
            if (r.SubscriptionPrice   != s.SubscriptionPrice)   diffs.Add($"承銷價: 網站={r.SubscriptionPrice} / Service={s.SubscriptionPrice}");
            if (r.ReferencePrice      != s.ReferencePrice)      diffs.Add($"參考價: 網站={r.ReferencePrice} / Service={s.ReferencePrice}");
            if (r.PremiumRatioPercent != s.PremiumRatioPercent) diffs.Add($"報酬率: 網站={r.PremiumRatioPercent} / Service={s.PremiumRatioPercent}");
            if (r.SubscriptionEndDate != s.SubscriptionEndDate) diffs.Add($"截止日: 網站={r.SubscriptionEndDate} / Service={s.SubscriptionEndDate}");
            if (r.LotteryDate         != s.LotteryDate)         diffs.Add($"抽籤日: 網站={r.LotteryDate} / Service={s.LotteryDate}");

            if (diffs.Count > 0)
            {
                allMatch = false;
                Console.WriteLine($"  [{code}] 差異:");
                foreach (var d in diffs)
                    Console.WriteLine($"    {d}");
            }
            else
            {
                Console.WriteLine($"  [{code}] {s.StockName} ✓ 完全一致");
            }
        }

        if (allMatch && onlyInRaw.Count == 0 && onlyInService.Count == 0)
            Console.WriteLine("\n結論: Service 回傳資料與網站完全一致 ✓");
        else
            Console.WriteLine("\n結論: 存在差異，請查看上方細節");

        // 斷言
        Assert.That(onlyInRaw,     Is.Empty, "Service 應包含所有網站上的申購股票");
        Assert.That(onlyInService, Is.Empty, "Service 不應回傳網站上沒有的股票");
        Assert.That(allMatch,      Is.True,  "所有共同股票的欄位應完全一致");
    }

    // 直接解析 HTML，不走 IbfsService，作為比對基準（與 IbfsService 相同邏輯）
    private static List<StockSubscription> ParseRawSubscriptions(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var rows = doc.DocumentNode.SelectNodes("//table//tr");
        if (rows is null || rows.Count < 2) return new();

        var headers = rows[0].SelectNodes(".//th | .//td");
        if (headers is null) return new();

        // Data rows use <th> for the first cell, so include it in the count
        var firstDataCells = rows.Skip(1)
            .Select(r => r.SelectNodes(".//th | .//td"))
            .FirstOrDefault(c => c is not null && c.Count > 0);
        int offset = firstDataCells is not null
            ? Math.Max(0, firstDataCells.Count - headers.Count)
            : 0;

        int hIdxName        = IndexOf(headers, "股票名稱");
        int hIdxStatus      = IndexOf(headers, "申購");
        int hIdxRefPrice    = IndexOf(headers, "參考價");
        int hIdxPremium     = IndexOf(headers, "報酬率");
        int hIdxShares      = IndexOf(headers, "申購股數");
        int hIdxDeduction   = IndexOf(headers, "預扣");
        int hIdxEndDate     = IndexOf(headers, "截止日");
        int hIdxLotteryDate = IndexOf(headers, "抽籤日");

        if (new[] { hIdxName, hIdxStatus, hIdxRefPrice, hIdxPremium, hIdxShares, hIdxDeduction, hIdxEndDate, hIdxLotteryDate }.Any(i => i < 0))
            return new();

        int idxName        = hIdxName        + offset;
        int idxStatus      = hIdxStatus      + offset;
        int idxPriceCell   = hIdxRefPrice    + offset;
        int idxPremium     = hIdxPremium     + offset;
        int idxShares      = hIdxShares      + offset;
        int idxDeduction   = hIdxDeduction   + offset;
        int idxEndDate     = hIdxEndDate     + offset;
        int idxLotteryDate = hIdxLotteryDate + offset;

        var result = new List<StockSubscription>();
        foreach (var row in rows.Skip(1))
        {
            var cells = row.SelectNodes(".//th | .//td");
            if (cells is null) continue;

            int maxIdx = new[] { idxName, idxStatus, idxPriceCell, idxPremium, idxShares, idxDeduction, idxEndDate, idxLotteryDate }.Max();
            if (cells.Count <= maxIdx) continue;

            var status = cells[idxStatus].InnerText.Trim();
            if (status != "申購") continue;

            var nameLines = SplitLines(cells[idxName].InnerText);
            if (nameLines.Length < 2) continue;
            var code = nameLines[0];
            var name = nameLines[1];

            var priceLines = SplitLines(cells[idxPriceCell].InnerText);
            if (priceLines.Length < 2) continue;
            if (!TryParseDecimal(priceLines[1], out var refPrice)) continue;

            var deductionRaw = cells[idxDeduction].InnerText.Trim().TrimStart('$').Replace(",", "");
            if (!TryParseDecimal(deductionRaw, out var subPrice)) continue;

            if (!TryParsePremium(cells[idxPremium].InnerText, out var premium)) continue;

            var shareLines = SplitLines(cells[idxShares].InnerText);
            if (!int.TryParse((shareLines.LastOrDefault() ?? "").Replace(",", ""), out var shares)) continue;

            var endDateLines = SplitLines(cells[idxEndDate].InnerText);
            var endDateStr = endDateLines.LastOrDefault() ?? "";
            if (!DateOnly.TryParseExact(endDateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) continue;

            var lotteryLines = SplitLines(cells[idxLotteryDate].InnerText);
            var lotteryDateStr = lotteryLines.FirstOrDefault() ?? "";
            if (!DateOnly.TryParseExact(lotteryDateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) continue;

            result.Add(new StockSubscription
            {
                StockCode             = code,
                StockName             = name,
                SubscriptionPrice     = subPrice,
                ReferencePrice        = refPrice,
                PremiumRatioPercent   = premium,
                SubscriptionShares    = shares,
                SubscriptionEndDate   = endDateStr,
                LotteryDate           = lotteryDateStr,
            });
        }
        return result;
    }

    private static string[] SplitLines(string raw) =>
        raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
           .Select(l => l.Trim())
           .Where(l => l.Length > 0)
           .ToArray();

    private static bool TryParsePremium(string raw, out decimal value)
    {
        var token = raw.Trim().TrimEnd('%');
        return TryParseDecimal(token, out value);
    }

    private static bool TryParseDecimal(string raw, out decimal value) =>
        decimal.TryParse(raw.Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static int IndexOf(HtmlNodeCollection cells, string keyword)
    {
        for (int i = 0; i < cells.Count; i++)
            if (cells[i].InnerText.Contains(keyword))
                return i;
        return -1;
    }
}
