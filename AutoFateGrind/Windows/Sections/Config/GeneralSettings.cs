using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Game.Player;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class GeneralSettings
{
    public static void Draw(Configuration cfg)
    {
        DrawLanguageGroup(cfg);
        DrawWindowGroup(cfg);
        DrawBehaviorGroup(cfg);
        DrawCollectGroup(cfg);
    }

    private static void DrawLanguageGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.Language));

        SettingsRow.Draw(Loc.T(L.Settings.Language),
            Loc.T(L.Settings.LanguageHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.DrawLanguageCombo(cfg));
    }

    private static void DrawWindowGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GeneralWindow));

        SettingsRow.Draw(Loc.T(L.Settings.OpenOnLogin),
            Loc.T(L.Settings.OpenOnLoginHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.AutoShowOnLogin, v => cfg.AutoShowOnLogin = v, "##gen_autoshow"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.LivePopout),
            Loc.T(L.Settings.LivePopoutHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.ShowLivePopout, v =>
            {
                cfg.ShowLivePopout = v;
                Plugin.Instance.LiveFateWindow.IsOpen = v;
            }, "##gen_popout"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.NameFormat),
            Loc.T(L.Settings.NameFormatHelp),
            SettingsControls.RowComboWidth,
            () => DrawNameFormat(cfg));
    }

    private static void DrawNameFormat(Configuration cfg)
    {
        var text = cfg.FateNameFormat;
        ImGui.SetNextItemWidth(SettingsControls.RowComboWidth * ImGuiHelpers.GlobalScale);
        using (SettingsControls.PushFrameColors())
        {
            if (ImGui.InputText("##gen_namefmt", ref text, FateNameFormatter.MaxFormatLength))
            {
                cfg.FateNameFormat = text;
                cfg.SaveDebounced();
            }
        }
    }

    private static void DrawBehaviorGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GeneralBehavior));

        SettingsRow.Draw(Loc.T(L.Settings.SwapZones),
            Loc.T(L.Settings.SwapZonesHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.SwapZonesWhenEmpty, v => cfg.SwapZonesWhenEmpty = v, "##gen_swap"),
            SettingsRow.ToggleHeight);

        using (Motion.PushSwitch("##gen_swap_body", cfg.SwapZonesWhenEmpty))
        {
            if (cfg.SwapZonesWhenEmpty)
            {
                SettingsRow.Draw(Loc.T(L.Settings.SwapWait),
                    Loc.T(L.Settings.SwapWaitHelp),
                    SettingsControls.RowSliderWidth,
                    () => SettingsControls.DrawIntSlider(cfg, "##gen_swap_wait",
                        () => cfg.SwapZoneWaitSec, v => cfg.SwapZoneWaitSec = v, SwapWaitMinSec, SwapWaitMaxSec, Loc.T(L.Settings.MinTimeFormat)));
            }
        }

        SettingsRow.Draw(Loc.T(L.Settings.KeepTwist),
            Loc.T(L.Settings.KeepTwistHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.KeepTwistOfFate, v => cfg.KeepTwistOfFate = v, "##gen_keeptwist"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.MountWhileWaiting),
            Loc.T(L.Settings.MountWhileWaitingHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.MountWhileWaitingForFates, v => cfg.MountWhileWaitingForFates = v, "##gen_mount_waiting"),
            SettingsRow.ToggleHeight);

        DrawMountRow(cfg);

        SettingsRow.Draw(Loc.T(L.Settings.EconomyTravel),
            Loc.T(L.Settings.EconomyTravelHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.EconomyTravel, v => cfg.EconomyTravel = v, "##gen_economy"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.AutoPause),
            Loc.T(L.Settings.AutoPauseHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.AutoPauseInContent, v => cfg.AutoPauseInContent = v, "##gen_autopause"),
            SettingsRow.ToggleHeight);

        SettingsRow.Draw(Loc.T(L.Settings.AutoResume),
            Loc.T(L.Settings.AutoResumeHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.AutoResumeOnFault, v => cfg.AutoResumeOnFault = v, "##gen_autoresume"),
            SettingsRow.ToggleHeight);
    }

    private const float MountComboWidth = 220f;
    private const int MountListRefreshMs = 2000;
    private static MountOption[] mountOptions = [];
    private static string[] mountLabels = [];
    private static long mountListBuiltAtMs = long.MinValue;

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

                if (SettingsControls.DrawSearchableCombo("##gen_mount", mountLabels, ref selected, MountComboWidth))
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
        if (now - mountListBuiltAtMs < MountListRefreshMs)
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

    private const int SwapWaitMinSec = 0;
    private const int SwapWaitMaxSec = 300;
    private const int CollectHandInBatchMin = 1;
    // Nine keeps AFG's trip ahead of BossMod's fixed 10-item hand-in, so the two never race for the NPC.
    private const int CollectHandInBatchMax = 9;

    private static void DrawCollectGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GeneralCollect));

        SettingsRow.Draw(Loc.T(L.Settings.CollectHandIn),
            Loc.T(L.Settings.CollectHandInHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.CollectHandInEnabled, v => cfg.CollectHandInEnabled = v, "##gen_collect_handin"),
            SettingsRow.ToggleHeight);

        if (!cfg.CollectHandInEnabled)
        {
            return;
        }

        SettingsRow.Draw(Loc.T(L.Settings.CollectHandInBatch),
            Loc.T(L.Settings.CollectHandInBatchHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##gen_collect_batch",
                () => cfg.CollectHandInBatch, v => cfg.CollectHandInBatch = v,
                CollectHandInBatchMin, CollectHandInBatchMax, Loc.T(L.Settings.CollectHandInBatchFormat)));
    }
}
