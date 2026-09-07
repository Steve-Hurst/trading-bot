using System;
using Core.Models;

namespace Risk
{
    public class RiskManager
    {
        public const double InitialCapital = 500.0;
        public const double MaxDrawdownPct = 5.0; // £25 hard drawdown ceiling
        public const double BaseStakePerPoint = 0.10; // £0.10 / pt
        public const double MaxAllowedSpreadPips = 1.0;
        public const double MaxMarginUtilizationPct = 65.0;

        public bool CanOpenPosition(AccountSummary account, TickData tick, double requestedStake)
        {
            // 1. Hard Drawdown Guardrail
            if (account.DrawdownPct >= MaxDrawdownPct)
            {
                return false;
            }

            // 2. Spread Filter Guardrail
            if (tick.SpreadPips > MaxAllowedSpreadPips)
            {
                return false;
            }

            // 3. Margin Utilization Guardrail
            if (account.UsedMargin > 0 && (account.UsedMargin / account.Balance) * 100 > MaxMarginUtilizationPct)
            {
                return false;
            }

            // 4. Stake Sizing Guardrail (£0.10/point max on £500 equity)
            if (requestedStake > BaseStakePerPoint)
            {
                return false;
            }

            return true;
        }

        public (double stopLoss, double takeProfit) CalculateBracketLevels(OrderSide side, double entryPrice, double stopPips = 12.0, double takeProfitPips = 20.0)
        {
            double stopOffset = stopPips * 0.00010;
            double tpOffset = takeProfitPips * 0.00010;

            if (side == OrderSide.Buy)
            {
                return (Math.Round(entryPrice - stopOffset, 5), Math.Round(entryPrice + tpOffset, 5));
            }
            else
            {
                return (Math.Round(entryPrice + stopOffset, 5), Math.Round(entryPrice - tpOffset, 5));
            }
        }
    }
}
