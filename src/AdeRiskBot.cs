using System;
using cAlgo.API;
using cAlgo.API.Internals;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class AdeRiskBot : Robot
    {
        [Parameter("Enable Entries", DefaultValue = false)]
        public bool EnableEntries { get; set; }

        [Parameter("Risk Per Trade (%)", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 1.0)]
        public double RiskPercent { get; set; }

        [Parameter("Daily Loss Limit (%)", DefaultValue = 3.0, MinValue = 0.5, MaxValue = 3.0)]
        public double DailyLossLimitPercent { get; set; }

        [Parameter("Stop Loss (pips)", DefaultValue = 250, MinValue = 1)]
        public double StopLossPips { get; set; }

        [Parameter("Take Profit (pips)", DefaultValue = 500, MinValue = 1)]
        public double TakeProfitPips { get; set; }

        [Parameter("Trailing Stop Enabled", DefaultValue = true)]
        public bool TrailingStopEnabled { get; set; }

        [Parameter("Trailing Distance (pips)", DefaultValue = 150, MinValue = 1)]
        public double TrailingDistancePips { get; set; }

        [Parameter("Fast EMA Period", DefaultValue = 9, MinValue = 2, MaxValue = 100)]
        public int FastEmaPeriod { get; set; }

        [Parameter("Slow EMA Period", DefaultValue = 21, MinValue = 3, MaxValue = 300)]
        public int SlowEmaPeriod { get; set; }

        [Parameter("Require Gold Symbol", DefaultValue = true)]
        public bool RequireGoldSymbol { get; set; }

        private const string BotLabel = "AdeRiskBot-Demo";
        private ExponentialMovingAverage _fastEma;
        private ExponentialMovingAverage _slowEma;
        private double _dayStartEquity;
        private string _baselineKey;
        private string _cutoffKey;
        private bool _cutoffTriggered;

        protected override void OnStart()
        {
            // Fail closed: this bot is not permitted to run on a live account.
            if (Account.IsLive)
            {
                Print("SAFETY STOP: live account detected. This bot is demo-only.");
                Stop();
                return;
            }

            if (RiskPercent > 1.0 || DailyLossLimitPercent > 3.0)
            {
                Print("SAFETY STOP: configured risk exceeds project limits.");
                Stop();
                return;
            }

            _baselineKey = string.Format("AdeRiskBot.DayStartEquity.{0}.{1}",
                Account.Number, Server.Time.ToString("yyyyMMdd"));

            var savedBaseline = LocalStorage.GetString(_baselineKey, LocalStorageScope.Device);
            if (!string.IsNullOrWhiteSpace(savedBaseline) &&
                double.TryParse(savedBaseline, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
                parsed > 0)
            {
                _dayStartEquity = parsed;
            }
            else
            {
                _dayStartEquity = Account.Equity;
                LocalStorage.SetString(_baselineKey,
                    _dayStartEquity.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    LocalStorageScope.Device);
                LocalStorage.Flush(LocalStorageScope.Device);
            }

            if (FastEmaPeriod >= SlowEmaPeriod)
            {
                Print("SAFETY STOP: Fast EMA Period must be lower than Slow EMA Period.");
                Stop();
                return;
            }

            _fastEma = Indicators.ExponentialMovingAverage(Bars.ClosePrices, FastEmaPeriod);
            _slowEma = Indicators.ExponentialMovingAverage(Bars.ClosePrices, SlowEmaPeriod);

            _cutoffKey = _baselineKey + ".CutoffTriggered";
            _cutoffTriggered = string.Equals(
                LocalStorage.GetString(_cutoffKey, LocalStorageScope.Device), "true",
                StringComparison.OrdinalIgnoreCase);
            CheckDailyCutoff();
            Timer.Start(1);
            Print("AdeRiskBot started in demo-only mode. EMA crossover entries are {0}. Day-start equity: {1}",
                EnableEntries ? "ENABLED BY PARAMETER" : "OFF",
                _dayStartEquity);
            Print("Experimental EMA crossover only. Keep entries off until this cBot compiles and passes demo validation.");
        }

        protected override void OnBar()
        {
            CheckDailyCutoff();
            if (_cutoffTriggered || !EnableEntries)
                return;

            TryOpenEmaCrossoverTrade();
        }

        protected override void OnTimer()
        {
            CheckDailyCutoff();
            if (!_cutoffTriggered && TrailingStopEnabled)
                ManageTrailingStops();
        }

        protected override void OnTick()
        {
            CheckDailyCutoff();
            if (!_cutoffTriggered && TrailingStopEnabled)
                ManageTrailingStops();
        }

        private bool IsGoldSymbol()
        {
            var name = SymbolName.ToUpperInvariant();
            return name.Contains("XAU") || name.Contains("GOLD");
        }

        private void TryOpenEmaCrossoverTrade()
        {
            // Hard gates: demo-only guard is enforced in OnStart; entries must be enabled,
            // daily cutoff must be inactive, and this is restricted to a gold symbol by default.
            if (!EnableEntries || _cutoffTriggered)
                return;

            if (RequireGoldSymbol && !IsGoldSymbol())
            {
                Print("ENTRY BLOCKED: current symbol is not identified as gold: {0}", SymbolName);
                return;
            }

            if (FastEmaPeriod >= SlowEmaPeriod || Bars.Count < SlowEmaPeriod + 3)
                return;

            // One bot-owned position at a time for the current symbol.
            if (Positions.FindAll(BotLabel, SymbolName).Length > 0)
                return;

            var previousClosed = Bars.Count - 3;
            var lastClosed = Bars.Count - 2;
            var fastPrevious = _fastEma.Result[previousClosed];
            var slowPrevious = _slowEma.Result[previousClosed];
            var fastLast = _fastEma.Result[lastClosed];
            var slowLast = _slowEma.Result[lastClosed];

            TradeType? signal = null;
            if (fastPrevious <= slowPrevious && fastLast > slowLast)
                signal = TradeType.Buy;
            else if (fastPrevious >= slowPrevious && fastLast < slowLast)
                signal = TradeType.Sell;

            if (signal == null)
                return;

            var volume = CalculateVolumeForRisk(StopLossPips);
            if (volume < Symbol.VolumeInUnitsMin)
            {
                Print("ENTRY BLOCKED: calculated volume is below broker minimum. Check symbol contract and stop-loss settings.");
                return;
            }

            // Stop-loss and take-profit are attached to the market order in pips.
            var result = ExecuteMarketOrder(signal.Value, SymbolName, volume, BotLabel,
                StopLossPips, TakeProfitPips);

            if (result.IsSuccessful)
                Print("DEMO ENTRY CONFIRMED: {0} {1}, volume {2}, SL {3} pips, TP {4} pips.",
                    signal.Value, SymbolName, volume, StopLossPips, TakeProfitPips);
            else
                Print("ENTRY FAILED: {0} {1}: {2}", signal.Value, SymbolName, result.Error);
        }

        private bool IsDailyCutoffReached()
        {
            if (_dayStartEquity <= 0)
                return true;

            var loss = _dayStartEquity - Account.Equity;
            var lossLimit = _dayStartEquity * DailyLossLimitPercent / 100.0;
            return loss >= lossLimit;
        }

        private void CheckDailyCutoff()
        {
            // Baseline is keyed by broker-server date and persisted on device.
            // On a new server day, capture and persist a new baseline.
            var expectedKey = string.Format("AdeRiskBot.DayStartEquity.{0}.{1}",
                Account.Number, Server.Time.ToString("yyyyMMdd"));

            if (!string.Equals(expectedKey, _baselineKey, StringComparison.Ordinal))
            {
                _baselineKey = expectedKey;
                var savedBaseline = LocalStorage.GetString(_baselineKey, LocalStorageScope.Device);
                if (!string.IsNullOrWhiteSpace(savedBaseline) &&
                    double.TryParse(savedBaseline, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
                    parsed > 0)
                {
                    _dayStartEquity = parsed;
                }
                else
                {
                    _dayStartEquity = Account.Equity;
                    LocalStorage.SetString(_baselineKey,
                        _dayStartEquity.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        LocalStorageScope.Device);
                    LocalStorage.Flush(LocalStorageScope.Device);
                }
                _cutoffKey = _baselineKey + ".CutoffTriggered";
                _cutoffTriggered = string.Equals(
                    LocalStorage.GetString(_cutoffKey, LocalStorageScope.Device), "true",
                    StringComparison.OrdinalIgnoreCase);
                Print("New broker-server day. Day-start equity baseline: {0}", _dayStartEquity);
            }

            if (!_cutoffTriggered && IsDailyCutoffReached())
            {
                _cutoffTriggered = true;
                LocalStorage.SetString(_cutoffKey, "true", LocalStorageScope.Device);
                LocalStorage.Flush(LocalStorageScope.Device);
                Print("DAILY CUTOFF TRIGGERED: equity loss reached {0}% of day-start equity. Entries blocked.",
                    DailyLossLimitPercent);
            }

            if (!_cutoffTriggered)
                return;

            // Retry closing bot-owned positions on each timer/tick while cutoff remains active.
            // A transient broker/network failure must not permanently suppress further attempts.
            foreach (var position in Positions.FindAll(BotLabel, SymbolName))
            {
                var result = ClosePosition(position);
                if (result.IsSuccessful)
                    Print("Cutoff close confirmed for position {0}.", position.Id);
                else
                    Print("WARNING: cutoff close failed for position {0}: {1}. Will retry while cutoff remains active.",
                        position.Id, result.Error);
            }
        }

        private void ManageTrailingStops()
        {
            foreach (var position in Positions.FindAll(BotLabel, SymbolName))
            {
                var distance = TrailingDistancePips * Symbol.PipSize;
                double proposedStop;

                if (position.TradeType == TradeType.Buy)
                {
                    proposedStop = Symbol.Bid - distance;
                    if (position.StopLoss == null || proposedStop > position.StopLoss.Value + Symbol.TickSize)
                    {
                        var result = ModifyPosition(position, proposedStop, position.TakeProfit, ProtectionType.Absolute);
                        if (!result.IsSuccessful)
                            Print("Trailing-stop update failed for position {0}: {1}", position.Id, result.Error);
                    }
                }
                else
                {
                    proposedStop = Symbol.Ask + distance;
                    if (position.StopLoss == null || proposedStop < position.StopLoss.Value - Symbol.TickSize)
                    {
                        var result = ModifyPosition(position, proposedStop, position.TakeProfit, ProtectionType.Absolute);
                        if (!result.IsSuccessful)
                            Print("Trailing-stop update failed for position {0}: {1}", position.Id, result.Error);
                    }
                }
            }
        }

        // Utility for a future reviewed signal engine. Not used to submit orders in this scaffold.
        private double CalculateVolumeForRisk(double stopLossPips)
        {
            if (stopLossPips <= 0 || RiskPercent <= 0 || RiskPercent > 1.0)
                return 0;

            var riskAmount = Account.Equity * RiskPercent / 100.0;
            var volume = Symbol.VolumeForFixedRisk(riskAmount, stopLossPips, RoundingMode.Down);
            return Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);
        }
    }
}
