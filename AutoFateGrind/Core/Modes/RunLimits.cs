namespace AutoFateGrind.Core.Modes;

// Optional caps that end a run whatever the goal is: a FATE count, a clock, and a level gain.
public static class RunLimits
{
    public const int MaxFates = 9999;
    public const int MaxMinutes = 1440;
    public const int MaxLevels = 99;

    public static bool Reached(Configuration cfg, ModeContext ctx)
        => (cfg.StopAfterFatesEnabled && ctx.CompletedCount >= FateCap(cfg))
        || (cfg.StopAfterMinutesEnabled && ctx.Elapsed >= TimeSpan.FromMinutes(MinuteCap(cfg)))
        || (cfg.StopAfterLevelsEnabled && ctx.LevelsGained >= LevelCap(cfg));

    public static bool Any(Configuration cfg)
        => cfg.StopAfterFatesEnabled || cfg.StopAfterMinutesEnabled || cfg.StopAfterLevelsEnabled;

    public static int FateCap(Configuration cfg) => Math.Clamp(cfg.TargetFateCount, 1, MaxFates);

    public static int MinuteCap(Configuration cfg) => Math.Clamp(cfg.TargetMinutes, 1, MaxMinutes);

    public static int LevelCap(Configuration cfg) => Math.Clamp(cfg.TargetLevels, 1, MaxLevels);
}
