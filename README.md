# AI-Supervised Algorithmic AutoTrading System (C# .NET 9)

This solution contains isolated, deterministic algorithmic trading daemons running locally on the `Cookie` workstation:

1. **`pyramidai.autotrader-bot01` (Port `:9011`):** Pepperstone cTrader Open API Execution Engine (EUR/USD Mean Reversion). Standby mode pending 4-day Pepperstone API key delivery.
2. **`pyramidai.autotrader-ig01` (Port `:9012`):** IG Index UK Spread Betting Engine (EUR/USD Mean Reversion & GBP/USD London Breakout). 100% Tax-Free micro-spread betting (£0.10/pt stake).

---

## 1. Overview & Operational Invariants
* **Initial Account Capital:** £500.00
* **Position Sizing:** Micro-stake (£0.10/point spread bet or 0.01 micro-lot)
* **Hard Drawdown Guardrail:** ≤ 5.0% (£25.00 total account drawdown ceiling)
* **Per-Trade Stop Loss:** 12–15 pips (~£0.90 – £1.50 risk)
* **Per-Trade Take Profit:** 20–30 pips (~£1.50 – £3.00 profit)
* **Risk-to-Reward Ratio:** ≥ 1:1.67
* **Max Allowed Spread:** ≤ 1.0 pip (orders rejected during news or spread widening)
* **Active Session Window:** London & NY session overlap (07:00 – 19:00 UTC)

---

## 2. Architecture & Ecosystem Compliance

* **Bot 01 (Pepperstone):** `E:\GDrive\c#\trading-bot\AutoTraderBot01\` → Port `:9011` → `C:\Batch\bin\pyramidai.autotrader-bot01.exe`
* **Bot IG (IG Index):** `E:\GDrive\c#\trading-bot\AutoTraderBotIG\` → Port `:9012` → `C:\Batch\bin\pyramidai.autotrader-ig01.exe`
* **File Logs Directory:** `E:\Logs\pyramidai.autotrader-bot01_Logs\` and `E:\Logs\pyramidai.autotrader-ig01_Logs\`
* **Database Telemetry Sink:** `[AIv1].[dbo].[ExecutionLogs]` and `[dbo].[BotMetrics]`
* **Code Warehouse Integration:** Hydrated automatically via `_Commit.bat` and indexed in `[AIv1].[dbo].[CodeSymbols]`
* **Deployment Location:** `C:\Batch\bin\pyramidai.autotrader-bot01.exe`
* **Windows Service Name:** `pyramidai.autotrader-bot01`
* **Windows Service Description:** `Steve Hurst AutoTrader Bot 01 - EURUSD Mean Reversion Pepperstone Execution Engine`
* **Cookie-Control Secrets Vault:** Embeds token during `_build.bat` from `http://localhost:9500/build?appName=pyramidai.autotrader-bot01`

---

## 3. Configurable Endpoints & Secrets Resolution

The bot dynamically manages and reports all active endpoints and authentication secrets:

| System / Provider | Config Flag | Default Endpoint | Required Secret Names |
| :--- | :--- | :--- | :--- |
| **Broker API (Pepperstone)** | `-endpoint <url>` | `demo.ctraderapi.com:5035` | `PEPPERSTONE_CLIENT_ID`<br/>`PEPPERSTONE_CLIENT_SECRET`<br/>`PEPPERSTONE_ACCOUNT_ID`<br/>`PEPPERSTONE_ACCESS_TOKEN` |
| **Broker API (IG Index)** | `-endpoint <url>` | `api.ig.com/gateway/deal` | `IG_API_KEY`<br/>`IG_ACCOUNT_ID`<br/>`IG_IDENTIFIER`<br/>`IG_PASSWORD` |
| **Secrets Vault** | `-vault <url>` | `http://localhost:9500` | Token requested automatically in `_build.bat` |
| **Status Health API** | `-port <int>` | `http://localhost:9011/status`| Open HTTP port (no auth required) |
| **Database Sink** | Config / DB | `[AIv1].[dbo].[ExecutionLogs]` | `SQL-AI-SERVER`, `SQL-AI-DATABASE`, etc. |

---

## 4. HTTP Monitoring & Health Endpoints (Port `:9011`)

The bot exposes an HTTP server on `http://localhost:9011` compatible with `SWV4Status` and `cookie.cookieHealthCheck`:

