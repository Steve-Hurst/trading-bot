using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Broker;
using Core.Models;
using Logging;
using Risk;

namespace Strategies
{
    public class EURUSDMeanReversionStrategy : IStrategy
    {
        public string StrategyName => "EURUSD_MeanReversion_RSI_BB";
        public string TargetSymbol => "CS.D.EURUSD.TODAY.IP";
        public string DisplaySymbol => "EURUSD";

        private readonly RiskManager _riskManager = new();
        private readonly List<double> _closePrices = new();
        private readonly int _bbPeriod = 20;
        private readonly double _bbStdDev = 2.0;
        private readonly int _rsiPeriod = 14;

        private double _lastRsi = 50.0;
        private double _upperBand = 0.0;
        private double _lowerBand = 0.0;
        private double _middleBand = 0.0;
        private DateTime _lastTradeTime = DateTime.MinValue;

        public async Task OnTickAsync(TickData tick, AccountSummary account, ITradingBroker broker, TelemetryLogger logger)
        {
            // Spread filter check
            if (tick.SpreadPips > RiskManager.MaxAllowedSpreadPips)
            {
                return;
            }

            // Prevent rapid multi-entry triggers (cooldown 60s)
            if ((DateTime.UtcNow - _lastTradeTime).TotalSeconds < 60)
            {
                return;
            }

            // Mean reversion trigger conditions (Oversold < 30 / Overbought > 70 with Bollinger penetration)
            if (_lowerBand > 0 && tick.Bid <= _lowerBand && _lastRsi < 30.0)
            {
                if (_riskManager.CanOpenPosition(account, tick, RiskManager.BaseStakePerPoint))
                {
                    _lastTradeTime = DateTime.UtcNow;
                    var (sl, tp) = _riskManager.CalculateBracketLevels(OrderSide.Buy, tick.Ask, 12.0, 20.0);

                    logger.Info("EURUSDMeanReversionStrategy.OnTickAsync", 
                        $"BUY Trigger on {DisplaySymbol}: Bid {tick.Bid:F5} <= LowerBand {_lowerBand:F5}, RSI {_lastRsi:F1} < 30.0",
                        new { bid = tick.Bid, lower_band = _lowerBand, rsi = _lastRsi, sl, tp }, DisplaySymbol);

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
                        StopDistancePoints = 12.0,
                        ProfitDistancePoints = 20.0,
                        CurrencyCode = "GBP",
                        StrategyFunction = "EURUSDMeanReversionStrategy.OnTickAsync"
                    };

                    await broker.ExecuteOrderAsync(req);
                }
            }
            else if (_upperBand > 0 && tick.Ask >= _upperBand && _lastRsi > 70.0)
            {
                if (_riskManager.CanOpenPosition(account, tick, RiskManager.BaseStakePerPoint))
                {
                    _lastTradeTime = DateTime.UtcNow;
                    var (sl, tp) = _riskManager.CalculateBracketLevels(OrderSide.Sell, tick.Bid, 12.0, 20.0);

                    logger.Info("EURUSDMeanReversionStrategy.OnTickAsync", 
                        $"SELL Trigger on {DisplaySymbol}: Ask {tick.Ask:F5} >= UpperBand {_upperBand:F5}, RSI {_lastRsi:F1} > 70.0",
                        new { ask = tick.Ask, upper_band = _upperBand, rsi = _lastRsi, sl, tp }, DisplaySymbol);

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
                        StopDistancePoints = 12.0,
                        ProfitDistancePoints = 20.0,
                        CurrencyCode = "GBP",
                        StrategyFunction = "EURUSDMeanReversionStrategy.OnTickAsync"
                    };

                    await broker.ExecuteOrderAsync(req);
                }
            }
        }

        public Task OnBarAsync(BarData bar, AccountSummary account, ITradingBroker broker, TelemetryLogger logger)
        {
            _closePrices.Add(bar.Close);
            if (_closePrices.Count > 200) _closePrices.RemoveAt(0);

            if (_closePrices.Count >= _bbPeriod)
            {
                CalculateBollingerBands();
            }

            if (_closePrices.Count >= _rsiPeriod + 1)
            {
                _lastRsi = CalculateRsi();
            }

            return Task.CompletedTask;
        }

        private void CalculateBollingerBands()
        {
            var slice = _closePrices.Skip(Math.Max(0, _closePrices.Count - _bbPeriod)).Take(_bbPeriod).ToList();
            _middleBand = slice.Average();
            double variance = slice.Select(p => Math.Pow(p - _middleBand, 2)).Average();
            double stdDev = Math.Sqrt(variance);

            _upperBand = _middleBand + (_bbStdDev * stdDev);
            _lowerBand = _middleBand - (_bbStdDev * stdDev);
        }

        private double CalculateRsi()
        {
            int n = _rsiPeriod;
            if (_closePrices.Count < n + 1) return 50.0;

            double gains = 0.0;
            double losses = 0.0;

            for (int i = _closePrices.Count - n; i < _closePrices.Count; i++)
            {
                double change = _closePrices[i] - _closePrices[i - 1];
                if (change > 0) gains += change;
                else losses += Math.Abs(change);
            }

            if (losses == 0.0) return 100.0;
            double rs = (gains / n) / (losses / n);
            return 100.0 - (100.0 / (1.0 + rs));
        }
    }
}
