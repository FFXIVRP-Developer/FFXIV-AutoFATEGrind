using ECommons.DalamudServices;
using System.Numerics;

namespace AutoFateGrind.Core.Zones;

internal readonly record struct ZoneAetheryte(uint Id, string Name, Vector3 Position);

// An attunable aetheryte in ANOTHER territory that the game itself nominates as a zone's entry point
// (TerritoryType.Aetheryte), for a zone that owns none of its own.
internal readonly record struct ZoneGateway(uint AetheryteId, uint TerritoryId, string Name, Vector3 Position);

internal static class ZoneAetherytes
{
    private static readonly Dictionary<uint, ZoneAetheryte[]> byTerritory = new();
    private static readonly Dictionary<uint, uint[]> attunableIdsByTerritory = new();
    private static readonly Dictionary<uint, ZoneGateway?> gatewayByTerritory = new();

    public static bool TryFindNearest(uint territoryId, Vector3 target, out ZoneAetheryte nearest)
    {
        var candidates = InTerritory(territoryId);
        nearest = default;
        var bestDistance = float.MaxValue;
        for (var index = 0; index < candidates.Length; index++)
        {
            var distance = Vector3.DistanceSquared(candidates[index].Position, target);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            nearest = candidates[index];
        }
        return bestDistance < float.MaxValue;
    }

    private static ZoneAetheryte[] InTerritory(uint territoryId)
    {
        if (byTerritory.TryGetValue(territoryId, out var cached)) return cached;
        var resolved = ResolveTeleportableAetherytes(territoryId);
        byTerritory[territoryId] = resolved;
        return resolved;
    }

    // Whether a zone is attuned is a question about ids alone, so it must not go through InTerritory: that
    // drops any row whose position will not resolve, which would read back as an unattuned zone.
    public static uint[] AttunableIdsIn(uint territoryId)
    {
        if (attunableIdsByTerritory.TryGetValue(territoryId, out var cached)) return cached;
        var resolved = ResolveAttunableIds(territoryId);
        attunableIdsByTerritory[territoryId] = resolved;
        return resolved;
    }

    // Answers only for an overworld field zone that owns no attunable aetheryte — game-wide, The Dravanian
    // Hinterlands alone. Zones that own one keep routing through their own rows, because the border shards
    // sitting in overworld zones resolve to the adjacent city and park the run there (issue #21); non-field
    // territories are excluded because Limsa Upper Decks and the inns own none either and already work.
    public static bool TryFindGateway(uint territoryId, out ZoneGateway gateway)
    {
        if (!gatewayByTerritory.TryGetValue(territoryId, out var cached))
        {
            cached = ResolveGateway(territoryId);
            gatewayByTerritory[territoryId] = cached;
        }
        gateway = cached ?? default;
        return cached is not null;
    }

    private static ZoneGateway? ResolveGateway(uint territoryId)
    {
        if (AttunableIdsIn(territoryId).Length > 0) return null;

        var territories = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
        if (territories?.GetRowOrDefault(territoryId) is not { } territory) return null;
        // Fork (README-FORK item 13): city districts too. Limsa Upper Decks has no aetheryte, and "already works" was not
        // true for AutoRepair: "territory 128 has no aetheryte to teleport to; giving up" after every FATE (2026-10-03,
        // the Maelstrom Mender), with the gear at 13%. Inns stay out (reached through the innkeeper).
        var intendedUse = territory.TerritoryIntendedUse.ValueNullable?.RowId;
        if (intendedUse != ZoneRegistry.StandardFieldUse && intendedUse != TownUse) return null;

        var aetherytes = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>();
        if (aetherytes is null) return null;
        var gatewayId = territory.Aetheryte.RowId;
        if (aetherytes.GetRowOrDefault(gatewayId) is not { IsAetheryte: true } gatewayRow
            || gatewayRow.Territory.RowId == 0 || gatewayRow.Territory.RowId == territoryId)
        {
            // Fork: no hub named for the territory: take the main aetheryte of the aethernet one of its shards belongs to.
            if (HubOfShardIn(aetherytes, territoryId) is not { } hub) return null;
            gatewayRow = hub;
            gatewayId = hub.RowId;
        }
        if (!TryResolvePosition(gatewayRow, out var position)) return null;

        return new ZoneGateway(gatewayId, gatewayRow.Territory.RowId, ResolveName(gatewayRow), position);
    }

    // Fork: TerritoryIntendedUse row 0 is a town (city districts like Limsa Upper Decks).
    private const uint TownUse = 0;

    // Fork: the main aetheryte (outside the territory) of the aethernet that one of the territory's shards belongs to.
    private static Lumina.Excel.Sheets.Aetheryte? HubOfShardIn(Lumina.Excel.ExcelSheet<Lumina.Excel.Sheets.Aetheryte> aetherytes, uint territoryId)
    {
        foreach (var shard in aetherytes)
        {
            if (shard.IsAetheryte || shard.Territory.RowId != territoryId || shard.AethernetGroup == 0) continue;
            foreach (var main in aetherytes)
            {
                if (main.IsAetheryte && main.AethernetGroup == shard.AethernetGroup
                    && main.Territory.RowId != 0 && main.Territory.RowId != territoryId)
                {
                    return main;
                }
            }
        }
        return null;
    }

    private static uint[] ResolveAttunableIds(uint territoryId)
    {
        var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>();
        if (sheet is null) return [];

        var found = new List<uint>(4);
        foreach (var row in sheet)
        {
            if (!row.IsAetheryte) continue;
            if (row.Territory.RowId != territoryId) continue;
            found.Add(row.RowId);
        }
        return found.ToArray();
    }

    private static ZoneAetheryte[] ResolveTeleportableAetherytes(uint territoryId)
    {
        var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>();
        if (sheet is null) return [];

        var ids = AttunableIdsIn(territoryId);
        var found = new List<ZoneAetheryte>(ids.Length);
        for (var index = 0; index < ids.Length; index++)
        {
            if (sheet.GetRowOrDefault(ids[index]) is not { } row) continue;
            if (!TryResolvePosition(row, out var position)) continue;
            found.Add(new ZoneAetheryte(row.RowId, ResolveName(row), position));
        }
        return found.ToArray();
    }

    private static string ResolveName(Lumina.Excel.Sheets.Aetheryte row)
    {
        var name = row.PlaceName.ValueNullable?.Name.ExtractText();
        return string.IsNullOrWhiteSpace(name) ? $"aetheryte #{row.RowId}" : name;
    }

    private static bool TryResolvePosition(Lumina.Excel.Sheets.Aetheryte row, out Vector3 position)
    {
        if (AetheryteGeometry.TryResolvePosition(row, out position)) return true;
        RunLog.Warning($"Could not resolve a position for aetheryte {row.RowId}; skipping it as a teleport target");
        return false;
    }
}
