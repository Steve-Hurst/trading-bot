using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Config;
using Core.Models;
using Logging;
using Secrets;
using Strategies;

namespace Backtesting
{
    public class BacktestResultSummary
    {
        public string AlgorithmName { get; set; } = BuildInfo.AlgorithmName;
        public string AlgorithmVersion { get; set; } = BuildInfo.Version;
        public string GitCommitSha { get; set; } = BuildInfo.GitCommitSha;
        public string GitBranch { get; set; } = BuildInfo.GitBranch;
        public string GitLabel { get; set; } = BuildInfo.GitLabel;
        public string Exchange { get; set; } = "Pepperstone";
        public string Symbol { get; set; } = "EURUSD";
        public string Timeframe { get; set; } = "M1";
        public string DataSource { get; set; } = "data/seed_data.json";
        public DateTime StartDateUtc { get; set; }
        public DateTime EndDateUtc { get; set; }
        public double InitialCapital { get; set; } = 500.0;
        public double FinalCapital { get; set; }
        public double NetProfitGbp { get; set; }
        public double NetProfitPct { get; set; }
        public double SharpeRatio { get; set; }
        public double SortinoRatio { get; set; }
        public double ProfitFactor { get; set; }
        public double MaxDrawdownGbp { get; set; }
        public double MaxDrawdownPct { get; set; }
        public int TotalTrades { get; set; }
        public int WinningTrades { get; set; }
        public int LosingTrades { get; set; }
        public double WinRatePct { get; set; }
        public double AvgTradePnLGbp { get; set; }
        public int MaxConsecutiveLosses { get; set; }
        public double ExpectancyGbp { get; set; }
        public object? Parameters { get; set; }
        public List<double> EquityCurve { get; set; } = new();
        public List<object> TradeLogs { get; set; } = new();
    }

    public class SeedDataFormat
    {
        public string? schema_version { get; set; }
        public string? dataset_id { get; set; }
        public Dictionary<string, object>? metadata { get; set; }
        public Dictionary<string, object>? strategy_parameters { get; set; }
        public List<SeedBar>? bars { get; set; }
    }

    public class SeedBar
    {
        public string? time { get; set; }
        public double open { get; set; }
        public double high { get; set; }
        public double low { get; set; }
        public double close { get; set; }
        public double volume { get; set; }
    }

    public class BacktestEngine
    {
        private readonly TelemetryLogger _logger;

        public BacktestEngine(TelemetryLogger logger)
        {
            _logger = logger;
        }

