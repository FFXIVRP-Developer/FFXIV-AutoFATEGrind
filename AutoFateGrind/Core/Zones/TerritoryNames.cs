using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoFateGrind.Core.Zones;

internal static class TerritoryNames
{
    private static readonly Dictionary<uint, string> byTerritory = new();

    public static string Of(uint territoryId)
    {
        if (byTerritory.TryGetValue(territoryId, out var cached)) return cached;
        var name = Svc.Data.GetExcelSheet<TerritoryType>()?
            .GetRowOrDefault(territoryId)?.PlaceName.ValueNullable?.Name.ExtractText();
        var resolved = string.IsNullOrWhiteSpace(name) ? $"territory {territoryId}" : name;
        byTerritory[territoryId] = resolved;
        return resolved;
    }
}
