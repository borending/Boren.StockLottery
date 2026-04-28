using System.Globalization;
using Boren.StockLottery.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Boren.StockLottery.Services;

public class LotteryOrchestrator : ILotteryOrchestrator
{
    private readonly IIbfsService _ibfsService;
    private readonly IStockRepository _repository;
    private readonly ICalendarService _calendarService;
    private readonly AppSettings _settings;
    private readonly ILogger<LotteryOrchestrator> _logger;

    public LotteryOrchestrator(
        IIbfsService ibfsService,
        IStockRepository repository,
        ICalendarService calendarService,
        IOptions<AppSettings> options,
        ILogger<LotteryOrchestrator> logger)
    {
        _ibfsService = ibfsService;
        _repository = repository;
        _calendarService = calendarService;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("=== 抽籤檢查開始於 {Time} ===", DateTime.Now);

        var subscriptions = await _ibfsService.GetActiveSubscriptionsAsync(ct);
        if (subscriptions.Count == 0)
        {
            _logger.LogInformation("今日無申購中股票。");
            return;
        }

        if (_settings.MaxSubscriptionPrice > 0)
        {
            var before = subscriptions.Count;
            subscriptions = subscriptions
                .Where(s => s.SubscriptionPrice <= _settings.MaxSubscriptionPrice)
                .ToList();
            _logger.LogInformation("金額篩選（上限 {Max:0}）：{Before} → {After} 筆",
                _settings.MaxSubscriptionPrice, before, subscriptions.Count);
        }

        int newCount = 0, calendarCount = 0;

        foreach (var stock in subscriptions)
        {
            if (ct.IsCancellationRequested) break;

            if (await _repository.IsProcessedAsync(stock.StockCode, stock.LotteryDate))
            {
                _logger.LogDebug("跳過已處理的股票 {Code}（抽籤日 {Date}）",
                    stock.StockCode, stock.LotteryDate);
                continue;
            }

            newCount++;
            _logger.LogInformation("處理中 {Code} {Name}（抽籤日 {Date}）",
                stock.StockCode, stock.StockName, stock.LotteryDate);

            _logger.LogInformation("{Code} {Name}：參考價={RefPrice}，申購價={SubPrice}，溢價={Premium:F2}%",
                stock.StockCode, stock.StockName, stock.ReferencePrice, stock.SubscriptionPrice, stock.PremiumRatioPercent);

            if (stock.PremiumRatioPercent >= (decimal)_settings.PremiumThresholdPercent)
            {
                _logger.LogInformation("溢價 {Premium:F2}% >= 門檻 {Threshold}%，新增行事曆提醒",
                    stock.PremiumRatioPercent, _settings.PremiumThresholdPercent);
                try
                {
                    await _calendarService.CreateEventsAsync(stock, stock.PremiumRatioPercent, ct);
                    calendarCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "建立 {Code} 行事曆事件失敗", stock.StockCode);
                }
            }
            else
            {
                _logger.LogInformation("溢價 {Premium:F2}% < 門檻 {Threshold}%，略過行事曆",
                    stock.PremiumRatioPercent, _settings.PremiumThresholdPercent);
            }

            await _repository.MarkProcessedAsync(stock.StockCode, stock.LotteryDate);
        }

        _logger.LogInformation(
            "=== 完成：共 {Total} 筆申購，{New} 筆新增，{Calendar} 個行事曆事件已建立 ===",
            subscriptions.Count, newCount, calendarCount);
    }
}
