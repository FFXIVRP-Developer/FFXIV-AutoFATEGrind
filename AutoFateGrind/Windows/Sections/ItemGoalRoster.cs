using AutoFateGrind.Core.Game.Items;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Zones;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;
using System.Text;

namespace AutoFateGrind.Windows.Sections;

internal static class ItemGoalRoster
{
    private const float Gap = 8f;
    private const int MaxDrops = 12;

    private static readonly (uint ItemId, int Have, int Need, string? Text)[] progressLabels = new (uint, int, int, string?)[MaxDrops];
    private static readonly StringBuilder noteBuilder = new();

    public static void Draw(Configuration cfg, AutoFateController ctrl, bool scrollIntoView)
    {
        if (ItemGoalCatalog.Find(cfg.ActiveMode.Id) is not { } goal)
        {
            return;
        }

        DrawHeader(cfg, goal, scrollIntoView);
        Styling.VSpace(6f);
        DrawPlanNote(cfg);
        Styling.VSpace(10f);
        DrawGrid(cfg, goal);
    }

    public static void DrawPlanNote(Configuration cfg)
    {
        if (ItemGoalCatalog.Find(cfg.ActiveMode.Id) is not { } goal)
        {
            return;
        }

        var zones = ZoneSelection.ResolveStartList(cfg);
        var note = zones.Count == 0 ? Loc.T(L.Grind.DetailAllCollected) : Loc.T(L.Grind.ItemGoalPlanZones, JoinZoneNames(zones));
        using (Fonts.PushCaption())
        {
            var origin = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            TextDraw.Wrapped(note, new Vector2(origin.X + 2f * ImGuiHelpers.GlobalScale, origin.Y), width, Styling.TextMuted);
            ImGui.Dummy(new Vector2(width, TextDraw.MeasureWrapped(note, width).Y));
        }
    }

    private static string JoinZoneNames(IReadOnlyList<ZoneInfo> zones)
    {
        noteBuilder.Clear();
        for (var index = 0; index < zones.Count; index++)
        {
            if (index > 0)
            {
                noteBuilder.Append(", ");
            }

            noteBuilder.Append(zones[index].Name);
        }

        return noteBuilder.ToString();
    }

    private static void DrawHeader(Configuration cfg, ItemGoalDefinition goal, bool scrollIntoView)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Layout.LibraryHeaderHeight * scale;
        var midY = origin.Y + height * 0.5f;

        var label = Loc.T(goal.Name);
        var labelSize = TextDraw.SectionTitleSize(label);
        TextDraw.SectionTitle(label, new Vector2(origin.X, midY - labelSize.Y * 0.5f), Styling.TextStrong);

        var (have, need) = ItemGoalProgress.Totals(goal, cfg);
        using (Fonts.PushCaption())
        {
            var summary = Loc.T(L.Grind.ItemsSummary, have, need);
            var summarySize = TextDraw.Measure(summary);
            TextDraw.At(summary, new Vector2(origin.X + width - summarySize.X, midY - summarySize.Y * 0.5f), Styling.TextDim);
        }

        ImGui.Dummy(new Vector2(width, height));
        if (scrollIntoView)
        {
            ImGui.SetScrollHereY(0f);
        }
    }

    private static void DrawGrid(Configuration cfg, ItemGoalDefinition goal)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = Gap * scale;
        var avail = ImGui.GetContentRegionAvail().X;
        var columns = Math.Max(1, (int)MathF.Floor((avail + gap) / (Layout.ZoneCardMinWidth * scale + gap)));
        var cardWidth = (avail - gap * (columns - 1)) / columns;

        using var itemSpacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap));
        for (var index = 0; index < goal.Drops.Length; index++)
        {
            if (index % columns != 0)
            {
                ImGui.SameLine(0f, gap);
            }

            DrawCard(cfg, goal, index, cardWidth);
        }
    }

    private static void DrawCard(Configuration cfg, ItemGoalDefinition goal, int dropIndex, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(width, Layout.ZoneCardHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var status = ItemGoalProgress.Status(goal, dropIndex, cfg);
        var dl = ImGui.GetWindowDrawList();

        ImGui.PushID((nint)(dropIndex + 1));
        var hit = Hit.Area("##item", size, false);
        ImGui.PopID();

        var active = status.Done ? 1f : 0f;
        Paint.Glass(dl, origin, end, Styling.CardRounding * scale, Styling.AccentMint, 0.02f + 0.12f * active, 0f);

        var midY = origin.Y + size.Y * 0.5f;
        var discRadius = 9f * scale;
        var discCenter = new Vector2(origin.X + 14f * scale + discRadius, midY);
        if (status.Done)
        {
            TextDraw.IconCentered(FontAwesomeIcon.Check, discCenter, Styling.AccentMint);
        }
        else
        {
            dl.AddCircle(discCenter, discRadius, Paint.Col(Styling.WithAlpha(Styling.BorderDim, 0.9f)), 0, 1.4f * scale);
        }

        var rightX = end.X - 12f * scale;
        rightX -= DrawProgress(dropIndex, status, rightX, midY) + 8f * scale;

        var textX = discCenter.X + discRadius + 12f * scale;
        var name = TextDraw.Truncate(ItemGoalProgress.ItemName(status.ItemId), rightX - textX);
        var nameSize = TextDraw.Measure(name);
        TextDraw.At(name, new Vector2(textX, midY - nameSize.Y * 0.5f), status.Done ? Styling.TextDim : Styling.TextSecondary);

        if (Hit.HoveringRect(origin, end))
        {
            Tooltip.Show(Loc.T(L.Grind.ItemDropsIn, ItemGoalProgress.ZoneNames(goal, dropIndex)));
        }
    }

    private static float DrawProgress(int dropIndex, ItemGoalDropStatus status, float rightX, float midY)
    {
        using (Fonts.PushCaption())
        {
            var label = ProgressLabel(dropIndex, status);
            var color = status.Done ? Styling.AccentMint : Styling.TextDim;
            var labelSize = TextDraw.Measure(label);
            TextDraw.At(label, new Vector2(rightX - labelSize.X, midY - labelSize.Y * 0.5f), color);
            return labelSize.X;
        }
    }

    private static string ProgressLabel(int dropIndex, ItemGoalDropStatus status)
    {
        var cached = progressLabels[dropIndex];
        if (cached.Text is not null && cached.ItemId == status.ItemId && cached.Have == status.Have && cached.Need == status.Need)
        {
            return cached.Text;
        }

        var text = $"{Math.Min(status.Have, status.Need)} / {status.Need}";
        progressLabels[dropIndex] = (status.ItemId, status.Have, status.Need, text);
        return text;
    }
}
