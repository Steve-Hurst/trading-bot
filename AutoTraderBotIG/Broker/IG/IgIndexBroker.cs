using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Models;
using Logging;
using Secrets;

namespace Broker.IG
{
    public class IgIndexBroker : ITradingBroker
    {
        public string BrokerName => "IG_Index_UK_SpreadBet";
        public bool IsConnected { get; private set; }

        public event Action<TickData>? OnTickReceived;
        public event Action<BarData>? OnBarReceived;

        private readonly TelemetryLogger _logger;
        private readonly HttpClient _httpClient;
        private readonly BotConfiguration _config;

        private string _apiKey = string.Empty;
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _accountId = string.Empty;
        private string _cstToken = string.Empty;
        private string _securityToken = string.Empty;
        private string _lightstreamerEndpoint = string.Empty;
        private string _environment = "demo";
        private string _baseUrl = "https://demo-api.ig.com/gateway/deal";

        private readonly Dictionary<string, Position> _localPositions = new();
        private double _accountBalance = 500.0;
        private double _accountEquity = 500.0;
        private CancellationTokenSource? _pollCts;

        public IgIndexBroker(TelemetryLogger logger, BotConfiguration? config = null)
        {
            _logger = logger;
            _config = config ?? new BotConfiguration();
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                // Fetch credentials from centralized Cookie Secrets service
                _apiKey = await GetSecrets.GetSecretAsync("AutoBot_IG_ApiKey");
                if (string.IsNullOrEmpty(_apiKey)) _apiKey = await GetSecrets.GetSecretAsync("IG-API-KEY");

                _username = await GetSecrets.GetSecretAsync("AutoBot_IG_Username");
                if (string.IsNullOrEmpty(_username)) _username = await GetSecrets.GetSecretAsync("IG-USERNAME");

                _password = await GetSecrets.GetSecretAsync("AutoBot_IG_Password");
                if (string.IsNullOrEmpty(_password)) _password = await GetSecrets.GetSecretAsync("IG-PASSWORD");

                _accountId = await GetSecrets.GetSecretAsync("AutoBot_IG_AccountId");
                if (string.IsNullOrEmpty(_accountId)) _accountId = await GetSecrets.GetSecretAsync("IG-ACCOUNT-ID");

                _environment = (await GetSecrets.GetSecretAsync("IG-ENV")).ToLowerInvariant();
                if (string.IsNullOrEmpty(_environment)) _environment = "demo";

                _baseUrl = _environment == "live" 
                    ? "https://api.ig.com/gateway/deal" 
                    : "https://demo-api.ig.com/gateway/deal";

                if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_username) || string.IsNullOrEmpty(_password))
                {
                    _logger.Warn("IgIndexBroker.ConnectAsync", 
                        $"IG credentials check - Username: '{_username}', ApiKeyPresent: {!string.IsNullOrEmpty(_apiKey)}. Missing in Secrets service. Operating in standby simulated spread betting mode.");
                    IsConnected = false;
                    return false;
                }

                _logger.Info("IgIndexBroker.ConnectAsync", 
                    $"Authenticating session against IG Index REST API ({_baseUrl}) for account {_accountId}...");

