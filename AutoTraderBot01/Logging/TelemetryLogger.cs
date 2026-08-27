using System;
using System.IO;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Config;
using Core.Models;
using Secrets;

namespace Logging
{
    public class TelemetryLogger
    {
        private readonly string _logDir;
        private readonly string _connectionString;
        private readonly Channel<ExecutionLogEntry> _logChannel;
        private readonly Task _backgroundWorker;

        public class ExecutionLogEntry
        {
            public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
            public string SourceSystem { get; set; } = BuildInfo.AppName;
            public string AlgorithmName { get; set; } = BuildInfo.AlgorithmName;
            public string AlgorithmVersion { get; set; } = BuildInfo.Version;
            public string Runtime { get; set; } = "csharp_net9";
            public string LogLevel { get; set; } = "INFO";
            public string Exchange { get; set; } = "Pepperstone";
            public string Symbol { get; set; } = "EURUSD";
            public string GitCommitSha { get; set; } = BuildInfo.GitCommitSha;
            public string GitBranch { get; set; } = BuildInfo.GitBranch;
            public string GitLabel { get; set; } = BuildInfo.GitLabel;
            public string StrategyFunction { get; set; } = "General";
            public string ExecutionId { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public double LatencyMs { get; set; }
            public double SlippagePips { get; set; }
            public double SpreadPips { get; set; }
            public double DrawdownPct { get; set; }
            public object? AdditionalData { get; set; }
        }

        public TelemetryLogger()
        {
            _logDir = $@"E:\Logs\{BuildInfo.AppName}_Logs";
            try
            {
                if (!Directory.Exists(_logDir))
                {
                    Directory.CreateDirectory(_logDir);
                }
            }
            catch { }

            // Initialize SQL connection string from Secrets or default local instance
            string dbServer = GetSecrets.GetSecret("SQL-AI-SERVER");
            if (string.IsNullOrEmpty(dbServer)) dbServer = GetSecrets.GetSecret("SQL-EQUITIES-SERVER");
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

            _connectionString = builder.ConnectionString;
            _logChannel = Channel.CreateUnbounded<ExecutionLogEntry>(new UnboundedChannelOptions { SingleReader = true });
            _backgroundWorker = Task.Run(ProcessLogQueueAsync);
        }

        public void Log(string level, string strategyFunction, string message, double latencyMs = 0, double slippagePips = 0, double spreadPips = 0, double drawdownPct = 0, string executionId = "", object? extra = null)
        {
            var entry = new ExecutionLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                LogLevel = level,
                StrategyFunction = strategyFunction,
                Message = message,
                LatencyMs = latencyMs,
                SlippagePips = slippagePips,
                SpreadPips = spreadPips,
                DrawdownPct = drawdownPct,
                ExecutionId = executionId,
                AdditionalData = extra
            };

            // Write to console immediately
            string consoleLine = $"[{entry.TimestampUtc:yyyy-MM-dd HH:mm:ss.fff}] [{entry.LogLevel}] [{strategyFunction}] {message}";
            if (latencyMs > 0 || spreadPips > 0)
            {
                consoleLine += $" | Latency: {latencyMs:F1}ms, Spread: {spreadPips:F2}p, DD: {drawdownPct:F2}% | Git: {BuildInfo.GitLabel}";
            }
            Console.WriteLine(consoleLine);

            _logChannel.Writer.TryWrite(entry);
        }

        public void Info(string function, string message, double latencyMs = 0, double slippagePips = 0, double spreadPips = 0, double drawdownPct = 0, string executionId = "", object? extra = null)
            => Log("INFO", function, message, latencyMs: latencyMs, slippagePips: slippagePips, spreadPips: spreadPips, drawdownPct: drawdownPct, executionId: executionId, extra: extra);

        public void Warn(string function, string message, double latencyMs = 0, double slippagePips = 0, double spreadPips = 0, double drawdownPct = 0, string executionId = "", object? extra = null)
            => Log("WARN", function, message, latencyMs: latencyMs, slippagePips: slippagePips, spreadPips: spreadPips, drawdownPct: drawdownPct, executionId: executionId, extra: extra);

        public void Error(string function, string message, Exception? ex = null, object? extra = null)
        {
            string fullMsg = ex == null ? message : $"{message} | Ex: {ex.Message} | Stack: {ex.StackTrace}";
            Log("ERROR", function, fullMsg, extra: extra);
        }

