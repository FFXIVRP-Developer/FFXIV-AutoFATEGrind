using AutoFateGrind.Core.Zones;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using System.Numerics;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace AutoFateGrind.Core.Game.Ops;

internal enum IdleSpotCapture : byte
{
    Saved,
    NotLoaded,
    Airborne,
    NoAetheryte,
}

internal static class IdleSpotOps
{
    private const float TargetNameRangeMeters = 15f;

    // A spot is only worth saving where a break can teleport back to, so the check mirrors the teleport's own.
    public static IdleSpotCapture TryCapture(out IdleSpot? spot)
    {
        spot = null;
        if (Svc.Objects.LocalPlayer is not { } player || Svc.Condition[ConditionFlag.BetweenAreas]) return IdleSpotCapture.NotLoaded;
        if (Svc.Condition[ConditionFlag.InFlight] || Svc.Condition[ConditionFlag.Diving]) return IdleSpotCapture.Airborne;

        var territoryId = Svc.ClientState.TerritoryType;
        if (!ZoneAetherytes.IsTeleportable(territoryId)) return IdleSpotCapture.NoAetheryte;

        var position = player.Position;
        spot = new IdleSpot
        {
            TerritoryId = territoryId,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            Name = NearbyTargetName(position),
        };
        return IdleSpotCapture.Saved;
    }

    // Another player's name would end up in the config, and a far target would name a spot it isn't at.
    private static string NearbyTargetName(Vector3 position)
    {
        if (Svc.Targets.Target is not { } target || target.ObjectKind == ObjectKind.Pc) return "";
        return Vector3.Distance(target.Position, position) <= TargetNameRangeMeters ? target.Name.TextValue : "";
    }
}
