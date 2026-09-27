using AutoFateGrind.Core.Localization;

namespace AutoFateGrind.Core.Game.Items;

internal readonly record struct ItemGoalDrop(uint ItemId, uint[] ZoneIds, int PerWeapon);

internal enum ItemGoalGate : byte
{
    None,
    QuestAccepted,
    ZenithEquipped,
}

internal sealed class ItemGoalDefinition
{
    public required string ModeId { get; init; }
    public required string DisplayName { get; init; }
    public required LocString Name { get; init; }
    public required LocString GoalToken { get; init; }
    public required LocString Note { get; init; }
    public required ItemGoalDrop[] Drops { get; init; }
    public ItemGoalGate Gate { get; init; } = ItemGoalGate.None;
    public uint QuestId { get; init; }
}

// Drop tables follow FateToolKit's, verified against each item's description in the Item sheet: one atma or crystal per
// zone, memories shared between two zones, demiatma per Dawntrail zone, paste from any Dawntrail zone.
internal static class ItemGoalCatalog
{
    public const string AtmaModeId = "relicatma";
    public const string LuminousModeId = "relicluminous";
    public const string MemoriesModeId = "relicmemories";
    public const string LawsOrderModeId = "reliclawsorder";
    public const string DemiatmaModeId = "phantomdemiatma";
    public const string PasteModeId = "phantompaste";

    public const int MaxWeaponCount = 20;

    private static readonly uint[] DawntrailZones = [1187, 1188, 1189, 1190, 1191, 1192];

    public static readonly ItemGoalDefinition[] Definitions =
    [
        new()
        {
            ModeId = AtmaModeId,
            DisplayName = "Atma (Zodiac)",
            Name = L.Grind.ItemGoalAtma,
            GoalToken = L.Grind.GoalAtma,
            Note = L.Grind.NoteAtma,
            Gate = ItemGoalGate.ZenithEquipped,
            QuestId = 66971,
            Drops =
            [
                new(7851, [148], 1), new(7852, [146], 1), new(7853, [139], 1), new(7854, [152], 1),
                new(7855, [145], 1), new(7856, [134], 1), new(7857, [140], 1), new(7858, [180], 1),
                new(7859, [135], 1), new(7860, [154], 1), new(7861, [141], 1), new(7862, [138], 1),
            ],
        },
        new()
        {
            ModeId = LuminousModeId,
            DisplayName = "Luminous Crystals (Anima)",
            Name = L.Grind.ItemGoalLuminous,
            GoalToken = L.Grind.GoalLuminous,
            Note = L.Grind.NoteLuminous,
            Drops =
            [
                new(13569, [397], 1), new(13570, [401], 1), new(13571, [402], 1),
                new(13572, [398], 1), new(13573, [400], 1), new(13574, [399], 1),
            ],
        },
        new()
        {
            ModeId = MemoriesModeId,
            DisplayName = "Memories of the Dying (Resistance)",
            Name = L.Grind.ItemGoalMemories,
            GoalToken = L.Grind.GoalMemories,
            Note = L.Grind.NoteMemories,
            Drops =
            [
                new(31573, [397, 401], 20),
                new(31574, [398, 400], 20),
                new(31575, [399, 402], 20),
            ],
        },
        new()
        {
            ModeId = LawsOrderModeId,
            DisplayName = "Haunting and Vexatious Memories (Resistance)",
            Name = L.Grind.ItemGoalLawsOrder,
            GoalToken = L.Grind.GoalLawsOrder,
            Note = L.Grind.NoteLawsOrder,
            Gate = ItemGoalGate.QuestAccepted,
            QuestId = 69575,
            Drops =
            [
                new(32957, [612, 620, 621], 18),
                new(32958, [613, 614, 622], 18),
            ],
        },
        new()
        {
            ModeId = DemiatmaModeId,
            DisplayName = "Demiatma (Phantom)",
            Name = L.Grind.ItemGoalDemiatma,
            GoalToken = L.Grind.GoalDemiatma,
            Note = L.Grind.NoteDemiatma,
            Gate = ItemGoalGate.QuestAccepted,
            QuestId = 70855,
            Drops =
            [
                new(47744, [1187], 3), new(47745, [1188], 3), new(47746, [1189], 3),
                new(47747, [1190], 3), new(47748, [1191], 3), new(47749, [1192], 3),
            ],
        },
        new()
        {
            ModeId = PasteModeId,
            DisplayName = "Crystal Paste (Phantom)",
            Name = L.Grind.ItemGoalPaste,
            GoalToken = L.Grind.GoalPaste,
            Note = L.Grind.NotePaste,
            Gate = ItemGoalGate.QuestAccepted,
            QuestId = 70991,
            Drops = [new(50059, DawntrailZones, 1200)],
        },
    ];

    public static ItemGoalDefinition? Find(string? modeId)
    {
        if (modeId is null)
        {
            return null;
        }

        for (var index = 0; index < Definitions.Length; index++)
        {
            if (Definitions[index].ModeId == modeId)
            {
                return Definitions[index];
            }
        }

        return null;
    }

    public static bool IsItemGoal(string? modeId) => Find(modeId) is not null;
}
