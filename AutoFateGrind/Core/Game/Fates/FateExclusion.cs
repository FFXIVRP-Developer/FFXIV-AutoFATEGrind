namespace AutoFateGrind.Core.Game.Fates;

internal enum FateExclusion : byte
{
    None,
    Blacklisted,
    SessionStuck,
    SkippedRule,
    OutsideLevelBand,
    OutsideLevelWindow,
    TooLong,
    NotStarted,
    TooLittleTime,
    TooMuchProgress,
    Finished,
    NotOnMap,
}
