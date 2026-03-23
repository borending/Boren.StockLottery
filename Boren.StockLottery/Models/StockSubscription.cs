namespace Boren.StockLottery.Models;

public class StockSubscription
{
    public string LotteryDate { get; set; } = "";           // 抽籤日期 "yyyy-MM-dd"
    public string StockName { get; set; } = "";              // 股票名稱
    public string StockCode { get; set; } = "";              // 股票代號
    public string SubscriptionEndDate { get; set; } = "";   // 申購截止日 "yyyy-MM-dd"
    public decimal SubscriptionPrice { get; set; }           // 承銷價(元)
    public int SubscriptionShares { get; set; }              // 申購股數
    public decimal ReferencePrice { get; set; }              // 參考價(元)
    public decimal PremiumRatioPercent { get; set; }         // 報酬率試算(%)
}