        public async Task<BacktestResultSummary> RunBacktestAsync(string dataFilePath)
        {
            if (!File.Exists(dataFilePath))
            {
                // Fallback check in parent or data folder
                string altPath = Path.Combine(AppContext.BaseDirectory, "data", Path.GetFileName(dataFilePath));
                if (File.Exists(altPath)) dataFilePath = altPath;
                else throw new FileNotFoundException($"Seed data file not found at {dataFilePath}");
            }

            string jsonContent = await File.ReadAllTextAsync(dataFilePath);
            var seedData = JsonSerializer.Deserialize<SeedDataFormat>(jsonContent);
            if (seedData?.bars == null || seedData.bars.Count == 0)
            {
                throw new InvalidOperationException("Seed data file contains no bar records.");
            }

            Console.WriteLine($"\n================================================================================");
            Console.WriteLine($" RUNNING BACKTEST ON SEED DATA: {Path.GetFileName(dataFilePath)}");
            Console.WriteLine($" Total Bars: {seedData.bars.Count} | Algorithm: {BuildInfo.AlgorithmName} v{BuildInfo.Version}");
            Console.WriteLine($" Git Release: {BuildInfo.GitLabel} (Commit: {BuildInfo.GitCommitSha[..8]})");
            Console.WriteLine($"================================================================================\n");

            double initialCapital = 500.0;
            double equity = initialCapital;
            double peakEquity = initialCapital;
            double maxDrawdownGbp = 0.0;
            double maxDrawdownPct = 0.0;

            var equityCurve = new List<double> { equity };
            var tradeReturns = new List<double>();
            var tradeLogs = new List<object>();

            int totalTrades = 0;
            int wins = 0;
            int losses = 0;
            double grossProfit = 0.0;
            double grossLoss = 0.0;
            int consecutiveLosses = 0;
            int maxConsecutiveLosses = 0;

            // Strategy indicators buffer
            var strategy = new EURUSDMeanReversionStrategy();
            var closeBuffer = new List<double>();
            Position? activePosition = null;

            DateTime startDate = DateTime.MinValue;
            DateTime endDate = DateTime.MinValue;

            for (int i = 0; i < seedData.bars.Count; i++)
            {
                var barItem = seedData.bars[i];
                DateTime barTime = DateTime.TryParse(barItem.time, out var dt) ? dt : DateTime.UtcNow.AddMinutes(i);
                if (i == 0) startDate = barTime;
                endDate = barTime;

                closeBuffer.Add(barItem.close);

                // Check active position SL/TP
                if (activePosition != null)
                {
                    double exitPrice = 0;
                    bool closed = false;
                    string closeReason = "";

                    if (activePosition.Side == OrderSide.Buy)
                    {
                        if (barItem.low <= activePosition.StopLoss)
                        {
                            exitPrice = activePosition.StopLoss.Value;
                            closed = true;
                            closeReason = "StopLoss";
                        }
                        else if (barItem.high >= activePosition.TakeProfit)
                        {
                            exitPrice = activePosition.TakeProfit.Value;
                            closed = true;
                            closeReason = "TakeProfit";
                        }
                    }
                    else // Sell
                    {
                        if (barItem.high >= activePosition.StopLoss)
                        {
                            exitPrice = activePosition.StopLoss.Value;
                            closed = true;
                            closeReason = "StopLoss";
                        }
                        else if (barItem.low <= activePosition.TakeProfit)
                        {
                            exitPrice = activePosition.TakeProfit.Value;
                            closed = true;
                            closeReason = "TakeProfit";
                        }
                    }

                    if (closed)
                    {
                        double pips = activePosition.Side == OrderSide.Buy
                            ? (exitPrice - activePosition.EntryPrice) * 10000
                            : (activePosition.EntryPrice - exitPrice) * 10000;

                        double pnlGbp = Math.Round(pips * 0.076, 2);
                        equity += pnlGbp;
                        equityCurve.Add(equity);
                        tradeReturns.Add(pnlGbp / initialCapital);

                        totalTrades++;
                        if (pnlGbp >= 0)
                        {
                            wins++;
                            grossProfit += pnlGbp;
                            consecutiveLosses = 0;
                        }
                        else
                        {
                            losses++;
                            grossLoss += Math.Abs(pnlGbp);
                            consecutiveLosses++;
                            maxConsecutiveLosses = Math.Max(maxConsecutiveLosses, consecutiveLosses);
                        }

                        if (equity > peakEquity) peakEquity = equity;
                        double ddGbp = peakEquity - equity;
                        double ddPct = peakEquity > 0 ? (ddGbp / peakEquity) * 100.0 : 0;
                        maxDrawdownGbp = Math.Max(maxDrawdownGbp, ddGbp);
                        maxDrawdownPct = Math.Max(maxDrawdownPct, ddPct);

                        tradeLogs.Add(new
                        {
                            trade_id = totalTrades,
                            entry_time = activePosition.EntryTimeUtc,
                            exit_time = barTime,
                            side = activePosition.Side.ToString(),
                            entry_price = activePosition.EntryPrice,
                            exit_price = exitPrice,
                            pnl_gbp = pnlGbp,
                            exit_reason = closeReason,
                            running_equity = equity
                        });

                        activePosition = null;
                    }
                }

                // Strategy Signal Generation (Bollinger Bands + RSI)
                if (closeBuffer.Count >= 20 && activePosition == null)
                {
                    var slice = closeBuffer.Skip(closeBuffer.Count - 20).Take(20).ToList();
                    double sma = slice.Average();
                    double stdDev = Math.Sqrt(slice.Sum(p => Math.Pow(p - sma, 2)) / 20);
                    double upperBb = sma + (2.0 * stdDev);
                    double lowerBb = sma - (2.0 * stdDev);

                    // RSI 14
                    double avgGain = 0, avgLoss = 0;
                    for (int j = closeBuffer.Count - 14; j < closeBuffer.Count; j++)
                    {
                        double diff = closeBuffer[j] - closeBuffer[j - 1];
                        if (diff >= 0) avgGain += diff;
                        else avgLoss += Math.Abs(diff);
                    }
                    avgGain /= 14; avgLoss /= 14;
                    double rsi = avgLoss == 0 ? 100.0 : 100.0 - (100.0 / (1.0 + (avgGain / avgLoss)));

                    // BUY Signal
                    if (barItem.close <= lowerBb && rsi <= 30.0)
                    {
                        activePosition = new Position
                        {
                            Symbol = "EURUSD",
                            Side = OrderSide.Buy,
                            EntryPrice = barItem.close,
                            StopLoss = Math.Round(barItem.close - (12.0 * 0.00010), 5),
                            TakeProfit = Math.Round(barItem.close + (20.0 * 0.00010), 5),
                            EntryTimeUtc = barTime
                        };
                    }
                    // SELL Signal
                    else if (barItem.close >= upperBb && rsi >= 70.0)
                    {
                        activePosition = new Position
                        {
                            Symbol = "EURUSD",
                            Side = OrderSide.Sell,
                            EntryPrice = barItem.close,
                            StopLoss = Math.Round(barItem.close + (12.0 * 0.00010), 5),
                            TakeProfit = Math.Round(barItem.close - (20.0 * 0.00010), 5),
                            EntryTimeUtc = barTime
                        };
                    }
                }
            }

            // Quantitative Metrics
            double netProfitGbp = Math.Round(equity - initialCapital, 2);
            double netProfitPct = Math.Round((netProfitGbp / initialCapital) * 100.0, 2);
            double winRatePct = totalTrades > 0 ? Math.Round(((double)wins / totalTrades) * 100.0, 2) : 0.0;
            double profitFactor = grossLoss > 0 ? Math.Round(grossProfit / grossLoss, 2) : (grossProfit > 0 ? 99.0 : 1.0);
            double avgTradePnl = totalTrades > 0 ? Math.Round(netProfitGbp / totalTrades, 2) : 0.0;

            // Sharpe & Sortino Ratios (Annualized assuming 252 trading days)
            double meanReturn = tradeReturns.Count > 0 ? tradeReturns.Average() : 0.0;
            double stdDevReturn = tradeReturns.Count > 1 ? Math.Sqrt(tradeReturns.Sum(r => Math.Pow(r - meanReturn, 2)) / (tradeReturns.Count - 1)) : 0.0;
            double downsideDev = tradeReturns.Count > 1 ? Math.Sqrt(tradeReturns.Where(r => r < 0).Sum(r => Math.Pow(r, 2)) / (tradeReturns.Count - 1)) : 0.0;

            double sharpeRatio = stdDevReturn > 0 ? Math.Round((meanReturn / stdDevReturn) * Math.Sqrt(252), 2) : 0.0;
            double sortinoRatio = downsideDev > 0 ? Math.Round((meanReturn / downsideDev) * Math.Sqrt(252), 2) : 0.0;

            var result = new BacktestResultSummary
            {
                AlgorithmName = BuildInfo.AlgorithmName,
                AlgorithmVersion = BuildInfo.Version,
                GitCommitSha = BuildInfo.GitCommitSha,
                GitBranch = BuildInfo.GitBranch,
                GitLabel = BuildInfo.GitLabel,
                Symbol = "EURUSD",
                Timeframe = "M1",
                DataSource = Path.GetFileName(dataFilePath),
                StartDateUtc = startDate,
                EndDateUtc = endDate,
                InitialCapital = initialCapital,
                FinalCapital = Math.Round(equity, 2),
                NetProfitGbp = netProfitGbp,
                NetProfitPct = netProfitPct,
                SharpeRatio = sharpeRatio,
                SortinoRatio = sortinoRatio,
                ProfitFactor = profitFactor,
                MaxDrawdownGbp = Math.Round(maxDrawdownGbp, 2),
                MaxDrawdownPct = Math.Round(maxDrawdownPct, 2),
                TotalTrades = totalTrades,
                WinningTrades = wins,
                LosingTrades = losses,
                WinRatePct = winRatePct,
                AvgTradePnLGbp = avgTradePnl,
                MaxConsecutiveLosses = maxConsecutiveLosses,
                ExpectancyGbp = avgTradePnl,
                Parameters = seedData.strategy_parameters,
                EquityCurve = equityCurve,
                TradeLogs = tradeLogs
            };

            // Print Summary Table
            Console.WriteLine($"--------------------------------------------------------------------------------");
            Console.WriteLine($" BACKTEST RESULTS SUMMARY:");
            Console.WriteLine($" Initial Capital:       £{initialCapital:F2}");
            Console.WriteLine($" Final Equity:          £{result.FinalCapital:F2} (Net PnL: £{netProfitGbp:+0.00;-0.00;0.00} / {netProfitPct:F2}%)");
            Console.WriteLine($" Sharpe Ratio:          {sharpeRatio:F2}");
            Console.WriteLine($" Sortino Ratio:         {sortinoRatio:F2}");
            Console.WriteLine($" Profit Factor:         {profitFactor:F2}");
            Console.WriteLine($" Max Drawdown:          £{result.MaxDrawdownGbp:F2} ({result.MaxDrawdownPct:F2}%) [Invariant Limit: 5.0%]");
            Console.WriteLine($" Total Trades:          {totalTrades} (Wins: {wins}, Losses: {losses}, WinRate: {winRatePct:F1}%)");
            Console.WriteLine($" Max Consecutive Loss:  {maxConsecutiveLosses}");
            Console.WriteLine($"--------------------------------------------------------------------------------\n");

            // Save to file and SQL Data Warehouse
            await SaveBacktestResultsAsync(result);

            return result;
        }

