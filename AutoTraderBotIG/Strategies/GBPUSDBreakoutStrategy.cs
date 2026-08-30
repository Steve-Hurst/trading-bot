using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Broker;
using Core.Models;
using Logging;
using Risk;

namespace Strategies
{
    public class GBPUSDBreakoutStrategy : IStrategy
    {
        public string StrategyName => "GBPUSD_LondonOpen_Breakout";
        public string TargetSymbol => "CS.D.GBPUSD.TODAY.IP";
        public string DisplaySymbol => "GBPUSD";

        private readonly RiskManager _riskManager = new();
        private double _asianHigh = 0.0;
        private double _asianLow = 0.0;
        private bool _channelEstablished = false;
        private DateTime _lastTradeDate = DateTime.MinValue;

        public async Task OnTickAsync(TickData tick, AccountSummary account, ITradingBroker broker, TelemetryLogger logger)
        {
            if (tick.SpreadPips > RiskManager.MaxAllowedSpreadPips) return;

            DateTime now = DateTime.UtcNow;

            // Only trade London morning session (08:00 - 12:00 UTC)
            if (now.Hour >= 8 && now.Hour < 12 && _channelEstablished && _lastTradeDate.Date != now.Date)
            {
                if (tick.Ask > _asianHigh && _asianHigh > 0)
                {
                    if (_riskManager.CanOpenPosition(account, tick, RiskManager.BaseStakePerPoint))
                    {
                        _lastTradeDate = now;
                        var (sl, tp) = _riskManager.CalculateBracketLevels(OrderSide.Buy, tick.Ask, 15.0, 30.0);

                        logger.Info("GBPUSDBreakoutStrategy.OnTickAsync", 
                            $"London Breakout BUY: Ask {tick.Ask:F5} broke Asian High {_asianHigh:F5}",
                            new { ask = tick.Ask, asian_high = _asianHigh, sl, tp }, DisplaySymbol);

                        var req = new OrderRequest
                        {
                            Symbol = TargetSymbol,
                            DisplaySymbol = DisplaySymbol,
                            Side = OrderSide.Buy,
                            Type = OrderType.Market,
                            SizeStake = RiskManager.BaseStakePerPoint,
                            RequestedPrice = tick.Ask,
                            StopLossPrice = sl,
                            TakeProfitPrice = tp,
                            StopDistancePoints = 15.0,
                            ProfitDistancePoints = 30.0,
                            CurrencyCode = "GBP",
                            StrategyFunction = "GBPUSDBreakoutStrategy.OnTickAsync"
                        };

                        await broker.ExecuteOrderAsync(req);
                    }
                }
                else if (tick.Bid < _asianLow && _asianLow > 0)
                {
                    if (_riskManager.CanOpenPosition(account, tick, RiskManager.BaseStakePerPoint))
                    {
                        _lastTradeDate = now;
                        var (sl, tp) = _riskManager.CalculateBracketLevels(OrderSide.Sell, tick.Bid, 15.0, 30.0);

                        logger.Info("GBPUSDBreakoutStrategy.OnTickAsync", 
                            $"London Breakout SELL: Bid {tick.Bid:F5} broke Asian Low {_asianLow:F5}",
                            new { bid = tick.Bid, asian_low = _asianLow, sl, tp }, DisplaySymbol);

                        var req = new OrderRequest
                        {
                            Symbol = TargetSymbol,
                            DisplaySymbol = DisplaySymbol,
                            Side = OrderSide.Sell,
                            Type = OrderType.Market,
                            SizeStake = RiskManager.BaseStakePerPoint,
                            RequestedPrice = tick.Bid,
                            StopLossPrice = sl,
                            TakeProfitPrice = tp,
                            StopDistancePoints = 15.0,
                            ProfitDistancePoints = 30.0,
                            CurrencyCode = "GBP",
                            StrategyFunction = "GBPUSDBreakoutStrategy.OnTickAsync"
                        };

                        await broker.ExecuteOrderAsync(req);
                    }
                }
            }
        }

        public Task OnBarAsync(BarData bar, AccountSummary account, ITradingBroker broker, TelemetryLogger logger)
        {
            DateTime dt = bar.TimestampUtc;

            // Asian Session tracking (00:00 to 07:00 UTC)
            if (dt.Hour == 0 && dt.Minute == 0)
            {
                _asianHigh = bar.High;
                _asianLow = bar.Low;
                _channelEstablished = false;
            }
            else if (dt.Hour < 7)
            {
                if (bar.High > _asianHigh) _asianHigh = bar.High;
                if (bar.Low < _asianLow) _asianLow = bar.Low;
            }
            else if (dt.Hour == 7)
            {
                _channelEstablished = _asianHigh > 0 && _asianLow > 0;
            }

            return Task.CompletedTask;
        }
    }
}