                // Step 1: Authenticate via POST /session
                var authPayload = new
                {
                    identifier = _username,
                    password = _password
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/session")
                {
                    Content = new StringContent(JsonSerializer.Serialize(authPayload), Encoding.UTF8, "application/json")
                };

                request.Headers.Add("X-IG-API-KEY", _apiKey);
                request.Headers.Add("Version", "2");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    // Extract security tokens from headers
                    if (response.Headers.TryGetValues("CST", out var cstValues))
                    {
                        foreach (var v in cstValues) _cstToken = v;
                    }
                    if (response.Headers.TryGetValues("X-SECURITY-TOKEN", out var secValues))
                    {
                        foreach (var v in secValues) _securityToken = v;
                    }

                    string respBody = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(respBody);
                    if (doc.RootElement.TryGetProperty("lightstreamerEndpoint", out var lsElem))
                    {
                        _lightstreamerEndpoint = lsElem.GetString() ?? string.Empty;
                    }
                    if (doc.RootElement.TryGetProperty("currentAccountId", out var accElem) && string.IsNullOrEmpty(_accountId))
                    {
                        _accountId = accElem.GetString() ?? string.Empty;
                    }

                    IsConnected = true;
                    _logger.Info("IgIndexBroker.ConnectAsync", 
                        $"Successfully connected to IG Index Spread Betting API. Account: {_accountId}, Lightstreamer: {_lightstreamerEndpoint}");

                    StartMarketPolling();
                    return true;
                }
                else
                {
                    string errContent = await response.Content.ReadAsStringAsync();
                    _logger.Warn("IgIndexBroker.ConnectAsync", 
                        $"IG session authentication returned status {response.StatusCode}: {errContent}. Standby mode active.");
                    IsConnected = false;
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("IgIndexBroker.ConnectAsync", "Exception connecting to IG Index REST API", ex);
                IsConnected = false;
                return false;
            }
        }

        public Task DisconnectAsync()
        {
            _pollCts?.Cancel();
            IsConnected = false;
            _logger.Info("IgIndexBroker.DisconnectAsync", "Disconnected from IG Index API");
            return Task.CompletedTask;
        }

        public async Task<AccountSummary> GetAccountSummaryAsync()
        {
            if (!IsConnected)
            {
                return new AccountSummary
                {
                    Balance = _accountBalance,
                    Equity = _accountEquity,
                    InitialCapital = _config.InitialCapitalGbp
                };
            }

            try
            {
                var req = CreateAuthenticatedRequest(HttpMethod.Get, $"{_baseUrl}/accounts", version: "1");
                var response = await _httpClient.SendAsync(req);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("accounts", out var accountsElem))
                    {
                        foreach (var acc in accountsElem.EnumerateArray())
                        {
                            if (acc.TryGetProperty("accountId", out var idElem) && idElem.GetString() == _accountId)
                            {
                                if (acc.TryGetProperty("balance", out var balElem))
                                {
                                    double bal = balElem.GetProperty("balance").GetDouble();
                                    double deposit = balElem.GetProperty("deposit").GetDouble();
                                    double pnl = balElem.GetProperty("profitLoss").GetDouble();
                                    double available = balElem.GetProperty("available").GetDouble();

                                    _accountBalance = bal;
                                    _accountEquity = bal + pnl;

                                    return new AccountSummary
                                    {
                                        Balance = _accountBalance,
                                        Equity = _accountEquity,
                                        InitialCapital = _config.InitialCapitalGbp,
                                        UsedMargin = deposit,
                                        DailyRealizedPnL = pnl
                                    };
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("IgIndexBroker.GetAccountSummaryAsync", $"Error querying IG account status: {ex.Message}");
            }

            return new AccountSummary
            {
                Balance = _accountBalance,
                Equity = _accountEquity,
                InitialCapital = _config.InitialCapitalGbp
            };
        }

        public async Task<IReadOnlyList<Position>> GetOpenPositionsAsync()
        {
            if (!IsConnected)
            {
                lock (_localPositions)
                {
                    return new List<Position>(_localPositions.Values);
                }
            }

            try
            {
                var req = CreateAuthenticatedRequest(HttpMethod.Get, $"{_baseUrl}/positions", version: "2");
                var response = await _httpClient.SendAsync(req);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var list = new List<Position>();

                    if (doc.RootElement.TryGetProperty("positions", out var positionsElem))
                    {
                        foreach (var item in positionsElem.EnumerateArray())
                        {
                            var pos = item.GetProperty("position");
                            var market = item.GetProperty("market");

                            string dealId = pos.GetProperty("dealId").GetString() ?? string.Empty;
                            string direction = pos.GetProperty("direction").GetString() ?? "BUY";
                            double size = pos.GetProperty("size").GetDouble();
                            double openLevel = pos.GetProperty("level").GetDouble();
                            double? stopLevel = pos.TryGetProperty("stopLevel", out var sl) && sl.ValueKind == JsonValueKind.Number ? sl.GetDouble() : null;
                            double? limitLevel = pos.TryGetProperty("limitLevel", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetDouble() : null;
                            string epic = market.GetProperty("epic").GetString() ?? string.Empty;
                            double currentBid = market.TryGetProperty("bid", out var b) ? b.GetDouble() : openLevel;

                            list.Add(new Position
                            {
                                DealId = dealId,
                                Symbol = epic,
                                Side = direction.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? OrderSide.Buy : OrderSide.Sell,
                                SizeStake = size,
                                EntryPrice = openLevel,
                                CurrentPrice = currentBid,
                                StopLoss = stopLevel,
                                TakeProfit = limitLevel,
                                Status = PositionStatus.Open
                            });
                        }
                    }
                    return list;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("IgIndexBroker.GetOpenPositionsAsync", $"Error fetching IG positions: {ex.Message}");
            }

            lock (_localPositions)
            {
                return new List<Position>(_localPositions.Values);
            }
        }

        public async Task<OrderResult> ExecuteOrderAsync(OrderRequest request)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            if (!IsConnected)
            {
                // Simulated execution for sandbox testing
                string simDealId = $"IG-SIM-{Guid.NewGuid():N}"[..16];
                var simPos = new Position
                {
                    DealId = simDealId,
                    PositionId = DateTime.UtcNow.Ticks,
                    Symbol = request.Symbol,
                    DisplaySymbol = request.DisplaySymbol,
                    Side = request.Side,
                    SizeStake = request.SizeStake,
                    EntryPrice = request.RequestedPrice,
                    CurrentPrice = request.RequestedPrice,
                    StopLoss = request.StopLossPrice,
                    TakeProfit = request.TakeProfitPrice,
                    Status = PositionStatus.Open
                };

                lock (_localPositions)
                {
                    _localPositions[simDealId] = simPos;
                }

                _logger.Info("IgIndexBroker.ExecuteOrderAsync", 
                    $"[SIMULATED] IG Spread Bet executed: {request.Side} £{request.SizeStake:F2}/pt on {request.Symbol} @ {request.RequestedPrice:F5}");

                return new OrderResult
                {
                    Success = true,
                    OrderId = simDealId,
                    DealReference = simDealId,
                    ExecutionId = $"EXEC-{DateTime.UtcNow:yyyyMMddHHmmss}",
                    RequestedPrice = request.RequestedPrice,
                    FilledPrice = request.RequestedPrice,
                    SlippagePips = 0.0,
                    LatencyMs = sw.Elapsed.TotalMilliseconds,
                    Message = "Spread bet filled in IG Sandbox Mode"
                };
            }

            try
            {
                var payload = new
                {
                    epic = request.Symbol,
                    expiry = "-",
                    direction = request.Side == OrderSide.Buy ? "BUY" : "SELL",
                    size = request.SizeStake.ToString("F2"),
                    orderType = "MARKET",
                    timeInForce = "FILL_OR_KILL",
                    guaranteedStop = false,
                    stopLevel = request.StopLossPrice,
                    profitLevel = request.TakeProfitPrice,
                    currencyCode = "GBP",
                    forceOpen = true
                };

                var req = CreateAuthenticatedRequest(HttpMethod.Post, $"{_baseUrl}/positions/otc", version: "2");
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(req);
                sw.Stop();

                string respJson = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(respJson);
                    string dealRef = doc.RootElement.TryGetProperty("dealReference", out var dr) ? dr.GetString() ?? string.Empty : string.Empty;

                    _logger.Info("IgIndexBroker.ExecuteOrderAsync", 
                        $"IG Order Submitted: DealRef {dealRef}, Direction {request.Side}, Stake £{request.SizeStake:F2}/pt on {request.Symbol} (Latency: {sw.ElapsedMilliseconds}ms)");

                    return new OrderResult
                    {
                        Success = true,
                        OrderId = dealRef,
                        DealReference = dealRef,
                        ExecutionId = $"IG-EXEC-{DateTime.UtcNow:yyyyMMddHHmmss}",
                        RequestedPrice = request.RequestedPrice,
                        FilledPrice = request.RequestedPrice,
                        SlippagePips = 0.0,
                        LatencyMs = sw.Elapsed.TotalMilliseconds,
                        Message = $"Deal accepted by IG Gateway (DealRef: {dealRef})"
                    };
                }
                else
                {
                    _logger.Error("IgIndexBroker.ExecuteOrderAsync", $"IG Order placement rejected: {respJson}");
                    return new OrderResult
                    {
                        Success = false,
                        Message = $"IG rejected order: {respJson}",
                        LatencyMs = sw.Elapsed.TotalMilliseconds
                    };
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.Error("IgIndexBroker.ExecuteOrderAsync", "Exception dispatching IG order", ex);
                return new OrderResult
                {
                    Success = false,
                    Message = ex.Message,
                    LatencyMs = sw.Elapsed.TotalMilliseconds
                };
            }
        }

        public async Task<bool> ClosePositionByDealIdAsync(string dealId)
        {
            if (!IsConnected)
            {
                lock (_localPositions)
                {
                    if (_localPositions.Remove(dealId))
                    {
                        _logger.Info("IgIndexBroker.ClosePositionByDealIdAsync", $"[SIMULATED] Closed position {dealId}");
                        return true;
                    }
                }
                return false;
            }

            try
            {
                var req = CreateAuthenticatedRequest(HttpMethod.Delete, $"{_baseUrl}/positions/otc", version: "1");
                var payload = new { dealId = dealId, orderType = "MARKET", timeInForce = "FILL_OR_KILL" };
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(req);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.Error("IgIndexBroker.ClosePositionByDealIdAsync", $"Failed to close deal {dealId}", ex);
                return false;
            }
        }

        public Task<bool> ClosePositionAsync(long positionId)
        {
            return ClosePositionByDealIdAsync(positionId.ToString());
        }

        public async Task<bool> ModifyPositionAsync(string dealId, double? stopLoss, double? takeProfit)
        {
            if (!IsConnected)
            {
                lock (_localPositions)
                {
                    if (_localPositions.TryGetValue(dealId, out var pos))
                    {
                        pos.StopLoss = stopLoss;
                        pos.TakeProfit = takeProfit;
                        return true;
                    }
                }
                return false;
            }

            try
            {
                var req = CreateAuthenticatedRequest(HttpMethod.Put, $"{_baseUrl}/positions/otc/{dealId}", version: "2");
                var payload = new
                {
                    stopLevel = stopLoss,
                    limitLevel = takeProfit,
                    trailingStop = false
                };
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(req);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.Error("IgIndexBroker.ModifyPositionAsync", $"Failed to modify deal {dealId}", ex);
                return false;
            }
        }

        private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url, string version = "2")
        {
            var req = new HttpRequestMessage(method, url);
            req.Headers.Add("X-IG-API-KEY", _apiKey);
            req.Headers.Add("CST", _cstToken);
            req.Headers.Add("X-SECURITY-TOKEN", _securityToken);
            req.Headers.Add("Version", version);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return req;
        }

        private void StartMarketPolling()
        {
            _pollCts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                while (!_pollCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(1000, _pollCts.Token);
                        foreach (var symbol in _config.Symbols)
                        {
                            var req = CreateAuthenticatedRequest(HttpMethod.Get, $"{_baseUrl}/markets/{symbol}", version: "3");
                            var resp = await _httpClient.SendAsync(req, _pollCts.Token);
                            if (resp.IsSuccessStatusCode)
                            {
                                string body = await resp.Content.ReadAsStringAsync(_pollCts.Token);
                                using var doc = JsonDocument.Parse(body);
                                if (doc.RootElement.TryGetProperty("snapshot", out var snap))
                                {
                                    double bid = snap.GetProperty("bid").GetDouble();
                                    double offer = snap.GetProperty("offer").GetDouble();
                                    var tick = new TickData
                                    {
                                        Symbol = symbol,
                                        DisplaySymbol = symbol.Contains("EURUSD") ? "EURUSD" : (symbol.Contains("GBPUSD") ? "GBPUSD" : "USDJPY"),
                                        Bid = bid,
                                        Ask = offer,
                                        TimestampUtc = DateTime.UtcNow
                                    };
                                    OnTickReceived?.Invoke(tick);
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            }, _pollCts.Token);
        }

        public void InjectTick(TickData tick) => OnTickReceived?.Invoke(tick);
        public void InjectBar(BarData bar) => OnBarReceived?.Invoke(bar);
    }
}
