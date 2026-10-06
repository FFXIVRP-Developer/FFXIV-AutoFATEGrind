using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Fork;

// Fork: a run with settings handed in by another plugin (BoatRunner's FATE levelling: one class to a level cap, in the zones
// that suit its level), for that run only. The user's own settings are taken aside first and put back when the run ends;
// while the run goes, every save writes the user's settings, never the run's (the run saves on its own, mid-run).
// A levelling run is always solo and nonstop (user, 2026-10-06: "it has to be solo, it has to grind nonstop through the maps,
// it has to be fates that the character can do"): its own role, no goal, cap, break, trade or log-out of the user's.
internal static class RunOverride
{
    /// <summary>What another plugin asks for.</summary>
    internal sealed class Request
    {
        /// <summary>The classes by gear set (1-based, as in the game's list), in order.</summary>
        public List<byte> Gearsets { get; set; } = [];
        /// <summary>The level each class stops at; the run ends when every class is there.</summary>
        public int StopAtLevel { get; set; }
        public List<uint> Zones { get; set; } = [];
        /// <summary>The FATE level band around the character's level.</summary>
        public int MaxLevelAbove { get; set; } = 2;
        public int MaxLevelBelow { get; set; } = 10;
        /// <summary>FATE kinds left out, by name (PublicEvent.FateRule: Escort, Chase, ...); an unknown name is ignored.</summary>
        public List<string> SkipFateRules { get; set; } = [];
        /// <summary>Seconds a zone with no FATE is waited on before the next.</summary>
        public int SwapZoneWaitSec { get; set; } = 10;
    }

    private sealed record Snapshot(
        List<ClassQueueEntry> Queue, AfterClassQueueDone After, bool ApplyClass, List<uint> Zones, string ModeId,
        bool RangeOn, int Above, int Below, bool WindowOn, bool StopAfterFates, bool StopAfterMinutes,
        Multibox.MultiboxRole Role, bool SwapZones, int SwapWait, HashSet<int> SkippedRules, bool Humanizer, bool TradeOnCap,
        AfterRunAction AfterRun);

    private static Snapshot? original;
    private static long startedAtMs;

    /// <summary>Writes the settings to disk (replaced in the tests).</summary>
    internal static Action<Configuration> Persist = c => Plugin.PluginInterface.SavePluginConfig(c);

    internal static bool Active => original is not null;

    private static Snapshot Take(Configuration c) => new(
        [.. c.ClassQueue], c.AfterClassQueueDone, c.ApplyClassOnStart, [.. c.SelectedZones], c.ModeId,
        c.LevelRangeFilterEnabled, c.MaxLevelAbove, c.MaxLevelBelow, c.LevelWindowEnabled, c.StopAfterFatesEnabled, c.StopAfterMinutesEnabled,
        c.MultiboxRole, c.SwapZonesWhenEmpty, c.SwapZoneWaitSec, [.. c.SkippedFateRules], c.HumanizerEnabled, c.TradeOnCap, c.AfterRun);

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
        c.MultiboxRole = s.Role;
        c.SwapZonesWhenEmpty = s.SwapZones;
        c.SwapZoneWaitSec = s.SwapWait;
        c.SkippedFateRules = [.. s.SkippedRules];
        c.HumanizerEnabled = s.Humanizer;
        c.TradeOnCap = s.TradeOnCap;
        c.AfterRun = s.AfterRun;
    }

    /// <summary>The FATE kinds by name, as the game's FateRule values; unknown names left out.</summary>
    internal static HashSet<int> Rules(IEnumerable<string> names) =>
        [.. names.Select(n => Enum.TryParse<clib.Utils.PublicEvent.FateRule>(n, true, out var rule) ? (int?)rule : null)
                 .Where(r => r is not null).Select(r => r!.Value)];

    /// <summary>The request's settings in place of the user's (taken aside); false when an override already runs or the request is empty.</summary>
    internal static bool Apply(Configuration c, Request r)
    {
        if (Active || r.Gearsets.Count == 0 || r.Zones.Count == 0) return false;
        original = Take(c);
        // The classes and where: the request's.
        c.ClassQueue = [.. r.Gearsets.Select(g => new ClassQueueEntry { GearsetIndex = g, StopAtLevel = r.StopAtLevel })];
        c.AfterClassQueueDone = AfterClassQueueDone.StopRun;
        c.ApplyClassOnStart = true;
        c.SelectedZones = [.. r.Zones];
        // FATEs the character can do: a band around its level, the kinds it cannot do alone left out.
        c.LevelRangeFilterEnabled = true;
        c.MaxLevelAbove = r.MaxLevelAbove;
        c.MaxLevelBelow = r.MaxLevelBelow;
        c.LevelWindowEnabled = false;
        c.SkippedFateRules = Rules(r.SkipFateRules);
        // Solo and nonstop: no goal, cap, break, trade or log-out; the next zone when one has no FATE.
        c.ModeId = Modes.PlainFatesMode.ModeId;
        c.MultiboxRole = Multibox.MultiboxRole.Solo;
        c.StopAfterFatesEnabled = false;
        c.StopAfterMinutesEnabled = false;
        c.SwapZonesWhenEmpty = true;
        c.SwapZoneWaitSec = r.SwapZoneWaitSec;
        c.HumanizerEnabled = false;
        c.TradeOnCap = false;
        c.AfterRun = AfterRunAction.StayLoggedIn;
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
