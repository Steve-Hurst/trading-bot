# `cookie.autotrader-bot01` - Pepperstone EUR/USD AutoTrading Bot

## 1. Overview & Purpose
`cookie.autotrader-bot01` is a high-frequency, deterministic algorithmic trading daemon built in C# (.NET 9) executing on the local `Cookie` Windows workstation. It executes an institutional-grade **Mean Reversion & Volatility Band Strategy** on **EUR/USD** with ultra-low latency and strict risk invariants.

### Key Operational Invariants:
* **Initial Account Capital:** £500.00
* **Target Asset:** EUR/USD (Major FX / Spread Betting)
* **Position Sizing:** 0.01 micro-lot ($1,000 unit base / £0.10 per point)
* **Hard Drawdown Guardrail:** ≤ 5.0% (£25.00 total account drawdown ceiling)
* **Per-Trade Stop Loss:** 12 pips (~£0.91 / ~0.18% account equity)
* **Per-Trade Take Profit:** 20 pips (~£1.52 / ~0.30% account equity)
* **Risk-to-Reward Ratio:** ≥ 1:1.67
* **Max Allowed Spread:** ≤ 1.0 pip (orders rejected during news or spread widening)
* **Active Session Window:** 07:00 – 19:00 UTC (London & NY session overlap)

---

## 2. Architecture & Ecosystem Compliance

* **App Registry Name:** `cookie.autotrader-bot01`
* **Assigned HTTP Port:** `:9011`
* **File Logs Directory:** `E:\Logs\cookie.autotrader-bot01_Logs\`
* **Database Telemetry Sink:** `[AIv1].[dbo].[ExecutionLogs]`
* **Code Warehouse Integration:** Hydrated automatically via `_Commit.bat` and indexed in `[AIv1].[dbo].[CodeSymbols]`
* **Deployment Location:** `C:\Batch\bin\cookie.autotrader-bot01.exe`
* **Windows Service Name:** `cookie.autotrader-bot01`
* **Windows Service Description:** `Steve Hurst AutoTrader Bot 01 - EURUSD Mean Reversion Pepperstone Execution Engine`

---

## 3. HTTP Monitoring & Health Endpoints (Port `:9011`)

The bot exposes an HTTP server on `http://localhost:9011` compatible with `SWV4Status` and `cookie.cookieHealthCheck`:

| Endpoint | Method | Purpose |
| :--- | :---: | :--- |
| `/status` | `GET` | Returns full JSON diagnostic state: uptime, account equity, used margin, drawdown %, open positions, win rate, daily PnL, latency. |
| `/cacherefresh` | `GET` | Reloads parameters and secrets from the local Secrets service without process restart. |
| `/pause` | `GET` | Pauses new entry signals (leaves open positions managed by stop-loss/take-profit). |
| `/resume` | `GET` | Resumes automated trading. |
| `/emergency-close` | `GET` | Immediately liquidates all open positions across the broker engine. |

### Sample `/status` JSON Response:
```json
{
  "app": "cookie.autotrader-bot01",
  "version": "1.0.0.0",
  "build_date": "2026-08-26 22:55:00",
  "status": "active",
  "broker": "Pepperstone_Simulated",
  "symbol": "EURUSD",
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

## 4. CLI Flags & Service Management

```bash
# Run in simulated sandbox mode (Default)
cookie.autotrader-bot01.exe -sim

# Run connected to Live Pepperstone cTrader Open API
cookie.autotrader-bot01.exe -live

# Check version
cookie.autotrader-bot01.exe -version

# Query running status
cookie.autotrader-bot01.exe -status

# Install as Windows Service with SCM 3-tier auto-recovery (5s, 10s, 30s)
cookie.autotrader-bot01.exe -install

# Start / Stop / Remove Windows Service
cookie.autotrader-bot01.exe -start
cookie.autotrader-bot01.exe -stop
cookie.autotrader-bot01.exe -remove
```

---

## 5. cTrader Native cBot Option
For traders preferring to run directly inside the Pepperstone cTrader desktop GUI:
* Open cTrader Automate -> New cBot
* Copy and paste the contents of [`cTrader_cBot/Pepperstone_AutoBot01_EURUSD.cs`](file:///E:/GDrive/c%23/trading-bot/cTrader_cBot/Pepperstone_AutoBot01_EURUSD.cs)
* Build and attach to EUR/USD 1-minute or 5-minute chart.
