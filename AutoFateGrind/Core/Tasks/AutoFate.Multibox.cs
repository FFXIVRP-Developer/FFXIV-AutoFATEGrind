using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Multibox;
using clib.Utils;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Tasks;

// Fork (README-FORK item 16): the run's side of multibox follow (see MultiboxLink).
public sealed partial class AutoFate
{
    private uint lastPickedFateId;
    private uint lastFollowedZone;

    /// <summary>Every state computation: the leader publishes; a follower moves its zone to the leader's.</summary>
    private void MultiboxTick()
    {
        if (Plugin.Cfg.MultiboxRole == MultiboxRole.Leader)
        {
            var current = PublicEvent.CurrentFate;
            var fateId = current is { State: FateState.Running } ? current.Id : lastPickedFateId;
            MultiboxLink.Publish(Svc.ClientState.TerritoryType, fateId);
            return;
        }

        // Follower: the leader's zone, when it is one of this run's zones (same plan, the config comes from MAIN).
        if (MultiboxLink.Leader() is not { } leader || leader.Territory == zone.TerritoryId) return;
        for (var index = 0; index < zones.Count; index++)
        {
            if (zones[index].TerritoryId != leader.Territory) continue;
            if (lastFollowedZone != leader.Territory)
            {
                Diag($"Multibox: the leader ({leader.Name}) is in {zones[index].Name}; following");
                lastFollowedZone = leader.Territory;
            }
            zoneIndex = index;
            return;
        }
    }

    /// <summary>A follower's pick: the leader's FATE when it exists here and is still going; null to pick as usual.</summary>
    private PublicEvent? LeaderFate()
    {
        if (Plugin.Cfg.MultiboxRole != MultiboxRole.Follower) return null;
        if (MultiboxLink.Leader() is not { FateId: not 0 } leader || leader.Territory != Svc.ClientState.TerritoryType) return null;
        if (PublicEvent.GetFateById(leader.FateId) is not { Progress: < 100 } fate) return null; // another instance, or done
        if (fate.State is FateState.Ended or FateState.Failed) return null;
        return fate;
    }
}
