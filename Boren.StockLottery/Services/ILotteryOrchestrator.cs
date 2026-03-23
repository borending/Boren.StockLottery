namespace Boren.StockLottery.Services;

public interface ILotteryOrchestrator
{
    Task RunAsync(CancellationToken ct = default);
}
