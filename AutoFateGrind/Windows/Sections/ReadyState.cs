using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Game.Items;
using AutoFateGrind.Core.Game.SharedFates;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

internal static class ReadyState
{
    public enum Kind { SetupNeeded, PickZones, NothingToFarm, Blocked, Ready, Running, Paused }

    public readonly record struct Info(Kind Kind, Vector4 Accent, Vector4 AccentSoft, FontAwesomeIcon Icon, string Title, string Detail);

    private static int cachedFrame = -1;
    private static Info cached;

    public static Info Resolve(Configuration cfg, AutoFateController ctrl)
    {
        var frame = ImGui.GetFrameCount();
        if (frame == cachedFrame) return cached;

        cached = Compute(cfg, ctrl);
        cachedFrame = frame;
        return cached;
    }

    private static Info Compute(Configuration cfg, AutoFateController ctrl)
    {
        if (ctrl.Running)
        {
            if (ctrl.Paused)
            {
                var detail = ctrl.PauseReason == PauseReason.InContent
                    ? Loc.T(L.Grind.DetailPausedInContent)
                    : Loc.T(L.Grind.DetailPausedManual);
                return new Info(Kind.Paused, Styling.AccentAmber, Styling.AccentAmberSoft, FontAwesomeIcon.Pause, Loc.T(L.Grind.TitlePaused), detail);
            }

            return new Info(Kind.Running, Styling.AccentBlue, Styling.AccentBlueSoft, FontAwesomeIcon.Bolt, Loc.T(L.Grind.TitleRunning), PhaseLabel(ctrl.Phase));
        }

        if (!ExternalPlugins.AllRequiredInstalled())
        {
            return new Info(Kind.SetupNeeded, Styling.AccentRose, Styling.AccentRoseSoft, FontAwesomeIcon.ExclamationTriangle,
                Loc.T(L.Grind.TitleSetupNeeded), Loc.T(L.Grind.DetailSetupNeeded));
        }

        if (ItemGoalCatalog.Find(cfg.ActiveMode.Id) is { } goal && ItemBlocker(goal, cfg) is { } blocked)
        {
            return blocked;
        }

        var zones = ZoneSelection.ResolveStartList(cfg);
        if (zones.Count == 0 && ZoneSelection.IsYokaiGoal(cfg))
        {
            return new Info(Kind.NothingToFarm, Styling.AccentAmber, Styling.AccentAmberSoft, FontAwesomeIcon.Ghost,
                Loc.T(L.Grind.TitleNoYokai), Loc.T(L.Grind.DetailNoYokai));
        }

        if (zones.Count == 0 && ZoneSelection.GoalNeedsRankedZones(cfg))
        {
            return new Info(Kind.PickZones, Styling.AccentAmber, Styling.AccentAmberSoft, FontAwesomeIcon.MapMarkedAlt,
                Loc.T(L.Grind.TitleNoRankedZones), Loc.T(L.Grind.DetailNoRankedZones));
        }

        if (zones.Count == 0)
        {
            return new Info(Kind.PickZones, Styling.AccentAmber, Styling.AccentAmberSoft, FontAwesomeIcon.MapMarkedAlt,
                Loc.T(L.Grind.TitlePickZones), Loc.T(L.Grind.DetailPickZones));
        }

        if (ZoneSelection.GoalNeedsRankedZones(cfg))
        {
            SharedFateProgress.Request(zones);
        }

        if (ZoneSelection.GoalNeedsRankedZones(cfg) && SharedFateProgress.AllMaxed(zones))
        {
            return new Info(Kind.NothingToFarm, Styling.AccentAmber, Styling.AccentAmberSoft, FontAwesomeIcon.Trophy,
                Loc.T(L.Grind.TitleRanksMaxed), Loc.T(L.Grind.DetailRanksMaxed));
        }

        return new Info(Kind.Ready, Styling.AccentMint, Styling.AccentMintSoft, FontAwesomeIcon.CheckCircle,
            Loc.T(L.Grind.TitleReady), Loc.T(L.Grind.DetailReady));
    }

    private static Info? ItemBlocker(ItemGoalDefinition goal, Configuration cfg) => ItemGoalProgress.Blocker(goal, cfg) switch
    {
        ItemGoalBlocker.NeedQuest => new Info(Kind.Blocked, Styling.AccentAmber, Styling.AccentAmberSoft, FontAwesomeIcon.Scroll,
            Loc.T(L.Grind.TitleNeedQuest), Loc.T(L.Grind.DetailNeedQuest, ItemGoalProgress.QuestName(goal.QuestId))),
        ItemGoalBlocker.NeedZenith => new Info(Kind.Blocked, Styling.AccentAmber, Styling.AccentAmberSoft, FontAwesomeIcon.Gavel,
            Loc.T(L.Grind.TitleNeedZenith), Loc.T(L.Grind.DetailNeedZenith)),
        ItemGoalBlocker.AllCollected => new Info(Kind.NothingToFarm, Styling.AccentMint, Styling.AccentMintSoft, FontAwesomeIcon.CheckCircle,
            Loc.T(L.Grind.TitleAllCollected), Loc.T(L.Grind.DetailAllCollected)),
        _ => null,
    };

    public static string ShortLabel(Kind kind) => kind switch
    {
        Kind.Running       => Loc.T(L.Shell.StatusRunning),
        Kind.Paused        => Loc.T(L.Shell.StatusPaused),
        Kind.Ready         => Loc.T(L.Shell.StatusReady),
        Kind.PickZones     => Loc.T(L.Shell.StatusPickZones),
        Kind.NothingToFarm => Loc.T(L.Shell.StatusNothingToFarm),
        Kind.Blocked       => Loc.T(L.Shell.StatusBlocked),
        Kind.SetupNeeded   => Loc.T(L.Shell.StatusSetupNeeded),
        _                  => Loc.T(L.Shell.StatusIdle),
    };

    public static string PhaseLabel(AutoPhase phase) => phase switch
    {
        AutoPhase.Trading    => Loc.T(L.Run.PhaseTrading),
        AutoPhase.Repairing  => Loc.T(L.Run.PhaseRepairing),
        AutoPhase.Humanizing => Loc.T(L.Run.PhaseBreak),
        AutoPhase.OceanTrip  => "Ocean fishing", // Fork
        AutoPhase.Finishing  => Loc.T(L.Run.PhaseFinishing),
        AutoPhase.Grinding   => Loc.T(L.Run.PhaseGrinding),
        _                    => Loc.T(L.Run.PhaseStandingBy),
    };
}
