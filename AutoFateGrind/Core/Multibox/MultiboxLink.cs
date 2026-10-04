using System.IO;
using System.Text.Json;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace AutoFateGrind.Core.Multibox;

// Fork (README-FORK item 16): multibox follow. The Leader publishes where it grinds (world, instance, zone, FATE) to a
// small file every client of the same Windows user can read; a Follower goes to the leader's zone and takes the
// leader's FATE. Every client also writes a status card (clients\<content id>.json) so the Multibox settings tab can
// show who is connected, following or blocked, like AutoDuty's multibox list. No network: the clients share the disk.
// XIVProfiles' slave override sets every slave to Follower, so MAIN is the only leader without any setup there.
public enum MultiboxRole
{
    Leader = 0,
    Follower = 1,
}

// Fork (item 21): TargetId (the leader's current target, for slaves assisting a tank) and Tank (on a tank job).
internal sealed record LeaderState(uint World, uint Territory, uint FateId, float X, float Y, float Z, DateTime UpdatedUtc, string Name, uint Instance = 0,
                                   ulong TargetId = 0, bool Tank = false);

internal sealed record ClientCard(ulong ContentId, string Name, string World, MultiboxRole Role, bool Running, uint Territory, string Zone,
                                  uint Instance, uint FateId, string Fate, string Status, DateTime UpdatedUtc);

internal static unsafe class MultiboxLink
{
    // Older than this, the leader is not grinding (stopped, crashed, logged out): followers go their own way.
    public static readonly TimeSpan LeaderFresh = TimeSpan.FromSeconds(20);

    // A client card older than this reads as disconnected; much older ones are not listed at all.
    public static readonly TimeSpan ClientFresh = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ClientListed = TimeSpan.FromMinutes(30);

    private const int PublishEveryMs = 1_000;
    private const int ReadEveryMs = 1_000;

    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoFateGrind-fork");

    private static readonly string LeaderPath = Path.Combine(Root, "leader.json");
    private static readonly string ClientsFolder = Path.Combine(Root, "clients");

    private static long nextPublishMs;
    private static long nextReadMs;
    private static LeaderState? lastRead;

    /// <summary>The FATE this client fights or heads for (set by the run); 0 = none.</summary>
    public static uint CurrentFateId { get; set; }

    /// <summary>A follower's state in words ("following X", "blocked: ..."), set by the run.</summary>
    public static string FollowerStatus { get; set; } = "";

    public static uint CurrentInstance
    {
        get
        {
            var ui = UIState.Instance();
            return ui is null ? 0 : ui->PublicInstance.InstanceId;
        }
    }

    public static void Publish(uint territory, uint fateId)
    {
        if (Environment.TickCount64 < nextPublishMs) return;
        nextPublishMs = Environment.TickCount64 + PublishEveryMs;
        var player = Svc.Objects.LocalPlayer;
        if (player is null) return;
        var state = new LeaderState(player.CurrentWorld.RowId, territory, fateId, player.Position.X, player.Position.Y, player.Position.Z,
                                    DateTime.UtcNow, player.Name.TextValue, CurrentInstance,
                                    Svc.Targets.Target is Dalamud.Game.ClientState.Objects.Types.IBattleNpc { IsDead: false } t ? t.GameObjectId : 0,
                                    player.ClassJob.Value.Role == 1);
        WriteAtomic(LeaderPath, JsonSerializer.Serialize(state));
    }

    /// <summary>The leader's state when fresh (any world: the caller compares); null otherwise.</summary>
    public static LeaderState? LeaderAnyWorld()
    {
        if (Environment.TickCount64 >= nextReadMs)
        {
            nextReadMs = Environment.TickCount64 + ReadEveryMs;
            try
            {
                lastRead = File.Exists(LeaderPath) ? JsonSerializer.Deserialize<LeaderState>(File.ReadAllText(LeaderPath)) : null;
            }
            catch
            {
                // Mid-write or unreadable: keep the last good read for this second.
            }
        }
        return lastRead is { } leader && DateTime.UtcNow - leader.UpdatedUtc <= LeaderFresh ? leader : null;
    }

    /// <summary>The leader's state when fresh and on this client's world; null otherwise.</summary>
    public static LeaderState? Leader()
    {
        if (LeaderAnyWorld() is not { } leader) return null;
        var world = Svc.Objects.LocalPlayer?.CurrentWorld.RowId ?? 0;
        return leader.World == world ? leader : null;
    }

    public static void PublishClient(ClientCard card)
        => WriteAtomic(Path.Combine(ClientsFolder, card.ContentId + ".json"), JsonSerializer.Serialize(card));

    /// <summary>Every client card written in the last half hour, newest first.</summary>
    public static List<ClientCard> Clients()
    {
        var cards = new List<ClientCard>();
        try
        {
            if (!Directory.Exists(ClientsFolder)) return cards;
            foreach (var file in Directory.EnumerateFiles(ClientsFolder, "*.json"))
            {
                try
                {
                    if (JsonSerializer.Deserialize<ClientCard>(File.ReadAllText(file)) is { } card && DateTime.UtcNow - card.UpdatedUtc <= ClientListed)
                        cards.Add(card);
                }
                catch
                {
                    // One unreadable card (mid-write) does not hide the others.
                }
            }
        }
        catch
        {
            // Folder unreadable: an empty list.
        }
        cards.Sort((a, b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
        return cards;
    }

    private static void WriteAtomic(string path, string json)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true); // whole file or nothing for a reader
        }
        catch (Exception ex)
        {
            Svc.Log.Debug($"[AFG] Multibox: could not write {Path.GetFileName(path)}: {ex.Message}");
        }
    }
}
