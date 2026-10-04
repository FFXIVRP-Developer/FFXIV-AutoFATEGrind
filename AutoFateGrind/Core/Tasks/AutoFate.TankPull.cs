using clib.Utils;
using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Ipc;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

// Fork (README-FORK item 21): a leader on a tank job pulls the FATE to itself. FATE mobs within 20 m that are not fighting
// anyone yet each get its ranged attack (PLD Shield Lob, WAR Tomahawk, DRK Unmend, GNB Lightning Shot), nearest first,
// at most 8 per pass; BossMod is switched off for the pass so it does not fight over the target, then on again. The
// pass repeats every few seconds while untagged mobs are in reach, so the pack builds up and the slaves AoE it.
public sealed partial class AutoFate
{
    private const float TankPullReachMeters = 20f;
    private const int TankPullMaxPerPass = 8;
    private const int TankPullMaxInCombat = 4; // already holding a pack: BossMod (mitigation, dodges) is off for less time
    private const int TankPullCheckEveryMs = 3_000;
    private const int TankPullAfterPassMs = 6_000;

    // ClassJob row → its ranged attack (the base class has it too).
    private static readonly Dictionary<uint, uint> TankRangedAttack = new()
    {
        [1] = 24, [19] = 24,      // GLA / PLD: Shield Lob
        [3] = 46, [21] = 46,      // MRD / WAR: Tomahawk
        [32] = 3624,              // DRK: Unmend
        [37] = 16143,             // GNB: Lightning Shot
    };

    private long nextTankPullMs;

    private async Task TickTankPull(uint fateId, string preset)
    {
        var now = Environment.TickCount64;
        if (now < nextTankPullMs) return;
        nextTankPullMs = now + TankPullCheckEveryMs;
        if (!Plugin.Cfg.TankLeaderPull || Multibox.MultiboxFollowerWatch.IsFollower) return;
        if (Svc.Objects.LocalPlayer is not { } me || !TankRangedAttack.TryGetValue(me.ClassJob.RowId, out var action)) return;

        var mobs = FateMobScanner.UnpulledMobs(fateId, me.Position, TankPullReachMeters);
        if (mobs.Count == 0) return;
        var max = Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat] ? TankPullMaxInCombat : TankPullMaxPerPass;
        Diag($"Tank pull: {mobs.Count} FATE mob(s) within {TankPullReachMeters:F0} m not fighting yet; tagging up to {max}");

        BossModIPC.Instance.ClearActive();
        var tagged = 0;
        try
        {
            foreach (var mob in mobs.Take(max))
            {
                if (CancelToken.IsCancellationRequested || PublicEvent.GetFateById(fateId) is not { State: FFXIVClientStructs.FFXIV.Client.Game.Fate.FateState.Running }) break;
                if (mob.IsDead || (mob.StatusFlags & Dalamud.Game.ClientState.Objects.Enums.StatusFlags.InCombat) != 0) continue;
                Svc.Targets.Target = mob;
                // The ranged attack is a GCD: wait until it can go on this mob (up to 3 s; out of range or sight: skip it).
                if (!await WaitUntilTimed(() => ActionReady(action, mob.GameObjectId), 3_000, "tank pull: action ready", 5)) continue;
                if (!UseActionOn(action, mob.GameObjectId)) continue;
                tagged++;
                await WaitUntilTimed(() => mob.IsDead || (mob.StatusFlags & Dalamud.Game.ClientState.Objects.Enums.StatusFlags.InCombat) != 0, 1_500, "tank pull: tagged", 5);
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
