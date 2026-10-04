using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Multibox;

// Fork (README-FORK item 16): every client, running a run or not, writes its status card every 2 s while logged in, so
// the Multibox tab lists who is connected. The run fills in the FATE and a follower's status (AutoFate.Multibox).
internal static class MultiboxPresence
{
    private const int EveryMs = 2_000;
    private static long nextMs;

    public static void Start() => Svc.Framework.Update += Tick;

    public static void Stop() => Svc.Framework.Update -= Tick;

    private static void Tick(Dalamud.Plugin.Services.IFramework _)
    {
        if (Environment.TickCount64 < nextMs) return;
        nextMs = Environment.TickCount64 + EveryMs;
        var player = Svc.Objects.LocalPlayer;
        if (player is null) return;

        var running = Plugin.Instance?.Controller.Running ?? false;
        // The leader publishes while its run is going, breaks included (the run's own publish only runs while it picks
        // FATEs: a humanizer break went stale and parked the followers).
        if (running && !MultiboxFollowerWatch.IsFollower) MultiboxLink.Publish(Svc.ClientState.TerritoryType, MultiboxLink.CurrentFateId);
        var territory = Svc.ClientState.TerritoryType;
        var zone = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRowOrDefault(territory)?.PlaceName.ValueNullable?.Name.ExtractText() ?? territory.ToString();
        var fateId = running ? MultiboxLink.CurrentFateId : 0;
        var fate = fateId == 0 ? "" : PublicEventName(fateId);
        var role = MultiboxFollowerWatch.IsFollower ? MultiboxRole.Follower : MultiboxRole.Leader;
        var status = !running ? (MultiboxFollowerWatch.Parked ? (MultiboxFollowerWatch.Blocked is { } why ? $"parked: {why}" : "parked (leader stopped)") : "not running")
                   : role == MultiboxRole.Leader ? "leading"
                   : MultiboxLink.FollowerStatus;

        MultiboxLink.PublishClient(new ClientCard(ECommons.GameHelpers.Player.CID, player.Name.TextValue,
            player.CurrentWorld.ValueNullable?.Name.ExtractText() ?? "", role, running, territory, zone, MultiboxLink.CurrentInstance,
            fateId, fate, status, DateTime.UtcNow));
    }

    private static string PublicEventName(uint fateId)
        => Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Fate>().GetRowOrDefault(fateId)?.Name.ExtractText() ?? $"FATE {fateId}";
}
