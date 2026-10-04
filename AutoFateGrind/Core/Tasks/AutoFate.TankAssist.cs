using System.Numerics;
using AutoFateGrind.Core.Ipc;
using AutoFateGrind.Core.Multibox;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Tasks;

// Fork (README-FORK item 21): a tank leader pulls, the slaves fight the pack. With the leader on a tank job (published in
// its leader file) and Settings → Multibox → "Tank leader" on, a slave in a FATE:
//   - does not pull: BossMod AutoTarget General = Passive (it picks no target by itself)
//   - assists: takes the leader's current target (also published); with none, whatever is attacking it
//   - stays near the tank: BossMod StayCloseToPartyRole, Role = Tank, at its fighting distance (melee 3 m)
//   - walks to the tank, not to the nearest mob, when nothing is in reach (RepositionToFateMob)
// Its own rotation then fights, AoE included, against the pack the tank holds. Leader not a tank, or off: as before.
public sealed partial class AutoFate
{
    private const string StayCloseToPartyRoleModule = "BossMod.Autorotation.MiscAI.StayCloseToPartyRole";
    private const int TankAssistReapplyMs = 10_000;
    private const int AssistEveryMs = 500;

    private bool? tankAssistApplied;
    private long tankAssistReapplyAtMs;
    private long nextAssistMs;

    /// <summary>The leader is a tank and this slave assists it (setting on, leader fresh, same zone and instance).</summary>
    private static LeaderState? TankLeader()
    {
        if (!Plugin.Cfg.TankLeaderAssist || !MultiboxFollowerWatch.IsFollower) return null;
        if (MultiboxLink.Leader() is not { Tank: true } leader) return null;
        if (leader.Territory != Svc.ClientState.TerritoryType || leader.Instance != MultiboxLink.CurrentInstance) return null;
        return leader;
    }

    /// <summary>The tank's position for a slave's idle walk; null when not assisting a tank.</summary>
    private static Vector3? TankLeaderPosition() => TankLeader() is { } leader ? new Vector3(leader.X, leader.Y, leader.Z) : null;

    /// <summary>The fight loop, every tick: BossMod settings when the mode changes (and every 10 s), and the assist.</summary>
    private void TickTankAssist(string preset)
    {
        var leader = TankLeader();
        var on = leader is not null;
        var now = Environment.TickCount64;
        if (tankAssistApplied != on || (on && now >= tankAssistReapplyAtMs))
        {
            ApplyTankAssistStrategies(preset, on);
            tankAssistReapplyAtMs = now + TankAssistReapplyMs;
        }
        if (!on || now < nextAssistMs) return;
        nextAssistMs = now + AssistEveryMs;

        var me = Svc.Objects.LocalPlayer;
        if (me is null) return;
        IBattleNpc? want = null;
        if (leader!.TargetId != 0)
            want = Svc.Objects.FirstOrDefault(o => o.GameObjectId == leader.TargetId) as IBattleNpc;
        if (want is null or { IsDead: true })
        {
            // The tank has no target (or it is not loaded here): keep a live one, else take whatever is attacking this slave.
            if (Svc.Targets.Target is IBattleNpc { IsDead: false }) return;
            want = Svc.Objects.OfType<IBattleNpc>().FirstOrDefault(n => !n.IsDead && n.TargetObjectId == me.GameObjectId);
        }
        if (want is not null && Svc.Targets.Target?.GameObjectId != want.GameObjectId)
            Svc.Targets.Target = want;
    }

    private void ApplyTankAssistStrategies(string preset, bool on)
    {
        var first = tankAssistApplied != on;
        tankAssistApplied = on;
        var general = BossModIPC.Instance.AddTransientStrategy(preset, AutoTargetModule, "General", on ? "Passive" : "Aggressive");
        bool role;
        if (on)
        {
            var myRole = Svc.Objects.LocalPlayer?.ClassJob.Value.Role ?? 0;
            // Melee DPS stand at their positional on the tank's target (item 22), not next to the tank.
            if (myRole == 2 && Plugin.Cfg.SpreadMeleePositional)
                role = !BossModIPC.Instance.CanClearTransientStrategy || BossModIPC.Instance.ClearTransientStrategy(preset, StayCloseToPartyRoleModule, "Role");
            else
            {
                var range = myRole is 1 or 2 ? "3" : Fork.Spread.RangedDistance().ToString(System.Globalization.CultureInfo.InvariantCulture);
                role = BossModIPC.Instance.AddTransientStrategy(preset, StayCloseToPartyRoleModule, "Role", "Tank")
                     & BossModIPC.Instance.AddTransientStrategy(preset, StayCloseToPartyRoleModule, "range", range);
            }
        }
        else
        {
            role = !BossModIPC.Instance.CanClearTransientStrategy
                || BossModIPC.Instance.ClearTransientStrategy(preset, StayCloseToPartyRoleModule, "Role");
        }
        if (first)
            Diag(on
                ? $"Tank leader: assisting the tank (no pulling {(general ? "accepted" : "REFUSED")}, stay near the tank {(role ? "accepted" : "REFUSED")})"
                : $"Tank leader: off (pulling again {(general ? "accepted" : "REFUSED")})");
    }
}
