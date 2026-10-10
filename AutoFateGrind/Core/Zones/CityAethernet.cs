using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System.Numerics;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace AutoFateGrind.Core.Zones;

internal readonly record struct AethernetShard(uint Id, uint Group, bool IsAetheryte, Vector3 Position);

internal readonly record struct AethernetHop(AethernetShard Source, AethernetShard Destination, float WalkMeters, float RideMeters)
{
    public bool SavesDistance => RideMeters < WalkMeters;
}

// Plans a same-city aethernet hop once, from AFG's own shard positions, and only when riding beats walking.
// clib's MoveTo decides twice from shifted positions and rides away from the target (issue #75).
internal static unsafe class CityAethernet
{
    // The menu, the hop and its loading screen, expressed as the walk they replace; high enough that a
    // coin-flip saving walks instead of paying a loading screen.
    private const float HopCostMeters = 100f;

    private static readonly Dictionary<uint, AethernetShard[]> shardsByTerritory = new();

    // Only a shard the game has spawned counts as a source: that is the one the character can reach and
    // interact with, and its live position replaces the map-marker estimate.
    public static bool TryPlanHop(uint territoryId, Vector3 from, Vector3 to, out AethernetHop hop)
    {
        hop = default;
        var shards = ShardsIn(territoryId);
        if (shards.Length < 2) return false;

        var source = Nearest(shards, from);
        var destination = Nearest(shards, to);
        if (source.Id == destination.Id || source.Group != destination.Group) return false;
        if (!IsAttuned(source.Id) || !IsAttuned(destination.Id)) return false;
        if (FindShardObject(source.Id) is not { } sourceObject) return false;

        source = source with { Position = sourceObject.Position };
        var walkMeters = FlatDistance(from, to);
        var rideMeters = FlatDistance(from, source.Position) + HopCostMeters + FlatDistance(destination.Position, to);
        hop = new AethernetHop(source, destination, walkMeters, rideMeters);
        return true;
    }

    // Entering a district from its hub has no walk to compare against, so the hop is planned whenever an
    // attuned shard of the hub's aethernet stands in the district: the one nearest the destination.
    public static bool TryPlanEntry(uint fromTerritoryId, Vector3 from, uint toTerritoryId, Vector3 to, uint group, out AethernetHop hop)
    {
        hop = default;
        if (!TryFindNearestAttuned(ShardsIn(toTerritoryId), to, group, out var destination)) return false;
        if (!TryFindNearestAttuned(ShardsIn(fromTerritoryId), from, group, out var source)) return false;
        if (FindShardObject(source.Id) is not { } sourceObject) return false;

        source = source with { Position = sourceObject.Position };
        var rideMeters = FlatDistance(from, source.Position) + HopCostMeters + FlatDistance(destination.Position, to);
        hop = new AethernetHop(source, destination, float.PositiveInfinity, rideMeters);
        return true;
    }

    public static bool HoldsShardOf(uint territoryId, uint group)
    {
        var shards = ShardsIn(territoryId);
        for (var index = 0; index < shards.Length; index++)
        {
            if (shards[index].Group == group && !shards[index].IsAetheryte) return true;
        }
        return false;
    }

    public static Dalamud.Game.ClientState.Objects.Types.IGameObject? FindShardObject(uint aetheryteId)
    {
        foreach (var gameObject in Svc.Objects)
        {
            if (gameObject.ObjectKind == ObjectKind.Aetheryte && gameObject.BaseId == aetheryteId) return gameObject;
        }
        return null;
    }

    public static string ShardName(uint aetheryteId)
    {
        var row = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>()?.GetRowOrDefault(aetheryteId);
        var name = row is not { } aetheryte
            ? null
            : aetheryte.IsAetheryte
                ? aetheryte.PlaceName.ValueNullable?.Name.ExtractText()
                : aetheryte.AethernetName.ValueNullable?.Name.ExtractText();
        return string.IsNullOrWhiteSpace(name) ? $"aetheryte #{aetheryteId}" : name;
    }

    private static AethernetShard Nearest(AethernetShard[] shards, Vector3 point)
    {
        var nearest = shards[0];
        var bestDistance = FlatDistance(nearest.Position, point);
        for (var index = 1; index < shards.Length; index++)
        {
            var distance = FlatDistance(shards[index].Position, point);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            nearest = shards[index];
        }
        return nearest;
    }

    private static bool TryFindNearestAttuned(AethernetShard[] shards, Vector3 point, uint group, out AethernetShard nearest)
    {
        nearest = default;
        var bestDistance = float.MaxValue;
        for (var index = 0; index < shards.Length; index++)
        {
            var shard = shards[index];
            if (shard.Group != group || !IsAttuned(shard.Id)) continue;
            var distance = FlatDistance(shard.Position, point);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            nearest = shard;
        }
        return bestDistance < float.MaxValue;
    }

    // Marker positions carry no height, so every comparison stays on the ground plane.
    private static float FlatDistance(Vector3 first, Vector3 second)
        => Vector2.Distance(new Vector2(first.X, first.Z), new Vector2(second.X, second.Z));

    private static bool IsAttuned(uint aetheryteId)
    {
        var uiState = UIState.Instance();
        return uiState is not null && uiState->IsAetheryteUnlocked(aetheryteId);
    }

    private static AethernetShard[] ShardsIn(uint territoryId)
    {
        if (shardsByTerritory.TryGetValue(territoryId, out var cached)) return cached;
        var resolved = ResolveShards(territoryId);
        shardsByTerritory[territoryId] = resolved;
        return resolved;
    }

    private static AethernetShard[] ResolveShards(uint territoryId)
    {
        var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>();
        if (sheet is null) return [];

        var found = new List<AethernetShard>(8);
        foreach (var row in sheet)
        {
            if (row.Territory.RowId != territoryId || row.AethernetGroup == 0) continue;
            if (!row.IsAetheryte && row.AethernetName.RowId == 0) continue;
            if (!AetheryteGeometry.TryResolvePosition(row, out var position)) continue;
            found.Add(new AethernetShard(row.RowId, row.AethernetGroup, row.IsAetheryte, position));
        }
        return found.ToArray();
    }
}
