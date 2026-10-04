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
        var current = PublicEvent.CurrentFate;
        MultiboxLink.CurrentFateId = current is { State: FateState.Running } ? current.Id : lastPickedFateId;

        if (Plugin.Cfg.MultiboxRole == MultiboxRole.Leader)
        {
            MultiboxLink.Publish(Svc.ClientState.TerritoryType, MultiboxLink.CurrentFateId);
            return;
        }

        // Follower: why it is (not) following, for the Multibox tab.
        if (MultiboxLink.LeaderAnyWorld() is not { } leader)
        {
            MultiboxLink.FollowerStatus = "blocked: leader not seen (not running?)";
            return;
        }
        var myWorld = Svc.Objects.LocalPlayer?.CurrentWorld.RowId ?? 0;
        if (leader.World != myWorld)
        {
            MultiboxLink.FollowerStatus = $"blocked: leader {leader.Name} is on another world";
            return;
        }

        if (leader.Territory != zone.TerritoryId)
        {
            // The leader's zone, when it is one of this run's zones (same plan, the config comes from MAIN).
            var index = -1;
            for (var i = 0; i < zones.Count; i++)
            {
                if (zones[i].TerritoryId == leader.Territory) { index = i; break; }
            }
            if (index < 0)
            {
                MultiboxLink.FollowerStatus = $"blocked: leader {leader.Name} is in a zone not in my plan";
                return;
            }
            if (lastFollowedZone != leader.Territory)
            {
                Diag($"Multibox: the leader ({leader.Name}) is in {zones[index].Name}; following");
                lastFollowedZone = leader.Territory;
            }
            zoneIndex = index;
            MultiboxLink.FollowerStatus = $"following {leader.Name} to {zones[index].Name}";
            return;
        }

        var myInstance = MultiboxLink.CurrentInstance;
        MultiboxLink.FollowerStatus = leader.Instance != myInstance && Svc.ClientState.TerritoryType == leader.Territory
            ? $"blocked: leader {leader.Name} is in instance {leader.Instance}, I am in {myInstance}"
            : $"following {leader.Name}";
    }

    /// <summary>A follower's pick: the leader's FATE when it exists here and is still going; null to pick as usual.</summary>
    private PublicEvent? LeaderFate()
    {
        if (Plugin.Cfg.MultiboxRole != MultiboxRole.Follower) return null;
        if (MultiboxLink.Leader() is not { FateId: not 0 } leader || leader.Territory != Svc.ClientState.TerritoryType) return null;
        if (leader.Instance != MultiboxLink.CurrentInstance) return null; // another instance: different FATEs
        if (PublicEvent.GetFateById(leader.FateId) is not { Progress: < 100 } fate) return null;
        if (fate.State is FateState.Ended or FateState.Failed) return null;
        return fate;
    }
}
