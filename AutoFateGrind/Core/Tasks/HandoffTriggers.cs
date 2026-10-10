using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Core.Trading;
using AutoFateGrind.Core.Zones;

namespace AutoFateGrind.Core.Tasks;

// Run start shares the post-FATE checks so a run begun over the trade threshold or with worn gear trades or
// repairs before its first FATE, not after it (issue #90).
internal static class HandoffTriggers
{
    public static bool QueueIfDue(AutoFateSession session, ZoneInfo zone)
    {
        if (session.StopWhenSafe) return false;

        if (Plugin.Cfg.AutoRepair && RepairOps.NeedsRepair(Plugin.Cfg.AutoRepairThresholdPct))
        {
            Diag($"Repair threshold tripped (lowest equipped at {RepairOps.LowestEquippedConditionPct():F0}% ≤ {Plugin.Cfg.AutoRepairThresholdPct}%); queueing repair hand-off.");
            session.PendingRepair = true;
            session.PendingRepairFromZone = zone;
            return true;
        }

        if (Plugin.Cfg.TradeOnCap && session.GemstoneCurrent >= Plugin.Cfg.TradeThreshold && TryQueueTrade(session, zone))
            return true;

        if (Plugin.Cfg.HumanizerEnabled
         && Plugin.Cfg.HumanizerCities.Count > 0
         && session.FatesSinceLastBreak >= session.FatesBeforeNextBreak(Plugin.Cfg.HumanizerFatesBeforeBreak))
        {
            Diag($"Humanizer threshold {session.FatesBeforeNextBreak(Plugin.Cfg.HumanizerFatesBeforeBreak)} reached (configured {Plugin.Cfg.HumanizerFatesBeforeBreak}, counter {session.FatesSinceLastBreak}); queueing break hand-off.");
            session.PendingHumanize = true;
            session.PendingHumanizeFromZone = zone;
            return true;
        }

        return false;
    }

    private static bool TryQueueTrade(AutoFateSession session, ZoneInfo zone)
    {
        var targetId = GemstoneCatalog.EnsurePersistedTarget();
        if (targetId == 0)
        {
            Diag("Trade-on-cap skipped: EnsurePersistedTarget returned 0 (no gem catalog item maps to a registered Bicolor trader).");
            return false;
        }

        var target = GemstoneCatalog.FindById(targetId);
        if (target is null)
        {
            Diag($"Trade-on-cap skipped: saved target id {targetId} is not in the gem catalog (was the item removed or renamed?).");
            return false;
        }

        var qty = GemstoneCatalog.ComputeBuyQuantity(session.GemstoneCurrent, target.CostPerOne);
        if (qty <= 0)
        {
            Diag($"Trade-on-cap skipped: spend mode {Plugin.Cfg.SpendMode} with {Plugin.Cfg.KeepGemstonesReserve}g reserve buys 0× {target.ItemName} ({target.CostPerOne}g each, wallet {session.GemstoneCurrent}g).");
            return false;
        }

        var trader = GemstoneTrader.PickForItem(targetId, zone.TerritoryId, zone.Expansion, out var availability);
        if (trader is null)
        {
            Diag(availability == TraderAvailability.AllLocked
                ? $"Trade-on-cap skipped: every Bicolor trader selling {target.ItemName} stands in an unattuned zone ({GemstoneTrader.DescribeSellerZones(targetId)}). Pick a different item in /afg config → Trader."
                : $"Trade-on-cap skipped: no registered Bicolor trader sells {target.ItemName}. Pick a different item in /afg config → Trader.");
            return false;
        }

        Diag($"Gemstone threshold {Plugin.Cfg.TradeThreshold}g reached: queueing auto-trade for {qty}× {target.ItemName} at {trader.Name} (territory {trader.TerritoryId}).");
        session.PendingTradeFromZone = zone;
        return true;
    }

    private static void Diag(string message)
        => RunLog.Info(message);
}
