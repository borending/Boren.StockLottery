using Boren.StockLottery.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Boren.StockLottery.Services;

public class SqliteStockRepository : IStockRepository
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteStockRepository> _logger;

    public SqliteStockRepository(IOptions<AppSettings> options, ILogger<SqliteStockRepository> logger)
    {
        _logger = logger;
        var dbPath = Path.GetFullPath(options.Value.DbPath, AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = $"Data Source={dbPath}";
    }

    public async Task InitializeAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ProcessedStocks (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                StockCode   TEXT NOT NULL,
                LotteryDate TEXT NOT NULL,
                ProcessedAt TEXT NOT NULL,
                UNIQUE(StockCode, LotteryDate)
            );
            """;
        await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("資料庫已初始化，位置：{ConnectionString}", _connectionString);
    }

    public async Task<bool> IsProcessedAsync(string stockCode, string lotteryDate)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM ProcessedStocks WHERE StockCode = @code AND LotteryDate = @date";
        cmd.Parameters.AddWithValue("@code", stockCode);
        cmd.Parameters.AddWithValue("@date", lotteryDate);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    public async Task MarkProcessedAsync(string stockCode, string lotteryDate)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO ProcessedStocks (StockCode, LotteryDate, ProcessedAt)
            VALUES (@code, @date, @processedAt)
            """;
        cmd.Parameters.AddWithValue("@code", stockCode);
        cmd.Parameters.AddWithValue("@date", lotteryDate);
        cmd.Parameters.AddWithValue("@processedAt", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }
}
