using Boren.StockLottery.Models;

namespace Boren.StockLottery.Services;

public interface ICalendarService
{
    Task InitializeAsync(CancellationToken ct = default);
    Task CreateEventsAsync(StockSubscription stock, decimal premiumRatio, CancellationToken ct = default);
}
