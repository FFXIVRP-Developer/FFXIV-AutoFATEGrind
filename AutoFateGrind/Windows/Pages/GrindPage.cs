using AutoFateGrind.Core.Game.Items;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Sections;
using AutoFateGrind.Windows.Shell;

namespace AutoFateGrind.Windows.Pages;

internal sealed class GrindPage
{
    private const float SwitchRevealMs = 320f;

    public void Draw(Plugin plugin, AppWindow window)
    {
        var cfg = plugin.Configuration;
        var ctrl = plugin.Controller;
        var running = ctrl.Running;

        using var reveal = Motion.PushSwitch("##afg_grind_state", running, SwitchRevealMs);
        if (running) RunningPanel.Draw(cfg, ctrl);
        else DrawIdle(plugin, window, cfg, ctrl);
    }

    private static void DrawIdle(Plugin plugin, AppWindow window, Configuration cfg, AutoFateController ctrl)
    {
        switch (Headline.Draw(cfg, ctrl, plugin.History))
        {
            case Headline.Action.OpenPlugins: window.Show(AppWindow.Page.Plugins); break;
            case Headline.Action.AutoPick: ZoneSelection.AutoPick(cfg); break;
        }

        Styling.VSpace(20f);

        var zoneCount = ZoneSelection.ResolveStartList(cfg).Count;
        GoalSection.Draw(cfg, ctrl, zoneCount);
        Styling.VSpace(14f);
        StopRow.Draw(cfg, ctrl);
        Styling.VSpace(26f);

        if (ItemGoalCatalog.IsItemGoal(cfg.ActiveMode.Id)) ItemGoalRoster.Draw(cfg, ctrl, false);
        else if (ZoneSelection.GoalPlansZones(cfg)) YokaiRoster.Draw(cfg, ctrl, false);
        else ZoneLibrary.Draw(cfg, ctrl, false);
        Styling.VSpace(12f);
    }
}
