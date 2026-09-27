using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

// Two questions, each answered with one segmented row: how long the run goes, and what happens afterwards.
internal static class StopRow
{
    private enum Limit { GoalOnly, Fates, Minutes }

    private const float PadX = 18f;
    private const float PadY = 14f;
    private const float RowGap = 10f;
    private const float Gap = 10f;
    private const float LabelWidth = 104f;
    private const float StepperWidth = 124f;
    private const float SegmentedMaxWidth = 620f;
    private const float SegmentHeight = 36f;
    private const int FateStep = 5;
    private const int MinuteStep = 5;

    private static readonly AfterRunAction[] afterOrder =
        [AfterRunAction.StayLoggedIn, AfterRunAction.ReturnToInn, AfterRunAction.Logout, AfterRunAction.CloseGame];

    private static readonly (LocString Short, LocString Detail)[] afterChoices =
    [
        (L.Grind.AfterStayShort, L.Grind.AfterStayDetail),
        (L.Grind.AfterInnShort, L.Grind.AfterInnDetail),
        (L.Grind.AfterLogoutShort, L.Grind.AfterLogoutDetail),
        (L.Grind.AfterCloseShort, L.Grind.AfterCloseDetail),
    ];

    private static readonly Segmented.Item[] limitItems = new Segmented.Item[3];
    private static readonly Segmented.Item[] afterItems = new Segmented.Item[4];

    public static void Draw(Configuration cfg, AutoFateController ctrl)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var padX = PadX * scale;
        var padY = PadY * scale;
        var editable = !ctrl.Running;
        var dl = ImGui.GetWindowDrawList();
        var left = origin.X + padX;
        var right = origin.X + width - padX;

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        var y = origin.Y + padY;
        y = DrawLimitRow(cfg, editable, left, right, y);
        y += RowGap * scale;
        y = DrawAfterRow(cfg, editable, left, right, y);

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

    private static float DrawLimitRow(Configuration cfg, bool editable, float left, float right, float y)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var rowHeight = SegmentHeight * scale;
        var gap = Gap * scale;
        var limit = CurrentLimit(cfg);

        var controlX = DrawRowLabel(Loc.T(L.Grind.HowLong), left, y, rowHeight);
        var unit = limit == Limit.Minutes ? Loc.T(L.Grind.UnitMinutes) : Loc.T(L.Grind.UnitFates);
        var stepperArea = limit == Limit.GoalOnly ? 0f : gap + StepperWidth * scale + gap + TextDraw.Measure(unit).X;
        var segmentedWidth = MathF.Min(SegmentedMaxWidth * scale, right - controlX - stepperArea);

        limitItems[(int)Limit.GoalOnly] = new Segmented.Item(GoalSummary.HasTarget(cfg) ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.HandPaper,
            GoalSummary.HasTarget(cfg) ? Loc.T(L.Grind.UntilGoalDone) : Loc.T(L.Grind.UntilYouStopIt));
        limitItems[(int)Limit.Fates] = new Segmented.Item(FontAwesomeIcon.ListOl, Loc.T(L.Grind.ANumberOfFates));
        limitItems[(int)Limit.Minutes] = new Segmented.Item(FontAwesomeIcon.Stopwatch, Loc.T(L.Grind.ALengthOfTime));

        ImGui.SetCursorScreenPos(new Vector2(controlX, y));
        var selected = (int)limit;
        if (Segmented.Draw("##afg_limit", limitItems, ref selected, editable, SegmentHeight, segmentedWidth) && editable)
        {
            ApplyLimit(cfg, (Limit)selected);
            limit = (Limit)selected;
        }

        if (limit != Limit.GoalOnly)
        {
            DrawLimitValue(cfg, editable, limit, controlX + segmentedWidth + gap, y, rowHeight);
        }

