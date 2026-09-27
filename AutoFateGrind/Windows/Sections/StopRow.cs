using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

// "Stop when": the goal itself, two optional caps that apply to any goal, and what happens afterwards.
internal static class StopRow
{
    private const float PadX = 18f;
    private const float PadY = 14f;
    private const float RowGap = 8f;
    private const float Gap = 10f;
    private const float DropdownWidth = 260f;
    private const int FateStep = 5;
    private const int MinuteStep = 5;

    private static readonly AfterRunAction[] afterOrder =
        [AfterRunAction.StayLoggedIn, AfterRunAction.ReturnToInn, AfterRunAction.Logout, AfterRunAction.CloseGame];

    private static readonly (LocString Name, LocString Detail)[] afterChoices =
    [
        (L.Grind.AfterStayName, L.Grind.AfterStayDetail),
        (L.Grind.AfterInnName, L.Grind.AfterInnDetail),
        (L.Grind.AfterLogoutName, L.Grind.AfterLogoutDetail),
        (L.Grind.AfterCloseName, L.Grind.AfterCloseDetail),
    ];

    private static readonly string[] afterLabels = new string[4];
    private static readonly string[] afterDetails = new string[4];

    public static void Draw(Configuration cfg, AutoFateController ctrl)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var padX = PadX * scale;
        var padY = PadY * scale;
        var editable = !ctrl.Running;
        var dl = ImGui.GetWindowDrawList();

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        var x = origin.X + padX;
        var y = origin.Y + padY;
        var title = Loc.T(L.Grind.StopWhen);
        var titleSize = TextDraw.SectionTitleSize(title);
        TextDraw.SectionTitle(title, new Vector2(x, y), Styling.TextStrong);
        y += titleSize.Y + 12f * scale;

        y = DrawGoalRow(cfg, x, y);
        y += RowGap * scale;
        y = DrawCapRow(cfg, editable, "##afg_cap_fates", x, y, () => cfg.StopAfterFatesEnabled, value => cfg.StopAfterFatesEnabled = value,
            () => cfg.TargetFateCount, value => cfg.TargetFateCount = value, FateStep, RunLimits.MaxFates, Loc.T(L.Grind.UnitFates));
        y += RowGap * scale;
        y = DrawCapRow(cfg, editable, "##afg_cap_minutes", x, y, () => cfg.StopAfterMinutesEnabled, value => cfg.StopAfterMinutesEnabled = value,
            () => cfg.TargetMinutes, value => cfg.TargetMinutes = value, MinuteStep, RunLimits.MaxMinutes, Loc.T(L.Grind.UnitMinutes));
        y += RowGap * scale;
        y = DrawAfterRow(cfg, editable, x, y);

        var end = new Vector2(origin.X + width, y + padY);

        dl.ChannelsSetCurrent(0);
        Paint.Glass(dl, origin, end, Styling.PanelRounding * scale, Styling.AccentViolet, 0.05f, 0f, elevated: true);
        dl.ChannelsMerge();

        if (!editable && Hit.HoveringRect(origin, end))
        {
            Tooltip.Show(Loc.T(L.Grind.PlanLocked));
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, end.Y - origin.Y));
    }

    private static float DrawGoalRow(Configuration cfg, float x, float y)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var rowHeight = ImGui.GetFrameHeight();
        var midY = y + rowHeight * 0.5f;
        var hasTarget = GoalSummary.HasTarget(cfg);
        var icon = hasTarget ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.Infinity;
        var iconSize = TextDraw.IconSize(icon);
        var iconX = x + (ToggleSwitch.Width * scale - iconSize.X) * 0.5f;
        TextDraw.Icon(icon, new Vector2(iconX, midY - iconSize.Y * 0.5f), hasTarget ? Styling.AccentMintSoft : Styling.TextDim);

        var text = hasTarget ? Loc.T(L.Grind.StopGoalReached) : Loc.T(L.Grind.StopNoGoal);
        var textSize = TextDraw.Measure(text);
        TextDraw.At(text, new Vector2(x + ToggleSwitch.Width * scale + Gap * scale, midY - textSize.Y * 0.5f), Styling.TextSecondary);
        return y + rowHeight;
    }

    private static float DrawCapRow(
        Configuration cfg, bool editable, string id, float x, float y,
        Func<bool> getEnabled, Action<bool> setEnabled, Func<int> getValue, Action<int> setValue, int step, int max, string unit)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var rowHeight = ImGui.GetFrameHeight();
        var midY = y + rowHeight * 0.5f;
        var gap = Gap * scale;

        ImGui.SetCursorScreenPos(new Vector2(x, midY - ToggleSwitch.Height * scale * 0.5f));
        var enabled = getEnabled();
        if (ToggleSwitch.Draw(id, ref enabled, editable))
        {
            setEnabled(enabled);
            cfg.SaveDebounced();
        }

        var cursorX = x + ToggleSwitch.Width * scale + gap;
        var label = Loc.T(L.Grind.StopOrAfter);
        var labelSize = TextDraw.Measure(label);
        TextDraw.At(label, new Vector2(cursorX, midY - labelSize.Y * 0.5f), enabled ? Styling.TextSecondary : Styling.TextMuted);
        cursorX += labelSize.X + gap;

        ImGui.SetCursorScreenPos(new Vector2(cursorX, y));
        var value = Math.Clamp(getValue(), 1, max);
        ImGui.PushID(id);
        if (Stepper.Draw("##value", ref value, step, 1, max, "%d") && editable)
        {
            setValue(Math.Clamp(value, 1, max));
            cfg.SaveDebounced();
        }

        ImGui.PopID();
        cursorX += Stepper.DefaultWidth * scale + gap;

        var unitSize = TextDraw.Measure(unit);
        TextDraw.At(unit, new Vector2(cursorX, midY - unitSize.Y * 0.5f), enabled ? Styling.TextDim : Styling.TextMuted);
        return y + rowHeight;
    }

    private static float DrawAfterRow(Configuration cfg, bool editable, float x, float y)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var rowHeight = ImGui.GetFrameHeight();
        var midY = y + rowHeight * 0.5f;

        var label = Loc.T(L.Grind.SentenceThen);
        var labelSize = TextDraw.Measure(label);
        var labelX = x + (ToggleSwitch.Width * scale - labelSize.X) * 0.5f;
        TextDraw.At(label, new Vector2(MathF.Max(x, labelX), midY - labelSize.Y * 0.5f), Styling.TextSecondary);

        for (var index = 0; index < afterChoices.Length; index++)
        {
            afterLabels[index] = Loc.T(afterChoices[index].Name);
            afterDetails[index] = Loc.T(afterChoices[index].Detail);
        }

        var selected = Math.Max(0, Array.IndexOf(afterOrder, cfg.AfterRun));
        ImGui.SetCursorScreenPos(new Vector2(x + ToggleSwitch.Width * scale + Gap * scale, y));
        using (ImRaii.Disabled(!editable))
        {
            if (Dropdown.DrawDetailed("##afg_after_run", afterLabels, afterDetails, ref selected, DropdownWidth, DropdownWidth + 120f))
            {
                cfg.AfterRun = afterOrder[selected];
                cfg.SaveDebounced();
            }
        }

        return y + rowHeight;
    }
}
