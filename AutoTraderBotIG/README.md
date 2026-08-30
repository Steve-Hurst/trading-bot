# Cookie AutoTrader Bot IG (`cookie.autotrader-ig01`)

**Application Name:** `cookie.autotrader-ig01`  
**Description:** Steve Hurst AutoTrader IG Bot 01 - Spread Betting Execution Engine (IG Index UK)  
**Port:** `9012`  
**Language / Runtime:** C# .NET 9  
**Target Market:** UK Financial Spread Betting (100% Tax-Free)  
**Default Instrument:** `CS.D.EURUSD.TODAY.IP` (EUR/USD)  
**Stake:** £0.10 / point (Micro-risk allocation on £500 initial capital)  
**Database Sink:** `[AIv1].[dbo].[ExecutionLogs]` & `[BotMetrics]`  
**Logs:** `E:\Logs\cookie.autotrader-ig01_Logs\`  

---

## 1. Quick Start & Execution Modes

### Simulated Sandbox Mode (Default)
```cmd
C:\Batch\bin\cookie.autotrader-ig01.exe -sim
```

### Live / Demo IG API Connection
```cmd
C:\Batch\bin\cookie.autotrader-ig01.exe -live -symbol CS.D.EURUSD.TODAY.IP
```

### Query Live Status
```cmd
curl http://localhost:9012/status
```

---

## 2. HTTP Endpoints (Port 9012)
* `GET /status` — Live health check, account balance, open positions, metrics
* `GET /cacherefresh` — Refresh configuration and secrets cache
* `GET /pause` — Pause strategy execution
* `GET /resume` — Resume strategy execution
* `GET /emergency-close` — Close all active positions immediately

---

## 3. Windows Service Management
```cmd
cookie.autotrader-ig01.exe -install
cookie.autotrader-ig01.exe -start
cookie.autotrader-ig01.exe -stop
cookie.autotrader-ig01.exe -remove
```
