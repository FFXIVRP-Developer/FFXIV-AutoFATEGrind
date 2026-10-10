using AutoFateGrind.Core.Ipc;
using AutoFateGrind.Core.Zones;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

// Break task: teleports to the plan's territory, then either wanders between random reachable points or
// stands at a saved spot until the configured break window elapses. The grind loop calls this through
// AutoFateController; on return the controller resumes the FATE grind in the origin zone, so this task
// only owns the break itself.
internal sealed partial class AutoHumanize(BreakPlan plan, int durationMs) : AutoCommon // Fork: partial (AutoHumanize.Retreat.cs)
{
    private BreakPlan plan = plan; // Fork: not readonly; a reached retreat (inn, housing) replaces it
    private readonly int durationMs = durationMs;

    // False until we actually reach the city and start the break. Lets the controller distinguish a real
    // break from a teleport-abort, so a skipped break re-triggers on the next FATE instead of being consumed.
    public bool BreakTaken { get; private set; }

    private const int   TeleportWatchdogMs    = 60_000;
    private const int   WalkWatchdogMs        = 90_000;
    private const int   NavmeshReadyWaitMs    = 60_000;
    private const int   PlayerWaitPollMs      = 500;
    private const int   RouteQueryTimeoutMs   = 5_000;
    private const int   CandidateAttempts     = 8;
    private const float ArrivalTolerance      = 4f;
    private const float CandidateHalfExtentXZ = 10f;
    private const float CandidateHalfExtentY  = 5f;
    // vnavmesh appends the raw target after a Detour partial path, so a route whose last leg is longer
    // than mesh noise ends at a wall and would then push straight through it.
    private const float PartialRouteGapMeters = 0.75f;
    private const float MaxDetourRatio        = 2f;
    private const int   IdleStatusRefreshMs   = 5_000;
    private const float SpotArrivalTolerance  = 1f;
    private const float SpotMountMinMeters    = 50f;

    private static readonly Random rng = new();

    // User-tunable knobs come straight from config so a /afg config edit mid-break takes effect on the
    // next hop. Snapped at use, not at construction, for the same reason.
    private static (int minMs, int maxMs) PauseRangeMs()
    {
        var cfg = Plugin.Cfg;
        var lo = Math.Max(0, cfg.HumanizerPauseMinSec);
        var hi = Math.Max(lo, cfg.HumanizerPauseMaxSec);
        return (lo * 1000, hi * 1000);
    }

    private static (float min, float max) WanderRange()
    {
        var cfg = Plugin.Cfg;
        var lo = Math.Max(1, cfg.HumanizerWanderMinMeters);
        var hi = Math.Max(lo, cfg.HumanizerWanderMaxMeters);
        return (lo, hi);
    }

    protected override async Task Execute()
    {
        // Fork: break at the configured retreat (inn, housing) when Lifestream gets there; upstream's plan (city or
        // idle spot) is the fallback. At the retreat the break stays where Lifestream ended: no spot walk.
        var territory = await ReachRetreat() ?? plan.TerritoryId;
        if (CancelToken.IsCancellationRequested) return;
        if (territory == 0)
        {
            Diag("Humanize aborted: the break location was not reached and no city or idle spot is set to fall back to.");
            return;
        }
        if (territory != plan.TerritoryId)
            plan = new BreakPlan(territory, RetreatLabels[(int)Plugin.Cfg.HumanizerRetreat], Spot: null,
                Wander: Plugin.Cfg.HumanizerBreakActivity == HumanizerBreakActivity.Wander);
        var label = plan.Place;
        var breakMin = Math.Max(1, durationMs / 60_000);
        Diag($"Humanize start: {plan.Describe()}, break {durationMs / 1000}s");
        Svc.Chat.Print($"[AFG] Humanize: taking a ~{breakMin}m break in {label}.");

        if (Svc.ClientState.TerritoryType != plan.TerritoryId)
        {
            var reached = false;
            await RunWithStatusPinned($"Teleporting to {label}",
                async () => reached = await TeleportToTerritory(plan.TerritoryId, TeleportTarget(), "humanize-teleport", TeleportWatchdogMs));
            if (!reached)
            {
                Diag($"Humanize aborted: could not reach {label} (still in {Svc.ClientState.TerritoryType}).");
                return;
            }
        }

        BreakTaken = true;
        await WaitForNavmeshReady(NavmeshReadyWaitMs);
        if (CancelToken.IsCancellationRequested) return;

        var deadline = Environment.TickCount64 + durationMs;
        if (plan.Wander)
        {
            await Wander(label, deadline);
            return;
        }
        await Idle(label, deadline);
    }

