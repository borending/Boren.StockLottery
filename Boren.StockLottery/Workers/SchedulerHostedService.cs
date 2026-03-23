using Boren.StockLottery.Configuration;
using Boren.StockLottery.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Boren.StockLottery.Workers;

public class SchedulerHostedService : BackgroundService
{
    private readonly ILotteryOrchestrator _orchestrator;
    private readonly IStockRepository _repository;
    private readonly ICalendarService _calendarService;
    private readonly AppSettings _settings;
    private readonly ILogger<SchedulerHostedService> _logger;

    public SchedulerHostedService(
        ILotteryOrchestrator orchestrator,
        IStockRepository repository,
        ICalendarService calendarService,
        IOptions<AppSettings> options,
        ILogger<SchedulerHostedService> logger)
    {
        _orchestrator = orchestrator;
        _repository = repository;
        _calendarService = calendarService;
        _settings = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initialize dependencies
        await _repository.InitializeAsync();
        await _calendarService.InitializeAsync(stoppingToken);

        _logger.LogInformation(
            "排程器已啟動，每日執行時間：{Hour:D2}:{Minute:D2}（本地時間）",
            _settings.ScheduleHour, _settings.ScheduleMinute);

        while (!stoppingToken.IsCancellationRequested)
        {
#if !DEBUG
            var now = DateTime.Now;
            var next = new DateTime(now.Year, now.Month, now.Day,
                                    _settings.ScheduleHour, _settings.ScheduleMinute, 0);
            if (now >= next)
                next = next.AddDays(1);

            var delay = next - now;
            _logger.LogInformation("下次執行時間：{Next}（距今 {Delay:hh\\:mm\\:ss}）", next, delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
#endif

            if (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _orchestrator.RunAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "排程執行失敗");
                }
            }
        }

        _logger.LogInformation("排程器已停止");
    }
}
