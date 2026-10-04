using AutoFateGrind.Core.Ipc;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System;
using System.Numerics;

namespace AutoFateGrind.Core.Tasks;

internal enum StallKind { None, NavWedge, Idle }

// Watches a single vnav-driven move for lack of progress. It partitions every non-progress case a clib
// MoveTo can land in, so no phase is blind:
//   • NavWedge — vnav is engaged (following, or re-pathing after it already followed once) yet the
//                character is not closing on the waypoint it steers at. Progress is measured against
//                that waypoint rather than as raw displacement: sliding along a wall displaces the
//                character without ever closing, and vnavmesh's own stop-and-retry cycle flickers
//                Path.IsRunning every half second, which a "continuously running" timer read as a fresh
//                start each time.
//   • Idle     — no movement while NOTHING legitimate is happening: not following, not pathfinding,
//                not casting/mounting/zone-transitioning. That is a wedged pre-pathfind phase, almost
//                always a clib teleport that was issued but never started casting.
// Consuming a waypoint or closing on the current one by ProgressEpsilonMeters resets the wedge timer;
// the initial pathfind never accrues. Without the waypoint IPC (older vnavmesh) it falls back to the
// displacement anchor.
internal sealed class MoveStallTracker
{
    private Vector3? anchor;
    private Vector3? target;
    private float bestTargetDistance = float.MaxValue;
    private int lastWaypointCount;
    private bool followedOnce;
    private bool pathInterrupted;
    private bool jumpedOutOfWedge;
    private long navWedgeSinceMs = Environment.TickCount64;
    private long idleSinceMs = Environment.TickCount64;

    public StallKind Check()
    {
        var player = Svc.Objects.LocalPlayer;
        if (player is null) return StallKind.None;

        var now = Environment.TickCount64;
        var pos = player.Position;
        var nav = NavmeshIPC.Instance;
        var navRunning = nav.IsRunning();
        var navBusy = navRunning || nav.IsBusy();
        var legitFrozen = StuckDetector.IsPositionFrozenLegit();

        if (legitFrozen || navBusy) idleSinceMs = now;
        else if (now - idleSinceMs >= StuckDetector.IdleStallTimeoutMs) return StallKind.Idle;

        if (legitFrozen)
        {
            navWedgeSinceMs = now;
            anchor = pos;
            return StallKind.None;
        }

        if (!navRunning)
        {
            if (followedOnce) pathInterrupted = true;
            if (!followedOnce || !navBusy) navWedgeSinceMs = now;
            return StallKind.None;
        }

        followedOnce = true;
        var count = nav.NumWaypoints();
        var progressed = count == NavmeshIPC.WaypointsUnavailable
            ? DisplacedFromAnchor(pos)
            : ClosedOnWaypoint(nav, count, pos);

        if (progressed)
        {
            navWedgeSinceMs = now;
            return StallKind.None;
        }
        if (now - navWedgeSinceMs < StuckDetector.NavWedgeTimeoutMs) return StallKind.None;

        // A lip or a rock edge the capsule snags on is usually cleared by one jump while vnav keeps steering.
        if (!jumpedOutOfWedge && CanJumpOutOfWedge())
        {
            jumpedOutOfWedge = true;
            navWedgeSinceMs = now;
            AutoCommon.UseGeneralAction(AutoCommon.JumpGeneralActionId);
            RunLog.Info($"No progress toward the next waypoint in {StuckDetector.NavWedgeTimeoutMs / 1000}s at {pos}; jumping once before giving up on the move");
            return StallKind.None;
        }
        return StallKind.NavWedge;
    }

    private static bool CanJumpOutOfWedge()
        => !Svc.Condition[ConditionFlag.InCombat]
        && !Svc.Condition[ConditionFlag.InFlight]
        && !Svc.Condition[ConditionFlag.Swimming]
        && !Svc.Condition[ConditionFlag.Diving]
        && !Svc.Condition[ConditionFlag.Jumping]
        && !Svc.Condition[ConditionFlag.Jumping61];

    private bool DisplacedFromAnchor(Vector3 pos)
    {
        if (anchor is not null && Vector3.Distance(anchor.Value, pos) <= StuckDetector.StuckMoveThresholdMeters) return false;
        anchor = pos;
        return true;
    }

    private bool ClosedOnWaypoint(NavmeshIPC nav, int count, Vector3 pos)
    {
        var consumed = target is not null && !pathInterrupted && count < lastWaypointCount;
        var newPath = target is null || pathInterrupted || count > lastWaypointCount;
        if (consumed || newPath)
        {
            pathInterrupted = false;
            lastWaypointCount = count;
            target = nav.CurrentWaypoint();
            bestTargetDistance = target is { } fresh ? Vector3.Distance(pos, fresh) : float.MaxValue;
            if (target is null) return DisplacedFromAnchor(pos);
            return consumed;
        }

        if (target is null) return DisplacedFromAnchor(pos);

        var distance = Vector3.Distance(pos, target.Value);
        if (distance >= bestTargetDistance - StuckDetector.ProgressEpsilonMeters) return false;
        bestTargetDistance = distance;
        return true;
    }
}