        public async Task RecordBotMetricsAsync(AccountSummary account, int openPositionsCount, double avgLatency, double avgSpread, double avgSlippage, long totalTicks, object? indicatorState = null)
        {
            try
            {
                var metricSnapshot = new
                {
                    timestamp_utc = DateTime.UtcNow,
                    bot_id = BuildInfo.AppName,
                    algorithm_name = BuildInfo.AlgorithmName,
                    algorithm_version = BuildInfo.Version,
                    git_commit_sha = BuildInfo.GitCommitSha,
                    git_branch = BuildInfo.GitBranch,
                    git_label = BuildInfo.GitLabel,
                    exchange = "Pepperstone",
                    symbol = "EURUSD",
                    account_balance = account.Balance,
                    account_equity = account.Equity,
                    used_margin = account.UsedMargin,
                    free_margin = account.FreeMargin,
                    margin_level_pct = account.MarginLevelPct,
                    peak_equity = account.PeakEquity,
                    drawdown_pct = account.DrawdownPct,
                    daily_realized_pnl = account.DailyRealizedPnL,
                    total_trades_today = account.TotalTradesToday,
                    winning_trades = account.WinningTradesToday,
                    losing_trades = account.LosingTradesToday,
                    win_rate_pct = account.WinRatePct,
                    open_positions_count = openPositionsCount,
                    avg_latency_ms = avgLatency,
                    avg_spread_pips = avgSpread,
                    avg_slippage_pips = avgSlippage,
                    total_ticks_processed = totalTicks,
                    indicators = indicatorState
                };

                string snapshotJson = JsonSerializer.Serialize(metricSnapshot, new JsonSerializerOptions { WriteIndented = true });

                // 1. Write to local metrics JSON file
                string metricsFilePath = Path.Combine(_logDir, $"metrics_{DateTime.UtcNow:yyyyMMdd}.json");
                await File.AppendAllTextAsync(metricsFilePath, snapshotJson + Environment.NewLine);

                // 2. Insert into [AIv1].[dbo].[BotMetrics]
                const string query = @"
                IF OBJECT_ID('[AIv1].[dbo].[BotMetrics]', 'U') IS NOT NULL
                BEGIN
                    INSERT INTO [AIv1].[dbo].[BotMetrics]
                    (TimestampUTC, BotID, AlgorithmName, AlgorithmVersion, GitCommitSHA, GitBranch, GitLabel, Exchange, Symbol,
                     AccountBalance, AccountEquity, UsedMargin, FreeMargin, MarginLevelPct, PeakEquity, DrawdownPct, DailyRealizedPnL,
                     TotalTradesToday, WinningTrades, LosingTrades, WinRatePct, OpenPositionsCount,
                     AvgLatencyMs, AvgSpreadPips, AvgSlippagePips, TotalTicksProcessed, MetricsJSON)
                    VALUES
                    (@TimestampUTC, @BotID, @AlgorithmName, @AlgorithmVersion, @GitCommitSHA, @GitBranch, @GitLabel, @Exchange, @Symbol,
                     @AccountBalance, @AccountEquity, @UsedMargin, @FreeMargin, @MarginLevelPct, @PeakEquity, @DrawdownPct, @DailyRealizedPnL,
                     @TotalTradesToday, @WinningTrades, @LosingTrades, @WinRatePct, @OpenPositionsCount,
                     @AvgLatencyMs, @AvgSpreadPips, @AvgSlippagePips, @TotalTicksProcessed, @MetricsJSON);
                END";

                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@TimestampUTC", DateTime.UtcNow);
                cmd.Parameters.AddWithValue("@BotID", BuildInfo.AppName);
                cmd.Parameters.AddWithValue("@AlgorithmName", BuildInfo.AlgorithmName);
                cmd.Parameters.AddWithValue("@AlgorithmVersion", BuildInfo.Version);
                cmd.Parameters.AddWithValue("@GitCommitSHA", BuildInfo.GitCommitSha);
                cmd.Parameters.AddWithValue("@GitBranch", BuildInfo.GitBranch);
                cmd.Parameters.AddWithValue("@GitLabel", BuildInfo.GitLabel);
                cmd.Parameters.AddWithValue("@Exchange", "Pepperstone");
                cmd.Parameters.AddWithValue("@Symbol", "EURUSD");
                cmd.Parameters.AddWithValue("@AccountBalance", account.Balance);
                cmd.Parameters.AddWithValue("@AccountEquity", account.Equity);
                cmd.Parameters.AddWithValue("@UsedMargin", account.UsedMargin);
                cmd.Parameters.AddWithValue("@FreeMargin", account.FreeMargin);
                cmd.Parameters.AddWithValue("@MarginLevelPct", account.MarginLevelPct);
                cmd.Parameters.AddWithValue("@PeakEquity", account.PeakEquity);
                cmd.Parameters.AddWithValue("@DrawdownPct", account.DrawdownPct);
                cmd.Parameters.AddWithValue("@DailyRealizedPnL", account.DailyRealizedPnL);
                cmd.Parameters.AddWithValue("@TotalTradesToday", account.TotalTradesToday);
                cmd.Parameters.AddWithValue("@WinningTrades", account.WinningTradesToday);
                cmd.Parameters.AddWithValue("@LosingTrades", account.LosingTradesToday);
                cmd.Parameters.AddWithValue("@WinRatePct", account.WinRatePct);
                cmd.Parameters.AddWithValue("@OpenPositionsCount", openPositionsCount);
                cmd.Parameters.AddWithValue("@AvgLatencyMs", avgLatency);
                cmd.Parameters.AddWithValue("@AvgSpreadPips", avgSpread);
                cmd.Parameters.AddWithValue("@AvgSlippagePips", avgSlippage);
                cmd.Parameters.AddWithValue("@TotalTicksProcessed", totalTicks);
                cmd.Parameters.AddWithValue("@MetricsJSON", snapshotJson);

                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                // Telemetry insertion handles offline database conditions gracefully
            }
        }

