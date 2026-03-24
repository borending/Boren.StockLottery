using Boren.StockLottery.Configuration;
using Boren.StockLottery.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        services.Configure<AppSettings>(ctx.Configuration.GetSection("AppSettings"));

        // Named HttpClient: ibfs subscription page
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

        services.AddSingleton<IStockRepository, SqliteStockRepository>();
        services.AddSingleton<IIbfsService, IbfsService>();
        services.AddSingleton<ICalendarService, GoogleCalendarService>();
        services.AddSingleton<ILotteryOrchestrator, LotteryOrchestrator>();
    })
    .Build();

var repository = host.Services.GetRequiredService<IStockRepository>();
var calendarService = host.Services.GetRequiredService<ICalendarService>();
var orchestrator = host.Services.GetRequiredService<ILotteryOrchestrator>();

await repository.InitializeAsync();
await calendarService.InitializeAsync(cts.Token);
await orchestrator.RunAsync(cts.Token);
