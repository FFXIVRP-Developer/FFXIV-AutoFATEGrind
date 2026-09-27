using AutoFateGrind.Core.Game.Fates;
using clib.Utils;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed partial class AutoFate
{
    // Skips that came from a bad route rather than a bad FATE; another completion means the character moved, so they get a second chance.
    private readonly HashSet<uint> retryAfterCompletionIds = new();

    private bool SoftStopReady() => PublicEvent.CurrentFate is not { State: FateState.Running, Progress: < 100 };

    private async Task FinishSoftStop()
    {
        Status = "Stopping after this FATE";
        await HoldForCollectReward();
        await ClearBlockingCombat();
        Diag("Soft stop: the current FATE is over and the character is out of combat; ending the run");
        Svc.Chat.Print("[AFG] Stopped after the FATE, as requested.");
        Status = "Stopped";
    }

    // Twist of Fate ends on leaving the zone, so while it is up the run stays put instead of rotating or teleporting.
    private static bool KeepingTwistOfFate() => Plugin.Cfg.KeepTwistOfFate && FateScanner.PlayerHasTwistOfFate();

    private void DeferSkipUntilNextCompletion(uint fateId) => retryAfterCompletionIds.Add(fateId);

    private void ReleaseDeferredSkips()
    {
        if (retryAfterCompletionIds.Count == 0)
        {
            return;
        }

        sessionStuckFateIds.ExceptWith(retryAfterCompletionIds);
        Diag($"Released {retryAfterCompletionIds.Count} FATE(s) skipped for routing trouble; they are eligible again after this completion");
        retryAfterCompletionIds.Clear();
    }
}