        private async Task ProcessLogQueueAsync()
        {
            while (await _logChannel.Reader.WaitToReadAsync())
            {
                while (_logChannel.Reader.TryRead(out var entry))
                {
                    await WriteToFileAsync(entry);
                    await WriteToDatabaseAsync(entry);
                }
            }
        }

        private async Task WriteToFileAsync(ExecutionLogEntry entry)
        {
            try
            {
                string filePath = Path.Combine(_logDir, $"{BuildInfo.AppName}_{DateTime.UtcNow:yyyyMMdd}.log");
                string payloadJson = JsonSerializer.Serialize(new
                {
                    algorithm = new
                    {
                        name = entry.AlgorithmName,
                        version = entry.AlgorithmVersion,
                        git_sha = entry.GitCommitSha,
                        git_branch = entry.GitBranch,
                        git_label = entry.GitLabel
                    },
                    metrics = new
                    {
                        latency_tick_to_order_ms = entry.LatencyMs,
                        slippage_pips = entry.SlippagePips,
                        spread_pips = entry.SpreadPips,
                        drawdown_pct = entry.DrawdownPct
                    },
                    extra = entry.AdditionalData
                });

                string logLine = $"{entry.TimestampUtc:o}\t{entry.LogLevel}\t{entry.Exchange}\t{entry.Symbol}\t{entry.StrategyFunction}\t{entry.ExecutionId}\t{entry.Message}\t{payloadJson}{Environment.NewLine}";
                await File.AppendAllTextAsync(filePath, logLine);
            }
            catch
            {
                // File writing fallback without throwing
            }
        }

        private async Task WriteToDatabaseAsync(ExecutionLogEntry entry)
        {
            try
            {
                string payloadJson = JsonSerializer.Serialize(new
                {
                    algorithm = new
                    {
                        name = entry.AlgorithmName,
                        version = entry.AlgorithmVersion,
                        git_sha = entry.GitCommitSha,
                        git_branch = entry.GitBranch,
                        git_label = entry.GitLabel
                    },
                    metrics = new
                    {
                        latency_tick_to_order_ms = entry.LatencyMs,
                        slippage_pips = entry.SlippagePips,
                        spread_pips = entry.SpreadPips,
                        drawdown_pct = entry.DrawdownPct
                    },
                    context = entry.AdditionalData
                });

                const string query = @"
                IF OBJECT_ID('[AIv1].[dbo].[ExecutionLogs]', 'U') IS NOT NULL
                BEGIN
                    INSERT INTO [AIv1].[dbo].[ExecutionLogs] 
                    (TimestampUTC, SourceSystem, Runtime, LogLevel, Exchange, Symbol, GitCommitSHA, StrategyFunction, ExecutionID, Message, LogDataJSON)
                    VALUES 
                    (@TimestampUTC, @SourceSystem, @Runtime, @LogLevel, @Exchange, @Symbol, @GitCommitSHA, @StrategyFunction, @ExecutionID, @Message, @LogDataJSON);
                END";

                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@TimestampUTC", entry.TimestampUtc);
                cmd.Parameters.AddWithValue("@SourceSystem", entry.SourceSystem);
                cmd.Parameters.AddWithValue("@Runtime", entry.Runtime);
                cmd.Parameters.AddWithValue("@LogLevel", entry.LogLevel);
                cmd.Parameters.AddWithValue("@Exchange", entry.Exchange);
                cmd.Parameters.AddWithValue("@Symbol", entry.Symbol);
                cmd.Parameters.AddWithValue("@GitCommitSHA", entry.GitCommitSha);
                cmd.Parameters.AddWithValue("@StrategyFunction", entry.StrategyFunction ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@ExecutionID", entry.ExecutionId ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Message", entry.Message ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@LogDataJSON", payloadJson);

                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // SQL Server sink handles transient offline states smoothly
            }
        }
    }
}
