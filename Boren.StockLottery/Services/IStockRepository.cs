namespace Boren.StockLottery.Services;

public interface IStockRepository
{
    Task InitializeAsync();
    Task<bool> IsProcessedAsync(string stockCode, string lotteryDate);
    Task MarkProcessedAsync(string stockCode, string lotteryDate);
}
