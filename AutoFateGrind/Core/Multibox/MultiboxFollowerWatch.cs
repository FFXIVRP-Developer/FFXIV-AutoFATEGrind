using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Tasks;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Multibox;

// Fork (README-FORK item 18): a client in "Follow the leader" mode is started and stopped with the leader.
//   leader starts (its file goes fresh)     → the follower starts its run (and travels to the leader)
//   leader stops (file stale for 30 s)      → the follower finishes its FATE (StopWhenSafe) and parks at the humanizer
//                                             break location (inn / apartment / house / FC; "City" = stays where it is)
//   leader starts again                     → the parked follower starts again
// A follower the user stopped by hand while the leader runs is left alone until the leader's next start.
// A slave that cannot use the leader's zone (below its level, or not unlocked: no aetheryte attuned) does not try: it
// finishes its FATE and parks, and starts again once the leader is somewhere it can follow.
internal static class MultiboxFollowerWatch
{
    private const int EveryMs = 3_000;
    // Without its leader (no party any more, or no leader grinding) a slave finishes its FATE and waits this long for the
    // leader before it parks; a leader that starts again in time re-invites it and it carries on.
    private const int WaitForLeaderMs = 60_000;

    private static long nextMs;
    private static bool? leaderWasRunning; // null until the first look after the plugin loaded
    private static long leaderGoneSinceMs;    // 0 while the leader grinds
    private static bool wasInParty;
    private static bool partyGone;            // dropped out of the party (the leader disbanded it): the leader stopped
    private static bool stopAskedByUs;
    private static bool startedByUs;
    private static long startAtMs; // the random start delay (0 = none set)
    private static readonly Random random = new();
    private const int StartDelayMinMs = 5_000, StartDelayMaxMs = 30_000;

    /// <summary>Parked at the break location because the leader stopped (for the Multibox tab).</summary>
    public static bool Parked { get; private set; }

    /// <summary>Why this slave cannot follow the leader where it is now; null when it can.</summary>
    public static string? Blocked { get; private set; }

    /// <summary>null = this slave can go where the leader grinds; else why not (zone level, zone not unlocked).</summary>
    public static string? CantFollowReason(LeaderState leader)
    {
        var zone = Zones.ZoneRegistry.Zones.FirstOrDefault(z => z.TerritoryId == leader.Territory);
        if (zone is null) return null; // not a FATE zone (the leader on a city break): nothing to check
        var level = Svc.PlayerState.Level;
        if (level > 0 && level < zone.MinLevel)
            return $"{zone.Name} needs Lv {zone.MinLevel}, I am Lv {level}";
        if (Svc.ClientState.TerritoryType != zone.TerritoryId && Zones.ZoneStateReader.AnyAetheryteAttuned()
         && !Zones.ZoneStateReader.IsTerritoryUnlocked(zone.TerritoryId))
            return $"{zone.Name} is not unlocked here (no aetheryte attuned)";
        return null;
    }

    public static bool IsFollower => Plugin.Cfg.MultiboxRole == MultiboxRole.Follower || Plugin.Cfg.ActiveMode is FollowLeaderMode;

    public static void Start() => Svc.Framework.Update += Tick;

    public static void Stop() => Svc.Framework.Update -= Tick;

    private static void Tick(Dalamud.Plugin.Services.IFramework _)
    {
        if (Environment.TickCount64 < nextMs) return;
        nextMs = Environment.TickCount64 + EveryMs;
        if (Plugin.Cfg.ActiveMode is not FollowLeaderMode || Plugin.Instance is not { } plugin) return;
        if (!Svc.ClientState.IsLoggedIn || Svc.Objects.LocalPlayer is null
         || Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BoundByDuty]) return;

        var now = Environment.TickCount64;

        // The leader grinds on another world: an idle slave travels there first (Lifestream), then follows as usual.
        if (!plugin.Controller.Running && MultiboxLink.LeaderAnyWorld() is { } far
            && Svc.Objects.LocalPlayer is { } self && far.World != self.CurrentWorld.RowId)
        {
            TravelToLeaderWorld(far);
            return;
        }

