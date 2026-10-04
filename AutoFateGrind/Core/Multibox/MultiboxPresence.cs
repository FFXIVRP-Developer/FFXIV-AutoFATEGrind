using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Multibox;

// Fork (README-FORK item 16): every client, running a run or not, writes its status card every 2 s while logged in, so
// the Multibox tab lists who is connected. The run fills in the FATE and a follower's status (AutoFate.Multibox).
internal static class MultiboxPresence
{
    private const int EveryMs = 2_000;
    private static long nextMs;
    private const int PositionEveryMs = 30_000;
    private static long nextPositionMs;

    public static void Start() => Svc.Framework.Update += Tick;

    public static void Stop() => Svc.Framework.Update -= Tick;

    private static void Tick(Dalamud.Plugin.Services.IFramework _)
    {
        if (Environment.TickCount64 < nextMs) return;
        nextMs = Environment.TickCount64 + EveryMs;
        var player = Svc.Objects.LocalPlayer;
        if (player is null) return;

        var running = Plugin.Instance?.Controller.Running ?? false;
        // Fork (item 20): a position line every 30 s of a run, to check the spreading from the logs.
        if (running && Environment.TickCount64 >= nextPositionMs)
        {
            nextPositionMs = Environment.TickCount64 + PositionEveryMs;
            var nearest = Svc.Objects.OfType<Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter>()
                .Where(pc => pc.GameObjectId != player.GameObjectId)
                .Select(pc => System.Numerics.Vector3.Distance(pc.Position, player.Position))
                .DefaultIfEmpty(-1f).Min();
            Svc.Log.Info($"[AFG] POSITION ({player.Position.X:F0},{player.Position.Y:F0},{player.Position.Z:F0}) fate={MultiboxLink.CurrentFateId} combat={Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat]} nearest player {(nearest < 0 ? "none" : $"{nearest:F0} m")}");
        }
        // The leader publishes while its run is going, breaks included (the run's own publish only runs while it picks
        // FATEs: a humanizer break went stale and parked the followers).
        if (running && !MultiboxFollowerWatch.IsFollower) MultiboxLink.Publish(Svc.ClientState.TerritoryType, MultiboxLink.CurrentFateId);
        var territory = Svc.ClientState.TerritoryType;
        var zone = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRowOrDefault(territory)?.PlaceName.ValueNullable?.Name.ExtractText() ?? territory.ToString();
        var fateId = running ? MultiboxLink.CurrentFateId : 0;
        var fate = fateId == 0 ? "" : PublicEventName(fateId);
        var role = MultiboxFollowerWatch.IsFollower ? MultiboxRole.Follower : MultiboxRole.Leader;
        var status = !running ? (MultiboxFollowerWatch.Parked ? (MultiboxFollowerWatch.Blocked is { } why ? $"parked: {why}" : "parked (leader stopped)") + (MultiboxFollowerWatch.Dormant ? ", left alone until the leader's next run" : "") : "not running")
                   : role == MultiboxRole.Leader ? "leading"
                   : MultiboxLink.FollowerStatus;

        MultiboxLink.PublishClient(new ClientCard(ECommons.GameHelpers.Player.CID, player.Name.TextValue,
            player.CurrentWorld.ValueNullable?.Name.ExtractText() ?? "", role, running, territory, zone, MultiboxLink.CurrentInstance,
            fateId, fate, status, DateTime.UtcNow));
    }

    private static string PublicEventName(uint fateId)
        => Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Fate>().GetRowOrDefault(fateId)?.Name.ExtractText() ?? $"FATE {fateId}";
}
