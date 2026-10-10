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
    private enum Limit { GoalOnly, Fates, Minutes, Levels }

    private readonly record struct Columns(float Label, float Control, float Right, float SegmentedWidth);

    private const float PadX = 18f;
    private const float PadY = 14f;
    private const float RowGap = 10f;
    private const float Gap = 10f;
    private const float LabelWidth = 104f;
    private const float StepperWidth = 124f;
    private const float SegmentHeight = 36f;
    private const int FateStep = 5;
    private const int MinuteStep = 5;
    private const int LevelStep = 1;

    private static readonly AfterRunAction[] afterOrder =
        [AfterRunAction.StayLoggedIn, AfterRunAction.ReturnToInn, AfterRunAction.Logout, AfterRunAction.CloseGame];

    private static readonly (LocString Short, LocString Detail)[] afterChoices =
    [
        (L.Grind.AfterStayShort, L.Grind.AfterStayDetail),
        (L.Grind.AfterInnShort, L.Grind.AfterInnDetail),
        (L.Grind.AfterLogoutShort, L.Grind.AfterLogoutDetail),
        (L.Grind.AfterCloseShort, L.Grind.AfterCloseDetail),
    ];

    private static readonly Segmented.Item[] limitItems = new Segmented.Item[4];
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

        FillItems(cfg);
        var controlX = left + LabelColumnWidth();
        var segmentedWidth = MathF.Min(MathF.Max(Segmented.PreferredWidth(limitItems), Segmented.PreferredWidth(afterItems)), right - controlX);
        var columns = new Columns(left, controlX, right, segmentedWidth);

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        var y = origin.Y + padY;
        y = DrawLimitRow(cfg, editable, columns, y);
        y += RowGap * scale;
        y = DrawAfterRow(cfg, editable, columns, y);

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

    private static void FillItems(Configuration cfg)
    {
        var hasTarget = GoalSummary.HasTarget(cfg);
        limitItems[(int)Limit.GoalOnly] = new Segmented.Item(hasTarget ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.HandPaper,
            hasTarget ? Loc.T(L.Grind.UntilGoalDone) : Loc.T(L.Grind.UntilYouStopIt));
        limitItems[(int)Limit.Fates] = new Segmented.Item(FontAwesomeIcon.ListOl, Loc.T(L.Grind.ANumberOfFates));
        limitItems[(int)Limit.Minutes] = new Segmented.Item(FontAwesomeIcon.Stopwatch, Loc.T(L.Grind.ALengthOfTime));
        limitItems[(int)Limit.Levels] = new Segmented.Item(FontAwesomeIcon.LevelUpAlt, Loc.T(L.Grind.ANumberOfLevels));

        afterItems[0] = new Segmented.Item(FontAwesomeIcon.MapMarkerAlt, Loc.T(afterChoices[0].Short));
        afterItems[1] = new Segmented.Item(FontAwesomeIcon.Bed, Loc.T(afterChoices[1].Short));
        afterItems[2] = new Segmented.Item(FontAwesomeIcon.SignOutAlt, Loc.T(afterChoices[2].Short));
        afterItems[3] = new Segmented.Item(FontAwesomeIcon.PowerOff, Loc.T(afterChoices[3].Short));
    }

    private static float DrawLimitRow(Configuration cfg, bool editable, Columns columns, float y)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var rowHeight = SegmentHeight * scale;
        var limit = CurrentLimit(cfg);

        DrawRowLabel(Loc.T(L.Grind.HowLong), columns.Label, y, rowHeight);

        ImGui.SetCursorScreenPos(new Vector2(columns.Control, y));
        var selected = (int)limit;
        if (Segmented.Draw("##afg_limit", limitItems, ref selected, editable, SegmentHeight, columns.SegmentedWidth) && editable)
        {
            ApplyLimit(cfg, (Limit)selected);
            limit = (Limit)selected;
        }

        if (limit == Limit.GoalOnly) return y + rowHeight;

        var besideX = columns.Control + columns.SegmentedWidth + Gap * scale;
        if (besideX + LimitValueWidth(limit) <= columns.Right)
        {
            DrawLimitValue(cfg, editable, limit, besideX, y, rowHeight);
            return y + rowHeight;
        }

        var belowY = y + rowHeight + RowGap * scale;
        var frameHeight = ImGui.GetFrameHeight();
        DrawLimitValue(cfg, editable, limit, columns.Control, belowY, frameHeight);
        return belowY + frameHeight;
    }

    private static float LimitValueWidth(Limit limit) =>
        (StepperWidth + Gap) * ImGuiHelpers.GlobalScale + TextDraw.Measure(UnitFor(limit)).X;

    private static void DrawLimitValue(Configuration cfg, bool editable, Limit limit, float x, float y, float rowHeight)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var frameHeight = ImGui.GetFrameHeight();
        var midY = y + rowHeight * 0.5f;
        var (id, step, max, current) = limit switch
        {
            Limit.Fates   => ("##afg_limit_fates", FateStep, RunLimits.MaxFates, cfg.TargetFateCount),
            Limit.Minutes => ("##afg_limit_minutes", MinuteStep, RunLimits.MaxMinutes, cfg.TargetMinutes),
            _             => ("##afg_limit_levels", LevelStep, RunLimits.MaxLevels, cfg.TargetLevels),
        };
        var value = Math.Clamp(current, 1, max);

        ImGui.SetCursorScreenPos(new Vector2(x, midY - frameHeight * 0.5f));
        if (Stepper.Draw(id, ref value, step, 1, max, "%d", StepperWidth) && editable)
        {
            var clamped = Math.Clamp(value, 1, max);
            switch (limit)
            {
                case Limit.Fates:   cfg.TargetFateCount = clamped; break;
                case Limit.Minutes: cfg.TargetMinutes = clamped; break;
                default:            cfg.TargetLevels = clamped; break;
            }

            cfg.SaveDebounced();
        }

        var unit = UnitFor(limit);
        var unitSize = TextDraw.Measure(unit);
        TextDraw.At(unit, new Vector2(x + StepperWidth * scale + Gap * scale, midY - unitSize.Y * 0.5f), Styling.TextDim);
    }

    private static float DrawAfterRow(Configuration cfg, bool editable, Columns columns, float y)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var rowHeight = SegmentHeight * scale;
        DrawRowLabel(Loc.T(L.Grind.Afterwards), columns.Label, y, rowHeight);

        ImGui.SetCursorScreenPos(new Vector2(columns.Control, y));
        var selected = Math.Max(0, Array.IndexOf(afterOrder, cfg.AfterRun));
        if (Segmented.Draw("##afg_after_run", afterItems, ref selected, editable, SegmentHeight, columns.SegmentedWidth) && editable)
        {
            cfg.AfterRun = afterOrder[selected];
            cfg.SaveDebounced();
        }

        y += rowHeight + 6f * scale;
        using (Fonts.PushCaption())
        {
            var detail = Loc.T(afterChoices[selected].Detail);
            var detailWidth = columns.Right - columns.Control;
            TextDraw.Wrapped(detail, new Vector2(columns.Control, y), detailWidth, Styling.TextMuted);
            return y + TextDraw.MeasureWrapped(detail, detailWidth).Y;
        }
    }

    private static float LabelColumnWidth()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var widest = MathF.Max(TextDraw.Measure(Loc.T(L.Grind.HowLong)).X, TextDraw.Measure(Loc.T(L.Grind.Afterwards)).X);
        return MathF.Max(LabelWidth * scale, widest + Gap * scale);
    }

    private static void DrawRowLabel(string label, float left, float y, float rowHeight)
    {
        var labelSize = TextDraw.Measure(label);
        TextDraw.At(label, new Vector2(left, y + (rowHeight - labelSize.Y) * 0.5f), Styling.TextSecondary);
    }

    private static string UnitFor(Limit limit) => limit switch
    {
        Limit.Minutes => Loc.T(L.Grind.UnitMinutes),
        Limit.Levels  => Loc.T(L.Grind.UnitLevels),
        _             => Loc.T(L.Grind.UnitFates),
    };

    // One cap at a time keeps the row a single choice; a config that has several on (older versions) reads as the FATE count.
    private static Limit CurrentLimit(Configuration cfg)
    {
        if (cfg.StopAfterFatesEnabled) return Limit.Fates;
        if (cfg.StopAfterMinutesEnabled) return Limit.Minutes;
        if (cfg.StopAfterLevelsEnabled) return Limit.Levels;
        return Limit.GoalOnly;
    }

    private static void ApplyLimit(Configuration cfg, Limit limit)
    {
        cfg.StopAfterFatesEnabled = limit == Limit.Fates;
        cfg.StopAfterMinutesEnabled = limit == Limit.Minutes;
        cfg.StopAfterLevelsEnabled = limit == Limit.Levels;
        cfg.SaveDebounced();
    }
}