        var leader = MultiboxLink.Leader(); // fresh and on this world = the leader is grinding
        var inParty = ECommons.PartyFunctions.UniversalParty.Length > 1;
        if (wasInParty && !inParty) { partyGone = true; Svc.Log.Info("[AFG] Multibox: no longer in the party; the leader stopped"); }
        if (inParty) partyGone = false;
        wasInParty = inParty;
        var leaderRunning = leader is not null && !partyGone;
        if (leaderRunning) leaderGoneSinceMs = 0;
        else if (leaderGoneSinceMs == 0) leaderGoneSinceMs = now;
        var waitedOut = !leaderRunning && now - leaderGoneSinceMs >= WaitForLeaderMs;
        var running = plugin.Controller.Running;
        var blocked = leader is null ? null : CantFollowReason(leader);
        if (blocked != Blocked && blocked is not null) Svc.Log.Info($"[AFG] Multibox: cannot follow the leader: {blocked}");
        Blocked = blocked;

        // The leader started (or was already going when this client loaded): start following.
        var leaderStarted = leaderRunning && leaderWasRunning != true;
        // Blocked while idle (e.g. the leader started in a zone above this slave): waits like a parked slave, so it starts
        // once the leader moves somewhere it can follow.
        if (leaderRunning && blocked is not null && !running) Parked = true;
        // stopAskedByUs: it stopped itself (blocked, e.g. the leader passed through a zone it has not unlocked) and the
        // leader is somewhere it can follow again: start, do not sit idle (2026-10-04 slave7 stayed stopped).
        var wantStart = leaderRunning && blocked is null && !running && (leaderStarted || Parked || startAtMs != 0 || stopAskedByUs);
        if (!wantStart) startAtMs = 0;
        else if (startAtMs == 0)
        {
            // Each slave sets off on its own 5-30 s after the leader starts, not all in the same 3 s.
            startAtMs = Environment.TickCount64 + random.Next(StartDelayMinMs, StartDelayMaxMs);
            Svc.Log.Info($"[AFG] Multibox: the leader is grinding; starting in {(startAtMs - Environment.TickCount64) / 1000}s");
        }
        if (wantStart && Environment.TickCount64 >= startAtMs)
        {
            startAtMs = 0;
            Svc.Chat.Print($"[AFG] The leader {leader?.Name ?? ""} is grinding; following.");
            Parked = false;
            stopAskedByUs = false;
            startedByUs = true;
            plugin.StartFromCommand();
        }

        // The leader stopped, or went where this slave cannot follow: finish the FATE, then park.
        if ((!leaderRunning || blocked is not null) && running && !stopAskedByUs)
        {
            Svc.Chat.Print(blocked is not null
                ? $"[AFG] Cannot follow the leader: {blocked}. Finishing this FATE, then parking."
                : "[AFG] The leader stopped; finishing this FATE, then waiting 60 s for it before parking.");
            stopAskedByUs = true;
            plugin.Controller.StopWhenSafe();
        }
        // Park: blocked right away; without the leader once it waited 60 s (also a slave that logged in to no leader).
        if (!running && !Parked && (blocked is not null ? stopAskedByUs : waitedOut && (stopAskedByUs || startedByUs || !inParty)))
        {
            stopAskedByUs = false;
            startedByUs = false;
            Park();
        }

        leaderWasRunning = leaderRunning;
    }

    private const int WorldTravelRetryMs = 120_000;
    private static long nextWorldTravelMs;

    private static void TravelToLeaderWorld(LeaderState leader)
    {
        if (Environment.TickCount64 < nextWorldTravelMs) return;
        try
        {
            var busy = Svc.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
            if (busy.HasFunction && busy.InvokeFunc()) return;
            nextWorldTravelMs = Environment.TickCount64 + WorldTravelRetryMs;
            var name = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.World>().GetRowOrDefault(leader.World)?.Name.ExtractText() ?? leader.World.ToString();
            Svc.Chat.Print($"[AFG] The leader {leader.Name} is on {name}; travelling there.");
            Svc.Log.Info($"[AFG] Multibox: the leader is on {name}; Lifestream world travel");
            Svc.PluginInterface.GetIpcSubscriber<string, bool>("Lifestream.ChangeWorld").InvokeFunc(name);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[AFG] Multibox: world travel to the leader failed: {ex.Message}");
        }
    }

    private static void Park()
    {
        Parked = true;
        if (Blocked is null) MultiboxParty.LeaveAsSlave(); // the leader stopped (a blocked slave stays in the party)
        // Always the inn of the world it is on: a break location like an apartment or a house is on the home world, and
        // going there world-travelled the slaves away from the leader (2026-10-04).
        const string command = "inn";
        try
        {
            var busy = Svc.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
            if (busy.HasFunction && busy.InvokeFunc()) return;
            Svc.PluginInterface.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand").InvokeAction(command);
            Svc.Chat.Print($"[AFG] Parking: /li {command}.");
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[AFG] Parking: Lifestream /li {command} failed: {ex.Message}");
        }
    }
}
