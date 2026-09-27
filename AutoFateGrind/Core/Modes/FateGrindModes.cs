namespace AutoFateGrind.Core.Modes;

// Goals answer "what am I farming"; the run count and clock that used to be modes are RunLimits now.
public static class FateGrindModes
{
    public const string RetiredRunCountId = "runcount";
    public const string RetiredTimeBoxedId = "timeboxed";
    public const string RetiredEndlessId = "endless";

    private static readonly List<IFateGrindMode> registered = Build();

    private static List<IFateGrindMode> Build()
    {
        var definitions = Game.Items.ItemGoalCatalog.Definitions;
        var modes = new List<IFateGrindMode>(4 + definitions.Length)
        {
            new MaxGemstonesMode(),
            new PlainFatesMode(),
            new SharedFateRanksMode(),
        };
        for (var index = 0; index < definitions.Length; index++)
        {
            modes.Add(new ItemGoalMode(definitions[index]));
        }

        modes.Add(new YokaiMedalsMode());
        return modes;
    }

    public static IReadOnlyList<IFateGrindMode> All => registered;

    public static IFateGrindMode Default => registered[0];

    public static IFateGrindMode? GetById(string? id)
    {
        if (id is null)
        {
            return null;
        }

        for (var index = 0; index < registered.Count; index++)
        {
            if (registered[index].Id == id)
            {
                return registered[index];
            }
        }

        return null;
    }

    public static string IdForLegacy(GrindMode legacy) => legacy switch
    {
        GrindMode.MaxGemstones => MaxGemstonesMode.ModeId,
        GrindMode.RunCount     => PlainFatesMode.ModeId,
        GrindMode.Endless      => PlainFatesMode.ModeId,
        GrindMode.MaxFates     => SharedFateRanksMode.ModeId,
        _                      => MaxGemstonesMode.ModeId,
    };
}
