using AutoFateGrind.Core.Game.Items;
using AutoFateGrind.Core.Game.SharedFates;
using AutoFateGrind.Core.Game.Yokai;
using AutoFateGrind.Core.Trading;
using AutoFateGrind.Core.Zones;

namespace AutoFateGrind.Core.Modes;

public sealed class MaxGemstonesMode : IFateGrindMode
{
    public const string ModeId = "maxgemstones";
    public string Id => ModeId;
    public string DisplayName => "Farm Gemstones";
    public string Description => "Stops when Bicolor Gemstones hit your target. Auto-trade resumes the grind.";
    public bool IsComplete(ModeContext ctx) => GemstoneCatalog.CurrentWalletCount() >= Plugin.Cfg.TargetGemstoneCount;

    public string? GetRemainingDisplay(ModeContext ctx)
    {
        var have = GemstoneCatalog.CurrentWalletCount();
        var target = Plugin.Cfg.TargetGemstoneCount;
        return have < target ? $"{have} / {target} gems" : null;
    }
}

// No target of its own: the run ends on a limit or when the user presses Stop.
public sealed class PlainFatesMode : IFateGrindMode
{
    public const string ModeId = "plainfates";
    public string Id => ModeId;
    public string DisplayName => "Just FATEs";
    public string Description => "Grinds your zones with no target of its own; a FATE or time limit, or Stop, ends the run.";
    public bool IsComplete(ModeContext ctx) => false;
}

public sealed class SharedFateRanksMode : IFateGrindMode
{
    public const string ModeId = "sharedfateranks";
    public string Id => ModeId;
    public string DisplayName => "Shared FATE Ranks";
    public string Description => "Grinds each selected zone until its Shared FATE rank is maxed, then moves on to the next one, and stops once every zone with ranks is maxed.";
    public bool IsComplete(ModeContext ctx) => SharedFateProgress.AllMaxed(ctx.Zones);

    public string? GetRemainingDisplay(ModeContext ctx)
    {
        var remaining = SharedFateProgress.CountUnmaxed(ctx.Zones);
        return remaining > 0 ? $"{remaining} zone{(remaining == 1 ? "" : "s")} left" : null;
    }

    public bool AcceptsZone(uint territoryId) => SharedFateCatalog.HasRanks(territoryId);

    public bool IsZoneDone(uint territoryId) => SharedFateProgress.IsMaxed(territoryId);

    public string ZoneDoneReason(uint territoryId) => "Shared FATE rank maxed";
}

internal sealed class ItemGoalMode(ItemGoalDefinition definition) : IFateGrindMode
{
    public ItemGoalDefinition Definition => definition;
    public string Id => definition.ModeId;
    public string DisplayName => definition.DisplayName;
    public string Description => "Grinds the zones where the items still drop, moves on once a zone has nothing left to give, and stops when every item is collected.";

    // Losing the drop condition mid-run (quest turned in, relic unequipped) ends the run instead of grinding for nothing.
    public bool IsComplete(ModeContext ctx) => !ItemGoalProgress.IsAvailable(definition) || ItemGoalProgress.AllCollected(definition, Plugin.Cfg);

    public string? GetRemainingDisplay(ModeContext ctx)
    {
        var left = ItemGoalProgress.ItemsLeft(definition, Plugin.Cfg);
        return left > 0 ? $"{left} item{(left == 1 ? "" : "s")} left" : null;
    }

    public bool PlansZones => true;

    public IReadOnlyList<ZoneInfo> PlanZones(Configuration cfg) => ItemGoalProgress.PlanZones(definition, cfg);

    public bool AcceptsZone(uint territoryId) => ItemGoalProgress.DropsHere(definition, territoryId);

    public bool IsZoneDone(uint territoryId) => ItemGoalProgress.IsZoneDone(definition, territoryId, Plugin.Cfg);

    public string ZoneDoneReason(uint territoryId) => "every item that drops here is collected";
}

public sealed class YokaiMedalsMode : IFateGrindMode
{
    public const string ModeId = "yokaimedals";
    public string Id => ModeId;
    public string DisplayName => "Yo-kai Medals";
    public string Description => "Farms Legendary Medals for every Yo-kai minion you own, summoning each minion and travelling to its zones, and stops once all of them reach your target.";
    public bool IsComplete(ModeContext ctx) => YokaiProgress.IsComplete(Plugin.Cfg);

    public string? GetRemainingDisplay(ModeContext ctx)
    {
        var (collected, needed) = YokaiProgress.Totals(Plugin.Cfg);
        return collected < needed ? $"{needed - collected} medals left" : null;
    }

    public bool PlansZones => true;

    // Fork: every zone an unfinished minion drops in (was the first target's three); the minion is picked per zone.
    public IReadOnlyList<ZoneInfo> PlanZones(Configuration cfg) => YokaiProgress.ZonesForAll(cfg);

    // Fork: a zone is done once no unfinished minion drops there; the run rotates past it.
    public bool IsZoneDone(uint territoryId) => YokaiProgress.IsZoneDone(Plugin.Cfg, territoryId);

    public string ZoneDoneReason(uint territoryId) => "no yo-kai that still needs medals drops here";
}
