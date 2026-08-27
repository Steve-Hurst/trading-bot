using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Broker;
using Broker.Pepperstone;
using Broker.Simulated;
using Config;
using Core.Models;
using Logging;
using Risk;
using Strategies;

namespace Service
{
    public class BotEngine
    {
        public bool IsRunning { get; private set; }
        public string BrokerName => _broker?.BrokerName ?? "Unassigned";
        public string TargetSymbol => _strategy.TargetSymbol;

        private readonly TelemetryLogger _logger;
        private readonly IStrategy _strategy;
        private readonly RiskManager _riskManager;
        private ITradingBroker _broker;
        private readonly Stopwatch _uptimeSw = new();
        private readonly bool _useLiveBroker;
        private long _tickCount = 0;
        private TickData? _lastTick;

        public BotEngine(TelemetryLogger logger, bool useLiveBroker = false)
        {
            _logger = logger;
            _useLiveBroker = useLiveBroker;
            _strategy = new EURUSDMeanReversionStrategy();
            _riskManager = new RiskManager();

            if (_useLiveBroker)
            {
                _broker = new PepperstoneOpenApiBroker(_logger);
            }
            else
            {
                _broker = new SimulatedPepperstoneBroker();
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.Info("BotEngine.StartAsync", 
                $"Initializing {BuildInfo.AppName} v{BuildInfo.Version} on symbol {TargetSymbol} (Broker: {_broker.BrokerName})");

            bool connected = await _broker.ConnectAsync();
            if (!connected && _useLiveBroker)
            {
                _logger.Warn("BotEngine.StartAsync", "Failed to connect to Live Pepperstone API. Falling back to Simulated Broker sandbox.");
                _broker = new SimulatedPepperstoneBroker();
                await _broker.ConnectAsync();
            }

            _broker.OnTickReceived += HandleTick;
            _broker.OnBarReceived += HandleBar;

            IsRunning = true;
            _uptimeSw.Start();

            _logger.Info("BotEngine.StartAsync", 
                $"AutoTrader Bot 01 actively running. Capital: £500, MaxDD: {RiskManager.MaxDrawdownPct}%, Micro-lot: {RiskManager.MaxPositionLotSize}");

            // Periodic metrics recording loop (every 30 seconds)
            int snapshotTimer = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
                snapshotTimer += 5;
                if (snapshotTimer >= 30 && IsRunning)
                {
                    snapshotTimer = 0;
                    try
                    {
                        var account = await _broker.GetAccountSummaryAsync();
                        var positions = await _broker.GetOpenPositionsAsync();
                        await _logger.RecordBotMetricsAsync(
                            account,
                            positions.Count,
                            avgLatency: 8.5,
                            avgSpread: _lastTick?.SpreadPips ?? 0.8,
                            avgSlippage: 0.05,
                            totalTicks: _tickCount,
                            indicatorState: new { symbol = TargetSymbol, last_bid = _lastTick?.Bid, last_ask = _lastTick?.Ask }
                        );
                    }
                    catch { }
                }
            }

            await StopAsync();
        }

        public async Task StopAsync()
        {
            IsRunning = false;
            _uptimeSw.Stop();
            _logger.Info("BotEngine.StopAsync", "Shutting down trading bot engine gracefully...");
            
            if (_broker != null)
            {
                _broker.OnTickReceived -= HandleTick;
                _broker.OnBarReceived -= HandleBar;
                await _broker.DisconnectAsync();
            }
        }

        public void Pause()
        {
            IsRunning = false;
            _logger.Warn("BotEngine.Pause", "Trading bot paused by operator command.");
        }

        public void Resume()
        {
            IsRunning = true;
            _logger.Info("BotEngine.Resume", "Trading bot resumed by operator command.");
        }

        public async Task<int> EmergencyCloseAllAsync()
        {
            _logger.Warn("BotEngine.EmergencyCloseAllAsync", "EMERGENCY STOP TRIGGERED: Closing all active positions!");
            var positions = await _broker.GetOpenPositionsAsync();
            int closed = 0;
            foreach (var pos in positions)
            {
                if (await _broker.ClosePositionAsync(pos.PositionId))
                {
                    closed++;
                }
            }
            return closed;
        }

        public async Task<object> GetLiveStatusAsync()
        {
            var account = await _broker.GetAccountSummaryAsync();
            var openPositions = await _broker.GetOpenPositionsAsync();

            return new
            {
                is_running = IsRunning,
                uptime_seconds = (long)_uptimeSw.Elapsed.TotalSeconds,
                total_ticks_processed = _tickCount,
                last_bid = _lastTick?.Bid ?? 0,
                last_ask = _lastTick?.Ask ?? 0,
                spread_pips = _lastTick?.SpreadPips ?? 0,
                account_balance_gbp = account.Balance,
                account_equity_gbp = account.Equity,
                used_margin_gbp = account.UsedMargin,
                free_margin_gbp = account.FreeMargin,
                margin_level_pct = account.MarginLevelPct,
                drawdown_pct = account.DrawdownPct,
                daily_realized_pnl_gbp = account.DailyRealizedPnL,
                total_trades_today = account.TotalTradesToday,
                winning_trades = account.WinningTradesToday,
                losing_trades = account.LosingTradesToday,
                win_rate_pct = account.WinRatePct,
                open_positions_count = openPositions.Count,
                open_positions = openPositions.Select(p => new
                {
                    position_id = p.PositionId,
                    symbol = p.Symbol,
                    side = p.Side.ToString(),
                    lots = p.VolumeLots,
                    entry_price = p.EntryPrice,
                    current_price = p.CurrentPrice,
                    unrealized_pnl_gbp = p.UnrealizedPnL,
                    stop_loss = p.StopLoss,
                    take_profit = p.TakeProfit
                })
            };
        }

        private void HandleTick(TickData tick)
        {
            if (!IsRunning) return;
            Interlocked.Increment(ref _tickCount);
            _lastTick = tick;

            _ = Task.Run(async () =>
            {
                try
                {
                    var account = await _broker.GetAccountSummaryAsync();
                    await _strategy.OnTickAsync(tick, account, _broker, _logger);
                }
                catch (Exception ex)
                {
                    _logger.Error("BotEngine.HandleTick", "Exception processing market tick", ex);
                }
            });
        }

        private void HandleBar(BarData bar)
        {
            if (!IsRunning) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var account = await _broker.GetAccountSummaryAsync();
                    
                    // Enforce Risk Guardrails
                    if (account.DrawdownPct >= RiskManager.MaxDrawdownPct)
                    {
                        _logger.Warn("BotEngine.HandleBar", 
                            $"CRITICAL: Drawdown reached {account.DrawdownPct:F2}%. Halting new entries.");
                        return;
                    }

                    await _strategy.OnBarAsync(bar, account, _broker, _logger);
                }
                catch (Exception ex)
                {
                    _logger.Error("BotEngine.HandleBar", "Exception evaluating market bar signal", ex);
                }
            });
        }
    }
}