        private async Task SaveBacktestResultsAsync(BacktestResultSummary res)
        {
            try
            {
                // 1. Save local JSON report
                string logDir = $@"E:\Logs\{BuildInfo.AppName}_Logs";
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);

                string fileName = $"backtest_{res.AlgorithmName}_{res.GitLabel}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
                string fullPath = Path.Combine(logDir, fileName);
                string jsonPayload = JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(fullPath, jsonPayload);
                Console.WriteLine($"[BACKTEST LOG] Saved report to {fullPath}");

                // 2. Insert into [AIv1].[dbo].[BacktestResults]
                string dbServer = GetSecrets.GetSecret("SQL-AI-SERVER");
                if (string.IsNullOrEmpty(dbServer)) dbServer = @"COOKIE\SHARES";
                string dbName = GetSecrets.GetSecret("SQL-AI-DATABASE");
                if (string.IsNullOrEmpty(dbName)) dbName = "AIv1";
                string dbUser = GetSecrets.GetSecret("SQL-AI-USERNAME");
                string dbPass = GetSecrets.GetSecret("SQL-AI-PASSWORD");

                var builder = new SqlConnectionStringBuilder
                {
                    DataSource = dbServer,
                    InitialCatalog = dbName,
                    TrustServerCertificate = true,
                    ConnectTimeout = 5
                };

                if (!string.IsNullOrEmpty(dbUser) && !string.IsNullOrEmpty(dbPass))
                {
                    builder.UserID = dbUser;
                    builder.Password = dbPass;
                    builder.IntegratedSecurity = false;
                }
                else
                {
                    builder.IntegratedSecurity = true;
                }

                const string query = @"
                IF OBJECT_ID('[AIv1].[dbo].[BacktestResults]', 'U') IS NOT NULL
                BEGIN
                    INSERT INTO [AIv1].[dbo].[BacktestResults]
                    (TimestampUTC, AlgorithmName, AlgorithmVersion, GitCommitSHA, GitBranch, GitLabel, Exchange, Symbol, Timeframe, DataSource,
                     StartDateUTC, EndDateUTC, InitialCapital, FinalCapital, NetProfitGbp, NetProfitPct,
                     SharpeRatio, SortinoRatio, ProfitFactor, MaxDrawdownGbp, MaxDrawdownPct,
                     TotalTrades, WinningTrades, LosingTrades, WinRatePct, AvgTradePnLGbp, MaxConsecutiveLosses, ExpectancyGbp,
                     ParametersJSON, EquityCurveJSON, TradeLogJSON)
                    VALUES
                    (@TimestampUTC, @AlgorithmName, @AlgorithmVersion, @GitCommitSHA, @GitBranch, @GitLabel, @Exchange, @Symbol, @Timeframe, @DataSource,
                     @StartDateUTC, @EndDateUTC, @InitialCapital, @FinalCapital, @NetProfitGbp, @NetProfitPct,
                     @SharpeRatio, @SortinoRatio, @ProfitFactor, @MaxDrawdownGbp, @MaxDrawdownPct,
                     @TotalTrades, @WinningTrades, @LosingTrades, @WinRatePct, @AvgTradePnLGbp, @MaxConsecutiveLosses, @ExpectancyGbp,
                     @ParametersJSON, @EquityCurveJSON, @TradeLogJSON);
                END";

                using var conn = new SqlConnection(builder.ConnectionString);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@TimestampUTC", DateTime.UtcNow);
                cmd.Parameters.AddWithValue("@AlgorithmName", res.AlgorithmName);
                cmd.Parameters.AddWithValue("@AlgorithmVersion", res.AlgorithmVersion);
                cmd.Parameters.AddWithValue("@GitCommitSHA", res.GitCommitSha);
                cmd.Parameters.AddWithValue("@GitBranch", res.GitBranch);
                cmd.Parameters.AddWithValue("@GitLabel", res.GitLabel);
                cmd.Parameters.AddWithValue("@Exchange", res.Exchange);
                cmd.Parameters.AddWithValue("@Symbol", res.Symbol);
                cmd.Parameters.AddWithValue("@Timeframe", res.Timeframe);
                cmd.Parameters.AddWithValue("@DataSource", res.DataSource);
                cmd.Parameters.AddWithValue("@StartDateUTC", res.StartDateUtc == DateTime.MinValue ? (object)DBNull.Value : res.StartDateUtc);
                cmd.Parameters.AddWithValue("@EndDateUTC", res.EndDateUtc == DateTime.MinValue ? (object)DBNull.Value : res.EndDateUtc);
                cmd.Parameters.AddWithValue("@InitialCapital", res.InitialCapital);
                cmd.Parameters.AddWithValue("@FinalCapital", res.FinalCapital);
                cmd.Parameters.AddWithValue("@NetProfitGbp", res.NetProfitGbp);
                cmd.Parameters.AddWithValue("@NetProfitPct", res.NetProfitPct);
                cmd.Parameters.AddWithValue("@SharpeRatio", res.SharpeRatio);
                cmd.Parameters.AddWithValue("@SortinoRatio", res.SortinoRatio);
                cmd.Parameters.AddWithValue("@ProfitFactor", res.ProfitFactor);
                cmd.Parameters.AddWithValue("@MaxDrawdownGbp", res.MaxDrawdownGbp);
                cmd.Parameters.AddWithValue("@MaxDrawdownPct", res.MaxDrawdownPct);
                cmd.Parameters.AddWithValue("@TotalTrades", res.TotalTrades);
                cmd.Parameters.AddWithValue("@WinningTrades", res.WinningTrades);
                cmd.Parameters.AddWithValue("@LosingTrades", res.LosingTrades);
                cmd.Parameters.AddWithValue("@WinRatePct", res.WinRatePct);
                cmd.Parameters.AddWithValue("@AvgTradePnLGbp", res.AvgTradePnLGbp);
                cmd.Parameters.AddWithValue("@MaxConsecutiveLosses", res.MaxConsecutiveLosses);
                cmd.Parameters.AddWithValue("@ExpectancyGbp", res.ExpectancyGbp);
                cmd.Parameters.AddWithValue("@ParametersJSON", JsonSerializer.Serialize(res.Parameters));
                cmd.Parameters.AddWithValue("@EquityCurveJSON", JsonSerializer.Serialize(res.EquityCurve));
                cmd.Parameters.AddWithValue("@TradeLogJSON", JsonSerializer.Serialize(res.TradeLogs));

                await cmd.ExecuteNonQueryAsync();
                Console.WriteLine($"[BACKTEST SQL] Successfully stored backtest metrics in [AIv1].[dbo].[BacktestResults]");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BACKTEST WARN] Could not persist to SQL Server ({ex.Message}). Log saved to disk.");
            }
        }
    }
}
