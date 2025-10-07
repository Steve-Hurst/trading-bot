import pandas as pd
from strategies.basic_momentum import MomentumStrategy

class Backtester:
    def __init__(self, strategy, initial_capital=100):
        self.strategy = strategy
        self.initial_capital = initial_capital

    def run(self, data: pd.DataFrame) -> pd.DataFrame:
        data = self.strategy.generate_signals(data.copy())
        data['position'] = data['signal'].shift(1)
        data['returns'] = data['Close'].pct_change()
        data['strategy_returns'] = data['position'] * data['returns']
        data['equity'] = (1 + data['strategy_returns']).cumprod() * self.initial_capital
        return data

