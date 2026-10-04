using System.IO;
using System.Text.Json;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Multibox;

// Fork (README-FORK item 16): multibox follow. The Leader publishes where it grinds (zone, FATE, world) to a small file
// every client of the same Windows user can read; a Follower goes to the leader's zone and takes the leader's FATE.
// No network, no party protocol: the clients share the disk. XIVProfiles' slave override sets every slave to Follower,
// so MAIN is the only leader without any setup there.
public enum MultiboxRole
{
    Leader = 0,
    Follower = 1,
}

internal sealed record LeaderState(uint World, uint Territory, uint FateId, float X, float Y, float Z, DateTime UpdatedUtc, string Name);

internal static class MultiboxLink
{
    // Older than this, the leader is not grinding (stopped, crashed, logged out): followers go their own way.
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(20);
    private const int PublishEveryMs = 1_000;
    private const int ReadEveryMs = 1_000;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoFateGrind-fork", "leader.json");

    private static long nextPublishMs;
    private static long nextReadMs;
    private static LeaderState? lastRead;

    public static void Publish(uint territory, uint fateId)
    {
        if (Environment.TickCount64 < nextPublishMs) return;
        nextPublishMs = Environment.TickCount64 + PublishEveryMs;
        var player = Svc.Objects.LocalPlayer;
        if (player is null) return;
        try
        {
            var state = new LeaderState(player.CurrentWorld.RowId, territory, fateId,
                                        player.Position.X, player.Position.Y, player.Position.Z, DateTime.UtcNow, player.Name.TextValue);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state));
            File.Move(temp, FilePath, overwrite: true); // whole file or nothing for a reader
        }
        catch (Exception ex)
        {
            Svc.Log.Debug($"[AFG] Multibox: could not publish the leader state: {ex.Message}");
        }
    }

    /// <summary>The leader's state when fresh and on this client's world; null otherwise.</summary>
    public static LeaderState? Leader()
    {
        if (Environment.TickCount64 >= nextReadMs)
        {
            nextReadMs = Environment.TickCount64 + ReadEveryMs;
            try
            {
                lastRead = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<LeaderState>(File.ReadAllText(FilePath))
                    : null;
            }
            catch
            {
                // Mid-write or unreadable: keep the last good read for this second.
            }
        }

        if (lastRead is not { } leader || DateTime.UtcNow - leader.UpdatedUtc > Fresh) return null;
        var world = Svc.Objects.LocalPlayer?.CurrentWorld.RowId ?? 0;
        return leader.World == world ? leader : null;
    }
}
