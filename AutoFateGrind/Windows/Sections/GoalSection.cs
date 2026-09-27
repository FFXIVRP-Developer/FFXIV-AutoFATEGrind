using AutoFateGrind.Core;
using AutoFateGrind.Core.Game.Items;
using AutoFateGrind.Core.Game.SharedFates;
using AutoFateGrind.Core.Game.Yokai;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Tasks;
using AutoFateGrind.Core.Trading;
using AutoFateGrind.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoFateGrind.Windows.Sections;

// "Goal": category tabs, one card per goal with a live status line, and the active goal's own settings underneath.
internal static class GoalSection
{
    private enum Category { Farm, Ranks, Relics, Events }

    private const float Gap = 8f;
    private const float CardMinWidth = 300f;
    private const float CardHeight = 60f;
    private const float PadX = 18f;
    private const float PadY = 14f;
    private const float ListSlide = 8f;
    private const float StepperGap = 10f;
    private const int GemstoneStep = 50;
    private const int MaxYokaiMedals = 99;
    private const string Separator = "  ·  ";

    private static readonly Segmented.Item[] categoryItems = new Segmented.Item[4];
    private static readonly List<IFateGrindMode> cardScratch = new();
    private static int currentCategory = -1;

    public static void Draw(Configuration cfg, AutoFateController ctrl, int zoneCount)
    {
        DrawHeader(cfg, zoneCount);
        Styling.VSpace(10f);

        if (currentCategory < 0)
        {
            currentCategory = (int)CategoryOf(cfg.ActiveMode.Id);
        }

        RefreshCategoryItems();
        Segmented.Draw("##afg_goal_categories", categoryItems, ref currentCategory);
        Styling.VSpace(8f);

        using (Motion.PushSwitch("##afg_goal_cards", currentCategory, slide: ListSlide))
        {
            DrawCards(cfg, ctrl, (Category)currentCategory);
        }

        Styling.VSpace(10f);
        DrawDetail(cfg, ctrl);
    }

    private static void DrawHeader(Configuration cfg, int zoneCount)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Layout.LibraryHeaderHeight * scale;
        var midY = origin.Y + height * 0.5f;

        var label = Loc.T(L.Grind.GoalTitle);
        var labelSize = TextDraw.SectionTitleSize(label);
        TextDraw.SectionTitle(label, new Vector2(origin.X, midY - labelSize.Y * 0.5f), Styling.TextStrong);

        using (Fonts.PushCaption())
        {
            var summary = TextDraw.Truncate(GoalSummary.Sentence(cfg, zoneCount), width - labelSize.X - 16f * scale);
            var summarySize = TextDraw.Measure(summary);
            TextDraw.At(summary, new Vector2(origin.X + width - summarySize.X, midY - summarySize.Y * 0.5f), Styling.TextDim);
        }

