using System;
using cAlgo.API;
using cAlgo.API.Internals;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class AdeRiskBotV2 : Robot
    {
        private const string Version = "2.0";

        [Parameter("Enable Entries", DefaultValue = true, Group = "Trading")]
        public bool EnableEntries { get; set; }

        [Parameter("Risk Per Trade (%)", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 1.0, Group = "Risk")]
        public double RiskPercent { get; set; }

        [Parameter("Daily Loss Limit (%)", DefaultValue = 3.0, MinValue = 0.5, MaxValue = 3.0, Group = "Risk")]
        public double DailyLossLimitPercent { get; set; }

        [Parameter("Daily Profit Target (%)", DefaultValue = 5.0, MinValue = 1.0, MaxValue = 10.0, Group = "Risk")]
        public double DailyProfitTargetPercent { get; set; }

        [Parameter("Max Open Positions", DefaultValue = 2, MinValue = 1, MaxValue = 5, Group = "Trading")]
        public int MaxOpenPositions { get; set; }

        [Parameter("Stop Loss (pips)", DefaultValue = 120, MinValue = 1, Group = "Risk")]
        public double StopLossPips { get; set; }

        [Parameter("Take Profit (pips)", DefaultValue = 240, MinValue = 1, Group = "Risk")]
        public double TakeProfitPips { get; set; }

        [Parameter("Trailing Stop Enabled", DefaultValue = true, Group = "Management")]
        public bool TrailingStopEnabled { get; set; }

        [Parameter("Trailing Distance (pips)", DefaultValue = 60, MinValue = 1, Group = "Management")]
        public double TrailingDistancePips { get; set; }

        [Parameter("Fast EMA Period", DefaultValue = 5, MinValue = 2, MaxValue = 100, Group = "Signal")]
        public int FastEmaPeriod { get; set; }

        [Parameter("Slow EMA Period", DefaultValue = 13, MinValue = 3, MaxValue = 300, Group = "Signal")]
        public int SlowEmaPeriod { get; set; }

        [Parameter("Require Gold Symbol", DefaultValue = true, Group = "Filter")]
        public bool RequireGoldSymbol { get; set; }

        [Parameter("Use Trading Hours Filter", DefaultValue = true, Group = "Filter")]
        public bool UseTradingHours { get; set; }

        [Parameter("Trading Start Hour (UTC)", DefaultValue = 7, MinValue = 0, MaxValue = 23, Group = "Filter")]
        public int TradingStartHour { get; set; }

        [Parameter("Trading End Hour (UTC)", DefaultValue = 21, MinValue = 0, MaxValue = 23, Group = "Filter")]
        public int TradingEndHour { get; set; }

        private const string BotLabel = "AdeRiskBotV2-Demo";
        private ExponentialMovingAverage _fastEma;
        private ExponentialMovingAverage _slowEma;
        private double _dayStartEquity;
        private string _baselineKey;
        private string _cutoffKey;
        private bool _cutoffTriggered;
        private bool _profitTargetReached;

        protected override void OnStart()
        {
            Print("AdeRiskBot Version {0} starting (demo-only, aggressive mode).", Version);

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

            _baselineKey = string.Format("AdeRiskBotV2 DayStartEquity {0} {1}",
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

            _cutoffKey = _baselineKey + " CutoffTriggered";
            _cutoffTriggered = string.Equals(
                LocalStorage.GetString(_cutoffKey, LocalStorageScope.Device), "true",
                StringComparison.OrdinalIgnoreCase);
            _profitTargetReached = false;
            CheckDailyCutoff();
            Timer.Start(1);
            Print("AdeRiskBotV2 started. EMA crossover entries are {0}. Day-start equity: {1}. Max positions: {2}",
                EnableEntries ? "ENABLED" : "OFF",
                _dayStartEquity,
                MaxOpenPositions);
            Print("Aggressive settings active. Validate thoroughly on demo before any live consideration.");
        }

        protected override void OnBar()
        {
            CheckDailyCutoff();
            if (_cutoffTriggered || _profitTargetReached || !EnableEntries)
                return;

            if (!IsWithinTradingHours())
                return;

            TryOpenEmaCrossoverTrade();
        }

        protected override void OnTimer()
        {
            CheckDailyCutoff();
            if (!_cutoffTriggered && !_profitTargetReached && TrailingStopEnabled)
                ManageTrailingStops();
        }

        protected override void OnTick()
        {
            CheckDailyCutoff();
            if (!_cutoffTriggered && !_profitTargetReached && TrailingStopEnabled)
                ManageTrailingStops();
        }

        private bool IsGoldSymbol()
        {
            var name = SymbolName.ToUpperInvariant();
            return name.Contains("XAU") || name.Contains("GOLD");
        }

        private bool IsWithinTradingHours()
        {
            if (!UseTradingHours)
                return true;

            int hour = Server.Time.Hour;
            if (TradingStartHour <= TradingEndHour)
                return hour >= TradingStartHour && hour < TradingEndHour;
            else
                return hour >= TradingStartHour || hour < TradingEndHour;
        }

        private void TryOpenEmaCrossoverTrade()
        {
            if (!EnableEntries || _cutoffTriggered || _profitTargetReached)
                return;

            if (RequireGoldSymbol && !IsGoldSymbol())
            {
                Print("ENTRY BLOCKED: current symbol is not identified as gold: {0}", SymbolName);
                return;
            }

            if (FastEmaPeriod >= SlowEmaPeriod || Bars.Count < SlowEmaPeriod + 3)
                return;

            if (Positions.FindAll(BotLabel, SymbolName).Length >= MaxOpenPositions)
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

        private bool IsDailyProfitTargetReached()
        {
            if (_dayStartEquity <= 0)
                return false;

            var profit = Account.Equity - _dayStartEquity;
            var profitTarget = _dayStartEquity * DailyProfitTargetPercent / 100.0;
            return profit >= profitTarget;
        }

        private void CheckDailyCutoff()
        {
            var expectedKey = string.Format("AdeRiskBotV2 DayStartEquity {0} {1}",
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
                _cutoffKey = _baselineKey + " CutoffTriggered";
                _cutoffTriggered = string.Equals(
                    LocalStorage.GetString(_cutoffKey, LocalStorageScope.Device), "true",
                    StringComparison.OrdinalIgnoreCase);
                _profitTargetReached = false;
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

            if (!_profitTargetReached && IsDailyProfitTargetReached())
            {
                _profitTargetReached = true;
                Print("DAILY PROFIT TARGET REACHED: equity gain reached {0}% of day-start equity. New entries blocked for the day.",
                    DailyProfitTargetPercent);
            }

            if (!_cutoffTriggered && !_profitTargetReached)
                return;

            foreach (var position in Positions.FindAll(BotLabel, SymbolName))
            {
                var result = ClosePosition(position);
                if (result.IsSuccessful)
                    Print("Limit close confirmed for position {0}.", position.Id);
                else
                    Print("WARNING: limit close failed for position {0}: {1}. Will retry while limit remains active.",
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

        private long CalculateVolumeForRisk(double stopLossPips)
        {
            if (stopLossPips <= 0 || RiskPercent <= 0 || RiskPercent > 1.0)
                return 0L;

            var riskAmount = Account.Equity * RiskPercent / 100.0;
            var rawVolume = Symbol.VolumeForFixedRisk(riskAmount, stopLossPips, RoundingMode.Down);
            var normalizedVolume = Symbol.NormalizeVolumeInUnits(rawVolume, RoundingMode.Down);

            if (normalizedVolume > Symbol.VolumeInUnitsMax)
                normalizedVolume = Symbol.VolumeInUnitsMax;

            return (long)normalizedVolume;
        }
    }
}
