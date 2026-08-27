using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Config;
using Logging;
using Service;

namespace AutoTraderBot01
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            // 1. Check CLI Flags
            if (args.Length > 0)
            {
                string firstArg = args[0].ToLowerInvariant().TrimStart('-', '/');

                switch (firstArg)
                {
                    case "v":
                    case "version":
                        Console.WriteLine($"{BuildInfo.AppName} version {BuildInfo.Version} (Built: {BuildInfo.BuildDate})");
                        return;

                    case "status":
                        await WindowsServiceManager.QueryStatusAsync();
                        return;

                    case "install":
                    case "service-install":
                        string currentExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, $"{BuildInfo.AppName}.exe");
                        WindowsServiceManager.InstallService(currentExe);
                        return;

                    case "remove":
                    case "uninstall":
                    case "service-remove":
                        WindowsServiceManager.RemoveService();
                        return;

                    case "start":
                        WindowsServiceManager.StartService();
                        return;

                    case "stop":
                        WindowsServiceManager.StopService();
                        return;

                    case "backtest":
                    case "bt":
                        string dataPath = "data/seed_data.json";
                        for (int k = 1; k < args.Length; k++)
                        {
                            if ((args[k].ToLowerInvariant() == "-data" || args[k].ToLowerInvariant() == "--data") && k + 1 < args.Length)
                            {
                                dataPath = args[k + 1];
                            }
                        }
                        var btLogger = new TelemetryLogger();
                        var btEngine = new Backtesting.BacktestEngine(btLogger);
                        await btEngine.RunBacktestAsync(dataPath);
                        return;

                    case "help":
                    case "?":
                        PrintUsage();
                        return;
                }
            }

            bool useLiveBroker = false;
            int port = BuildInfo.DefaultPort;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant().TrimStart('-', '/');
                if (arg == "live") useLiveBroker = true;
                if (arg == "sim" || arg == "dryrun" || arg == "simulate") useLiveBroker = false;
                if (arg == "port" && i + 1 < args.Length && int.TryParse(args[i + 1], out int p)) port = p;
            }

            Console.Title = $"{BuildInfo.AppName} v{BuildInfo.Version} [{ (useLiveBroker ? "LIVE PEPPERSTONE" : "SIMULATED PEPPERSTONE") }]";
            Console.WriteLine("================================================================================");
            Console.WriteLine($" {BuildInfo.AppName} v{BuildInfo.Version} - Pepperstone AutoTrading Bot 01");
            Console.WriteLine($" Mode: {(useLiveBroker ? "LIVE PEPPERSTONE (cTrader Open API)" : "SIMULATED PEPPERSTONE (Sandbox)")}");
            Console.WriteLine($" Asset: EURUSD | Initial Capital: £500.00 | Lot Size: 0.01 micro-lot");
            Console.WriteLine($" Max Drawdown Guardrail: 5.0% (£25.00) | Stop-Loss: 12 pips | Take-Profit: 20 pips");
            Console.WriteLine($" HTTP Status API: http://localhost:{port}/status");
            Console.WriteLine("================================================================================");

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\n[SHUTDOWN] Intercepted termination signal. Shutting down...");
                cts.Cancel();
            };

            var logger = new TelemetryLogger();
            var engine = new BotEngine(logger, useLiveBroker: useLiveBroker);
            var httpServer = new StatusHttpServer(engine, logger, port: port);

            httpServer.Start();

            try
            {
                await engine.StartAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Clean shutdown
            }
            catch (Exception ex)
            {
                logger.Error("Program.Main", "Fatal unhandled exception in trading engine", ex);
            }
            finally
            {
                httpServer.Stop();
                logger.Info("Program.Main", "Application terminated cleanly.");
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine($"Usage: {BuildInfo.AppName} [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  -version             Print application version");
            Console.WriteLine("  -status              Query bot health and execution metrics");
            Console.WriteLine("  -sim / -dryrun       Run in simulated market sandbox (Default)");
            Console.WriteLine("  -live                Run connected to Live Pepperstone cTrader Open API");
            Console.WriteLine("  -port <port>         Override HTTP monitoring port (Default: 9011)");
            Console.WriteLine("  -install             Install as Windows Service with auto-recovery");
            Console.WriteLine("  -remove              Remove Windows Service");
            Console.WriteLine("  -start / -stop       Start/stop the Windows Service");
        }
    }
}
