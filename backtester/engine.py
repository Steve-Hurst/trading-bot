"""
Institutional Python Backtest Engine for AI AutoTrader
Reads seed_data.json, simulates execution, and computes performance metrics.
"""
import os
import json
import pandas as pd
import numpy as np
from typing import Dict, Any, Optional
from metrics import calculate_metrics

class PythonBacktester:
    def __init__(self, initial_capital: float = 500.0):
        self.initial_capital = initial_capital

    def load_seed_data(self, file_path: str) -> pd.DataFrame:
        if not os.path.exists(file_path):
            raise FileNotFoundError(f"Seed data file not found: {file_path}")
            
        with open(file_path, "r", encoding="utf-8") as f:
            data = json.load(f)
            
        bars = data.get("bars", [])
        df = pd.DataFrame(bars)
        if "time" in df.columns:
            df["time"] = pd.to_datetime(df["time"])
            df.set_index("time", inplace=True)
        return df

    def run_mean_reversion(self, df: pd.DataFrame, bb_period: int = 20, bb_std: float = 2.0, rsi_period: int = 14) -> Dict[str, Any]:
        # 1. Bollinger Bands
        df["sma"] = df["close"].rolling(window=bb_period).mean()
        df["std"] = df["close"].rolling(window=bb_period).std()
        df["upper_bb"] = df["sma"] + (bb_std * df["std"])
        df["lower_bb"] = df["sma"] - (bb_std * df["std"])
        
        # 2. RSI
        delta = df["close"].diff()
        gain = (delta.where(delta > 0, 0)).rolling(window=rsi_period).mean()
        loss = (-delta.where(delta < 0, 0)).rolling(window=rsi_period).mean()
        rs = gain / loss
        df["rsi"] = 100 - (100 / (1 + rs))
        
        # 3. Signals
        df["signal"] = 0
        df.loc[(df["close"] <= df["lower_bb"]) & (df["rsi"] <= 30), "signal"] = 1   # BUY
        df.loc[(df["close"] >= df["upper_bb"]) & (df["rsi"] >= 70), "signal"] = -1  # SELL
        
        # 4. Position & Returns
        df["position"] = df["signal"].shift(1).fillna(0)
        df["market_return"] = df["close"].pct_change().fillna(0)
        df["strategy_return"] = df["position"] * df["market_return"]
        df["equity"] = (1 + df["strategy_return"]).cumprod() * self.initial_capital
        
        # 5. Metrics
        metrics = calculate_metrics(df["strategy_return"], df["equity"])
        metrics["initial_capital"] = self.initial_capital
        metrics["final_equity"] = round(float(df["equity"].iloc[-1]), 2)
        metrics["net_profit_gbp"] = round(metrics["final_equity"] - self.initial_capital, 2)
        
        return metrics

if __name__ == "__main__":
    seed_file = os.path.join(os.path.dirname(__file__), "..", "data", "seed_data.json")
    if os.path.exists(seed_file):
        engine = PythonBacktester(initial_capital=500.0)
        df = engine.load_seed_data(seed_file)
        results = engine.run_mean_reversion(df)
        print("Python AI Backtester Results:")
        print(json.dumps(results, indent=2))
