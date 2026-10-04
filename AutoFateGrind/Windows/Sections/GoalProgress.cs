using AutoFateGrind.Core.Game.Items;
using AutoFateGrind.Core.Game.SharedFates;
using AutoFateGrind.Core.Game.Yokai;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Trading;
using ECommons.DalamudServices;

namespace AutoFateGrind.Windows.Sections;

internal static class GoalProgress
{
    public readonly record struct Info(float? Fraction, string CenterBig, string CenterSmall, string Remaining, bool Endless);

    private const string Separator = "  \u00b7  ";

    public static Info Resolve(Configuration cfg, AutoFateSession? session)
    {
        var completed = session?.CompletedCount ?? 0;
        if (ItemGoalCatalog.Find(cfg.ActiveMode.Id) is { } goal)
        {
            return ResolveItems(cfg, goal);
        }

        switch (cfg.ActiveMode.Id)
        {
            case MaxGemstonesMode.ModeId:
            {
                var have = session?.GemstoneCurrent ?? GemstoneCatalog.CurrentWalletCount();
                var target = Math.Max(1, cfg.TargetGemstoneCount);
                var left = Math.Max(0, target - have);
                return new Info(
                    Math.Clamp(have / (float)target, 0f, 1f),
                    have.ToString(Loc.Culture), Loc.T(L.Run.GoalOf, target),
                    left > 0 ? Loc.T(L.Run.GemsToGo, left) : Loc.T(L.Run.TargetReached), false);
            }
            case PlainFatesMode.ModeId:
                return ResolveLimits(cfg, session, completed);
            case YokaiMedalsMode.ModeId:
                return ResolveYokai(cfg, session);
            case SharedFateRanksMode.ModeId:
                return ResolveSharedFates(cfg);
            default:
                return new Info(null, completed.ToString(Loc.Culture), Loc.T(L.Run.Done), Loc.T(L.Run.UntilYouStop), true);
        }
    }

    // With no target of its own, the ring follows whichever cap is on: the FATE count first, else the clock.
    private static Info ResolveLimits(Configuration cfg, AutoFateSession? session, int completed)
    {
        if (cfg.StopAfterFatesEnabled)
        {
            var target = RunLimits.FateCap(cfg);
            var left = Math.Max(0, target - completed);
            return new Info(
                Math.Clamp(completed / (float)target, 0f, 1f),
                completed.ToString(Loc.Culture), Loc.T(L.Run.GoalOf, target),
                left > 0 ? Loc.T(L.Run.FatesLeft, left) : Loc.T(L.Run.TargetReached), false);
        }

        if (cfg.StopAfterMinutesEnabled)
        {
            var targetMinutes = RunLimits.MinuteCap(cfg);
            var elapsed = session?.Elapsed ?? TimeSpan.Zero;
            var remaining = TimeSpan.FromMinutes(targetMinutes) - elapsed;
            var remainingText = remaining > TimeSpan.Zero
                ? remaining.TotalHours >= 1
                    ? Loc.T(L.Run.HoursLeft, (int)remaining.TotalHours, remaining.Minutes)
                    : Loc.T(L.Run.MinutesLeft, remaining.Minutes, remaining.Seconds)
                : Loc.T(L.Run.TimeReached);
            return new Info(
                Math.Clamp((float)(elapsed.TotalMinutes / targetMinutes), 0f, 1f),
                Loc.T(L.Run.GoalMinutes, (int)elapsed.TotalMinutes), Loc.T(L.Run.GoalOfMinutes, targetMinutes),
                remainingText, false);
        }

        if (cfg.StopAfterLevelsEnabled)
        {
            var target = RunLimits.LevelCap(cfg);
            var gained = session?.LevelsGained ?? 0;
            var left = Math.Max(0, target - gained);
            return new Info(
                Math.Clamp(gained / (float)target, 0f, 1f),
                gained.ToString(Loc.Culture), Loc.T(L.Run.GoalOf, target),
                left > 0 ? Loc.T(L.Run.LevelsLeft, left) : Loc.T(L.Run.TargetReached), false);
        }

        return new Info(null, completed.ToString(Loc.Culture), Loc.T(L.Run.Done), Loc.T(L.Run.UntilYouStop), true);
    }

    private static Info ResolveItems(Configuration cfg, ItemGoalDefinition goal)
    {
        var (have, need) = ItemGoalProgress.Totals(goal, cfg);
        var left = Math.Max(0, need - have);
        return new Info(
            need > 0 ? Math.Clamp(have / (float)need, 0f, 1f) : 1f,
            have.ToString(Loc.Culture), Loc.T(L.Run.GoalOf, need),
            left > 0 ? Loc.Plural(L.Run.ItemsToGo, left) : Loc.T(L.Run.ItemsCollected), false);
    }

    private static Info ResolveSharedFates(Configuration cfg)
    {
        var (maxed, ranked) = SharedFateProgress.CountMaxed(cfg.SelectedZones);
        var zonesLeft = Loc.Plural(L.Run.SharedZonesLeft, Math.Max(0, ranked - maxed));
        var territory = Svc.ClientState.TerritoryType;
        if (!SharedFateCatalog.HasRanks(territory))
        {
            return new Info(ranked > 0 ? maxed / (float)ranked : 0f, maxed.ToString(Loc.Culture), Loc.T(L.Run.GoalOf, ranked), zonesLeft, false);
        }

        if (!SharedFateProgress.TryGet(territory, out var rank))
        {
            return new Info(0f, "?", Loc.T(L.Run.GoalOf, SharedFateCatalog.TotalFates), string.Concat(Loc.T(L.Run.SharedSyncing), Separator, zonesLeft), false);
        }

        var detail = rank.IsMaxed
            ? Loc.T(L.Run.SharedMaxedHere)
            : Loc.T(L.Run.SharedRank, rank.Rank, rank.MaxRank, rank.RankProgress, rank.RankSize);
        return new Info(rank.Fraction, rank.Completed.ToString(Loc.Culture), Loc.T(L.Run.GoalOf, rank.Total), string.Concat(detail, Separator, zonesLeft), false);
    }

    private static Info ResolveYokai(Configuration cfg, AutoFateSession? session)
    {
        var target = YokaiProgress.MedalTarget(cfg);
        var targetIndex = YokaiProgress.ResolveTargetIndex(cfg, session?.YokaiTargetMinionId ?? 0);
        if (targetIndex < 0)
        {
            return new Info(1f, target.ToString(Loc.Culture), Loc.T(L.Run.GoalOf, target), Loc.T(L.Run.TargetReached), false);
        }

        var have = Math.Min(YokaiProgress.Statuses[targetIndex].Medals, target);
        return new Info(
            Math.Clamp(have / (float)target, 0f, 1f),
            have.ToString(Loc.Culture), Loc.T(L.Run.GoalOf, target),
            Loc.T(L.Run.YokaiToGo, YokaiProgress.MinionName(targetIndex), target - have), false);
    }
}
