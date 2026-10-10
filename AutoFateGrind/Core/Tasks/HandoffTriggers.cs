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
        var plan = TradeList.Plan(session.GemstoneCurrent, zone.TerritoryId, zone.Expansion, session.TradeSkippedItemIds);
        if (plan is null)
        {
            Diag($"Trade-on-cap skipped, nothing on the shopping list can be bought now: {TradeList.DescribeBlocked(session.GemstoneCurrent, session.TradeSkippedItemIds)}.");
            return false;
        }

        Diag($"Gemstone threshold {Plugin.Cfg.TradeThreshold}g reached: queueing auto-trade at {plan.Trader.Name} (territory {plan.Trader.TerritoryId}) for {plan.DescribeItems()}.");
        session.PendingTradeFromZone = zone;
        return true;
    }

    private static void Diag(string message)
        => RunLog.Info(message);
}
