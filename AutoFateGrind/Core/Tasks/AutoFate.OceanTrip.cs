using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

// Fork: leaves the grind for an ocean fishing voyage (AutoOceanTrip), at the same safe point a soft stop uses: no
// running FATE, a pending Collect reward collected, out of combat. The controller runs the trip and resumes here.
public sealed partial class AutoFate
{
    private DateTime oceanTripBoundary;

    private bool OceanTripReady()
    {
        if (session.StopWhenSafe || AutoOceanTrip.DueBoundary(Plugin.Cfg) is not { } boundary)
        {
            return false;
        }
        oceanTripBoundary = boundary;
        return SoftStopReady();
    }

    private async Task HandOffToOceanTrip()
    {
        Status = "Leaving for ocean fishing";
        AutoOceanTrip.MarkTaken(oceanTripBoundary);
        Diag($"Ocean trip due (voyage at {oceanTripBoundary:HH:mm} UTC); finishing up in {zone.Name} and handing off");
        await HoldForCollectReward();
        await ClearBlockingCombat();
        session.PendingOceanTrip = true;
        session.PendingOceanTripFromZone = zone;
        Svc.Chat.Print($"[AFG] Ocean fishing voyage at {oceanTripBoundary.ToLocalTime():HH:mm}; pausing the FATEs.");
    }
}
