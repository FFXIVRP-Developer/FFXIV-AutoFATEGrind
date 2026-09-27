using AutoFateGrind.Core.Modes;

namespace AutoFateGrind.Core.Zones;

internal static class ZoneSelection
{
    public static bool GoalPlansZones(Configuration cfg) => cfg.ActiveMode.PlansZones;

    public static bool GoalNeedsRankedZones(Configuration cfg) => cfg.ActiveMode.Id == SharedFateRanksMode.ModeId;

    public static bool IsYokaiGoal(Configuration cfg) => cfg.ActiveMode.Id == YokaiMedalsMode.ModeId;

    public static bool IsSkippedByGoal(Configuration cfg, uint territoryId) => !cfg.ActiveMode.AcceptsZone(territoryId);

    public static IReadOnlyList<ZoneInfo> ResolveStartList(Configuration cfg)
    {
        if (GoalPlansZones(cfg))
        {
            return cfg.ActiveMode.PlanZones(cfg);
        }

        var byId = ZoneRegistry.Zones.ToDictionary(z => z.TerritoryId);
        return cfg.SelectedZones.Where(id => byId.ContainsKey(id) && !IsSkippedByGoal(cfg, id)).Select(id => byId[id]).ToList();
    }

    // Replaces the plan with every unlocked zone of the newest expansion the goal can use, so a fresh plan is one click.
    public static int AutoPick(Configuration cfg)
    {
        var registry = ZoneRegistry.Zones;
        for (var expansion = ExpansionKind.DT; expansion >= ExpansionKind.ARR; expansion--)
        {
            var picked = 0;
            for (var index = 0; index < registry.Length; index++)
            {
                var zone = registry[index];
                if (zone.Expansion != expansion || IsSkippedByGoal(cfg, zone.TerritoryId) || cfg.ActiveMode.IsZoneDone(zone.TerritoryId))
                {
                    continue;
                }

                if (!ZoneStateReader.IsTerritoryUnlocked(zone.TerritoryId))
                {
                    continue;
                }

                if (picked == 0)
                {
                    cfg.SelectedZones.Clear();
                }

                cfg.SelectedZones.Add(zone.TerritoryId);
                picked++;
            }

            if (picked > 0)
            {
                cfg.SaveDebounced();
                return picked;
            }
        }

        return 0;
    }
}
