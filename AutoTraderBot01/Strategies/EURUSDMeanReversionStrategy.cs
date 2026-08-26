using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Broker;
using Core.Models;
using Logging;

namespace Strategies
{
    public class EURUSDMeanReversionStrategy : IStrategy
    {
        public string StrategyName => "EURUSD_MeanReversion_RSI_BB";
        public string TargetSymbol => "EURUSD";

        // Strategy Parameters
        public int BollingerPeriod { get; set; } = 20;
        public double BollingerMultiplier { get; set; } = 2.0;
        public int RsiPeriod { get; set; } = 14;
        public double RsiOversold { get; set; } = 30.0;
        public double RsiOverbought { get; set; } = 70.0;
        public double MaxSpreadPips { get; set; } = 1.0;
        public double LotSize { get; set; } = 0.01;
        public double StopLossPips { get; set; } = 12.0;
        public double TakeProfitPips { get; set; } = 20.0;

        // Internal indicator buffers
        private readonly List<double> _closePrices = new();
        private readonly List<double> _rsiGains = new();
        private readonly List<double> _rsiLosses = new();
        private double? _lastRsi;
        private double? _upperBb;
        private double? _middleBb;
        private double? _lowerBb;
        private DateTime _lastOrderTime = DateTime.MinValue;

        public Task OnTickAsync(TickData tick, AccountSummary account, ITradingBroker broker, TelemetryLogger logger)
        {
            // Ticks update real-time spread tracking
            return Task.CompletedTask;
        }

