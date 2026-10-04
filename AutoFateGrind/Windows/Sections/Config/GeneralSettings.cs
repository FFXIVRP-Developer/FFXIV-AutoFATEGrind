using AutoFateGrind.Core.Game.Fates;
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
        DrawRunGroup(cfg);
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

        // Fork (item 17): literal English like the other fork rows.
        SettingsRow.Draw("Resume the run after a reload",
            "A run going when AFG unloads (a rebuild, a crash, a game restart) starts again on the next load, once in the world and out of a duty. Not while BoatRunner is busy with the boat. Off: AFG never starts on its own.",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.ResumeAfterReload, v => cfg.ResumeAfterReload = v, "##gen_resume_reload"),
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

    private static void DrawRunGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.GeneralRun));

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
}
