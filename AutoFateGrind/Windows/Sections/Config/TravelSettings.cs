using AutoFateGrind.Core.Game.Player;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Windows.Components;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class TravelSettings
{
    private const float MountComboWidth = 220f;
    private const int MountListRefreshMs = 2000;
    private const int SwapWaitMinSec = 0;
    private const int SwapWaitMaxSec = 300;
    private const int CollectHandInBatchMin = 1;
    // Nine keeps AFG's trip ahead of BossMod's fixed 10-item hand-in, so the two never race for the NPC.
    private const int CollectHandInBatchMax = 9;

    private static MountOption[] mountOptions = [];
    private static string[] mountLabels = [];
    private static long mountListBuiltAtMs;

    public static void Draw(Configuration cfg)
    {
        DrawMountGroup(cfg);
        DrawZoneSwapGroup(cfg);
        DrawFatePlayGroup(cfg);
    }

    private static void DrawMountGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.TravelMount));

        DrawMountRow(cfg);

        SettingsRow.Draw(Loc.T(L.Settings.MountWhileWaiting),
            Loc.T(L.Settings.MountWhileWaitingHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.MountWhileWaitingForFates, v => cfg.MountWhileWaitingForFates = v, "##travel_mount_waiting"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.EconomyTravel),
            Loc.T(L.Settings.EconomyTravelHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.EconomyTravel, v => cfg.EconomyTravel = v, "##travel_economy"),
            SettingsRow.ToggleHeight);
    }

    private static void DrawMountRow(Configuration cfg)
    {
        SettingsRow.Draw(Loc.T(L.Settings.Mount),
            Loc.T(L.Settings.MountHelp),
            MountComboWidth,
            () =>
            {
                RefreshMountList();
                var selected = 0;
                for (var index = 0; index < mountOptions.Length; index++)
                {
                    if (mountOptions[index].Id == cfg.PreferredMountId)
                    {
                        selected = index + 1;
                        break;
                    }
                }

                if (SettingsControls.DrawSearchableCombo("##travel_mount", mountLabels, ref selected, MountComboWidth))
                {
                    cfg.PreferredMountId = selected == 0 ? MountOps.Roulette : mountOptions[selected - 1].Id;
                    cfg.SaveDebounced();
                }
            });

        if (cfg.PreferredMountId != MountOps.Roulette && !MountOps.IsUsable(cfg.PreferredMountId))
        {
            SettingsRow.Note(Loc.T(L.Settings.MountNotOwned, MountOps.NameOf(cfg.PreferredMountId)), Styling.AccentRose);
        }
    }

    // Rebuilt on a timer so a mount unlocked or a language switched while the page is open shows up.
    private static void RefreshMountList()
    {
        var now = Environment.TickCount64;
        if (mountLabels.Length > 0 && now - mountListBuiltAtMs < MountListRefreshMs)
        {
            return;
        }
        mountListBuiltAtMs = now;
        mountOptions = MountOps.OwnedMounts();
        mountLabels = new string[mountOptions.Length + 1];
        mountLabels[0] = Loc.T(L.Settings.MountRandom);
        for (var index = 0; index < mountOptions.Length; index++)
        {
            mountLabels[index + 1] = mountOptions[index].Name;
        }
    }

    private static void DrawZoneSwapGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.TravelZoneSwap));

        SettingsRow.Draw(Loc.T(L.Settings.SwapZones),
            Loc.T(L.Settings.SwapZonesHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.SwapZonesWhenEmpty, v => cfg.SwapZonesWhenEmpty = v, "##travel_swap"),
            SettingsRow.ToggleHeight);

        using (Motion.PushSwitch("##travel_swap_body", cfg.SwapZonesWhenEmpty))
        {
            if (cfg.SwapZonesWhenEmpty)
            {
                SettingsRow.Draw(Loc.T(L.Settings.SwapWait),
                    Loc.T(L.Settings.SwapWaitHelp),
                    SettingsControls.RowSliderWidth,
                    () => SettingsControls.DrawIntSlider(cfg, "##travel_swap_wait",
                        () => cfg.SwapZoneWaitSec, v => cfg.SwapZoneWaitSec = v, SwapWaitMinSec, SwapWaitMaxSec, Loc.T(L.Settings.MinTimeFormat)));
            }
        }
    }

    private static void DrawFatePlayGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.TravelFatePlay));

        SettingsRow.Draw(Loc.T(L.Settings.KeepTwist),
            Loc.T(L.Settings.KeepTwistHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.KeepTwistOfFate, v => cfg.KeepTwistOfFate = v, "##travel_keeptwist"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.CollectHandIn),
            Loc.T(L.Settings.CollectHandInHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.CollectHandInEnabled, v => cfg.CollectHandInEnabled = v, "##travel_collect_handin"),
            SettingsRow.ToggleHeight);

        if (!cfg.CollectHandInEnabled)
        {
            return;
        }

        SettingsRow.Draw(Loc.T(L.Settings.CollectHandInBatch),
            Loc.T(L.Settings.CollectHandInBatchHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##travel_collect_batch",
                () => cfg.CollectHandInBatch, v => cfg.CollectHandInBatch = v,
                CollectHandInBatchMin, CollectHandInBatchMax, Loc.T(L.Settings.CollectHandInBatchFormat)));
    }
}
