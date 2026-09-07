using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Core.Models;

namespace Broker.Simulated
{
    public class SimulatedIgBroker : ITradingBroker
    {
        public string BrokerName => "Simulated_IG_SpreadBet";
        public bool IsConnected { get; private set; }

        public event Action<TickData>? OnTickReceived;
        public event Action<BarData>? OnBarReceived;
        public void TriggerBar(BarData bar) => OnBarReceived?.Invoke(bar);

        private readonly Random _rand = new(42);
        private double _currentBid = 1.08500;
        private double _spread = 0.00008; // 0.8 pips
        private CancellationTokenSource? _simCts;
        private readonly Dictionary<string, Position> _positions = new();
        private double _balance = 500.0;
        private double _equity = 500.0;

        public Task<bool> ConnectAsync()
        {
            IsConnected = true;
            _simCts = new CancellationTokenSource();
            _ = Task.Run(StartTickSimulation);
            return Task.FromResult(true);
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            _simCts?.Cancel();
            return Task.CompletedTask;
        }

        public Task<AccountSummary> GetAccountSummaryAsync()
        {
            double usedMargin = _positions.Count * 28.0; // ~£28 margin per £0.10/pt stake
            return Task.FromResult(new AccountSummary
            {
                Balance = _balance,
                Equity = _equity,
                InitialCapital = 500.0,
                UsedMargin = usedMargin
            });
        }

        public Task<IReadOnlyList<Position>> GetOpenPositionsAsync()
        {
            lock (_positions)
            {
                return Task.FromResult<IReadOnlyList<Position>>(new List<Position>(_positions.Values));
            }
        }

        public Task<OrderResult> ExecuteOrderAsync(OrderRequest request)
        {
            string dealId = $"IG-SIM-{Guid.NewGuid():N}"[..12];
            var pos = new Position
            {
                DealId = dealId,
                PositionId = DateTime.UtcNow.Ticks,
                Symbol = request.Symbol,
                DisplaySymbol = request.DisplaySymbol,
                Side = request.Side,
                SizeStake = request.SizeStake,
                EntryPrice = request.RequestedPrice > 0 ? request.RequestedPrice : _currentBid,
                CurrentPrice = _currentBid,
                StopLoss = request.StopLossPrice,
                TakeProfit = request.TakeProfitPrice,
                Status = PositionStatus.Open
            };

            lock (_positions)
            {
                _positions[dealId] = pos;
            }

            return Task.FromResult(new OrderResult
            {
                Success = true,
                OrderId = dealId,
                DealReference = dealId,
                ExecutionId = $"EXEC-{DateTime.UtcNow:yyyyMMddHHmmss}",
                RequestedPrice = pos.EntryPrice,
                FilledPrice = pos.EntryPrice,
                SlippagePips = 0.0,
                LatencyMs = 4.2,
                Message = "Simulated IG spread bet placed successfully"
            });
        }

        public Task<bool> ClosePositionAsync(long positionId)
        {
            return ClosePositionByDealIdAsync(positionId.ToString());
        }

        public Task<bool> ClosePositionByDealIdAsync(string dealId)
        {
            lock (_positions)
            {
                return Task.FromResult(_positions.Remove(dealId));
            }
        }

        public Task<bool> ModifyPositionAsync(string dealId, double? stopLoss, double? takeProfit)
        {
            lock (_positions)
            {
                if (_positions.TryGetValue(dealId, out var pos))
                {
                    pos.StopLoss = stopLoss;
                    pos.TakeProfit = takeProfit;
                    return Task.FromResult(true);
                }
            }
            return Task.FromResult(false);
        }

        private async Task StartTickSimulation()
        {
            while (IsConnected && _simCts != null && !_simCts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, _simCts.Token).ConfigureAwait(false);

                double delta = (_rand.NextDouble() - 0.499) * 0.00010;
                _currentBid += delta;
                double ask = _currentBid + _spread;

                var tick = new TickData
                {
                    Symbol = "CS.D.EURUSD.TODAY.IP",
                    DisplaySymbol = "EURUSD",
                    Bid = Math.Round(_currentBid, 5),
                    Ask = Math.Round(ask, 5),
                    TimestampUtc = DateTime.UtcNow
                };

                OnTickReceived?.Invoke(tick);
            }
        }
    }
}
