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
internal static unsafe class MultiboxParty
{
    private const int EveryMs = 5_000;
    private const double RetrySeconds = 20;
    private const int PartyCap = 8;

    private static long nextMs;
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
        if (Plugin.Cfg.MultiboxRole != MultiboxRole.Leader || Plugin.Instance?.Controller.Running != true) return;
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

    private static ushort? WorldId(string name)
        => (ushort?)Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.World>().FirstOrDefault(w => w.Name.ExtractText() == name).RowId;
}
