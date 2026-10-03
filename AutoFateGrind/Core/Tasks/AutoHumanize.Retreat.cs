using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

// Fork: where a humanizer break is taken. City is upstream's behaviour; the rest are reached with Lifestream.
public enum HumanizerRetreat { City, Inn, Apartment, PrivateHouse, FreeCompanyHouse }

// Fork: takes the break at an inn room or housing instead of a city. Lifestream does the travel (its /li
// shortcuts); the wander loop then runs unchanged in whatever territory Lifestream ended in, so a long
// "pause between hops" keeps the character standing still for the whole break.
public sealed partial class AutoHumanize
{
    public static readonly string[] RetreatLabels = ["City (wander)", "Inn room", "Apartment", "Private house", "Free Company house"];

    private const int RetreatWatchdogMs   = 180_000;
    private const int RetreatStartGraceMs = 1_500;
    private const int RetreatPollMs       = 500;

    private static string? RetreatCommand(HumanizerRetreat retreat) => retreat switch
    {
        HumanizerRetreat.Inn              => "inn",
        HumanizerRetreat.Apartment        => "apartment",
        HumanizerRetreat.PrivateHouse     => "home",
        HumanizerRetreat.FreeCompanyHouse => "fc",
        _                                 => null,
    };

    // Territory the break runs in, or null when no retreat is configured or Lifestream did not get anywhere
    // (no such property, Lifestream missing, timeout); the caller then falls back to the city break.
    private async Task<uint?> ReachRetreat()
    {
        var retreat = Plugin.Cfg.HumanizerRetreat;
        if (RetreatCommand(retreat) is not { } command) return null;

        var execute = Svc.PluginInterface.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand");
        var isBusy  = Svc.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        if (!execute.HasAction || !isBusy.HasFunction)
        {
            Diag($"Humanize retreat {retreat}: Lifestream IPC unavailable; falling back to a city break.");
            return null;
        }

        var start = Svc.ClientState.TerritoryType;
        Status = $"Travelling to {RetreatLabels[(int)retreat]}";
        Diag($"Humanize retreat {retreat}: /li {command} from territory {start}.");
        execute.InvokeAction(command);
        await DelayMs(RetreatStartGraceMs);

        var deadline = Environment.TickCount64 + RetreatWatchdogMs;
        while (isBusy.InvokeFunc() || Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Objects.LocalPlayer is null)
        {
            if (CancelToken.IsCancellationRequested || Environment.TickCount64 >= deadline)
            {
                Svc.PluginInterface.GetIpcSubscriber<object>("Lifestream.Abort").InvokeAction();
                Diag($"Humanize retreat {retreat}: {(CancelToken.IsCancellationRequested ? "cancelled" : "timed out")}; Lifestream aborted.");
                return null;
            }
            await DelayMs(RetreatPollMs);
        }

        var here = Svc.ClientState.TerritoryType;
        if (here == start)
        {
            Diag($"Humanize retreat {retreat}: Lifestream finished without changing territory; falling back to a city break.");
            return null;
        }
        Diag($"Humanize retreat {retreat}: arrived in territory {here}.");
        return here;
    }
}
