using clib.TaskSystem;
using AutoFateGrind.Core.Game.Ops;
using clib.Utils;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

// Fork (README-FORK item 20): keeping characters apart after they arrived (Spread.cs has the values).
//   idle nudge  out of combat (between FATEs, waiting for mobs): the crowd nudge again, damped so a crowd does not
//               shuffle in sync: the crowd must last 8 s, each check (every 4-7 s) moves only on a coin flip (one of a
//               close pair usually moves, not both), and after a move the next one is 15-30 s away
//               (2026-10-04: every 10 s, all five nudged in the same second)
//   fight step  in combat, a ranged job or healer with another player within 3 m steps to its own side of its target,
//               at its own distance; only when no enemy within 40 m is casting, it is not casting itself, and it has
//               stood still for 1.5 s (BossMod is not moving it: no dodge going on). At most every 12 s, 10 m at most.
public sealed partial class AutoFate
{
    private const int FightStepEveryMs = 12_000;
    private const int FightCheckEveryMs = 2_000;
    private const float FightStepCrowdMeters = 3f;
    private const float FightStepMaxMeters = 10f;
    private const int StillForMs = 1_500;

    private long nextIdleNudgeMs;
    private long crowdSeenSinceMs;
    private static readonly Random spreadRng = new();

    private bool IdleNudgeDue(Vector3 me)
    {
        var now = Environment.TickCount64;
        if (now < nextIdleNudgeMs) return false;
        nextIdleNudgeMs = now + spreadRng.Next(4_000, 7_000);
        if (!Fork.Spread.SomeoneWithin(me, 6f)) { crowdSeenSinceMs = 0; return false; }
        if (crowdSeenSinceMs == 0) { crowdSeenSinceMs = now; return false; }
        if (now - crowdSeenSinceMs < 8_000 || spreadRng.Next(2) == 0) return false;
        crowdSeenSinceMs = 0;
        nextIdleNudgeMs = now + spreadRng.Next(15_000, 30_000);
        return true;
    }
    private long nextFightStepMs;
    private Vector3 lastSpreadPos;
    private long stillSinceMs;

    /// <summary>The main loop, out of a FATE: a waiting character in a crowd steps away (every 10 s).</summary>
    private async Task TickIdleNudge(GrindState state)
    {
        if (state is not (GrindState.BetweenFates or GrindState.WaitingForFates)) return;
        if (!Plugin.Cfg.SpreadCrowdNudge || Svc.Condition[ConditionFlag.InCombat] || Svc.Condition[ConditionFlag.BetweenAreas]) return;
        if (Svc.Objects.LocalPlayer is not { } me || !IdleNudgeDue(me.Position)) return;
        if (Fork.Spread.CrowdNudge(me.Position) is not { } spot) return;
        Diag($"Spread: waiting with someone within 6 m; moving {Vector3.Distance(me.Position, spot):F0} m to a spot with more room");
        await WalkWithBossModParked(spot, MovementConfig.Default.WithTolerance(1.5f),
            () => Svc.Condition[ConditionFlag.InCombat] || CancelToken.IsCancellationRequested, "spread-idle-nudge");
    }

    /// <summary>The fight loop, every tick: the out-of-combat nudge while waiting for mobs, the fight step in combat.</summary>
    private async Task TickFightSpread(uint fateId, PublicEvent fate)
    {
        if (Svc.Objects.LocalPlayer is not { } me) return;
        var now = Environment.TickCount64;
        if (Vector3.Distance(me.Position, lastSpreadPos) > 0.3f) { lastSpreadPos = me.Position; stillSinceMs = now; }

        if (!Svc.Condition[ConditionFlag.InCombat])
        {
            if (Plugin.Cfg.SpreadCrowdNudge && IdleNudgeDue(me.Position)) await NudgeFromCrowd(fateId);
            return;
        }

        if (!Plugin.Cfg.SpreadInCombat || now < nextFightStepMs) return;
        nextFightStepMs = now + FightCheckEveryMs;
        var role = me.ClassJob.Value.Role;
        if (role is 1 or 2 || me.IsCasting) return;                                  // melee and tanks stay on their target
        if (now - stillSinceMs < StillForMs) return;                                   // BossMod is moving it (a dodge)
        if (Svc.Targets.Target is not IBattleChara target || target.IsDead) return;
        if (!Fork.Spread.SomeoneWithin(me.Position, FightStepCrowdMeters)) return;
        if (spreadRng.Next(2) == 0) return;                                           // one of a close pair, not both at once
        if (EnemyCastingNear(me.Position, 40f)) return;                                // something may be coming: no step

        var distance = MathF.Min(Fork.Spread.RangedDistance() * 0.8f, Vector3.Distance(me.Position, target.Position));
        var dest = Fork.Spread.AroundPoint(target.Position, MathF.Max(distance, target.HitboxRadius + 3f));
        var step = Vector3.Distance(me.Position, dest);
        if (step < 2f || step > FightStepMaxMeters) return;
        if (Game.Fates.FateGround.HorizontalDistance(dest, fate.Position) > fate.Radius * 0.9f) return;

        nextFightStepMs = now + FightStepEveryMs;
        Diag($"Spread: someone within {FightStepCrowdMeters:F0} m in the fight; stepping {step:F0} m to my side of {target.Name}");
        var targetId = target.GameObjectId;
        await WalkWithBossModParked(dest, MovementConfig.Default.WithTolerance(1.5f),
            () => !Svc.Condition[ConditionFlag.InCombat] || Svc.Targets.Target?.GameObjectId != targetId
               || EnemyCastingNear(Svc.Objects.LocalPlayer?.Position ?? dest, 40f) || PublicEvent.GetFateById(fateId) is not { State: FateState.Running },
            $"spread-fight-step-{fateId}");
    }

    private static bool EnemyCastingNear(Vector3 at, float metres)
    {
        foreach (var obj in Svc.Objects)
        {
            if (obj is IBattleNpc { BattleNpcKind: BattleNpcSubKind.Combatant, IsCasting: true } npc && Vector3.Distance(npc.Position, at) <= metres)
                return true;
        }
        return false;
    }
}
