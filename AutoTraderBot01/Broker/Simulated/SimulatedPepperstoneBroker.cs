using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Models;

namespace Broker.Simulated
{
    public class SimulatedPepperstoneBroker : ITradingBroker
    {
        public string BrokerName => "Pepperstone_Simulated";
        public bool IsConnected { get; private set; }

        public event Action<TickData>? OnTickReceived;
        public event Action<BarData>? OnBarReceived;

        private readonly ConcurrentDictionary<long, Position> _positions = new();
        private readonly AccountSummary _account = new AccountSummary
        {
            InitialCapital = 500.0,
            Balance = 500.0,
            Equity = 500.0,
            PeakEquity = 500.0
        };

        private double _currentBid = 1.08500;
        private double _currentAsk = 1.08507;
        private long _positionCounter = 1000;
        private readonly Random _random = new();
        private CancellationTokenSource? _tickLoopCts;

        // Bar aggregation
        private DateTime _currentBarMinute = DateTime.MinValue;
        private double _barOpen, _barHigh, _barLow, _barClose, _barVolume;

        public Task<bool> ConnectAsync()
        {
            IsConnected = true;
            _tickLoopCts = new CancellationTokenSource();
            Task.Run(() => GenerateMarketTicksAsync(_tickLoopCts.Token));
            return Task.FromResult(true);
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            _tickLoopCts?.Cancel();
            return Task.CompletedTask;
        }

        public Task<AccountSummary> GetAccountSummaryAsync()
        {
            UpdateAccountMetrics();
            return Task.FromResult(_account);
        }

        public Task<IReadOnlyList<Position>> GetOpenPositionsAsync()
        {
            return Task.FromResult<IReadOnlyList<Position>>(_positions.Values.Where(p => p.Status == PositionStatus.Open).ToList());
        }

        public async Task<OrderResult> ExecuteOrderAsync(OrderRequest request)
        {
            var sw = Stopwatch.StartNew();

            // Simulate realistic London LD4 server network execution latency (4ms - 12ms)
            int simulatedDelay = _random.Next(4, 12);
            await Task.Delay(simulatedDelay);

            // Compute slippage (-0.1 to +0.2 pips)
            double slippagePips = (_random.NextDouble() * 0.3) - 0.1;
            double filledPrice = request.Side == OrderSide.Buy 
                ? _currentAsk + (slippagePips * 0.0001)
                : _currentBid - (slippagePips * 0.0001);

            filledPrice = Math.Round(filledPrice, 5);

            long posId = Interlocked.Increment(ref _positionCounter);
            var position = new Position
            {
                PositionId = posId,
                Symbol = request.Symbol,
                Side = request.Side,
                VolumeLots = request.VolumeLots,
                EntryPrice = filledPrice,
                CurrentPrice = filledPrice,
                StopLoss = request.StopLossPrice,
                TakeProfit = request.TakeProfitPrice,
                Status = PositionStatus.Open,
                EntryTimeUtc = DateTime.UtcNow
            };

            _positions[posId] = position;
            UpdateAccountMetrics();

            sw.Stop();

            return new OrderResult
            {
                Success = true,
                OrderId = $"ORD-{posId}",
                ExecutionId = $"EXEC-PEP-{posId}-{DateTime.UtcNow:HHmmss}",
                RequestedPrice = request.RequestedPrice,
                FilledPrice = filledPrice,
                SlippagePips = Math.Round(slippagePips, 2),
                LatencyMs = sw.Elapsed.TotalMilliseconds,
                Message = $"Order filled on Pepperstone simulated matching engine at {filledPrice:F5}"
            };
        }