| Endpoint | Method | Purpose |
| :--- | :---: | :--- |
| `/status` | `GET` | Returns full JSON diagnostic state: target market, entries traded, endpoints list, required secrets, uptime, account equity, used margin, drawdown %, open positions, win rate, daily PnL, latency. |
| `/cacherefresh` | `GET` | Reloads parameters and secrets from the local Secrets service without process restart. |
| `/pause` | `GET` | Pauses new entry signals (leaves open positions managed by stop-loss/take-profit). |
| `/resume` | `GET` | Resumes automated trading. |
| `/emergency-close` | `GET` | Immediately liquidates all open positions across the broker engine. |

### Sample `/status` JSON Response:
```json
{
  "app": "pyramidai.autotrader-bot01",
  "version": "1.0.0.0",
  "build_date": "2026-08-29 12:00:00",
  "status": "active",
  "market": "Pepperstone_Sandbox",
  "entries_traded": [ "EURUSD" ],
  "broker": "PepperstoneOpenApiBroker",
  "endpoints": {
    "broker_api": "demo.ctraderapi.com:5035",
    "secrets_vault": "http://localhost:9500",
    "status_http": "http://localhost:9011",
    "database_sink": "[AIv1].[dbo].[ExecutionLogs]"
  },
  "required_secrets": {
    "secret_names": [
      "PEPPERSTONE_CLIENT_ID",
      "PEPPERSTONE_CLIENT_SECRET",
      "PEPPERSTONE_ACCOUNT_ID",
      "PEPPERSTONE_ACCESS_TOKEN"
    ],
    "control_token_present": true
  },
  "metrics": {
    "is_running": true,
    "uptime_seconds": 120,
    "total_ticks_processed": 340,
    "last_bid": 1.08517,
    "last_ask": 1.08525,
    "spread_pips": 0.8,
    "account_balance_gbp": 500.0,
    "account_equity_gbp": 500.0,
    "used_margin_gbp": 0.0,
    "free_margin_gbp": 500.0,
    "margin_level_pct": 999.0,
    "drawdown_pct": 0.0,
    "daily_realized_pnl_gbp": 0.0,
    "total_trades_today": 0,
    "winning_trades": 0,
    "losing_trades": 0,
    "win_rate_pct": 0.0,
    "open_positions_count": 0,
    "open_positions": []
  }
}
```

---

## 5. CLI Flags & Runtime Configuration

```bash
# Print version and build metadata
pyramidai.autotrader-bot01.exe -version

# Query running status via HTTP API
pyramidai.autotrader-bot01.exe -status

# Run in simulated sandbox mode (Default)
pyramidai.autotrader-bot01.exe -sim

# Run connected to Live Pepperstone cTrader Open API
pyramidai.autotrader-bot01.exe -live

# Run against specific market and entries
pyramidai.autotrader-bot01.exe -market Pepperstone_Live -symbols EURUSD,GBPUSD

# Custom broker and secrets vault endpoint
pyramidai.autotrader-bot01.exe -endpoint demo.ctraderapi.com:5035 -vault http://localhost:9500

# Custom secret key names
pyramidai.autotrader-bot01.exe -secretnames PEPPERSTONE_CLIENT_ID,PEPPERSTONE_CLIENT_SECRET,PEPPERSTONE_ACCOUNT_ID,PEPPERSTONE_ACCESS_TOKEN

# Override HTTP monitoring port
pyramidai.autotrader-bot01.exe -port 9015

# Load from complete JSON configuration file
pyramidai.autotrader-bot01.exe -config C:\Batch\Configs\bot01_config.json

# Install as Windows Service with SCM 3-tier auto-recovery (5s, 10s, 30s)
pyramidai.autotrader-bot01.exe -install

# Start / Stop / Remove Windows Service
pyramidai.autotrader-bot01.exe -start
pyramidai.autotrader-bot01.exe -stop
pyramidai.autotrader-bot01.exe -remove
```

---

## 6. Build, Commit & Deployment Scripts

* **`_build.bat`**:
  1. Requests build token and registration from `http://localhost:9500/build?appName=pyramidai.autotrader-bot01`.
  2. Generates `buildinfo.cs` with Git commit SHA, branch, date, and `CookieControlToken`.
  3. Compiles executable with `dotnet publish -c Release -r win-x64`.
  4. Deploys binary to `C:\Batch\bin\pyramidai.autotrader-bot01.exe` via `C:\Batch\_deploy.bat`.

* **`_commit.bat`**:
  1. Stages, commits, and pushes changes to GitHub (`github.com/Steve-Hurst/trading-bot.git`).
  2. Invokes `C:\Batch\bin\CodeScrapper.exe` to index source AST into `[AIv1].[dbo].[CodeSymbols]` and `[AIv1].[dbo].[CodeBaseWarehouse]`.
