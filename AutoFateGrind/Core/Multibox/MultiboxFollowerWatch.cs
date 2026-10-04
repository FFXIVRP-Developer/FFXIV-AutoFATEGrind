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
internal static class MultiboxFollowerWatch
{
    private const int EveryMs = 3_000;
    private static readonly TimeSpan LeaderGoneAfter = TimeSpan.FromSeconds(30);

    private static long nextMs;
    private static bool? leaderWasRunning; // null until the first look after the plugin loaded
    private static DateTime leaderLastSeenUtc = DateTime.MinValue;
    private static bool stopAskedByUs;
    private static bool startedByUs;

    /// <summary>Parked at the break location because the leader stopped (for the Multibox tab).</summary>
    public static bool Parked { get; private set; }

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

        var leader = MultiboxLink.Leader(); // fresh and on this world = the leader is grinding
        if (leader is not null) leaderLastSeenUtc = DateTime.UtcNow;
        var leaderRunning = leader is not null || DateTime.UtcNow - leaderLastSeenUtc < LeaderGoneAfter;
        var running = plugin.Controller.Running;

        // The leader started (or was already going when this client loaded): start following.
        var leaderStarted = leaderRunning && leaderWasRunning != true;
        if (leaderRunning && !running && (leaderStarted || Parked))
        {
            Svc.Chat.Print($"[AFG] The leader {leader?.Name ?? ""} is grinding; following.");
            Parked = false;
            stopAskedByUs = false;
            startedByUs = true;
            plugin.StartFromCommand();
        }

        // The leader stopped: finish the FATE, then park.
        if (!leaderRunning && running && !stopAskedByUs)
        {
            Svc.Chat.Print("[AFG] The leader stopped; finishing this FATE, then parking at the break location.");
            stopAskedByUs = true;
            plugin.Controller.StopWhenSafe();
        }
        if (!leaderRunning && !running && (stopAskedByUs || (startedByUs && !Parked)))
        {
            stopAskedByUs = false;
            startedByUs = false;
            Park();
        }

        leaderWasRunning = leaderRunning;
    }

    private static void Park()
    {
        Parked = true;
        var command = Plugin.Cfg.HumanizerRetreat switch
        {
            HumanizerRetreat.Inn              => "inn",
            HumanizerRetreat.Apartment        => "apartment",
            HumanizerRetreat.PrivateHouse     => "home",
            HumanizerRetreat.FreeCompanyHouse => "fc",
            _                                 => null, // City (wander): stay where it is
        };
        if (command is null)
        {
            Svc.Chat.Print("[AFG] Parked here (the break location is City).");
            return;
        }
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
