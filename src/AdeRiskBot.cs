using System;
using cAlgo.API;
using cAlgo.API.Internals;

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

        private const string BotLabel = "AdeRiskBot-Demo";
        private double _dayStartEquity;
        private string _baselineKey;
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

            _cutoffTriggered = IsDailyCutoffReached();
            Timer.Start(1);
            Print("AdeRiskBot started in demo-only mode. Entries are {0}. Day-start equity: {1}",
                EnableEntries ? "configured ON but no signal engine is enabled" : "OFF",
                _dayStartEquity);
            Print("This scaffold does not submit entry orders. Validate compilation and cutoff behavior in cTrader demo first.");
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
                _cutoffTriggered = false;
                Print("New broker-server day. Day-start equity baseline: {0}", _dayStartEquity);
            }

            if (_cutoffTriggered || !IsDailyCutoffReached())
                return;

            _cutoffTriggered = true;
            Print("DAILY CUTOFF TRIGGERED: equity loss reached {0}% of day-start equity. Entries blocked.",
                DailyLossLimitPercent);

            // Close only positions opened by this bot, not unrelated manual positions.
            foreach (var position in Positions.FindAll(BotLabel, SymbolName))
            {
                var result = ClosePosition(position);
                if (result.IsSuccessful)
                    Print("Cutoff close confirmed for position {0}.", position.Id);
                else
                    Print("WARNING: cutoff close failed for position {0}: {1}. Check cTrader and retry manually if needed.",
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
