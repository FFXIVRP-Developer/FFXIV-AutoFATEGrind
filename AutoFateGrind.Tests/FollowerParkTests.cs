using AutoFateGrind.Core.Fork;

namespace AutoFateGrind.Tests;

/// <summary>
///     README-FORK item 26. 2026-10-08: a multibox follower that logged in with no leader grinding parked 60 s later with
///     "/li inn 2" in the middle of another plugin's work (BoatRunner washing its inventory), and on a visited world Lifestream's
///     "inn N" went home first (ReturnToHome) before the inn. Parking leaves the character alone while another plugin uses it,
///     and on a visited world it is the inn of that world.
/// </summary>
public sealed class FollowerParkTests
{
    [Theory]
    [InlineData(null, true)]          // idle: parked in an inn
    [InlineData("BoatRunner", false)] // another plugin is using the character: dormant where it stands, no trip
    [InlineData("a duty or a loading screen", false)]
    public void GivenWhatElseIsBusy_ExpectATripOnlyWhenIdle(string? busyElsewhere, bool trip) =>
        Assert.Equal(trip, FollowerPark.Trip(busyElsewhere));

    [Theory]
    [InlineData(1, false, "inn 1")] // home world: the Grand Company city's inn
    [InlineData(3, false, "inn 2")]
    [InlineData(2, false, "inn 3")]
    [InlineData(0, false, "inn")]
    [InlineData(1, true, "inn")]    // visiting another world: Lifestream's own inn there ("inn N" returns home first)
    [InlineData(3, true, "inn")]
    public void GivenTheWorld_ExpectAnInnOnIt(byte grandCompany, bool visiting, string command) =>
        Assert.Equal(command, FollowerPark.InnCommand(grandCompany, visiting));

    [Fact]
    public void GivenTheWatch_ExpectParkingToAskBoth()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "AutoFateGrind", "Core", "Multibox", "MultiboxFollowerWatch.cs"))) dir = Path.GetDirectoryName(dir)!;
        var source = File.ReadAllText(Path.Combine(dir, "AutoFateGrind", "Core", "Multibox", "MultiboxFollowerWatch.cs"));
        var park = source[source.IndexOf("private static void Park()", StringComparison.Ordinal)..];
        Assert.Contains("BusyElsewhere()", park);
        Assert.Contains("FollowerPark.Trip(", park);
        Assert.Contains("FollowerPark.InnCommand(", park);
    }
}
