using AutoFateGrind.Core.Zones;

namespace AutoFateGrind.Core.Tasks;

// Spot is null for a city break: Wander walks the city, otherwise the break idles where the teleport lands.
internal readonly record struct BreakPlan(uint TerritoryId, string Place, IdleSpot? Spot, bool Wander)
{
    public string Describe()
    {
        if (Spot is { } spot)
        {
            var name = string.IsNullOrEmpty(spot.Name) ? "saved spot" : spot.Name;
            return $"idle at {name} in {Place} ({spot.Position})";
        }
        return Wander ? $"wander in {Place}" : $"idle at the aetheryte in {Place}";
    }
}

// Idle breaks fall back to standing at a city aetheryte when no spot is saved, so the cities list feeds both styles.
internal static class HumanizeBreaks
{
    public static bool HasDestination(Configuration cfg)
    {
        if (UsesSpots(cfg)) return true;

        var cities = CityCatalog.All;
        for (var index = 0; index < cities.Length; index++)
        {
            if (cfg.HumanizerCities.Contains(cities[index].TerritoryId)) return true;
        }
        return false;
    }

    public static BreakPlan? Pick(Configuration cfg, IdleSpot? previous, Random rng)
    {
        if (UsesSpots(cfg))
        {
            var spot = PickSpot(cfg.HumanizerIdleSpots, previous, rng);
            return new BreakPlan(spot.TerritoryId, TerritoryNames.Of(spot.TerritoryId), spot, Wander: false);
        }

        var allowed = new List<CityInfo>(CityCatalog.All.Length);
        for (var index = 0; index < CityCatalog.All.Length; index++)
        {
            var city = CityCatalog.All[index];
            if (cfg.HumanizerCities.Contains(city.TerritoryId)) allowed.Add(city);
        }
        if (allowed.Count == 0) return null;

        var picked = allowed[rng.Next(allowed.Count)];
        return new BreakPlan(picked.TerritoryId, picked.Name, Spot: null, Wander: cfg.HumanizerBreakActivity == HumanizerBreakActivity.Wander);
    }

    private static bool UsesSpots(Configuration cfg)
        => cfg.HumanizerBreakActivity == HumanizerBreakActivity.IdleAtSpot && cfg.HumanizerIdleSpots.Count > 0;

    private static IdleSpot PickSpot(List<IdleSpot> spots, IdleSpot? previous, Random rng)
    {
        var previousIndex = previous is null ? -1 : spots.IndexOf(previous);
        if (spots.Count == 1 || previousIndex < 0) return spots[rng.Next(spots.Count)];

        var index = rng.Next(spots.Count - 1);
        return spots[index >= previousIndex ? index + 1 : index];
    }
}