        ImGui.Dummy(new Vector2(width, height));
    }

    private static void RefreshCategoryItems()
    {
        categoryItems[(int)Category.Farm] = new Segmented.Item(FontAwesomeIcon.Gem, Loc.T(L.Grind.CatFarm));
        categoryItems[(int)Category.Ranks] = new Segmented.Item(FontAwesomeIcon.Trophy, Loc.T(L.Grind.CatRanks));
        categoryItems[(int)Category.Relics] = new Segmented.Item(FontAwesomeIcon.Cubes, Loc.T(L.Grind.CatRelics));
        categoryItems[(int)Category.Events] = new Segmented.Item(FontAwesomeIcon.Ghost, Loc.T(L.Grind.CatEvents));
    }

    private static void DrawCards(Configuration cfg, AutoFateController ctrl, Category category)
    {
        cardScratch.Clear();
        var all = FateGrindModes.All;
        for (var index = 0; index < all.Count; index++)
        {
            if (CategoryOf(all[index].Id) == category)
            {
                cardScratch.Add(all[index]);
            }
        }

        var scale = ImGuiHelpers.GlobalScale;
        var gap = Gap * scale;
        var avail = ImGui.GetContentRegionAvail().X;
        var columns = Math.Max(1, (int)MathF.Floor((avail + gap) / (CardMinWidth * scale + gap)));
        var cardWidth = (avail - gap * (columns - 1)) / columns;

        using var itemSpacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(gap, gap));
        for (var index = 0; index < cardScratch.Count; index++)
        {
            if (index % columns != 0)
            {
                ImGui.SameLine(0f, gap);
            }

            DrawCard(cfg, ctrl, cardScratch[index], cardWidth);
        }
    }

    private static void DrawCard(Configuration cfg, AutoFateController ctrl, IFateGrindMode mode, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(width, CardHeight * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var selected = mode.Id == cfg.ActiveMode.Id;
        var interactive = !ctrl.Running;

        ImGui.PushID(mode.Id);
        var hit = Hit.Area("##goal", size, interactive);
        var hover = Motion.Hover(Motion.Key("##goal"), hit.Hovered);
        var active = Motion.Approach(Motion.Key("##goal", 1), selected ? 1f : 0f, 14f);
        ImGui.PopID();

        if (hit.Clicked && !selected)
        {
            cfg.ModeId = mode.Id;
            cfg.SaveDebounced();
        }

        var dl = ImGui.GetWindowDrawList();
        Paint.Glass(dl, origin, end, Styling.CardRounding * scale, Styling.AccentViolet, 0.02f + 0.16f * active, hover);

        var midY = origin.Y + size.Y * 0.5f;
        var discRadius = 9f * scale;
        var discCenter = new Vector2(origin.X + 14f * scale + discRadius, midY);
        DrawSelector(dl, discCenter, discRadius, active);

        var lineHeight = ImGui.GetTextLineHeight();
        var icon = IconFor(mode.Id);
        var iconSize = TextDraw.IconSize(icon);
        var textX = discCenter.X + discRadius + 12f * scale;
        var nameY = origin.Y + 11f * scale;
        var rightX = end.X - 12f * scale;
        TextDraw.Icon(icon, new Vector2(textX, nameY + (lineHeight - iconSize.Y) * 0.5f), selected ? Styling.AccentVioletSoft : Styling.TextDim);

        var nameX = textX + iconSize.X + 8f * scale;
        var nameColor = Vector4.Lerp(Styling.TextSecondary, Styling.TextStrong, MathF.Max(active, hover));
        TextDraw.At(TextDraw.Truncate(NameFor(mode), rightX - nameX), new Vector2(nameX, nameY), nameColor);

        var (status, statusColor) = StatusFor(cfg, mode);
        using (Fonts.PushCaption())
        {
            TextDraw.At(TextDraw.Truncate(status, rightX - textX), new Vector2(textX, nameY + lineHeight + 3f * scale), statusColor);
        }

        if (!interactive && Hit.HoveringRect(origin, end))
        {
            Tooltip.Show(Loc.T(L.Grind.PlanLocked));
        }
        else if (hit.Hovered)
        {
            Tooltip.Show(NoteFor(cfg, mode));
        }
    }

    private static void DrawSelector(ImDrawListPtr dl, Vector2 center, float radius, float active)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var ring = Vector4.Lerp(Styling.WithAlpha(Styling.BorderDim, 0.9f), Styling.AccentVioletSoft, active);
        dl.AddCircle(center, radius, Paint.Col(ring), 0, 1.4f * scale);
        if (active <= 0.01f)
        {
            return;
        }

        dl.AddCircleFilled(center, radius * active, Paint.Col(Styling.AccentViolet));
        if (active > 0.5f)
        {
            Paint.Check(dl, center, radius * 1.1f, Styling.WithAlpha(Styling.TextStrong, (active - 0.5f) * 2f), 1.8f * scale);
        }
    }

    private static void DrawDetail(Configuration cfg, AutoFateController ctrl)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var padX = PadX * scale;
        var padY = PadY * scale;
        var innerWidth = width - padX * 2f;
        var editable = !ctrl.Running;
        var mode = cfg.ActiveMode;
        var dl = ImGui.GetWindowDrawList();

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        var x = origin.X + padX;
        var y = origin.Y + padY;
        var subtitle = NameFor(mode);
        var subtitleSize = TextDraw.SmallCapsSize(subtitle);
        TextDraw.SmallCaps(subtitle, new Vector2(x, y), Styling.TextSecondary);
        y += subtitleSize.Y + 10f * scale;

        if (ItemGoalCatalog.Find(mode.Id) is { } goal)
        {
            y = DrawItemDetail(cfg, goal, x, y, innerWidth, editable);
        }
        else
        {
            y = mode.Id switch
            {
                MaxGemstonesMode.ModeId => DrawGemstoneDetail(cfg, x, y, innerWidth, editable),
                SharedFateRanksMode.ModeId => DrawCaption(SharedFateNote(cfg), x, y, innerWidth, Styling.TextMuted),
                YokaiMedalsMode.ModeId => DrawYokaiDetail(cfg, x, y, innerWidth, editable),
                _ => DrawCaption(Loc.T(L.Grind.NotePlain), x, y, innerWidth, Styling.TextMuted),
            };
        }

        var end = new Vector2(origin.X + width, y + padY);

        dl.ChannelsSetCurrent(0);
        Paint.Glass(dl, origin, end, Styling.PanelRounding * scale, Styling.AccentViolet, 0.07f, 0f, elevated: true);
        dl.ChannelsMerge();

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, end.Y - origin.Y));
    }

    private static float DrawGemstoneDetail(Configuration cfg, float x, float y, float width, bool editable)
    {
        var value = cfg.TargetGemstoneCount;
        y = DrawStepperRow("##afg_target_gems", Loc.T(L.Grind.StopAt), ref value, GemstoneStep, 1, AfgConstants.BicolorCap, Loc.T(L.Grind.UnitGemstones), x, y, editable);
        if (value != cfg.TargetGemstoneCount)
        {
            cfg.TargetGemstoneCount = value;
            cfg.SaveDebounced();
        }

        y += 8f * ImGuiHelpers.GlobalScale;
        return DrawCaption(Loc.T(L.Grind.NoteGemstones, GemstoneCatalog.CurrentWalletCount().ToString("N0", Loc.Culture)), x, y, width, Styling.TextMuted);
    }

    private static float DrawItemDetail(Configuration cfg, ItemGoalDefinition goal, float x, float y, float width, bool editable)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var value = ItemGoalProgress.WeaponCount(cfg);
        y = DrawStepperRow("##afg_relic_count", Loc.T(L.Grind.WeaponsToMake), ref value, 1, 1, ItemGoalCatalog.MaxWeaponCount, Loc.T(L.Grind.UnitWeapons), x, y, editable);
        if (value != cfg.TargetRelicCount)
        {
            cfg.TargetRelicCount = value;
            cfg.SaveDebounced();
        }

        y += 8f * scale;
        y = DrawCaption(Loc.T(goal.Note), x, y, width, Styling.TextMuted);

        var gateNote = goal.Gate switch
        {
            ItemGoalGate.QuestAccepted => Loc.T(L.Grind.ItemGoalQuest, ItemGoalProgress.QuestName(goal.QuestId)),
            ItemGoalGate.ZenithEquipped => Loc.T(L.Grind.ItemGoalZenith),
            _ => null,
        };
        if (gateNote is not null)
        {
            y += 4f * scale;
            y = DrawCaption(gateNote, x, y, width, ItemGoalProgress.IsAvailable(goal) ? Styling.TextMuted : Styling.AccentAmber);
        }

        var (have, need) = ItemGoalProgress.Totals(goal, cfg);
        y += 4f * scale;
        return DrawCaption(Loc.T(L.Grind.ItemsSummary, have, need), x, y, width, Styling.TextDim);
    }

    private static float DrawYokaiDetail(Configuration cfg, float x, float y, float width, bool editable)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var value = cfg.TargetYokaiMedals;
        y = DrawStepperRow("##afg_yokai_target", Loc.T(L.Grind.StopAt), ref value, 1, 1, MaxYokaiMedals, Loc.T(L.Grind.UnitYokaiMedals), x, y, editable);
        if (value != cfg.TargetYokaiMedals)
        {
            cfg.TargetYokaiMedals = value;
            cfg.SaveDebounced();
        }

        y += 8f * scale;
        y = DrawCaption(Loc.T(L.Grind.NoteYokai), x, y, width, Styling.TextMuted);

        var (owned, weaponsLeft) = YokaiProgress.Ownership();
        var watchMissing = !YokaiOps.OwnsWatch();
        var watch = YokaiOps.IsWatchEquipped() ? Loc.T(L.Grind.YokaiEventWatchEquipped)
            : watchMissing ? Loc.T(L.Grind.YokaiEventWatchMissing)
            : Loc.T(L.Grind.YokaiEventWatchStored);
        var status = string.Concat(
            Loc.T(L.Grind.YokaiEventOwned, owned, YokaiCatalog.Entries.Length), Separator,
            Loc.Plural(L.Grind.YokaiEventWeaponsLeft, weaponsLeft), Separator,
            watch);
        y += 4f * scale;
        return DrawCaption(status, x, y, width, watchMissing ? Styling.AccentAmber : Styling.TextDim);
    }

    // The stepper still draws while a run is going, but its changes are dropped, matching the locked cards.
    private static float DrawStepperRow(string id, string label, ref int value, int step, int min, int max, string unit, float x, float y, bool editable)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = StepperGap * scale;
        var rowHeight = ImGui.GetFrameHeight();
        var labelSize = TextDraw.Measure(label);
        TextDraw.At(label, new Vector2(x, y + (rowHeight - labelSize.Y) * 0.5f), Styling.TextSecondary);

        var stepperX = x + labelSize.X + gap;
        ImGui.SetCursorScreenPos(new Vector2(stepperX, y));
        var edited = value;
        if (Stepper.Draw(id, ref edited, step, min, max, "%d") && editable)
        {
            value = Math.Clamp(edited, min, max);
        }

        var unitSize = TextDraw.Measure(unit);
        TextDraw.At(unit, new Vector2(stepperX + Stepper.DefaultWidth * scale + gap, y + (rowHeight - unitSize.Y) * 0.5f), Styling.TextDim);
        return y + rowHeight;
    }

    private static float DrawCaption(string text, float x, float y, float width, Vector4 color)
    {
        using (Fonts.PushCaption())
        {
            TextDraw.Wrapped(text, new Vector2(x, y), width, color);
            return y + TextDraw.MeasureWrapped(text, width).Y;
        }
    }

    private static string SharedFateNote(Configuration cfg)
    {
        var (maxed, ranked) = SharedFateProgress.CountMaxed(cfg.SelectedZones);
        var note = Loc.T(L.Grind.NoteSharedFates);
        return ranked == 0 ? note : string.Concat(note, " ", Loc.T(L.Grind.NoteSharedFatesSummary, maxed, ranked));
    }

    private static Category CategoryOf(string modeId)
    {
        if (ItemGoalCatalog.IsItemGoal(modeId)) return Category.Relics;
        return modeId switch
        {
            SharedFateRanksMode.ModeId => Category.Ranks,
            YokaiMedalsMode.ModeId => Category.Events,
            _ => Category.Farm,
        };
    }

    private static FontAwesomeIcon IconFor(string modeId) => modeId switch
    {
        MaxGemstonesMode.ModeId => FontAwesomeIcon.Gem,
        PlainFatesMode.ModeId => FontAwesomeIcon.Bolt,
        SharedFateRanksMode.ModeId => FontAwesomeIcon.Trophy,
        ItemGoalCatalog.AtmaModeId => FontAwesomeIcon.Sun,
        ItemGoalCatalog.LuminousModeId => FontAwesomeIcon.Snowflake,
        ItemGoalCatalog.MemoriesModeId => FontAwesomeIcon.Book,
        ItemGoalCatalog.LawsOrderModeId => FontAwesomeIcon.Skull,
        ItemGoalCatalog.DemiatmaModeId => FontAwesomeIcon.Cubes,
        ItemGoalCatalog.PasteModeId => FontAwesomeIcon.Flask,
        YokaiMedalsMode.ModeId => FontAwesomeIcon.Ghost,
        _ => FontAwesomeIcon.Flag,
    };

    private static string NameFor(IFateGrindMode mode)
    {
        if (ItemGoalCatalog.Find(mode.Id) is { } goal)
        {
            return Loc.T(goal.Name);
        }

        return mode.Id switch
        {
            MaxGemstonesMode.ModeId => Loc.T(L.Grind.CardGemstones),
            PlainFatesMode.ModeId => Loc.T(L.Grind.CardPlain),
            SharedFateRanksMode.ModeId => Loc.T(L.Grind.CardShared),
            YokaiMedalsMode.ModeId => Loc.T(L.Grind.CardYokai),
            _ => mode.DisplayName,
        };
    }

    private static string NoteFor(Configuration cfg, IFateGrindMode mode)
    {
        if (ItemGoalCatalog.Find(mode.Id) is { } goal)
        {
            return Loc.T(goal.Note);
        }

        return mode.Id switch
        {
            MaxGemstonesMode.ModeId => Loc.T(L.Grind.NoteGemstones, GemstoneCatalog.CurrentWalletCount().ToString("N0", Loc.Culture)),
            SharedFateRanksMode.ModeId => Loc.T(L.Grind.NoteSharedFates),
            YokaiMedalsMode.ModeId => Loc.T(L.Grind.NoteYokai),
            _ => Loc.T(L.Grind.NotePlain),
        };
    }

    private static (string Text, Vector4 Color) StatusFor(Configuration cfg, IFateGrindMode mode)
    {
        if (ItemGoalCatalog.Find(mode.Id) is { } goal)
        {
            switch (ItemGoalProgress.Blocker(goal, cfg))
            {
                case ItemGoalBlocker.NeedQuest: return (Loc.T(L.Grind.StatusQuestMissing), Styling.AccentAmber);
                case ItemGoalBlocker.NeedZenith: return (Loc.T(L.Grind.StatusZenithMissing), Styling.AccentAmber);
            }

            var (have, need) = ItemGoalProgress.Totals(goal, cfg);
            return (Loc.T(L.Grind.ItemsSummary, have, need), have >= need ? Styling.AccentMint : Styling.TextDim);
        }

        switch (mode.Id)
        {
            case MaxGemstonesMode.ModeId:
            {
                var wallet = GemstoneCatalog.CurrentWalletCount();
                var target = Math.Max(1, cfg.TargetGemstoneCount);
                return (Loc.T(L.Grind.StatusGems, wallet.ToString("N0", Loc.Culture), target.ToString("N0", Loc.Culture)), wallet >= target ? Styling.AccentMint : Styling.TextDim);
            }
            case SharedFateRanksMode.ModeId:
            {
                var (maxed, ranked) = SharedFateProgress.CountMaxed(cfg.SelectedZones);
                if (ranked == 0)
                {
                    return (Loc.T(L.Grind.StatusRanksNoZones), Styling.AccentAmber);
                }

                return (Loc.T(L.Grind.StatusRanks, maxed, ranked), maxed >= ranked ? Styling.AccentMint : Styling.TextDim);
            }
            case YokaiMedalsMode.ModeId:
            {
                if (!YokaiOps.OwnsWatch())
                {
                    return (Loc.T(L.Grind.YokaiEventWatchMissing), Styling.AccentAmber);
                }

                var (collected, needed) = YokaiProgress.Totals(cfg);
                if (needed == 0)
                {
                    return (Loc.T(L.Grind.ZonesYokaiNone), Styling.AccentMint);
                }

                return (Loc.T(L.Grind.YokaiSummary, collected, needed), collected >= needed ? Styling.AccentMint : Styling.TextDim);
            }
            default:
                return (Loc.T(L.Grind.StatusPlain), Styling.TextDim);
        }
    }
}
