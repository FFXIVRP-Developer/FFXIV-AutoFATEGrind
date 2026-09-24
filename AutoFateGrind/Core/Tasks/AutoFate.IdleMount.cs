using AutoFateGrind.Core.Ipc;
using AutoFateGrind.Core.Zones;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Tasks;

public sealed partial class AutoFate
{
    private const uint MountRouletteActionId = 9;
    private const int MountRetryMs = 5_000;
    private long idleMountFirstAttemptAtMs;
    private long nextIdleMountRetryAtMs;
    private bool idleMountRefusalLogged;

    private bool CanIdleMount()
        => !CancelToken.IsCancellationRequested
        && Svc.Objects.LocalPlayer is { IsDead: false, IsCasting: false }
        && !Svc.Condition[ConditionFlag.InCombat]
        && !Svc.Condition[ConditionFlag.Mounting]
        && !Svc.Condition[ConditionFlag.Mounting71]
        && !Svc.Condition[ConditionFlag.BetweenAreas]
        && !Svc.Condition[ConditionFlag.BetweenAreas51]
        && !NavmeshIPC.Instance.IsBusy();

    private void BeginIdleMountPeriod()
    {
        var reactionDelayMs = Pacing.ReactionDelayMs();
        idleMountFirstAttemptAtMs = Environment.TickCount64 + reactionDelayMs;
        nextIdleMountRetryAtMs = 0;
        idleMountRefusalLogged = false;
        if (reactionDelayMs > 0)
            Diag($"Pacing: idle mount; waiting {reactionDelayMs / 1000.0:F1}s before the first request");
    }

    private void EndIdleMountPeriod()
    {
        idleMountFirstAttemptAtMs = 0;
        nextIdleMountRetryAtMs = 0;
        idleMountRefusalLogged = false;
    }

    private void TryIdleMount()
    {
        if (!Plugin.Cfg.MountWhileWaitingForFates || ZoneSelection.IsYokaiGoal(Plugin.Cfg))
        {
            EndIdleMountPeriod();
            return;
        }

        if (idleMountFirstAttemptAtMs == 0)
            BeginIdleMountPeriod();

        var now = Environment.TickCount64;
        if (now < idleMountFirstAttemptAtMs || now < nextIdleMountRetryAtMs
         || !CanIdleMount() || Svc.Condition[ConditionFlag.Mounted] || Svc.Condition[ConditionFlag.RidingPillion])
            return;

        nextIdleMountRetryAtMs = now + MountRetryMs;
        if (UseGeneralAction(MountRouletteActionId, out var actionStatus))
        {
            Diag("Idle mount requested while waiting for FATEs");
        }
        else if (!idleMountRefusalLogged)
        {
            var reason = actionStatus is null ? "ActionManager unavailable"
                : actionStatus == 0 ? "UseAction returned false" : $"action status {actionStatus}";
            Diag($"Idle mount refused while waiting for FATEs ({reason})");
            idleMountRefusalLogged = true;
        }
    }
}
