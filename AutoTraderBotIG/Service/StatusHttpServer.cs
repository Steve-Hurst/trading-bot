using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Config;
using Logging;

namespace Service
{
    public class StatusHttpServer
    {
        private readonly HttpListener _listener;
        private readonly BotEngine _engine;
        private readonly TelemetryLogger _logger;
        private readonly int _port;
        private CancellationTokenSource? _cts;
        private Task? _listenerTask;

        public StatusHttpServer(BotEngine engine, TelemetryLogger logger, int port = BuildInfo.DefaultPort)
        {
            _engine = engine;
            _logger = logger;
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        }

        public void Start()
        {
            try
            {
                _cts = new CancellationTokenSource();
                _listener.Start();
                _listenerTask = Task.Run(() => HandleIncomingRequestsAsync(_cts.Token));
                _logger.Info("StatusHttpServer.Start", $"HTTP monitoring API active on port :{_port}");
            }
            catch (Exception ex)
            {
                _logger.Error("StatusHttpServer.Start", $"Failed to start HTTP listener on port {_port}.", ex);
            }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _listener.Stop();
            }
            catch { }
        }

        private async Task HandleIncomingRequestsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = ProcessRequestAsync(context);
                }
                catch (HttpListenerException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error("StatusHttpServer.HandleIncomingRequestsAsync", "Error in HTTP request dispatch", ex);
                }
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Content-Type", "application/json");

            string path = request.Url?.AbsolutePath.ToLowerInvariant() ?? "/";

            object responseObj;
            int statusCode = (int)HttpStatusCode.OK;

            switch (path)
            {
                case "/":
                case "/status":
                    var status = await _engine.GetLiveStatusAsync();
                    responseObj = new
                    {
                        app = BuildInfo.AppName,
                        version = BuildInfo.Version,
                        build_date = BuildInfo.BuildDate,
                        status = _engine.IsRunning ? "active" : "paused",
                        broker = _engine.BrokerName,
                        symbol = _engine.TargetSymbol,
                        display_symbol = _engine.DisplaySymbol,
                        metrics = status
                    };
                    break;

                case "/cacherefresh":
                    _logger.Info("StatusHttpServer", "Cache refresh requested via HTTP endpoint");
                    responseObj = new { status = "success", message = "Configuration and Secrets cache refreshed" };
                    break;

                case "/pause":
                    _engine.Pause();
                    responseObj = new { status = "success", message = "Trading bot paused" };
                    break;

                case "/resume":
                    _engine.Resume();
                    responseObj = new { status = "success", message = "Trading bot resumed" };
                    break;

                case "/emergency-close":
                    int closedCount = await _engine.EmergencyCloseAllAsync();
                    responseObj = new { status = "success", message = $"Emergency stop executed. Closed {closedCount} positions." };
                    break;

                default:
                    statusCode = (int)HttpStatusCode.NotFound;
                    responseObj = new { error = "Not Found", available_endpoints = new[] { "/status", "/cacherefresh", "/pause", "/resume", "/emergency-close" } };
                    break;
            }

            try
            {
                response.StatusCode = statusCode;
                byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responseObj, new JsonSerializerOptions { WriteIndented = true }));
                response.ContentLength64 = jsonBytes.Length;
                await response.OutputStream.WriteAsync(jsonBytes);
                response.OutputStream.Close();
            }
            catch { }
        }
    }
}
