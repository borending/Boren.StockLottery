using Boren.StockLottery.Models;

namespace Boren.StockLottery.Services;

public interface IIbfsService
{
    /// <summary>
    /// Fetches active public subscriptions (status = 申購) from ibfs.com.tw.
    /// Includes reference price and pre-calculated premium ratio.
    /// </summary>
    Task<List<StockSubscription>> GetActiveSubscriptionsAsync(CancellationToken ct = default);
}
