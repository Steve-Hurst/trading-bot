using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Config;
using Core.Models;
using Microsoft.Data.SqlClient;

namespace Logging
{
    public class TelemetryLogger
    {
        private readonly string _connectionString = "Server=localhost;Database=AIv1;Trusted_Connection=True;TrustServerCertificate=True;";
        private readonly string _logDir = @"E:\Logs\cookie.autotrader-ig01_Logs";
        private readonly ConcurrentQueue<ExecutionLogEntry> _queue = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _workerTask;

        public class ExecutionLogEntry
        {
            public string SourceSystem { get; set; } = BuildInfo.AppName;
            public string Runtime { get; set; } = "csharp_net9";
            public string LogLevel { get; set; } = "INFO";
            public string Exchange { get; set; } = "IG_SpreadBet";
            public string Symbol { get; set; } = "EURUSD";
            public string GitCommitSHA { get; set; } = BuildInfo.GitCommitSha;
            public string StrategyFunction { get; set; } = string.Empty;
            public string ExecutionID { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public object? LogData { get; set; }
        }

        public TelemetryLogger()
        {
            try
            {
                if (!Directory.Exists(_logDir))
                {
                    Directory.CreateDirectory(_logDir);
                }
            }
            catch { }

            _workerTask = Task.Run(ProcessQueueAsync);
        }

        public void Info(string strategyFunction, string message, object? logData = null, string symbol = "EURUSD", string execId = "")
        {
            Log("INFO", strategyFunction, message, logData, symbol, execId);
        }

        public void Warn(string strategyFunction, string message, object? logData = null, string symbol = "EURUSD", string execId = "")
        {
            Log("WARN", strategyFunction, message, logData, symbol, execId);
        }

        public void Error(string strategyFunction, string message, Exception? ex = null, string symbol = "EURUSD", string execId = "")
        {
            var logData = ex != null ? new { error = ex.Message, stack = ex.StackTrace } : null;
            Log("ERROR", strategyFunction, message, logData, symbol, execId);
        }

        private void Log(string level, string strategyFunction, string message, object? logData, string symbol, string execId)
        {
            string ts = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            Console.WriteLine($"[{ts}] [{level}] [{strategyFunction}] {message}");

            var entry = new ExecutionLogEntry
            {
                LogLevel = level,
                StrategyFunction = strategyFunction,
                Message = message,
                LogData = logData,
                Symbol = symbol,
                ExecutionID = execId
            };

            _queue.Enqueue(entry);
        }

