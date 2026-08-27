"""
Quantitative Performance Metrics Calculator for AI Supervised Backtesting
Standard mathematical metrics: Sharpe, Sortino, Profit Factor, Max Drawdown
"""
import numpy as np
import pandas as pd
from typing import Dict, Any

def calculate_metrics(returns: pd.Series, equity_curve: pd.Series, risk_free_rate: float = 0.04) -> Dict[str, Any]:
    if returns.empty or len(returns) < 2:
        return {
            "sharpe_ratio": 0.0,
            "sortino_ratio": 0.0,
            "max_drawdown_pct": 0.0,
            "profit_factor": 0.0,
            "win_rate_pct": 0.0
        }
    
    # 1. Total & Annualized Return
    total_return = (equity_curve.iloc[-1] / equity_curve.iloc[0]) - 1.0
    
    # 2. Sharpe Ratio (Annualized assuming 252 trading days / 1440 1-min bars)
    rf_per_period = risk_free_rate / 252
    excess_returns = returns - rf_per_period
    std_dev = returns.std()
    sharpe = (excess_returns.mean() / std_dev * np.sqrt(252)) if std_dev > 0 else 0.0
    
    # 3. Sortino Ratio (Downside deviation only)
    downside_returns = returns[returns < 0]
    downside_std = downside_returns.std()
    sortino = (excess_returns.mean() / downside_std * np.sqrt(252)) if downside_std > 0 else 0.0
    
    # 4. Max Drawdown
    rolling_peak = equity_curve.cummax()
    drawdown = (equity_curve - rolling_peak) / rolling_peak
    max_drawdown_pct = abs(drawdown.min()) * 100.0 if not drawdown.empty else 0.0
    
    # 5. Profit Factor
    gains = returns[returns > 0].sum()
    losses = abs(returns[returns < 0].sum())
    profit_factor = (gains / losses) if losses > 0 else (99.0 if gains > 0 else 1.0)
    
    # 6. Win Rate
    total_trades = len(returns[returns != 0])
    wins = len(returns[returns > 0])
    win_rate_pct = (wins / total_trades * 100.0) if total_trades > 0 else 0.0
    
    return {
        "total_return_pct": round(float(total_return * 100.0), 2),
        "sharpe_ratio": round(float(sharpe), 2),
        "sortino_ratio": round(float(sortino), 2),
        "max_drawdown_pct": round(float(max_drawdown_pct), 2),
        "profit_factor": round(float(profit_factor), 2),
        "win_rate_pct": round(float(win_rate_pct), 2),
        "total_trades": int(total_trades)
    }
