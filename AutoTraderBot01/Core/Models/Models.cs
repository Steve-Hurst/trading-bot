using System;

namespace Core.Models
{
    public enum OrderSide
    {
        Buy,
        Sell
    }

    public enum OrderType
    {
        Market,
        Limit,
        Stop
    }

    public enum PositionStatus
    {
        Open,
        Closed,
        Cancelled
    }

    public class TickData
    {
        public string Symbol { get; set; } = "EURUSD";
        public double Bid { get; set; }
        public double Ask { get; set; }
        public double SpreadPips => Math.Round((Ask - Bid) * 10000, 2);
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }

    public class BarData
    {
        public string Symbol { get; set; } = "EURUSD";
        public DateTime TimestampUtc { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public double Volume { get; set; }
    }

    public class OrderRequest
    {
        public string RequestId { get; set; } = Guid.NewGuid().ToString("N")[..12];
        public string Symbol { get; set; } = "EURUSD";
        public OrderSide Side { get; set; }
        public OrderType Type { get; set; } = OrderType.Market;
        public double VolumeLots { get; set; } = 0.01;
        public double RequestedPrice { get; set; }
        public double? StopLossPrice { get; set; }
        public double? TakeProfitPrice { get; set; }
        public string StrategyFunction { get; set; } = "EURUSDMeanReversionStrategy.EvaluateSignal";
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }

    public class OrderResult
    {
        public bool Success { get; set; }
        public string OrderId { get; set; } = string.Empty;
        public string ExecutionId { get; set; } = string.Empty;
        public double RequestedPrice { get; set; }
        public double FilledPrice { get; set; }
        public double SlippagePips { get; set; }
        public double LatencyMs { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }

    public class Position
    {
        public long PositionId { get; set; }
        public string Symbol { get; set; } = "EURUSD";
        public OrderSide Side { get; set; }
        public double VolumeLots { get; set; } = 0.01;
        public double EntryPrice { get; set; }
        public double CurrentPrice { get; set; }
        public double? StopLoss { get; set; }
        public double? TakeProfit { get; set; }
        public double UnrealizedPnL { get; set; }
        public DateTime EntryTimeUtc { get; set; } = DateTime.UtcNow;
        public PositionStatus Status { get; set; } = PositionStatus.Open;
    }

    public class AccountSummary
    {
        public double Balance { get; set; } = 500.0;
        public double Equity { get; set; } = 500.0;
        public double InitialCapital { get; set; } = 500.0;
        public double UsedMargin { get; set; } = 0.0;
        public double FreeMargin => Equity - UsedMargin;
        public double MarginLevelPct => UsedMargin > 0 ? (Equity / UsedMargin) * 100 : 999.0;
        public double PeakEquity { get; set; } = 500.0;
        public double DrawdownPct => PeakEquity > 0 ? Math.Max(0.0, ((PeakEquity - Equity) / PeakEquity) * 100.0) : 0.0;
        public double DailyRealizedPnL { get; set; } = 0.0;
        public int TotalTradesToday { get; set; } = 0;
        public int WinningTradesToday { get; set; } = 0;
        public int LosingTradesToday { get; set; } = 0;
        public double WinRatePct => TotalTradesToday > 0 ? ((double)WinningTradesToday / TotalTradesToday) * 100.0 : 0.0;
    }
}