        public Task<bool> ClosePositionAsync(long positionId)
        {
            if (_positions.TryGetValue(positionId, out var pos) && pos.Status == PositionStatus.Open)
            {
                pos.Status = PositionStatus.Closed;
                double exitPrice = pos.Side == OrderSide.Buy ? _currentBid : _currentAsk;
                double pipDifference = pos.Side == OrderSide.Buy 
                    ? (exitPrice - pos.EntryPrice) * 10000 
                    : (pos.EntryPrice - exitPrice) * 10000;

                // 0.01 lot EURUSD = $0.10 per pip ~ £0.076 per pip
                double realizedPnlGbp = Math.Round(pipDifference * 0.076, 2);

                _account.Balance += realizedPnlGbp;
                _account.DailyRealizedPnL += realizedPnlGbp;
                _account.TotalTradesToday++;

                if (realizedPnlGbp >= 0) _account.WinningTradesToday++;
                else _account.LosingTradesToday++;

                UpdateAccountMetrics();
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> ModifyPositionAsync(long positionId, double? stopLoss, double? takeProfit)
        {
            if (_positions.TryGetValue(positionId, out var pos) && pos.Status == PositionStatus.Open)
            {
                pos.StopLoss = stopLoss;
                pos.TakeProfit = takeProfit;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        private async Task GenerateMarketTicksAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // Generate tick walk (mean reversion around baseline)
                double delta = (_random.NextDouble() - 0.499) * 0.00012;
                _currentBid = Math.Round(Math.Max(1.0500, Math.Min(1.1200, _currentBid + delta)), 5);
                
                // Spread oscillates between 0.6 and 0.9 pips
                double spread = Math.Round(0.00006 + (_random.NextDouble() * 0.00003), 5);
                _currentAsk = Math.Round(_currentBid + spread, 5);

                var tick = new TickData
                {
                    Symbol = "EURUSD",
                    Bid = _currentBid,
                    Ask = _currentAsk,
                    TimestampUtc = DateTime.UtcNow
                };

                // Check open positions for StopLoss / TakeProfit hits
                EvaluatePositionExits(tick);

                // Aggregate 1-minute bars
                AggregateBar(tick);

                OnTickReceived?.Invoke(tick);

                // Ticks arrive every 200ms - 800ms
                await Task.Delay(_random.Next(200, 600), token).ConfigureAwait(false);
            }
        }

        private void EvaluatePositionExits(TickData tick)
        {
            foreach (var pos in _positions.Values.Where(p => p.Status == PositionStatus.Open))
            {
                double currentPrice = pos.Side == OrderSide.Buy ? tick.Bid : tick.Ask;
                pos.CurrentPrice = currentPrice;

                double pips = pos.Side == OrderSide.Buy 
                    ? (currentPrice - pos.EntryPrice) * 10000 
                    : (pos.EntryPrice - currentPrice) * 10000;
                
                pos.UnrealizedPnL = Math.Round(pips * 0.076, 2);

                // Stop Loss Hit
                if (pos.StopLoss.HasValue)
                {
                    if ((pos.Side == OrderSide.Buy && currentPrice <= pos.StopLoss.Value) ||
                        (pos.Side == OrderSide.Sell && currentPrice >= pos.StopLoss.Value))
                    {
                        ClosePositionAsync(pos.PositionId);
                        continue;
                    }
                }

                // Take Profit Hit
                if (pos.TakeProfit.HasValue)
                {
                    if ((pos.Side == OrderSide.Buy && currentPrice >= pos.TakeProfit.Value) ||
                        (pos.Side == OrderSide.Sell && currentPrice <= pos.TakeProfit.Value))
                    {
                        ClosePositionAsync(pos.PositionId);
                        continue;
                    }
                }
            }
        }

        private void AggregateBar(TickData tick)
        {
            DateTime tickMinute = new DateTime(tick.TimestampUtc.Year, tick.TimestampUtc.Month, tick.TimestampUtc.Day, 
                                               tick.TimestampUtc.Hour, tick.TimestampUtc.Minute, 0, DateTimeKind.Utc);

            if (_currentBarMinute == DateTime.MinValue)
            {
                _currentBarMinute = tickMinute;
                _barOpen = _barHigh = _barLow = _barClose = tick.Bid;
                _barVolume = 1;
            }
            else if (tickMinute > _currentBarMinute)
            {
                // Emit completed bar
                var bar = new BarData
                {
                    Symbol = tick.Symbol,
                    TimestampUtc = _currentBarMinute,
                    Open = _barOpen,
                    High = _barHigh,
                    Low = _barLow,
                    Close = _barClose,
                    Volume = _barVolume
                };

                OnBarReceived?.Invoke(bar);

                _currentBarMinute = tickMinute;
                _barOpen = _barHigh = _barLow = _barClose = tick.Bid;
                _barVolume = 1;
            }
            else
            {
                _barHigh = Math.Max(_barHigh, tick.Bid);
                _barLow = Math.Min(_barLow, tick.Bid);
                _barClose = tick.Bid;
                _barVolume++;
            }
        }

        private void UpdateAccountMetrics()
        {
            double openPnL = _positions.Values.Where(p => p.Status == PositionStatus.Open).Sum(p => p.UnrealizedPnL);
            int openCount = _positions.Values.Count(p => p.Status == PositionStatus.Open);

            // FCA margin on 0.01 lot EURUSD (1:30 leverage) = ~£28.00 per open position
            _account.UsedMargin = openCount * 28.0;
            _account.Equity = Math.Round(_account.Balance + openPnL, 2);
            if (_account.Equity > _account.PeakEquity)
            {
                _account.PeakEquity = _account.Equity;
            }
        }
    }
}