        public async Task RecordBotMetricsAsync(AccountSummary account, int openPositions, double avgLatency, double avgSpread, double avgSlippage, long totalTicks, object? indicatorState = null)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                string sql = @"
                    INSERT INTO [dbo].[BotMetrics] 
                    ([BotID], [AlgorithmName], [AlgorithmVersion], [GitCommitSHA], [GitBranch], [GitLabel], [Exchange], [Symbol],
                     [AccountBalance], [AccountEquity], [UsedMargin], [FreeMargin], [MarginLevelPct], [DrawdownPct], [DailyRealizedPnL],
                     [TotalTradesToday], [WinningTradesToday], [LosingTradesToday], [WinRatePct], [OpenPositionsCount],
                     [AvgLatencyMs], [AvgSpreadPips], [AvgSlippagePips], [TotalTicksProcessed], [IndicatorStateJSON])
                    VALUES 
                    (@BotID, @AlgorithmName, @AlgorithmVersion, @GitCommitSHA, @GitBranch, @GitLabel, @Exchange, @Symbol,
                     @AccountBalance, @AccountEquity, @UsedMargin, @FreeMargin, @MarginLevelPct, @DrawdownPct, @DailyRealizedPnL,
                     @TotalTradesToday, @WinningTradesToday, @LosingTradesToday, @WinRatePct, @OpenPositionsCount,
                     @AvgLatencyMs, @AvgSpreadPips, @AvgSlippagePips, @TotalTicksProcessed, @IndicatorStateJSON);";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@BotID", BuildInfo.AppName);
                cmd.Parameters.AddWithValue("@AlgorithmName", BuildInfo.AlgorithmName);
                cmd.Parameters.AddWithValue("@AlgorithmVersion", BuildInfo.Version);
                cmd.Parameters.AddWithValue("@GitCommitSHA", BuildInfo.GitCommitSha);
                cmd.Parameters.AddWithValue("@GitBranch", BuildInfo.GitBranch);
                cmd.Parameters.AddWithValue("@GitLabel", BuildInfo.GitLabel);
                cmd.Parameters.AddWithValue("@Exchange", "IG_SpreadBet");
                cmd.Parameters.AddWithValue("@Symbol", "EURUSD");
                cmd.Parameters.AddWithValue("@AccountBalance", account.Balance);
                cmd.Parameters.AddWithValue("@AccountEquity", account.Equity);
                cmd.Parameters.AddWithValue("@UsedMargin", account.UsedMargin);
                cmd.Parameters.AddWithValue("@FreeMargin", account.FreeMargin);
                cmd.Parameters.AddWithValue("@MarginLevelPct", account.MarginLevelPct);
                cmd.Parameters.AddWithValue("@DrawdownPct", account.DrawdownPct);
                cmd.Parameters.AddWithValue("@DailyRealizedPnL", account.DailyRealizedPnL);
                cmd.Parameters.AddWithValue("@TotalTradesToday", account.TotalTradesToday);
                cmd.Parameters.AddWithValue("@WinningTradesToday", account.WinningTradesToday);
                cmd.Parameters.AddWithValue("@LosingTradesToday", account.LosingTradesToday);
                cmd.Parameters.AddWithValue("@WinRatePct", account.WinRatePct);
                cmd.Parameters.AddWithValue("@OpenPositionsCount", openPositions);
                cmd.Parameters.AddWithValue("@AvgLatencyMs", avgLatency);
                cmd.Parameters.AddWithValue("@AvgSpreadPips", avgSpread);
                cmd.Parameters.AddWithValue("@AvgSlippagePips", avgSlippage);
                cmd.Parameters.AddWithValue("@TotalTicksProcessed", totalTicks);
                cmd.Parameters.AddWithValue("@IndicatorStateJSON", indicatorState != null ? JsonSerializer.Serialize(indicatorState) : (object)DBNull.Value);

                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }

        private async Task ProcessQueueAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                if (_queue.TryDequeue(out var entry))
                {
                    await WriteToFileAsync(entry);
                    await WriteToSqlAsync(entry);
                }
                else
                {
                    await Task.Delay(100, _cts.Token).ConfigureAwait(false);
                }
            }
        }

        private async Task WriteToFileAsync(ExecutionLogEntry entry)
        {
            try
            {
                string filePath = Path.Combine(_logDir, $"{DateTime.UtcNow:yyyyMMdd}_Execution.log");
                string logLine = $"{DateTime.UtcNow:O}\t{entry.LogLevel}\t{entry.StrategyFunction}\t{entry.Message}\t{JsonSerializer.Serialize(entry.LogData)}\n";
                await File.AppendAllTextAsync(filePath, logLine);
            }
            catch { }
        }

        private async Task WriteToSqlAsync(ExecutionLogEntry entry)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                string sql = @"
                    INSERT INTO [dbo].[ExecutionLogs] 
                    ([SourceSystem], [Runtime], [LogLevel], [Exchange], [Symbol], [GitCommitSHA], [StrategyFunction], [ExecutionID], [Message], [LogDataJSON])
                    VALUES 
                    (@SourceSystem, @Runtime, @LogLevel, @Exchange, @Symbol, @GitCommitSHA, @StrategyFunction, @ExecutionID, @Message, @LogDataJSON);";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@SourceSystem", entry.SourceSystem);
                cmd.Parameters.AddWithValue("@Runtime", entry.Runtime);
                cmd.Parameters.AddWithValue("@LogLevel", entry.LogLevel);
                cmd.Parameters.AddWithValue("@Exchange", entry.Exchange);
                cmd.Parameters.AddWithValue("@Symbol", entry.Symbol);
                cmd.Parameters.AddWithValue("@GitCommitSHA", entry.GitCommitSHA);
                cmd.Parameters.AddWithValue("@StrategyFunction", (object?)entry.StrategyFunction ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ExecutionID", (object?)entry.ExecutionID ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Message", (object?)entry.Message ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@LogDataJSON", entry.LogData != null ? JsonSerializer.Serialize(entry.LogData) : (object)DBNull.Value);

                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }
    }
}
