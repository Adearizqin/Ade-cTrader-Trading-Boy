from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
SOURCE = (ROOT / "src" / "AdeRiskBot.cs").read_text(encoding="utf-8")


class CBotSafetyContractTests(unittest.TestCase):
    def test_live_account_guard_is_present(self):
        self.assertIn("if (Account.IsLive)", SOURCE)
        self.assertIn("Stop();", SOURCE)

    def test_entry_is_disabled_by_default_and_risk_is_capped(self):
        self.assertIn('[Parameter("Enable Entries", DefaultValue = false)]', SOURCE)
        self.assertIn('MaxValue = 1.0', SOURCE)
        self.assertIn('MaxValue = 3.0', SOURCE)

    def test_order_submission_is_gated_by_enable_flag_and_cutoff(self):
        self.assertIn("protected override void OnBar()", SOURCE)
        self.assertIn("if (_cutoffTriggered || !EnableEntries)", SOURCE)
        self.assertIn("ExecuteMarketOrder(signal.Value", SOURCE)

    def test_strategy_uses_closed_bar_ema_cross_and_one_position_limit(self):
        self.assertIn("Indicators.ExponentialMovingAverage(Bars.ClosePrices, FastEmaPeriod)", SOURCE)
        self.assertIn("Indicators.ExponentialMovingAverage(Bars.ClosePrices, SlowEmaPeriod)", SOURCE)
        self.assertIn("Bars.Count - 3", SOURCE)
        self.assertIn("Bars.Count - 2", SOURCE)
        self.assertIn("Positions.FindAll(BotLabel, SymbolName).Length > 0", SOURCE)

    def test_gold_symbol_filter_is_enabled_by_default(self):
        self.assertIn('[Parameter("Require Gold Symbol", DefaultValue = true)]', SOURCE)
        self.assertIn('name.Contains("XAU") || name.Contains("GOLD")', SOURCE)

    def test_daily_cutoff_close_is_retried(self):
        self.assertIn("Retry closing bot-owned positions on each timer/tick", SOURCE)
        self.assertIn("Will retry while cutoff remains active", SOURCE)

    def test_daily_cutoff_latch_is_persisted_across_restarts(self):
        self.assertIn('LocalStorage.SetString(_cutoffKey, "true", LocalStorageScope.Device);', SOURCE)
        self.assertIn('LocalStorage.GetString(_cutoffKey, LocalStorageScope.Device)', SOURCE)
        self.assertIn('_cutoffKey = _baselineKey + ".CutoffTriggered";', SOURCE)


if __name__ == "__main__":
    unittest.main()
