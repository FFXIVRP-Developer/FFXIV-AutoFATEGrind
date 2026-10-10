using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Core.Ipc;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ECommons.DalamudServices;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class HumanizerSettings
{
    private const long ModuleCheckIntervalMs = 5_000;

    private static readonly HumanizerBreakActivity[] breakActivities =
        [HumanizerBreakActivity.Wander, HumanizerBreakActivity.IdleAtSpot];

    private static readonly SettingsControls.Choices.Choice[] breakActivityChoices =
    [
        new(L.Settings.BreakActivityWanderName, L.Settings.BreakActivityWanderDetail),
        new(L.Settings.BreakActivityIdleName, L.Settings.BreakActivityIdleDetail),
    ];

    private static string? moduleCheckPreset;
    private static long moduleCheckTick;
    private static bool moduleMissing;

    public static void Draw(Configuration cfg)
    {
        DrawPacingGroup(cfg);
        DrawCombatMovementGroup(cfg);
        DrawBreaksGroup(cfg);
        using var more = Motion.PushSection("##hum_more", cfg.HumanizerEnabled);
        if (more is null)
        {
            return;
        }

        var idle = cfg.HumanizerBreakActivity == HumanizerBreakActivity.IdleAtSpot;
        if (idle)
        {
            DrawIdleSpotsGroup(cfg);
        }
        else
        {
            DrawWanderingGroup(cfg);
        }

        if (!idle || cfg.HumanizerIdleSpots.Count == 0)
        {
            DrawCitiesGroup(cfg);
        }
    }

    private static void DrawPacingGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.Pacing));

        SettingsRow.Draw(Loc.T(L.Settings.PacingEnable),
            Loc.T(L.Settings.PacingEnableHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.PacingEnabled, v => cfg.PacingEnabled = v, "##pace_on"),
            SettingsRow.ToggleHeight);

        using var body = Motion.PushSwitch("##pace_body", cfg.PacingEnabled);
        if (!cfg.PacingEnabled)
        {
            SettingsRow.Note(Loc.T(L.Settings.PacingOff));
            return;
        }

        SettingsRow.Draw(Loc.T(L.Settings.ReactionDelay),
            Loc.T(L.Settings.ReactionDelayHelp),
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawRangeInline(cfg, "##pace_reaction_min", "##pace_reaction_max",
                () => cfg.PacingReactionMinSec, v => cfg.PacingReactionMinSec = v,
                () => cfg.PacingReactionMaxSec, v => cfg.PacingReactionMaxSec = v, 30, 0, Loc.T(L.Settings.SecondsFormat)));

        SettingsRow.Draw(Loc.T(L.Settings.PickVariety),
            Loc.T(L.Settings.PickVarietyHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.PacingPickVariety, v => cfg.PacingPickVariety = v, "##pace_variety"),
            SettingsRow.ToggleHeight);
    }

    private static void DrawCombatMovementGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.CombatMovement));

        SettingsRow.Draw(Loc.T(L.Settings.CombatMovementEnable),
            Loc.T(L.Settings.CombatMovementEnableHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.CombatMovementEnabled, v => cfg.CombatMovementEnabled = v, "##combat_move_on"),
            SettingsRow.ToggleHeight);

        using var body = Motion.PushSwitch("##combat_move_body", cfg.CombatMovementEnabled);
        if (!cfg.CombatMovementEnabled)
        {
            SettingsRow.Note(Loc.T(L.Settings.CombatMovementOff));
            return;
        }

        var none = Loc.T(L.Settings.MovementLevelNone);
        string[] delayLabels = [none, Loc.T(L.Settings.DelayShort), Loc.T(L.Settings.DelayLong)];
        string[] roomLabels = [none, Loc.T(L.Settings.RoomSmall), Loc.T(L.Settings.RoomMedium), Loc.T(L.Settings.RoomLarge)];

        SettingsRow.Draw(Loc.T(L.Settings.DodgeDelay),
            Loc.T(L.Settings.DodgeDelayHelp),
            SettingsControls.ComboRangeWidth(),
            () => SettingsControls.DrawComboRange(cfg, "##combat_dodge_min", "##combat_dodge_max", delayLabels,
                () => cfg.CombatDodgeDelayMin, v => cfg.CombatDodgeDelayMin = v,
                () => cfg.CombatDodgeDelayMax, v => cfg.CombatDodgeDelayMax = v));

        SettingsRow.Draw(Loc.T(L.Settings.MoveDelay),
            Loc.T(L.Settings.MoveDelayHelp),
            SettingsControls.ComboRangeWidth(),
            () => SettingsControls.DrawComboRange(cfg, "##combat_move_min", "##combat_move_max", delayLabels,
                () => cfg.CombatMoveDelayMin, v => cfg.CombatMoveDelayMin = v,
                () => cfg.CombatMoveDelayMax, v => cfg.CombatMoveDelayMax = v));

        SettingsRow.Draw(Loc.T(L.Settings.DangerRoom),
            Loc.T(L.Settings.DangerRoomHelp),
            SettingsControls.ComboRangeWidth(),
            () => SettingsControls.DrawComboRange(cfg, "##combat_room_min", "##combat_room_max", roomLabels,
                () => cfg.CombatCushionMin, v => cfg.CombatCushionMin = v,
                () => cfg.CombatCushionMax, v => cfg.CombatCushionMax = v));

        if (PresetLacksMovementModule(cfg.CombatPresetName))
        {
            SettingsRow.Note(Loc.T(L.Settings.CombatMovementNoModule, cfg.CombatPresetName), Styling.AccentAmber);
        }
    }

    // Fetching the preset serializes it on BossMod's side, so the check is only refreshed every few seconds.
    private static bool PresetLacksMovementModule(string preset)
    {
        var now = Environment.TickCount64;
        if (preset != moduleCheckPreset || now - moduleCheckTick >= ModuleCheckIntervalMs)
        {
            moduleCheckPreset = preset;
            moduleCheckTick = now;
            moduleMissing = BossModMovementTuning.PresetHasModule(preset) == false;
        }

        return moduleMissing;
    }

    private static void DrawBreaksGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.HumanizerBreaks));

        SettingsRow.Draw(Loc.T(L.Settings.HumanizerEnable),
            Loc.T(L.Settings.HumanizerEnableHelp),
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.HumanizerEnabled, v => cfg.HumanizerEnabled = v, "##hum_on"),
            SettingsRow.ToggleHeight);

        using var body = Motion.PushSwitch("##hum_body", cfg.HumanizerEnabled);
        if (!cfg.HumanizerEnabled)
        {
            SettingsRow.Note(Loc.T(L.Settings.HumanizerOff));
            return;
        }

        SettingsRow.Draw(Loc.T(L.Settings.FatesBetween),
            Loc.T(L.Settings.FatesBetweenHelp),
            SettingsControls.RowSliderWidth,
            () => SettingsControls.DrawIntSlider(cfg, "##hum_fates",
                () => cfg.HumanizerFatesBeforeBreak, v => cfg.HumanizerFatesBeforeBreak = Math.Clamp(v, 1, 100),
                1, 100, Loc.T(L.Settings.FatesFormat)));

        SettingsRow.Draw(Loc.T(L.Settings.BreakLength),
            Loc.T(L.Settings.BreakLengthHelp),
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawRangeInline(cfg, "##hum_min", "##hum_max",
                () => cfg.HumanizerBreakMinMinutes, v => cfg.HumanizerBreakMinMinutes = v,
                () => cfg.HumanizerBreakMaxMinutes, v => cfg.HumanizerBreakMaxMinutes = v, 60, 1, Loc.T(L.Settings.MinutesFormat)));

        var activity = Math.Max(0, Array.IndexOf(breakActivities, cfg.HumanizerBreakActivity));
        SettingsRow.Draw(Loc.T(L.Settings.BreakActivity),
            Loc.T(L.Settings.BreakActivityHelp),
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##hum_activity", breakActivityChoices, activity, choice =>
            {
                cfg.HumanizerBreakActivity = breakActivities[choice];
                cfg.SaveDebounced();
            }));
        SettingsRow.Caption(Loc.T(breakActivityChoices[activity].Detail));
    }

    private static void DrawIdleSpotsGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.IdleSpots));

        SettingsRow.DrawBlock(Loc.T(L.Settings.SavedSpots),
            Loc.T(L.Settings.SavedSpotsHelp),
            () => DrawIdleSpotList(cfg));
    }

    private static void DrawIdleSpotList(Configuration cfg)
    {
        var spots = cfg.HumanizerIdleSpots;
        if (spots.Count == 0)
        {
            SettingsRow.Note(Loc.T(L.Settings.NoSpots));
        }

        var removeIndex = -1;
        var buttonSize = ImGui.GetFrameHeight();
        for (var spotIndex = 0; spotIndex < spots.Count; spotIndex++)
        {
            var spot = spots[spotIndex];
            using var id = ImRaii.PushId(spotIndex);
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
            {
                ImGui.TextUnformatted(spot.Name.Length > 0 ? spot.Name : TerritoryNames.Of(spot.TerritoryId));
            }

            if (spot.Name.Length > 0)
            {
                ImGui.SameLine();
                using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                {
                    ImGui.TextUnformatted(TerritoryNames.Of(spot.TerritoryId));
                }
            }

            ImGui.SameLine(SettingsGroup.InnerRightLocalX() - buttonSize);
            if (IconButton.Draw(FontAwesomeIcon.Times, "##hum_spot_rm", buttonSize, Styling.AccentRose, Loc.T(L.Common.Remove)))
            {
                removeIndex = spotIndex;
            }
        }

        if (removeIndex >= 0)
        {
            spots.RemoveAt(removeIndex);
            cfg.SaveDebounced();
        }

        using (ImRaii.Disabled(Svc.Objects.LocalPlayer is null))
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMint))
        {
            if (ImGui.Button($"{Loc.T(L.Settings.SaveSpot)}##hum_spot_save"))
            {
                SaveCurrentSpot(cfg);
            }
        }
    }

    private static void SaveCurrentSpot(Configuration cfg)
    {
        switch (IdleSpotOps.TryCapture(out var spot))
        {
            case IdleSpotCapture.Saved:
                cfg.HumanizerIdleSpots.Add(spot!);
                GameTextGlyphs.Add(spot!.Name);
                cfg.SaveDebounced();
                Svc.Chat.Print(Loc.T(L.Settings.SpotSavedChat, TerritoryNames.Of(spot.TerritoryId)));
                break;
            case IdleSpotCapture.Airborne:
                Svc.Chat.PrintError(Loc.T(L.Settings.SpotAirborneChat));
                break;
            case IdleSpotCapture.NoAetheryte:
                Svc.Chat.PrintError(Loc.T(L.Settings.SpotNoAetheryteChat));
                break;
        }
    }

    private static void DrawWanderingGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.HumanizerWandering));

        SettingsRow.Draw(Loc.T(L.Settings.PauseBetween),
            Loc.T(L.Settings.PauseBetweenHelp),
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawRangeInline(cfg, "##hum_pause_min", "##hum_pause_max",
                () => cfg.HumanizerPauseMinSec, v => cfg.HumanizerPauseMinSec = v,
                () => cfg.HumanizerPauseMaxSec, v => cfg.HumanizerPauseMaxSec = v, MaxPauseSec, 0, Loc.T(L.Settings.SecondsFormat)));

        SettingsRow.Draw(Loc.T(L.Settings.WalkDistance),
            Loc.T(L.Settings.WalkDistanceHelp),
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawRangeInline(cfg, "##hum_wander_min", "##hum_wander_max",
                () => cfg.HumanizerWanderMinMeters, v => cfg.HumanizerWanderMinMeters = v,
                () => cfg.HumanizerWanderMaxMeters, v => cfg.HumanizerWanderMaxMeters = v, 200, 5, Loc.T(L.Settings.MetersFormat)));
    }

    // Fork: up to 999 minutes (double-click the field to type), so a break can be spent standing still.
    private const int MaxPauseSec = 999 * 60;

    private static void DrawCitiesGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin(Loc.T(L.Settings.HumanizerCities));

        // Fork: break location; the cities below stay the fallback when Lifestream cannot get there.
        SettingsRow.Draw("Break location",
            "City is upstream's break (a city below, or a saved idle spot). Inn and housing are reached with Lifestream (/li inn, apartment, home, fc); a private or FC house is only entered when Lifestream's house registration says \"Enter house\". There the break wanders or idles as the break activity says. If Lifestream can't get there, the break falls back to upstream's (city or idle spot).",
            SettingsControls.RowComboWidth,
            () =>
            {
                var selected = (int)cfg.HumanizerRetreat;
                if (SettingsControls.DrawPlainCombo("##hum_retreat", ref selected, Core.Tasks.AutoHumanize.RetreatLabels, SettingsControls.RowComboWidth))
                {
                    cfg.HumanizerRetreat = (Core.Tasks.HumanizerRetreat)selected;
                    cfg.SaveDebounced();
                }
            });

        SettingsRow.DrawBlock(Loc.T(L.Settings.AllowedCities),
            Loc.T(L.Settings.AllowedCitiesHelp),
            () => DrawHumanizerCityList(cfg));
    }

    private static void DrawHumanizerCityList(Configuration cfg)
    {
        var grouped = CityCatalog.All.GroupBy(c => c.Expansion).OrderByDescending(g => g.Key);
        foreach (var group in grouped)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
                ImGui.TextUnformatted(ExpansionLabels.Name(group.Key));

            foreach (var city in group)
            {
                var selected = cfg.HumanizerCities.Contains(city.TerritoryId);
                var id = $"##hum_city_{city.TerritoryId}";
                if (ToggleSwitch.Draw(id, ref selected))
                {
                    if (selected) cfg.HumanizerCities.Add(city.TerritoryId);
                    else          cfg.HumanizerCities.Remove(city.TerritoryId);
                    cfg.SaveDebounced();
                }
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
                using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
                    ImGui.TextUnformatted(city.Name);
            }
            ImGui.Spacing();
        }

        if (cfg.HumanizerCities.Count == 0 && cfg.HumanizerRetreat == Core.Tasks.HumanizerRetreat.City) // Fork: no warning with a retreat
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentRose))
                ImGui.TextWrapped(Loc.T(L.Settings.NoCities));
    }
}
