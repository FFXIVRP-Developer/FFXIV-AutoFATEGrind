using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Multibox;
using clib.Utils;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

// Fork (README-FORK item 16): the run's side of multibox follow (see MultiboxLink).
public sealed partial class AutoFate
{
    private uint lastPickedFateId;
    private uint lastLoggedPickId;
    private long lastLoggedPickMs;
    private uint lastFollowedZone;

    /// <summary>Every state computation: the leader publishes; a follower moves its zone to the leader's.</summary>
    private void MultiboxTick()
    {
        // The FATE this client picked (where it is going or fighting); the one it stands in only when it picked none.
        // Overlapping FATEs made the published id flicker between the one picked and the one walked through, and a slave
        // restarted its move on every flip (2026-10-04: FATEs 811/812, eight restarts in a second).
        var current = PublicEvent.CurrentFate;
        MultiboxLink.CurrentFateId = lastPickedFateId != 0 && PublicEvent.GetFateById(lastPickedFateId) is { State: not (FateState.Ended or FateState.Failed) }
            ? lastPickedFateId
            : current is { State: FateState.Running } ? current.Id : 0;

        if (!MultiboxFollowerWatch.IsFollower)
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

        if (MultiboxFollowerWatch.CantFollowReason(leader) is { } cant)
        {
            MultiboxLink.FollowerStatus = $"blocked: {cant}"; // MultiboxFollowerWatch parks it
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
        if (leader.Instance != myInstance && leader.Instance != 0 && myInstance != 0 && Svc.ClientState.TerritoryType == leader.Territory)
        {
            MultiboxLink.FollowerStatus = Environment.TickCount64 < nextInstanceHopMs
                ? $"blocked: leader {leader.Name} is in instance {leader.Instance}, I am in {myInstance} (next try in {(nextInstanceHopMs - Environment.TickCount64) / 1000}s)"
                : $"changing to instance {leader.Instance} (leader {leader.Name})";
            instanceHopTarget = leader.Instance;
            return;
        }
        instanceHopTarget = 0;
        MultiboxLink.FollowerStatus = $"following {leader.Name}";
    }

    // ---- Following the leader into its instance --------------------------------------------------------------
    // A follower in another instance of the leader's zone sees other FATEs: it finishes its own FATE, teleports to the
    // zone's aetheryte and has Lifestream change the instance there (Lifestream.ChangeInstance needs an aetheryte in
    // reach). At most one try every 90 s: a full instance or a refused change does not turn into a loop.
    private const int InstanceHopRetryMs = 90_000;
    private uint instanceHopTarget;
    private long nextInstanceHopMs;

    /// <summary>ComputeState, when not in a FATE: hop to the leader's instance now?</summary>
    private bool WantsInstanceHop()
        => MultiboxFollowerWatch.IsFollower && instanceHopTarget != 0 && Environment.TickCount64 >= nextInstanceHopMs
        && !Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat];

    private async Task HopToLeaderInstance()
    {
        var target = instanceHopTarget;
        nextInstanceHopMs = Environment.TickCount64 + InstanceHopRetryMs;
        if (!LifestreamFunc<bool>("Lifestream.CanChangeInstance", out _, quiet: true) && !LifestreamFunc<int>("Lifestream.GetCurrentInstance", out _, quiet: true))
        {
            Diag("Multibox: Lifestream is not loaded; cannot change the instance");
            return;
        }

        Status = $"Following the leader into instance {target}";
        if (!(LifestreamFunc<bool>("Lifestream.CanChangeInstance", out var canNow) && canNow))
        {
            // Not at an aetheryte: to the zone's own (a teleport inside the zone lands next to it).
            var aetheryte = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRowOrDefault(Svc.ClientState.TerritoryType)?.Aetheryte.RowId ?? 0;
            if (aetheryte == 0)
            {
                Diag("Multibox: this zone has no aetheryte to change the instance at");
                return;
            }
            Diag($"Multibox: teleporting to aetheryte {aetheryte} to change to instance {target}");
            if (!TeleportToAetheryte(aetheryte))
            {
                Diag("Multibox: the teleport to the aetheryte was refused");
                return;
            }
            await WaitUntilTimed(() => Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas], 15_000, "instance hop: teleport start");
            await WaitUntilTimed(() => !Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas] && Svc.Objects.LocalPlayer is not null, 60_000, "instance hop: teleport end");
            if (!await WaitUntilTimed(() => LifestreamFunc<bool>("Lifestream.CanChangeInstance", out var can, quiet: true) && can, 15_000, "instance hop: at the aetheryte"))
            {
                Diag("Multibox: at the aetheryte, but Lifestream cannot change the instance here");
                return;
            }
        }

        Diag($"Multibox: changing to instance {target} (Lifestream)");
        try { Svc.PluginInterface.GetIpcSubscriber<int, object>("Lifestream.ChangeInstance").InvokeAction((int)target); }
        catch (Exception ex) { Diag($"Multibox: Lifestream.ChangeInstance failed: {ex.Message}"); return; }

        if (await WaitUntilTimed(() => MultiboxLink.CurrentInstance == target && !Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas], 90_000, "instance hop: in the leader's instance"))
        {
            Diag($"Multibox: in instance {target} with the leader");
            instanceHopTarget = 0;
        }
    }

    private static unsafe bool TeleportToAetheryte(uint aetheryte)
    {
        var telepo = FFXIVClientStructs.FFXIV.Client.Game.UI.Telepo.Instance();
        return telepo is not null && telepo->Teleport(aetheryte, 0);
    }

    private static bool LifestreamFunc<T>(string name, out T value, bool quiet = false)
    {
        value = default!;
        try
        {
            var gate = Svc.PluginInterface.GetIpcSubscriber<T>(name);
            if (!gate.HasFunction) return false;
            value = gate.InvokeFunc();
            return true;
        }
        catch (Exception ex)
        {
            if (!quiet) Svc.Log.Debug($"[AFG] {name} failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>A follower's pick: the leader's FATE when it exists here and is still going; null to pick as usual.</summary>
    private PublicEvent? LeaderFate()
    {
        if (!MultiboxFollowerWatch.IsFollower) return null;
        if (MultiboxLink.Leader() is not { FateId: not 0 } leader || leader.Territory != Svc.ClientState.TerritoryType) return null;
        if (leader.Instance != MultiboxLink.CurrentInstance) return null; // another instance: different FATEs
        if (PublicEvent.GetFateById(leader.FateId) is not { Progress: < 100 } fate) return null;
        if (fate.State is FateState.Ended or FateState.Failed) return null;
        // Fork (item 18): a FATE above this slave's level would sync nothing up and kill it (Settings → Filters: max above).
        var level = Svc.PlayerState.Level;
        if (level > 0 && fate.Level > level + Plugin.Cfg.MaxLevelAbove)
        {
            MultiboxLink.FollowerStatus = $"blocked: the leader's FATE is Lv {fate.Level}, I am Lv {level} (max {Plugin.Cfg.MaxLevelAbove} above)";
            return null;
        }
        return fate;
    }
}
