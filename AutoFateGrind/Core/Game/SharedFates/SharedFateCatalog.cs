namespace AutoFateGrind.Core.Game.SharedFates;

internal readonly record struct SharedFateZone(uint TerritoryId, uint AchievementId, byte TabIndex);

internal readonly record struct SharedFateRank(bool Known, int Completed, int Total, int Rank, int MaxRank, int RankProgress, int RankSize)
{
    public bool IsMaxed => Known && Completed >= Total;

    public float Fraction => Total > 0 ? Math.Clamp(Completed / (float)Total, 0f, 1f) : 0f;
}

// Each zone's Shared FATE rank is the progress counter of its "Free Market Friend" achievement (0..66), which the game
// requests per expansion tab; the ids are static, so they work on every client language.
internal static class SharedFateCatalog
{
    public const int TotalFates = 66;
    public const int TabCount = 3;
    public const byte ShadowbringersTab = 0;
    public const byte EndwalkerTab = 1;
    public const byte DawntrailTab = 2;

    private static readonly int[] ThreeRankSizes = [6, 60];
    private static readonly int[] FourRankSizes = [6, 20, 40];

    public static readonly SharedFateZone[] Entries =
    [
        new(813, 2343, ShadowbringersTab),
        new(814, 2344, ShadowbringersTab),
        new(815, 2345, ShadowbringersTab),
        new(816, 2346, ShadowbringersTab),
        new(817, 2347, ShadowbringersTab),
        new(818, 2348, ShadowbringersTab),
        new(956, 3022, EndwalkerTab),
        new(957, 3023, EndwalkerTab),
        new(958, 3024, EndwalkerTab),
        new(959, 3025, EndwalkerTab),
        new(961, 3026, EndwalkerTab),
        new(960, 3027, EndwalkerTab),
        new(1187, 3559, DawntrailTab),
        new(1188, 3560, DawntrailTab),
        new(1189, 3561, DawntrailTab),
        new(1190, 3562, DawntrailTab),
        new(1191, 3563, DawntrailTab),
        new(1192, 3564, DawntrailTab),
    ];

    public static bool HasRanks(uint territoryId) => IndexOfTerritory(territoryId) >= 0;

    public static int IndexOfTerritory(uint territoryId)
    {
        for (var index = 0; index < Entries.Length; index++)
        {
            if (Entries[index].TerritoryId == territoryId)
            {
                return index;
            }
        }

        return -1;
    }

    public static int IndexOfAchievement(uint achievementId)
    {
        for (var index = 0; index < Entries.Length; index++)
        {
            if (Entries[index].AchievementId == achievementId)
            {
                return index;
            }
        }

        return -1;
    }

    public static int MaxRank(byte tabIndex) => RankSizes(tabIndex).Length + 1;

    public static SharedFateRank Resolve(int entryIndex, int completed, int total)
    {
        var sizes = RankSizes(Entries[entryIndex].TabIndex);
        var maxRank = sizes.Length + 1;
        var clampedTotal = total > 0 ? total : TotalFates;
        var clampedCompleted = Math.Clamp(completed, 0, clampedTotal);
        var remaining = clampedCompleted;
        for (var index = 0; index < sizes.Length; index++)
        {
            if (remaining < sizes[index])
            {
                return new SharedFateRank(true, clampedCompleted, clampedTotal, index + 1, maxRank, remaining, sizes[index]);
            }

            remaining -= sizes[index];
        }

        return new SharedFateRank(true, clampedCompleted, clampedTotal, maxRank, maxRank, 0, 0);
    }

    private static int[] RankSizes(byte tabIndex) => tabIndex == DawntrailTab ? FourRankSizes : ThreeRankSizes;
}
