using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Game.Yokai;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using AutoFateGrind.Windows.Sections;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoFateGrind.Windows.Shell;

internal static class ActionDock
{
    private const float PadX = 18f;
    private const float ButtonGap = 8f;

    public static void Draw(Plugin plugin, Vector2 size, float windowRounding)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var dl = ImGui.GetWindowDrawList();
        Dock.Background(dl, origin, end, windowRounding);

        var padX = PadX * scale;
        var buttonHeight = Layout.HeroButtonHeight * scale;
        var innerWidth = size.X - padX * 2f;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + padX, origin.Y + (size.Y - buttonHeight) * 0.5f));

        var ctrl = plugin.Controller;
        // Fork (item 18): a slave has no Start, Pause or Stop: it runs at its leader's will.
        if (plugin.Configuration.MultiboxRole == Core.Multibox.MultiboxRole.Follower) DrawSlaveLine(ctrl);
        else if (ctrl.Running) DrawRunControls(plugin, innerWidth);
        else DrawStart(plugin, innerWidth);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    private static void DrawSlaveLine(Core.Tasks.AutoFateController ctrl)
    {
        var line = ctrl.Running ? $"Slave · {Core.Multibox.MultiboxLink.FollowerStatus}"
            : Core.Multibox.MultiboxFollowerWatch.Blocked is { } why ? $"Slave · parked: {why}"
            : Core.Multibox.MultiboxFollowerWatch.Parked ? "Slave · parked: starts again when the leader does"
            : "Slave · starts and stops with the leader";
        ImGui.TextDisabled(line);
    }

    private static void DrawRunControls(Plugin plugin, float innerWidth)
    {
        var ctrl = plugin.Controller;
        var gap = ButtonGap * ImGuiHelpers.GlobalScale;
        var half = (innerWidth - gap) * 0.5f;

        if (PauseButton.Draw(ctrl.PauseReason, half)) ctrl.TogglePause();

        ImGui.SameLine(0f, gap);

        var session = ctrl.SessionSnapshot;
        var state = ctrl.Paused ? Loc.T(L.Grind.StatePaused) : Loc.T(L.Grind.StateRunning);
        var stopSub = ctrl.StopPending ? Loc.T(L.Grind.StopAfterFate)
            : session is null ? state
            : Loc.T(L.Grind.StopSub, state, Formatting.Elapsed(session.Elapsed));
        if (StopButton.Draw(stopSub, half))
        {
            if (ImGui.GetIO().KeyCtrl && !ctrl.StopPending) ctrl.StopWhenSafe();
            else ctrl.Stop();
        }

        if (ImGui.IsItemHovered()) Tooltip.Show(Loc.T(ctrl.StopPending ? L.Grind.StopNowHint : L.Grind.StopSoftHint));
    }

    private static void DrawStart(Plugin plugin, float innerWidth)
    {
        var cfg = plugin.Configuration;
        var ctrl = plugin.Controller;
        var startList = ZoneSelection.ResolveStartList(cfg);
        var depsOk = ExternalPlugins.AllRequiredInstalled();
        var yokai = ZoneSelection.IsYokaiGoal(cfg);
        var ranked = ZoneSelection.GoalNeedsRankedZones(cfg);
        var watchMissing = yokai && !YokaiOps.OwnsWatch();
        var ranksMaxed = ranked && Core.Game.SharedFates.SharedFateProgress.AllMaxed(startList);
        var itemReason = ItemGoalReason(cfg);
        var canStart = startList.Count > 0 && depsOk && !watchMissing && !ranksMaxed && itemReason is null;
        var reason = !depsOk ? Loc.T(L.Grind.ReasonInstall)
            : watchMissing ? Loc.T(L.Grind.ReasonNoWatch)
            : ranksMaxed ? Loc.T(L.Grind.ReasonRanksMaxed)
            : itemReason is not null ? itemReason
            : startList.Count > 0 ? string.Empty
            : yokai ? Loc.T(L.Grind.ReasonNoYokai)
            : ranked ? Loc.T(L.Grind.ReasonNoRankedZones)
            : Loc.T(L.Grind.ReasonPickZone);
        var sub = GoalSummary.StartSub(cfg, startList.Count);

        if (StartButton.Draw(sub, canStart, reason, innerWidth)) ctrl.RunAll(startList);
    }

    private static string? ItemGoalReason(Configuration cfg)
    {
        if (Core.Game.Items.ItemGoalCatalog.Find(cfg.ActiveMode.Id) is not { } goal) return null;
        return Core.Game.Items.ItemGoalProgress.Blocker(goal, cfg) switch
        {
            Core.Game.Items.ItemGoalBlocker.NeedQuest => Loc.T(L.Grind.ReasonNeedQuest),
            Core.Game.Items.ItemGoalBlocker.NeedZenith => Loc.T(L.Grind.ReasonNeedZenith),
            Core.Game.Items.ItemGoalBlocker.AllCollected => Loc.T(L.Grind.ReasonAllCollected),
            _ => null,
        };
    }
}
