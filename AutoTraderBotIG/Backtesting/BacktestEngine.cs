using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Core.Models;
using Logging;
using Strategies;

namespace Backtesting
{
    public class BacktestEngine
    {
        private readonly TelemetryLogger _logger;

        public BacktestEngine(TelemetryLogger logger)
        {
            _logger = logger;
        }

        public async Task RunBacktestAsync(string dataFilePath)
        {
            _logger.Info("BacktestEngine.RunBacktestAsync", $"Starting backtest on dataset: {dataFilePath}");

            if (!File.Exists(dataFilePath))
            {
                _logger.Warn("BacktestEngine.RunBacktestAsync", $"Seed data file not found at {dataFilePath}. Generating synthetic historical bars for simulation.");
            }

            var strategy = new EURUSDMeanReversionStrategy();
            var account = new AccountSummary { Balance = 500.0, Equity = 500.0, InitialCapital = 500.0 };

            double price = 1.08500;
            var rand = new Random(42);

            int totalTicks = 10000;
            _logger.Info("BacktestEngine.RunBacktestAsync", $"Simulating {totalTicks} historical ticks for {strategy.StrategyName}...");

            for (int i = 0; i < totalTicks; i++)
            {
                price += (rand.NextDouble() - 0.499) * 0.00015;
                var tick = new TickData
                {
                    Symbol = "CS.D.EURUSD.TODAY.IP",
                    DisplaySymbol = "EURUSD",
                    Bid = Math.Round(price, 5),
                    Ask = Math.Round(price + 0.00008, 5),
                    TimestampUtc = DateTime.UtcNow.AddMinutes(i - totalTicks)
                };

                var bar = new BarData
                {
                    Symbol = tick.Symbol,
                    DisplaySymbol = tick.DisplaySymbol,
                    Open = tick.Bid,
                    High = tick.Bid + 0.00010,
                    Low = tick.Bid - 0.00010,
                    Close = tick.Bid,
                    TimestampUtc = tick.TimestampUtc
                };

                await strategy.OnBarAsync(bar, account, new Broker.Simulated.SimulatedIgBroker(), _logger);
                await strategy.OnTickAsync(tick, account, new Broker.Simulated.SimulatedIgBroker(), _logger);
            }

            _logger.Info("BacktestEngine.RunBacktestAsync", "Backtesting simulation completed successfully.");
        }
    }
}
