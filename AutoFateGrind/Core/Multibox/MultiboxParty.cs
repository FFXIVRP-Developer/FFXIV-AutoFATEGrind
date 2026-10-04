using ECommons;
using ECommons.DalamudServices;
using ECommons.PartyFunctions;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace AutoFateGrind.Core.Multibox;

// Fork (README-FORK item 19): the leader and its slaves in one party, like AutoDuty's multibox (same game calls).
//   leader, while its run is going: invites every connected slave that is not in the party (one try per slave every
//     20 s, up to the 8-member cap); same world → InviteToParty, another world → InviteToPartyContentId.
//   slave: accepts an invite from the leader with InfoProxyPartyInvite.RespondToInvitation (as Henchman does: the game is
//     answered directly, no Yes/No window is clicked);
//     any other invite is left to the user (or to the auto-decline of Settings → Party invites). A slave already in a
//     party with others is left alone.
//   leader, when its run stops: disbands the party when everyone else in it is one of its slaves (a party with anyone
//     else is left alone), so the boat's party queue starts clean; the next start invites everyone again. A slave that
//     parks because the leader stopped also leaves the party itself (MultiboxFollowerWatch), in case the disband did not.
internal static unsafe class MultiboxParty
{
    private const int EveryMs = 5_000;
    private const double RetrySeconds = 20;
    private const int PartyCap = 8;

    private static long nextMs;
    private static bool wasRunning;
    private static readonly Dictionary<ulong, DateTime> lastInviteUtc = [];

    public static void Start()
    {
        Svc.Framework.Update += Tick;
    }

    public static void Stop()
    {
        Svc.Framework.Update -= Tick;
    }

    /// <summary>The invite on screen comes from the leader (the auto-decline leaves it alone).</summary>
    public static bool IsLeaderInvite()
    {
        if (Plugin.Cfg.MultiboxRole != MultiboxRole.Follower) return false;
        var proxy = InfoProxyPartyInvite.Instance();
        if (proxy is null || proxy->InviterWorldId == 0) return false;
        var inviter = proxy->InviterName.ToString();
        return inviter.Length > 0 && MultiboxLink.LeaderAnyWorld() is { } leader && leader.Name == inviter;
    }

    private static void Tick(Dalamud.Plugin.Services.IFramework _)
    {
        AcceptLeaderInvite();
        if (Environment.TickCount64 < nextMs) return;
        nextMs = Environment.TickCount64 + EveryMs;
        var running = Plugin.Instance?.Controller.Running == true;
        var stopped = wasRunning && !running;
        wasRunning = running;
        if (Plugin.Cfg.MultiboxRole != MultiboxRole.Leader) return;
        if (stopped) { DisbandSlaveParty(); return; }
        if (!running) return;
        if (Svc.Objects.LocalPlayer is not { } me || Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas]
         || Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BoundByDuty]) return;

        var members = UniversalParty.Members;
        if (members.Count >= PartyCap) return;
        var inParty = members.Select(m => m.ContentID).ToHashSet();
        var myWorld = me.CurrentWorld.ValueNullable?.Name.ExtractText() ?? "";
        var free = PartyCap - Math.Max(1, members.Count);

        foreach (var slave in MultiboxLink.Clients())
        {
            if (free <= 0) break;
            if (slave.Role != MultiboxRole.Follower || DateTime.UtcNow - slave.UpdatedUtc > MultiboxLink.ClientFresh) continue;
            if (slave.ContentId == 0 || inParty.Contains(slave.ContentId)) continue;
            if (lastInviteUtc.TryGetValue(slave.ContentId, out var at) && (DateTime.UtcNow - at).TotalSeconds < RetrySeconds) continue;

            lastInviteUtc[slave.ContentId] = DateTime.UtcNow;
            free--;
            try
            {
                var proxy = InfoProxyPartyInvite.Instance();
                if (slave.World == myWorld && WorldId(slave.World) is { } worldId and not 0)
                    proxy->InviteToParty(slave.ContentId, slave.Name, worldId);
                else
                    proxy->InviteToPartyContentId(slave.ContentId, 0);
                Svc.Log.Info($"[AFG] Multibox: invited {slave.Name} to the party");
            }
            catch (Exception ex)
            {
                Svc.Log.Warning($"[AFG] Multibox: inviting {slave.Name} failed: {ex.Message}");
            }
        }
    }

    private static long nextAcceptMs;

    private static void AcceptLeaderInvite()
    {
        if (Environment.TickCount64 < nextAcceptMs) return;
        nextAcceptMs = Environment.TickCount64 + 1_000;
        if (!IsLeaderInvite() || UniversalParty.Length > 1) return;
        var proxy = InfoProxyPartyInvite.Instance();
        Svc.Log.Info($"[AFG] Multibox: accepting the party invite from the leader {proxy->InviterName}");
        proxy->RespondToInvitation(proxy->InviterName.StringPtr, true);
    }

    /// <summary>The leader's run stopped: break up a party made of this client and its slaves only.</summary>
    private static void DisbandSlaveParty()
    {
        var members = UniversalParty.Members;
        if (members.Count <= 1) return;
        var me = ECommons.GameHelpers.Player.CID;
        var slaves = MultiboxLink.Clients().Where(c => c.Role == MultiboxRole.Follower).Select(c => c.ContentId).ToHashSet();
        var strangers = members.Where(m => m.ContentID != me && !slaves.Contains(m.ContentID)).Select(m => m.Name).ToList();
        if (strangers.Count > 0)
        {
            Svc.Log.Info($"[AFG] Multibox: the run stopped; not disbanding, the party has others in it ({string.Join(", ", strangers)})");
            return;
        }
        try
        {
            InfoProxyPartyMember.Instance()->DisbandParty();
            Svc.Log.Info($"[AFG] Multibox: the run stopped; disbanded the party ({members.Count - 1} slave(s))");
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[AFG] Multibox: disbanding the party failed: {ex.Message}");
        }
    }

    /// <summary>A slave parking because the leader stopped: leave the party (when the leader's disband did not happen).</summary>
    public static void LeaveAsSlave()
    {
        if (UniversalParty.Length <= 1) return;
        try
        {
            InfoProxyPartyMember.Instance()->LeaveParty();
            Svc.Log.Info("[AFG] Multibox: the leader stopped; left the party");
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[AFG] Multibox: leaving the party failed: {ex.Message}");
        }
    }

    private static ushort? WorldId(string name)
        => (ushort?)Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.World>().FirstOrDefault(w => w.Name.ExtractText() == name).RowId;
}
