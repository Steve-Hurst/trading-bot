using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Config;
using Logging;
using Service;

namespace AutoTraderBotIG
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
            var config = new Core.Models.BotConfiguration();

            for (int i = 0; i < args.Length; i++)
            {
                string rawArg = args[i].ToLowerInvariant().TrimStart('-', '/');
                string nextVal = (i + 1 < args.Length && !args[i + 1].StartsWith("-") && !args[i + 1].StartsWith("/")) ? args[i + 1] : string.Empty;

                if (rawArg == "live")
                {
                    useLiveBroker = true;
                    config.Market = "IG_SpreadBet_Live";
                    config.BrokerEndpoint = "https://api.ig.com/gateway/deal";
                }
                else if (rawArg == "sim" || rawArg == "dryrun" || rawArg == "simulate")
                {
                    useLiveBroker = false;
                    config.Market = "IG_SpreadBet_Sandbox";
                    config.BrokerEndpoint = "https://demo-api.ig.com/gateway/deal";
                }
                else if ((rawArg == "market" || rawArg == "exchange") && !string.IsNullOrEmpty(nextVal))
                {
                    config.Market = nextVal;
                    i++;
                }
                else if ((rawArg == "symbol" || rawArg == "entry") && !string.IsNullOrEmpty(nextVal))
                {
                    config.Symbols = new[] { nextVal.ToUpperInvariant() };
                    i++;
                }
                else if ((rawArg == "symbols" || rawArg == "entries") && !string.IsNullOrEmpty(nextVal))
                {
                    config.Symbols = nextVal.ToUpperInvariant().Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    i++;
                }
                else if (rawArg == "broker" && !string.IsNullOrEmpty(nextVal))
                {
                    config.Broker = nextVal;
                    i++;
                }
                else if (rawArg == "port" && int.TryParse(nextVal, out int p))
                {
                    config.StatusPort = p;
                    i++;
                }
                else if ((rawArg == "endpoint" || rawArg == "broker-endpoint") && !string.IsNullOrEmpty(nextVal))
                {
                    config.BrokerEndpoint = nextVal;
                    i++;
                }
                else if ((rawArg == "secrets-endpoint" || rawArg == "vault") && !string.IsNullOrEmpty(nextVal))
                {
                    config.SecretsVaultEndpoint = nextVal;
                    i++;
                }
                else if ((rawArg == "secretnames" || rawArg == "secrets" || rawArg == "secretname") && !string.IsNullOrEmpty(nextVal))
                {
                    config.RequiredSecretNames = nextVal.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    i++;
                }
                else if (rawArg == "config" && !string.IsNullOrEmpty(nextVal) && File.Exists(nextVal))
                {
                    try
                    {
                        string json = File.ReadAllText(nextVal);
                        var loaded = System.Text.Json.JsonSerializer.Deserialize<Core.Models.BotConfiguration>(json);
                        if (loaded != null) config = loaded;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WARN] Could not parse config file '{nextVal}': {ex.Message}");
                    }
                    i++;
                }
            }

            Console.Title = $"{BuildInfo.AppName} v{BuildInfo.Version} [{config.Market}]";
            Console.WriteLine("================================================================================");
            Console.WriteLine($" {BuildInfo.AppName} v{BuildInfo.Version} - {BuildInfo.ServiceDescription}");
            Console.WriteLine($" Target Market: {config.Market} | Broker: {config.Broker}");
            Console.WriteLine($" Epic / Symbol: {string.Join(", ", config.Symbols)}");
            Console.WriteLine($" Broker Endpoint: {config.BrokerEndpoint}");
            Console.WriteLine($" Secrets Vault: {config.SecretsVaultEndpoint} (Token: {(!string.IsNullOrEmpty(BuildInfo.CookieControlToken) && BuildInfo.CookieControlToken != "0000000000000000000000000000000000000000000000000000000000000000" ? "LOADED" : "SANDBOX_DEFAULT")})");
            Console.WriteLine($" Required Secrets: {string.Join(", ", config.RequiredSecretNames)}");
            Console.WriteLine($" Initial Capital: £{config.InitialCapitalGbp:F2} | Stake: £{config.StakePerPoint:F2}/pt | Max DD: {config.HardMaxDrawdownPct}%");
            Console.WriteLine($" HTTP Status API: http://localhost:{config.StatusPort}/status");
            Console.WriteLine("================================================================================");

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\n[SHUTDOWN] Intercepted termination signal. Shutting down IG Bot...");
                cts.Cancel();
            };

            var logger = new TelemetryLogger();
            var engine = new BotEngine(logger, config: config, useLiveBroker: useLiveBroker);
            var httpServer = new StatusHttpServer(engine, logger, port: config.StatusPort);

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
                logger.Error("Program.Main", "Fatal unhandled exception in IG trading engine", ex);
            }
            finally
            {
                httpServer.Stop();
                logger.Info("Program.Main", "IG Bot Application terminated cleanly.");
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine($"Usage: {BuildInfo.AppName} [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  -version                     Print application version and build metadata");
            Console.WriteLine("  -status                      Query live bot health, open positions, and metrics");
            Console.WriteLine("  -market <name>               Target market/exchange (e.g. IG_SpreadBet_Sandbox, IG_SpreadBet_Live)");
            Console.WriteLine("  -symbol <epic>               IG Epic to trade (e.g. CS.D.EURUSD.TODAY.IP, CS.D.GBPUSD.TODAY.IP)");
            Console.WriteLine("  -broker <name>               Broker adapter (IgIndexBroker, SimulatedIgBroker)");
            Console.WriteLine("  -endpoint <url>              Configurable broker connection endpoint");
            Console.WriteLine("  -vault <url>                 Cookie-Control Secrets Vault endpoint (Default: http://localhost:9500)");
            Console.WriteLine("  -secretnames <list>          Comma-separated list of secret key names for auth tokens");
            Console.WriteLine("  -config <path.json>          Path to JSON configuration file");
            Console.WriteLine("  -sim / -dryrun               Run in simulated market sandbox (Default)");
            Console.WriteLine("  -live                        Run connected to Live IG Index Spread Betting API");
            Console.WriteLine("  -port <port>                 Override HTTP monitoring port (Default: 9012)");
            Console.WriteLine("  -install                     Install as Windows Service with auto-recovery");
            Console.WriteLine("  -remove                      Remove Windows Service");
            Console.WriteLine("  -start / -stop               Start/stop the Windows Service");
        }
    }
}
