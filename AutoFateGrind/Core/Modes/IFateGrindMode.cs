using AutoFateGrind.Core.Zones;

namespace AutoFateGrind.Core.Modes;

public readonly struct ModeContext
{
    public int CompletedCount { get; init; }
    public IReadOnlyList<ZoneInfo> Zones { get; init; }
    public TimeSpan Elapsed { get; init; }
    public int LevelsGained { get; init; }
    public bool LevelingJobAtMax { get; init; }
}

public interface IFateGrindMode
{
    // Stable serialization key — never change once shipped (persisted in config as ModeId).
    string Id { get; }

    string DisplayName { get; }
    string Description { get; }

    bool IsComplete(ModeContext ctx);

    string? GetRemainingDisplay(ModeContext ctx) => null;

    // A goal that chooses its own zones replaces the zone library with its own roster.
    bool PlansZones => false;

    IReadOnlyList<ZoneInfo> PlanZones(Configuration cfg) => [];

    // A selected zone the goal cannot use is kept in the plan but skipped.
    bool AcceptsZone(uint territoryId) => true;

    // A zone with nothing left to earn is rotated past, and the run ends once every zone is done.
    bool IsZoneDone(uint territoryId) => false;

    string ZoneDoneReason(uint territoryId) => "nothing left to earn here";
}
