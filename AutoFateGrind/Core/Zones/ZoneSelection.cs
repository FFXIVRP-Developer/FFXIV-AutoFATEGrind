using AutoFateGrind.Core.Game.SharedFates;
using AutoFateGrind.Core.Game.Yokai;
using AutoFateGrind.Core.Modes;

namespace AutoFateGrind.Core.Zones;

internal static class ZoneSelection
{
    public static bool GoalPlansZones(Configuration cfg) => cfg.ActiveMode.Id == YokaiMedalsMode.ModeId;

    // Only Shadowbringers and later zones carry a Shared FATE rank; other selected zones are skipped by that goal.
    public static bool GoalNeedsRankedZones(Configuration cfg) => cfg.ActiveMode.Id == SharedFateRanksMode.ModeId;

    public static bool IsSkippedByGoal(Configuration cfg, uint territoryId)
        => GoalNeedsRankedZones(cfg) && !SharedFateCatalog.HasRanks(territoryId);

    public static IReadOnlyList<ZoneInfo> ResolveStartList(Configuration cfg)
    {
        if (GoalPlansZones(cfg))
        {
            return YokaiProgress.ZonesFor(YokaiProgress.ResolveTargetIndex(cfg, 0));
        }

        var byId = ZoneRegistry.Zones.ToDictionary(z => z.TerritoryId);
        return cfg.SelectedZones.Where(id => byId.ContainsKey(id) && !IsSkippedByGoal(cfg, id)).Select(id => byId[id]).ToList();
    }
}