    // A spot beside a border shard would resolve to the neighbouring city (issue #21), so the teleport
    // aims at the zone's own aetheryte nearest the spot and the walk covers the rest.
    private Vector3 TeleportTarget()
    {
        if (plan.Spot is not { } spot) return Vector3.Zero;
        return ZoneAetherytes.TryFindNearest(plan.TerritoryId, spot.Position, out var aetheryte) ? aetheryte.Position : spot.Position;
    }

    private bool LeftBreakTerritory()
    {
        if (Svc.ClientState.TerritoryType == plan.TerritoryId) return false;
        Diag($"Humanize: territory changed to {Svc.ClientState.TerritoryType} (expected {plan.TerritoryId}); ending early.");
        return true;
    }

    private async Task Idle(string label, long deadline)
    {
        if (plan.Spot is { } spot)
        {
            await WalkToSpot(spot.Position, deadline);
            if (CancelToken.IsCancellationRequested) return;
        }

        await SafeDismount("humanize-idle-dismount");

        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) return;
            if (LeftBreakTerritory()) return;

            var remainingSec = Math.Max(0, (deadline - Environment.TickCount64) / 1000);
            Status = $"Idling in {label} (~{remainingSec}s left)";
            await IdleFor(IdleStatusRefreshMs, deadline);
        }

        Diag($"Humanize done: idled in {label}.");
    }

    // Arriving anywhere still counts as the break: a walk that ends short idles where it stopped.
    private async Task WalkToSpot(Vector3 spot, long deadline)
    {
        await RideAethernetShortcut(spot, "humanize-aethernet");
        if (CancelToken.IsCancellationRequested) return;

        Status = $"Walking to the idle spot in {plan.Place}";
        var territoryId = plan.TerritoryId;
        var arrived = await WalkWithRetries(
            () => new MoveOp(o => o.Move(territoryId, spot, SpotMovement(spot),
                stopCondition: () => Environment.TickCount64 >= deadline || CancelToken.IsCancellationRequested)),
            WalkWatchdogMs, "humanize-spot-walk",
            () => WithinReach(spot, SpotArrivalTolerance) || Environment.TickCount64 >= deadline);
        if (!arrived && !CancelToken.IsCancellationRequested)
        {
            Diag($"Humanize: could not reach the idle spot {spot}; idling where the walk ended.");
        }
    }

    private static MovementConfig SpotMovement(Vector3 spot)
    {
        var far = Svc.Objects.LocalPlayer is { } player && Vector3.Distance(player.Position, spot) >= SpotMountMinMeters;
        return (far ? MovementConfig.Everything : MovementConfig.Default).WithTolerance(SpotArrivalTolerance);
    }

    private async Task Wander(string label, long deadline)
    {
        await SafeDismount("humanize-dismount");

        var hops = 0;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) return;
            if (LeftBreakTerritory()) return;

            // Fork: pause before each hop (upstream paused after it), so a long pause means no movement from arrival on.
            var (pauseLo, pauseHi) = PauseRangeMs();
            var pauseMs = pauseLo == pauseHi ? pauseLo : rng.Next(pauseLo, pauseHi + 1);
            Status = $"Idling in {label}";
            if (pauseMs > 0) await IdleFor(pauseMs, deadline);
            if (CancelToken.IsCancellationRequested) return;
            if (Environment.TickCount64 >= deadline) break;

            var player = Svc.Objects.LocalPlayer;
            if (player is null) { await DelayMs(PlayerWaitPollMs); continue; }

            var dest = await PickRandomDestination(player.Position);
            if (CancelToken.IsCancellationRequested) return;
            if (dest is null)
            {
                Status = $"Idling in {label}";
                await IdleFor(1_500, deadline);
                continue;
            }

            hops++;
            var remainingSec = Math.Max(0, (deadline - Environment.TickCount64) / 1000);
            Status = $"Wandering in {label} (~{remainingSec}s left)";
            Diag($"Humanize hop {hops}: walking {Vector3.Distance(player.Position, dest.Value):F0}m to {dest.Value}");

            var perHopBudget = (int)Math.Min(WalkWatchdogMs, deadline - Environment.TickCount64);
            if (perHopBudget < 4_000) break;

            var move = new MoveOp(o => o.Move(plan.TerritoryId, dest.Value,
                MovementConfig.Default.WithTolerance(ArrivalTolerance),
                stopCondition: () => Environment.TickCount64 >= deadline || CancelToken.IsCancellationRequested));
            await RunCancellable(move, perHopBudget, $"humanize-walk-{hops}", StuckDetector.MoveStallAbort($"humanize-walk-{hops}"));
        }

        Diag($"Humanize done in {label} after {hops} hop(s).");
    }

    private async Task IdleFor(int pauseMs, long deadline)
    {
        var cappedPauseMs = (int)Math.Min(pauseMs, Math.Max(0L, deadline - Environment.TickCount64));
        await DelayMs(cappedPauseMs);
    }

    private async Task<Vector3?> PickRandomDestination(Vector3 from)
    {
        var (minR, maxR) = WanderRange();
        for (var attempt = 0; attempt < CandidateAttempts; attempt++)
        {
            if (CancelToken.IsCancellationRequested) return null;
            var angle = rng.NextDouble() * Math.PI * 2;
            var radius = minR + (float)rng.NextDouble() * (maxR - minR);
            var candidate = new Vector3(
                from.X + (float)Math.Cos(angle) * radius,
                from.Y,
                from.Z + (float)Math.Sin(angle) * radius);

            var snapped = NavmeshIPC.Instance.NearestPointReachable(candidate, CandidateHalfExtentXZ, CandidateHalfExtentY);
            if (snapped is null || Vector3.Distance(snapped.Value, from) < minR * 0.5f) continue;

            var route = await QueryWalkRoute(from, snapped.Value);
            var rejection = RejectRoute(route, from, snapped.Value);
            if (rejection is null) return snapped;
            Diag($"Humanize candidate {snapped.Value} rejected: {rejection}");
        }
        return null;
    }

    private async Task<List<Vector3>?> QueryWalkRoute(Vector3 from, Vector3 to)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(CancelToken);
        cancel.CancelAfter(RouteQueryTimeoutMs);
        var pending = NavmeshIPC.Instance.Pathfind(from, to, fly: false, cancel.Token);
        if (pending is null) return null;
        while (!pending.IsCompleted)
        {
            if (cancel.IsCancellationRequested) return null;
            await NextFrame();
        }
        return pending.IsCompletedSuccessfully ? pending.Result : null;
    }

    private static string? RejectRoute(List<Vector3>? route, Vector3 from, Vector3 dest)
    {
        if (route is null || route.Count < 2) return "no route";

        var gap = Vector3.Distance(route[^2], route[^1]);
        if (gap > PartialRouteGapMeters) return $"route stops {gap:F1}m short of the target (partial path)";

        var length = Vector3.Distance(from, route[0]);
        for (var pointIndex = 1; pointIndex < route.Count; pointIndex++)
        {
            length += Vector3.Distance(route[pointIndex - 1], route[pointIndex]);
        }
        var straight = Vector3.Distance(from, dest);
        if (length > straight * MaxDetourRatio) return $"detour of {length:F0}m for {straight:F0}m straight";

        return null;
    }
}
