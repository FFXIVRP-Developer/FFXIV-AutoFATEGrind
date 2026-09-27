using AutoFateGrind.Core.Game.SharedFates;
using AutoFateGrind.Core.Zones;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed partial class AutoFate
{
    private const int SharedFateSyncWaitMs = 8_000;
    private const int SharedFateSyncPollFrames = 10;

    private static bool SharedFateGoal => ZoneSelection.GoalNeedsRankedZones(Plugin.Cfg);

    private async Task SyncSharedFateRanks()
    {
        if (!SharedFateGoal)
        {
            return;
        }

        Status = "Reading Shared FATE ranks";
        SharedFateProgress.Request(zones, force: true);
        var known = await WaitUntilTimed(() => SharedFateProgress.AllKnown(zones), SharedFateSyncWaitMs, "shared-fate-sync", SharedFateSyncPollFrames);
        Diag($"Shared FATE ranks: {SharedFateProgress.Describe(zones)}");
        if (known)
        {
            return;
        }

        Warn("The game did not report every Shared FATE rank; zones without one are grinded until it does");
        Svc.Chat.PrintError("[AFG] The game did not report every Shared FATE rank; zones without one are grinded until it does.");
    }

    private void TickSharedFateRefresh()
    {
        if (SharedFateGoal)
        {
            SharedFateProgress.Tick();
        }
    }

    private void NoteSharedFateCompletion()
    {
        if (!SharedFateGoal)
        {
            return;
        }

        SharedFateProgress.NoteCompletion(zone.TerritoryId);
        Diag($"Shared FATE rank after this FATE: {SharedFateProgress.Describe(zone)}");
    }

    private string SharedFateHeartbeat() => SharedFateGoal ? $" rank={SharedFateProgress.Describe(zone)}" : string.Empty;
}
