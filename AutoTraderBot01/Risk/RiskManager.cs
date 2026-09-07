using System;
using Core.Models;
using Logging;

namespace Risk
{
    public class RiskManager
    {
        public const double MaxDrawdownPct = 5.0;            // Hard 5% max drawdown (£25 on £500 account)
        public const double MaxMarginUtilizationPct = 65.0;  // Max 65% margin load (£325 on £500 account)
        public const double MaxPositionLotSize = 0.01;       // Strict micro-lot cap
        public const double MaxDailyLossGbp = 10.0;          // Max £10 daily loss limit (2%)

        private int _consecutiveLosses = 0;
        private DateTime _coolDownUntilUtc = DateTime.MinValue;

        public bool ValidateOrder(OrderRequest request, AccountSummary account, TelemetryLogger logger)
        {
            // 1. Invariant Check: Drawdown Limit
            if (account.DrawdownPct >= MaxDrawdownPct)
            {
                logger.Warn("RiskManager.ValidateOrder", 
                    $"ORDER REJECTED: Account Drawdown ({account.DrawdownPct:F2}%) exceeds hard ceiling of {MaxDrawdownPct:F1}%. Emergency circuit breaker active!",
                    drawdownPct: account.DrawdownPct);
                return false;
            }

            // 2. Invariant Check: Margin Utilization
            double marginLoadPct = account.Equity > 0 ? (account.UsedMargin / account.Equity) * 100.0 : 100.0;
            if (marginLoadPct >= MaxMarginUtilizationPct)
            {
                logger.Warn("RiskManager.ValidateOrder", 
                    $"ORDER REJECTED: Margin Utilization ({marginLoadPct:F1}%) exceeds max threshold of {MaxMarginUtilizationPct:F1}%.",
                    drawdownPct: account.DrawdownPct);
                return false;
            }

            // 3. Invariant Check: Position Size Limit
            if (request.VolumeLots > MaxPositionLotSize)
            {
                logger.Warn("RiskManager.ValidateOrder", 
                    $"ORDER REJECTED: Requested lot size ({request.VolumeLots}) exceeds maximum allowed lot size of {MaxPositionLotSize}.",
                    drawdownPct: account.DrawdownPct);
                return false;
            }

            // 4. Invariant Check: Mandatory Stop-Loss
            if (!request.StopLossPrice.HasValue)
            {
                logger.Warn("RiskManager.ValidateOrder", 
                    "ORDER REJECTED: Order submitted without mandatory Stop-Loss protection.",
                    drawdownPct: account.DrawdownPct);
                return false;
            }

            // 5. Invariant Check: Daily Loss Limit
            if (account.DailyRealizedPnL <= -MaxDailyLossGbp)
            {
                logger.Warn("RiskManager.ValidateOrder", 
                    $"ORDER REJECTED: Daily realized loss (£{account.DailyRealizedPnL:F2}) exceeded daily risk budget (£{MaxDailyLossGbp:F2}).",
                    drawdownPct: account.DrawdownPct);
                return false;
            }

            // 6. Invariant Check: Loss Streak Cooldown
            if (DateTime.UtcNow < _coolDownUntilUtc)
            {
                logger.Warn("RiskManager.ValidateOrder", 
                    $"ORDER REJECTED: Consecutive loss cooldown active until {_coolDownUntilUtc:HH:mm:ss} UTC.",
                    drawdownPct: account.DrawdownPct);
                return false;
            }

            return true;
        }

        public void RecordTradeOutcome(double realizedPnlGbp, TelemetryLogger logger)
        {
            if (realizedPnlGbp < 0)
            {
                _consecutiveLosses++;
                if (_consecutiveLosses >= 3)
                {
                    _coolDownUntilUtc = DateTime.UtcNow.AddMinutes(60);
                    logger.Warn("RiskManager.RecordTradeOutcome", 
                        $"3 consecutive losing trades detected. Initiating 60-minute automated cooling-off period until {_coolDownUntilUtc:HH:mm:ss} UTC.");
                    _consecutiveLosses = 0;
                }
            }
            else
            {
                _consecutiveLosses = 0;
            }
        }
    }
}
