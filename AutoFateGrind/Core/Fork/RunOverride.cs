using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Fork;

// Fork: a run with settings handed in by another plugin (BoatRunner's FATE levelling: one class to a level cap, in the zones
// that suit its level), for that run only. The user's own settings are taken aside first and put back when the run ends;
// while the run goes, every save writes the user's settings, never the run's (the run saves on its own, mid-run).
internal static class RunOverride
{
    /// <summary>What another plugin asks for: classes by gear set (1-based, as in the game's list), the level to stop at, the zones, the FATE level band around the character's level.</summary>
    internal sealed class Request
    {
        public List<byte> Gearsets { get; set; } = [];
        public int StopAtLevel { get; set; }
        public List<uint> Zones { get; set; } = [];
        public int MaxLevelAbove { get; set; } = 3;
        public int MaxLevelBelow { get; set; } = 10;
    }

    private sealed record Snapshot(
        List<ClassQueueEntry> Queue, AfterClassQueueDone After, bool ApplyClass, List<uint> Zones, string ModeId,
        bool RangeOn, int Above, int Below, bool WindowOn, bool StopAfterFates, bool StopAfterMinutes);

    private static Snapshot? original;
    private static long startedAtMs;

    /// <summary>Writes the settings to disk (replaced in the tests).</summary>
    internal static Action<Configuration> Persist = c => Plugin.PluginInterface.SavePluginConfig(c);

    internal static bool Active => original is not null;

    private static Snapshot Take(Configuration c) => new(
        [.. c.ClassQueue], c.AfterClassQueueDone, c.ApplyClassOnStart, [.. c.SelectedZones], c.ModeId,
        c.LevelRangeFilterEnabled, c.MaxLevelAbove, c.MaxLevelBelow, c.LevelWindowEnabled, c.StopAfterFatesEnabled, c.StopAfterMinutesEnabled);

    private static void Put(Configuration c, Snapshot s)
    {
        c.ClassQueue = [.. s.Queue];
        c.AfterClassQueueDone = s.After;
        c.ApplyClassOnStart = s.ApplyClass;
        c.SelectedZones = [.. s.Zones];
        c.ModeId = s.ModeId;
        c.LevelRangeFilterEnabled = s.RangeOn;
        c.MaxLevelAbove = s.Above;
        c.MaxLevelBelow = s.Below;
        c.LevelWindowEnabled = s.WindowOn;
        c.StopAfterFatesEnabled = s.StopAfterFates;
        c.StopAfterMinutesEnabled = s.StopAfterMinutes;
    }

    /// <summary>The request's settings in place of the user's (taken aside); false when an override already runs or the request is empty.</summary>
    internal static bool Apply(Configuration c, Request r)
    {
        if (Active || r.Gearsets.Count == 0 || r.Zones.Count == 0) return false;
        original = Take(c);
        c.ClassQueue = [.. r.Gearsets.Select(g => new ClassQueueEntry { GearsetIndex = g, StopAtLevel = r.StopAtLevel })];
        c.AfterClassQueueDone = AfterClassQueueDone.StopRun;
        c.ApplyClassOnStart = true;
        c.SelectedZones = [.. r.Zones];
        c.ModeId = Modes.PlainFatesMode.ModeId;
        c.LevelRangeFilterEnabled = true;
        c.MaxLevelAbove = r.MaxLevelAbove;
        c.MaxLevelBelow = r.MaxLevelBelow;
        c.LevelWindowEnabled = false;
        c.StopAfterFatesEnabled = false;
        c.StopAfterMinutesEnabled = false;
        startedAtMs = Environment.TickCount64;
        return true;
    }

    /// <summary>The user's settings back (and saved).</summary>
    internal static void Restore(Configuration c)
    {
        if (original is not { } o) return;
        Put(c, o);
        original = null;
        Persist(c);
    }

    /// <summary>A save while an override runs: the user's settings are written, the run's kept in memory.</summary>
    internal static bool SaveOriginal(Configuration c)
    {
        if (original is not { } o) return false;
        var now = Take(c);
        Put(c, o);
        Persist(c);
        Put(c, now);
        return true;
    }

    /// <summary>Once the run has ended (a few seconds after its start, not running), the user's settings come back.</summary>
    internal static void Tick(Plugin plugin)
    {
        if (!Active || Environment.TickCount64 - startedAtMs < 5_000 || plugin.Controller.Running) return;
        Svc.Log.Information("[AFG] The run with another plugin's settings ended; your own settings are back.");
        Restore(Plugin.Cfg);
    }
}
