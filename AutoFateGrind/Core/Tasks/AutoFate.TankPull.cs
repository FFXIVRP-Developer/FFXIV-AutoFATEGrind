using System.Numerics;
using clib.TaskSystem;
using AutoFateGrind.Core.Game.Ops;
using clib.Utils;
using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Ipc;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

// Fork (README-FORK item 21): a leader on a tank job pulls the FATE to itself. FATE mobs within 20 m that are not fighting
// anyone yet, then those attacking a non-tank (Provoke when it is up), each get its ranged attack (PLD Shield Lob, WAR Tomahawk, DRK Unmend, GNB Lightning Shot), nearest first,
// at most 8 per pass; BossMod is switched off for the pass so it does not fight over the target, then on again. The
// pass repeats every few seconds while untagged mobs are in reach, so the pack builds up and the slaves AoE it.
public sealed partial class AutoFate
{
    // Pulling is the tank's top priority (2026-10-04: every 3 s + 6 s rest left mobs on the slaves; 20 m left farther FATE
    // mobs alone; Shield Lob without the tank stance did not hold them).
    private const float TankPullReachMeters = 20f;
    private const float TankPullApproachMeters = 120f; // farther FATE mobs: walk until they are in reach
    private const int TankPullMaxPerPass = 8;
    private const int TankPullCheckEveryMs = 1_000;
    private const int TankPullAfterPassMs = 1_000;

    // ClassJob row → (tank stance action, its status): without it a ranged attack hardly holds a mob.
    private static readonly Dictionary<uint, (uint Action, uint Status)> TankStance = new()
    {
        [1] = (28, 79), [19] = (28, 79),         // GLA / PLD: Iron Will
        [3] = (48, 91), [21] = (48, 91),         // MRD / WAR: Defiance
        [32] = (3629, 743),                      // DRK: Grit
        [37] = (16142, 1833),                    // GNB: Royal Guard
    };

    // ClassJob row → its ranged attack (the base class has it too).
    private static readonly Dictionary<uint, uint> TankRangedAttack = new()
    {
        [1] = 24, [19] = 24,      // GLA / PLD: Shield Lob
        [3] = 46, [21] = 46,      // MRD / WAR: Tomahawk
        [32] = 3624,              // DRK: Unmend
        [37] = 16143,             // GNB: Lightning Shot
    };

    private const uint ProvokeAction = 7533; // role action, every tank
    private long nextTankPullMs;

    private async Task TickTankPull(uint fateId, string preset)
    {
        var now = Environment.TickCount64;
        if (now < nextTankPullMs) return;
        nextTankPullMs = now + TankPullCheckEveryMs;
        if (!Plugin.Cfg.TankLeaderPull || Multibox.MultiboxFollowerWatch.IsFollower) return;
        if (Svc.Objects.LocalPlayer is not { } me || !TankRangedAttack.TryGetValue(me.ClassJob.RowId, out var action)) return;

        // The tank stance first: a ranged attack without it hardly draws a mob off a DPS.
        if (TankStance.TryGetValue(me.ClassJob.RowId, out var stance) && !me.StatusList.Any(s => s.StatusId == stance.Status)
            && ActionReady(stance.Action, me.GameObjectId) && UseActionOn(stance.Action, me.GameObjectId))
            Diag("Tank pull: the tank stance was off; turned it on");

        var mobs = FateMobScanner.PullCandidates(fateId, me.Position, TankPullReachMeters);
        if (mobs.Count == 0)
        {
            // Nothing in reach: walk toward the nearest FATE mob not on the tank (the pack follows), then pull it.
            var far = FateMobScanner.PullCandidates(fateId, me.Position, TankPullApproachMeters, requireSight: false);
            if (far.Count == 0) return;
            var (target, _) = far[0];
            var targetId = target.GameObjectId;
            Diag($"Tank pull: nearest FATE mob not on me is {Vector3.Distance(me.Position, target.Position):F0} m away; walking into reach");
            // In reach but out of sight (behind a wall or a slope): close in until it can be seen, not just into reach.
            var blocked = Vector3.Distance(me.Position, target.Position) - target.HitboxRadius <= TankPullReachMeters;
            await WalkWithBossModParked(target.Position, MovementConfig.Default.WithTolerance(target.HitboxRadius + (blocked ? 5f : TankPullReachMeters - 3f)),
                () => target.IsDead || target.TargetObjectId == me.GameObjectId || PublicEvent.GetFateById(fateId) is not { State: FFXIVClientStructs.FFXIV.Client.Game.Fate.FateState.Running }
                   || FateMobScanner.PullCandidates(fateId, Svc.Objects.LocalPlayer?.Position ?? me.Position, TankPullReachMeters).Count > 0,
                $"tank-pull-approach-{targetId}");
            nextTankPullMs = 0; // pull right away
            return;
        }
        var max = TankPullMaxPerPass;
        var wrongCount = mobs.Count(m => m.WrongPull);
        Diag($"Tank pull: {mobs.Count - wrongCount} untagged and {wrongCount} on a non-tank within {TankPullReachMeters:F0} m; taking up to {max}");

        BossModIPC.Instance.ClearActive();
        var tagged = 0;
        var myId = me.GameObjectId;
        try
        {
            foreach (var (mob, wrongPull) in mobs.Take(max))
            {
                if (CancelToken.IsCancellationRequested || PublicEvent.GetFateById(fateId) is not { State: FFXIVClientStructs.FFXIV.Client.Game.Fate.FateState.Running }) break;
                if (mob.IsDead || mob.TargetObjectId == myId) continue;
                if (!wrongPull && (mob.StatusFlags & Dalamud.Game.ClientState.Objects.Enums.StatusFlags.InCombat) != 0) continue; // someone tagged it meanwhile
                Svc.Targets.Target = mob;
                // A mob on a non-tank: Provoke when it is up (it forces the mob onto the tank), else the ranged attack.
                var use = wrongPull && ActionReady(ProvokeAction, mob.GameObjectId) ? ProvokeAction : action;
                // The ranged attack is a GCD: wait until it can go on this mob (up to 3 s; out of range or sight: skip it).
                if (!await WaitUntilTimed(() => ActionReady(use, mob.GameObjectId), 3_000, "tank pull: action ready", 5)) continue;
                if (!UseActionOn(use, mob.GameObjectId)) continue;
                tagged++;
                await WaitUntilTimed(() => mob.IsDead || mob.TargetObjectId == myId, 800, "tank pull: on the tank", 5);
            }
        }
        finally
        {
            AssertPresetActive(preset);
        }
        Diag($"Tank pull: tagged {tagged} mob(s)");
        nextTankPullMs = Environment.TickCount64 + TankPullAfterPassMs;
    }

    private static unsafe bool ActionReady(uint action, ulong target)
    {
        var am = ActionManager.Instance();
        return am is not null && am->GetActionStatus(ActionType.Action, action, target) == 0;
    }

    private static unsafe bool UseActionOn(uint action, ulong target)
    {
        var am = ActionManager.Instance();
        return am is not null && am->UseAction(ActionType.Action, action, target);
    }
}
