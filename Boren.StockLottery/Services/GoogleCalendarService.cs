using Boren.StockLottery.Configuration;
using Boren.StockLottery.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Boren.StockLottery.Services;

public class GoogleCalendarService : ICalendarService
{
    private readonly AppSettings _settings;
    private readonly ILogger<GoogleCalendarService> _logger;
    private CalendarService? _calendarService;

    public GoogleCalendarService(IOptions<AppSettings> options, ILogger<GoogleCalendarService> logger)
    {
        _settings = options.Value;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var credPath = Path.GetFullPath(_settings.GoogleCredentialsPath, AppContext.BaseDirectory);
        if (!File.Exists(credPath))
            throw new FileNotFoundException(
                $"Google credentials file not found at '{credPath}'. " +
                "Download it from Google Cloud Console -> APIs & Services -> Credentials.", credPath);

        var tokenFolder = Path.GetFullPath(_settings.GoogleTokenFolder, AppContext.BaseDirectory);
        Directory.CreateDirectory(tokenFolder);

        UserCredential credential;
        await using var stream = new FileStream(credPath, FileMode.Open, FileAccess.Read);
        // NOTE: First run will open a browser window for OAuth consent.
        // After consenting, the token is saved to tokenFolder and reused on subsequent runs.
        credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            GoogleClientSecrets.FromStream(stream).Secrets,
            new[] { CalendarService.Scope.Calendar },
            "user",
            ct,
            new FileDataStore(tokenFolder, true)
        );

        _calendarService = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Boren.StockLottery"
        });

        _logger.LogInformation("Google 行事曆初始化成功");
    }

    public async Task CreateEventsAsync(StockSubscription stock, decimal premiumRatio, CancellationToken ct = default)
    {
        if (_calendarService is null)
            throw new InvalidOperationException("GoogleCalendarService not initialized. Call InitializeAsync first.");

        DateOnly.TryParseExact(stock.SubscriptionEndDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var endDate);
        DateOnly.TryParseExact(stock.LotteryDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var lotteryDate);

        // SubscriptionPrice is the total subscription cost (扣款金額) from the ibfs page, e.g. 72070
        var title = $"{stock.StockCode}{stock.StockName} {stock.SubscriptionPrice:0}:{premiumRatio:F2}%";

        // Event 1: Day before subscription end date
        var dayBeforeEnd = endDate.AddDays(-1);
        await InsertAllDayEventAsync(title, dayBeforeEnd, ct);
        _logger.LogInformation("已在 {Date} 建立行事曆事件：{Title}", dayBeforeEnd, title);

        // Event 2: Lottery date
        await InsertAllDayEventAsync(title, lotteryDate, ct);
        _logger.LogInformation("已在 {Date} 建立行事曆事件：{Title}", lotteryDate, title);
    }

    private async Task InsertAllDayEventAsync(string title, DateOnly date, CancellationToken ct)
    {
        var calEvent = new Event
        {
            Summary = title,
            Start = new EventDateTime { Date = date.ToString("yyyy-MM-dd") },
            End = new EventDateTime { Date = date.AddDays(1).ToString("yyyy-MM-dd") }
        };

        var request = _calendarService!.Events.Insert(calEvent, _settings.CalendarId);
        await request.ExecuteAsync(ct);
    }
}