        return y + rowHeight;
    }

    private static void DrawLimitValue(Configuration cfg, bool editable, Limit limit, float x, float y, float rowHeight)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var frameHeight = ImGui.GetFrameHeight();
        var midY = y + rowHeight * 0.5f;
        var fates = limit == Limit.Fates;
        var max = fates ? RunLimits.MaxFates : RunLimits.MaxMinutes;
        var value = Math.Clamp(fates ? cfg.TargetFateCount : cfg.TargetMinutes, 1, max);

        ImGui.SetCursorScreenPos(new Vector2(x, midY - frameHeight * 0.5f));
        if (Stepper.Draw(fates ? "##afg_limit_fates" : "##afg_limit_minutes", ref value, fates ? FateStep : MinuteStep, 1, max, "%d", StepperWidth) && editable)
        {
            if (fates) cfg.TargetFateCount = Math.Clamp(value, 1, max);
            else cfg.TargetMinutes = Math.Clamp(value, 1, max);
            cfg.SaveDebounced();
        }

        var unit = fates ? Loc.T(L.Grind.UnitFates) : Loc.T(L.Grind.UnitMinutes);
        var unitSize = TextDraw.Measure(unit);
        TextDraw.At(unit, new Vector2(x + StepperWidth * scale + Gap * scale, midY - unitSize.Y * 0.5f), Styling.TextDim);
    }

    private static float DrawAfterRow(Configuration cfg, bool editable, float left, float right, float y)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var rowHeight = SegmentHeight * scale;
        var controlX = DrawRowLabel(Loc.T(L.Grind.Afterwards), left, y, rowHeight);
        var segmentedWidth = MathF.Min(SegmentedMaxWidth * scale, right - controlX);

        afterItems[0] = new Segmented.Item(FontAwesomeIcon.MapMarkerAlt, Loc.T(afterChoices[0].Short));
        afterItems[1] = new Segmented.Item(FontAwesomeIcon.Bed, Loc.T(afterChoices[1].Short));
        afterItems[2] = new Segmented.Item(FontAwesomeIcon.SignOutAlt, Loc.T(afterChoices[2].Short));
        afterItems[3] = new Segmented.Item(FontAwesomeIcon.PowerOff, Loc.T(afterChoices[3].Short));

        ImGui.SetCursorScreenPos(new Vector2(controlX, y));
        var selected = Math.Max(0, Array.IndexOf(afterOrder, cfg.AfterRun));
        if (Segmented.Draw("##afg_after_run", afterItems, ref selected, editable, SegmentHeight, segmentedWidth) && editable)
        {
            cfg.AfterRun = afterOrder[selected];
            cfg.SaveDebounced();
        }

        y += rowHeight + 6f * scale;
        using (Fonts.PushCaption())
        {
            var detail = Loc.T(afterChoices[selected].Detail);
            var detailWidth = right - controlX;
            TextDraw.Wrapped(detail, new Vector2(controlX, y), detailWidth, Styling.TextMuted);
            return y + TextDraw.MeasureWrapped(detail, detailWidth).Y;
        }
    }

    private static float DrawRowLabel(string label, float left, float y, float rowHeight)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var labelSize = TextDraw.Measure(label);
        TextDraw.At(label, new Vector2(left, y + (rowHeight - labelSize.Y) * 0.5f), Styling.TextSecondary);
        return left + MathF.Max(LabelWidth * scale, labelSize.X + Gap * scale);
    }

    // One cap at a time keeps the row a single choice; a config that has both on (older versions) reads as the FATE count.
    private static Limit CurrentLimit(Configuration cfg)
    {
        if (cfg.StopAfterFatesEnabled) return Limit.Fates;
        if (cfg.StopAfterMinutesEnabled) return Limit.Minutes;
        return Limit.GoalOnly;
    }

    private static void ApplyLimit(Configuration cfg, Limit limit)
    {
        cfg.StopAfterFatesEnabled = limit == Limit.Fates;
        cfg.StopAfterMinutesEnabled = limit == Limit.Minutes;
        cfg.SaveDebounced();
    }
}
