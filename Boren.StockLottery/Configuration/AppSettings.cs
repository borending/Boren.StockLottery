namespace Boren.StockLottery.Configuration;

public class AppSettings
{
    public double PremiumThresholdPercent { get; set; } = 30.0;
    public string DbPath { get; set; } = "data/lottery.db";
    public string ProcessedFilePath { get; set; } = "data/processed.txt";
    public string GoogleCredentialsPath { get; set; } = "credentials.json";
    public string GoogleTokenFolder { get; set; } = "data/token";
    public string CalendarId { get; set; } = "primary";
}