        public async Task OnBarAsync(BarData bar, AccountSummary account, ITradingBroker broker, TelemetryLogger logger)
        {
            if (bar.Symbol != TargetSymbol) return;

            // 1. Maintain Price History Buffer
            _closePrices.Add(bar.Close);
            if (_closePrices.Count > 200)
            {
                _closePrices.RemoveAt(0);
            }

            if (_closePrices.Count < Math.Max(BollingerPeriod, RsiPeriod + 1))
            {
                logger.Info("EURUSDMeanReversionStrategy.OnBarAsync", 
                    $"Warming up indicators. Collected {_closePrices.Count}/{Math.Max(BollingerPeriod, RsiPeriod + 1)} bars.", 
                    drawdownPct: account.DrawdownPct);
                return;
            }

            // 2. Compute Technical Indicators
            ComputeBollingerBands();
            ComputeRsi();

            if (!_upperBb.HasValue || !_lowerBb.HasValue || !_lastRsi.HasValue) return;

            // 3. Time / Session Filter (London/NY overlap: 07:00 - 19:00 GMT)
            int hourUtc = bar.TimestampUtc.Hour;
            bool isSessionActive = hourUtc >= 7 && hourUtc <= 19;
            if (!isSessionActive)
            {
                return;
            }

            // 4. Rate Limiting: Minimum 5 minutes between order executions
            if ((DateTime.UtcNow - _lastOrderTime).TotalMinutes < 5)
            {
                return;
            }

            // 5. Existing Open Positions Check (Max 1 concurrent position for this bot)
            var openPositions = await broker.GetOpenPositionsAsync();
            if (openPositions.Any(p => p.Symbol == TargetSymbol && p.Status == PositionStatus.Open))
            {
                return;
            }

            // 6. Signal Evaluation
            double currentPrice = bar.Close;

            // BUY Signal: Price <= Lower BB AND RSI < 30 (Oversold mean-reversion)
            if (currentPrice <= _lowerBb.Value && _lastRsi.Value <= RsiOversold)
            {
                double slPrice = Math.Round(currentPrice - (StopLossPips * 0.00010), 5);
                double tpPrice = Math.Round(currentPrice + (TakeProfitPips * 0.00010), 5);

                var orderReq = new OrderRequest
                {
                    Symbol = TargetSymbol,
                    Side = OrderSide.Buy,
                    VolumeLots = LotSize,
                    RequestedPrice = currentPrice,
                    StopLossPrice = slPrice,
                    TakeProfitPrice = tpPrice,
                    StrategyFunction = $"{StrategyName}.ExecuteBuyEntry"
                };

                logger.Info(orderReq.StrategyFunction, 
                    $"BUY Signal Triggered @ {currentPrice:F5} (RSI: {_lastRsi.Value:F1}, LowerBB: {_lowerBb.Value:F5}, SL: {slPrice:F5}, TP: {tpPrice:F5})", 
                    drawdownPct: account.DrawdownPct,
                    extra: new { rsi = _lastRsi.Value, lower_bb = _lowerBb.Value, upper_bb = _upperBb.Value });

                var result = await broker.ExecuteOrderAsync(orderReq);
                _lastOrderTime = DateTime.UtcNow;

                logger.Info(orderReq.StrategyFunction, 
                    $"BUY Order Executed: {result.Message}", 
                    latencyMs: result.LatencyMs, 
                    slippagePips: result.SlippagePips, 
                    drawdownPct: account.DrawdownPct, 
                    executionId: result.ExecutionId);
            }
            // SELL Signal: Price >= Upper BB AND RSI > 70 (Overbought mean-reversion)
            else if (currentPrice >= _upperBb.Value && _lastRsi.Value >= RsiOverbought)
            {
                double slPrice = Math.Round(currentPrice + (StopLossPips * 0.00010), 5);
                double tpPrice = Math.Round(currentPrice - (TakeProfitPips * 0.00010), 5);

                var orderReq = new OrderRequest
                {
                    Symbol = TargetSymbol,
                    Side = OrderSide.Sell,
                    VolumeLots = LotSize,
                    RequestedPrice = currentPrice,
                    StopLossPrice = slPrice,
                    TakeProfitPrice = tpPrice,
                    StrategyFunction = $"{StrategyName}.ExecuteSellEntry"
                };

                logger.Info(orderReq.StrategyFunction, 
                    $"SELL Signal Triggered @ {currentPrice:F5} (RSI: {_lastRsi.Value:F1}, UpperBB: {_upperBb.Value:F5}, SL: {slPrice:F5}, TP: {tpPrice:F5})", 
                    drawdownPct: account.DrawdownPct,
                    extra: new { rsi = _lastRsi.Value, lower_bb = _lowerBb.Value, upper_bb = _upperBb.Value });

                var result = await broker.ExecuteOrderAsync(orderReq);
                _lastOrderTime = DateTime.UtcNow;

                logger.Info(orderReq.StrategyFunction, 
                    $"SELL Order Executed: {result.Message}", 
                    latencyMs: result.LatencyMs, 
                    slippagePips: result.SlippagePips, 
                    drawdownPct: account.DrawdownPct, 
                    executionId: result.ExecutionId);
            }
        }

        private void ComputeBollingerBands()
        {
            if (_closePrices.Count < BollingerPeriod) return;

            var slice = _closePrices.Skip(_closePrices.Count - BollingerPeriod).Take(BollingerPeriod).ToList();
            double sma = slice.Average();
            double sumSquares = slice.Sum(p => Math.Pow(p - sma, 2));
            double stdDev = Math.Sqrt(sumSquares / BollingerPeriod);

            _middleBb = Math.Round(sma, 5);
            _upperBb = Math.Round(sma + (BollingerMultiplier * stdDev), 5);
            _lowerBb = Math.Round(sma - (BollingerMultiplier * stdDev), 5);
        }

        private void ComputeRsi()
        {
            if (_closePrices.Count <= RsiPeriod) return;

            double avgGain = 0;
            double avgLoss = 0;

            for (int i = _closePrices.Count - RsiPeriod; i < _closePrices.Count; i++)
            {
                double change = _closePrices[i] - _closePrices[i - 1];
                if (change >= 0) avgGain += change;
                else avgLoss += Math.Abs(change);
            }

            avgGain /= RsiPeriod;
            avgLoss /= RsiPeriod;

            if (avgLoss == 0)
            {
                _lastRsi = 100.0;
            }
            else
            {
                double rs = avgGain / avgLoss;
                _lastRsi = Math.Round(100.0 - (100.0 / (1.0 + rs)), 2);
            }
        }
    }
}
