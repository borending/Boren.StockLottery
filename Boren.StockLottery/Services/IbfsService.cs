using System.Globalization;
using Boren.StockLottery.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace Boren.StockLottery.Services;

public class IbfsService : IIbfsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IbfsService> _logger;

    private const string PageUrl = "rank/default6.aspx?xy=6&xt=1";

    public IbfsService(IHttpClientFactory httpClientFactory, ILogger<IbfsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<List<StockSubscription>> GetActiveSubscriptionsAsync(CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("ibfs");
        _logger.LogInformation("正在抓取 ibfs 申購頁面");

        string html;
        try
        {
            var response = await client.GetAsync(PageUrl, ct);
            response.EnsureSuccessStatusCode();
            html = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "抓取 ibfs 頁面失敗");
            return new List<StockSubscription>();
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var rows = doc.DocumentNode.SelectNodes("//table//tr");
        if (rows is null || rows.Count < 2)
        {
            _logger.LogWarning("ibfs 頁面找不到表格資料列");
            return new List<StockSubscription>();
        }

        // Map column indices from header row (header uses all <th>)
        var headers = rows[0].SelectNodes(".//th | .//td");
        if (headers is null)
        {
            _logger.LogWarning("ibfs 頁面找不到表頭欄位");
            return new List<StockSubscription>();
        }

        // Data rows use <th> for the first cell (stock code+name) and <td> for the rest.
        // Select both so indices align with header indices.
        var firstDataCells = rows.Skip(1)
            .Select(r => r.SelectNodes(".//th | .//td"))
            .FirstOrDefault(c => c is not null && c.Count > 0);
        int offset = firstDataCells is not null
            ? Math.Max(0, firstDataCells.Count - headers.Count)
            : 0;

        int hIdxName        = IndexOf(headers, "股票名稱");
        int hIdxStatus      = IndexOf(headers, "申購");
        int hIdxRefPrice    = IndexOf(headers, "參考價");    // 參考價 is line 1 of the price cell
        int hIdxPremium     = IndexOf(headers, "報酬率");
        int hIdxShares      = IndexOf(headers, "申購股數");
        int hIdxDeduction   = IndexOf(headers, "預扣");     // 預扣價款 e.g. "$72,070" — the actual subscription cost
        int hIdxEndDate     = IndexOf(headers, "截止日");
        int hIdxLotteryDate = IndexOf(headers, "抽籤日");

        if (new[] { hIdxName, hIdxStatus, hIdxRefPrice, hIdxPremium, hIdxShares, hIdxDeduction, hIdxEndDate, hIdxLotteryDate }.Any(i => i < 0))
        {
            _logger.LogWarning("無法對應必要欄位。找到的表頭：{Headers}",
                string.Join(", ", headers.Select(h => h.InnerText.Trim())));
            return new List<StockSubscription>();
        }

        int idxName        = hIdxName        + offset;
        int idxStatus      = hIdxStatus      + offset;
        int idxPriceCell   = hIdxRefPrice    + offset; // cell contains 承銷價 (line 0) and 參考價 (line 1)
        int idxPremium     = hIdxPremium     + offset;
        int idxShares      = hIdxShares      + offset;
        int idxDeduction   = hIdxDeduction   + offset; // "$72,070" total subscription cost
        int idxEndDate     = hIdxEndDate     + offset;
        int idxLotteryDate = hIdxLotteryDate + offset;

        var result = new List<StockSubscription>();

        foreach (var row in rows.Skip(1))
        {
            // Include <th> cells so the first cell (stock code+name) is at index 0
            var cells = row.SelectNodes(".//th | .//td");
            if (cells is null) continue;

            int maxIdx = new[] { idxName, idxStatus, idxPriceCell, idxPremium, idxShares, idxDeduction, idxEndDate, idxLotteryDate }.Max();
            if (cells.Count <= maxIdx) continue;

            var status = cells[idxStatus].InnerText.Trim();
            if (status != "申購") continue;

            // Stock code and name are both in the first <th> cell, on separate lines:
            // line 0 = stock code, line 1 = stock name, line 2 = market type
            var nameLines = SplitLines(cells[idxName].InnerText);
            if (nameLines.Length < 2) continue;
            var code = nameLines[0];
            var name = nameLines[1];

            // Price cell contains 承銷價 (line 0) and 參考價 (line 1) on separate lines
            var priceLines = SplitLines(cells[idxPriceCell].InnerText);
            if (priceLines.Length < 2) continue;
            if (!TryParseDecimal(priceLines[1], out var refPrice)) continue;

            // 扣款金額 cell contains the total subscription cost e.g. "$72,070" — strip $ and parse
            var deductionRaw = cells[idxDeduction].InnerText.Trim().TrimStart('$').Replace(",", "");
            if (!TryParseDecimal(deductionRaw, out var subPrice)) continue;

            if (!TryParsePremium(cells[idxPremium].InnerText, out var premium)) continue;

            // 申購股數 is the last line in the cell (first line = 承銷張數)
            var shareLines = SplitLines(cells[idxShares].InnerText);
            if (!int.TryParse((shareLines.LastOrDefault() ?? "").Replace(",", ""), out var shares)) continue;

            // 截止日 cell has two dates (開始日 / 截止日): take the last one
            var endDateLines = SplitLines(cells[idxEndDate].InnerText);
            var endDateStr = endDateLines.LastOrDefault() ?? "";
            if (!TryParseDate(endDateStr, out _)) continue;

            // 抽籤日 cell has three dates (抽籤日 / 扣款日 / 領券日): take the first one
            var lotteryLines = SplitLines(cells[idxLotteryDate].InnerText);
            var lotteryDateStr = lotteryLines.FirstOrDefault() ?? "";
            if (!TryParseDate(lotteryDateStr, out _)) continue;

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

        _logger.LogInformation("從 ibfs 找到 {Count} 筆申購中股票", result.Count);
        return result;
    }

    // Split cell InnerText by line breaks, trim each line, drop empty lines
    private static string[] SplitLines(string raw) =>
        raw.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
           .Select(l => l.Trim())
           .Where(l => l.Length > 0)
           .ToArray();

    // Premium ratio cell is like "89.11%" — strip % and parse
    private static bool TryParsePremium(string raw, out decimal value)
    {
        var token = raw.Trim().TrimEnd('%');
        return TryParseDecimal(token, out value);
    }

    private static bool TryParseDecimal(string raw, out decimal value) =>
        decimal.TryParse(raw.Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static bool TryParseDate(string raw, out DateOnly date) =>
        DateOnly.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static int IndexOf(HtmlNodeCollection cells, string keyword)
    {
        for (int i = 0; i < cells.Count; i++)
            if (cells[i].InnerText.Contains(keyword))
                return i;
        return -1;
    }
}
