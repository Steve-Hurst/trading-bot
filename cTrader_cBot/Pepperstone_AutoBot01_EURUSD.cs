// -------------------------------------------------------------------------------------------------
// Pepperstone AutoBot 01 - EURUSD Mean Reversion cBot (.NET C#)
// Designed for cTrader Automate & Pepperstone UK
// Initial Capital: £500 | Sizing: 0.01 micro-lot | Hard Drawdown Guard: 5%
// -------------------------------------------------------------------------------------------------
using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class Pepperstone_AutoBot01_EURUSD : Robot
    {
        [Parameter("Lot Size", Group = "Risk Management", DefaultValue = 0.01, MinValue = 0.01, Step = 0.01)]
        public double QuantityLots { get; set; }

        [Parameter("Max Drawdown (%)", Group = "Risk Management", DefaultValue = 5.0, MinValue = 1.0, MaxValue = 10.0)]
        public double MaxDrawdownPct { get; set; }

        [Parameter("Stop Loss (Pips)", Group = "Risk Management", DefaultValue = 12.0)]
        public double StopLossPips { get; set; }

        [Parameter("Take Profit (Pips)", Group = "Risk Management", DefaultValue = 20.0)]
        public double TakeProfitPips { get; set; }

        [Parameter("Max Spread (Pips)", Group = "Filters", DefaultValue = 1.0)]
        public double MaxSpreadPips { get; set; }

        [Parameter("Session Start Hour (UTC)", Group = "Filters", DefaultValue = 7)]
        public int SessionStartHour { get; set; }

        [Parameter("Session End Hour (UTC)", Group = "Filters", DefaultValue = 19)]
        public int SessionEndHour { get; set; }

        [Parameter("Bollinger Period", Group = "Indicators", DefaultValue = 20)]
        public int BollingerPeriod { get; set; }

        [Parameter("Bollinger StdDev", Group = "Indicators", DefaultValue = 2.0)]
        public double BollingerStdDev { get; set; }

        [Parameter("RSI Period", Group = "Indicators", DefaultValue = 14)]
        public int RsiPeriod { get; set; }

        private BollingerBands _bollingerBands;
        private RelativeStrengthIndex _rsi;
        private double _peakEquity;
        private DateTime _lastOrderTime = DateTime.MinValue;
        private const string Label = "AutoBot01_EURUSD";

        protected override void OnStart()
        {
            _peakEquity = Account.Equity;
            _bollingerBands = Indicators.BollingerBands(Bars.ClosePrices, BollingerPeriod, BollingerStdDev, MovingAverageType.Simple);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, RsiPeriod);

            Print("Pepperstone AutoBot 01 Initialized on {0}. Initial Equity: £{1:F2}", SymbolName, Account.Equity);
        }

        protected override void OnBar()
        {
            if (Account.Equity > _peakEquity)
            {
                _peakEquity = Account.Equity;
            }

            // 1. Drawdown Guardrail Check (5% Hard Stop)
            double currentDrawdown = ((_peakEquity - Account.Equity) / _peakEquity) * 100.0;
            if (currentDrawdown >= MaxDrawdownPct)
            {
                Print("CRITICAL: Drawdown reached {0:F2}%. Maximum risk limit of {1}% breached. Halting bot.", currentDrawdown, MaxDrawdownPct);
                return;
            }

            // 2. Session Time Filter (07:00 - 19:00 UTC)
            int hourUtc = Server.Time.Hour;
            if (hourUtc < SessionStartHour || hourUtc > SessionEndHour)
            {
                return;
            }

            // 3. Spread Filter Check
            double currentSpread = (Symbol.Ask - Symbol.Bid) / Symbol.PipSize;
            if (currentSpread > MaxSpreadPips)
            {
                return;
            }

            // 4. Rate Limiting: Minimum 5 minutes between entries
            if ((Server.Time - _lastOrderTime).TotalMinutes < 5)
            {
                return;
            }

            // 5. Check if position already open
            var openPosition = Positions.Find(Label, SymbolName);
            if (openPosition != null)
            {
                return;
            }

            double lastClose = Bars.ClosePrices.Last(1);
            double upperBand = _bollingerBands.Top.Last(1);
            double lowerBand = _bollingerBands.Bottom.Last(1);
            double rsiVal = _rsi.Result.Last(1);

            // BUY Signal: Close <= Lower Band AND RSI <= 30
            if (lastClose <= lowerBand && rsiVal <= 30.0)
            {
                double volumeInUnits = Symbol.QuantityToVolumeInUnits(QuantityLots);
                var result = ExecuteMarketOrder(TradeType.Buy, SymbolName, volumeInUnits, Label, StopLossPips, TakeProfitPips);
                if (result.IsSuccessful)
                {
                    _lastOrderTime = Server.Time;
                    Print("BUY Order Executed at {0:F5} (RSI: {1:F1}, LowerBB: {2:F5})", result.Position.EntryPrice, rsiVal, lowerBand);
                }
            }
            // SELL Signal: Close >= Upper Band AND RSI >= 70
            else if (lastClose >= upperBand && rsiVal >= 70.0)
            {
                double volumeInUnits = Symbol.QuantityToVolumeInUnits(QuantityLots);
                var result = ExecuteMarketOrder(TradeType.Sell, SymbolName, volumeInUnits, Label, StopLossPips, TakeProfitPips);
                if (result.IsSuccessful)
                {
                    _lastOrderTime = Server.Time;
                    Print("SELL Order Executed at {0:F5} (RSI: {1:F1}, UpperBB: {2:F5})", result.Position.EntryPrice, rsiVal, upperBand);
                }
            }
        }
    }
}
