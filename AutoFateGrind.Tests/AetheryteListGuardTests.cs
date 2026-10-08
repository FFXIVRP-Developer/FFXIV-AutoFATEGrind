using AutoFateGrind.Core.Fork;

namespace AutoFateGrind.Tests;

/// <summary>
///     README-FORK item 25. The game crashed (access violation in Telepo.UpdateAetheryteList) when AFG's window, drawn while
///     the game was still logging in, read the aetheryte list (HeaderBar → ReadyState → YokaiProgress → ZoneStateReader
///     .AttunedSet → Svc.AetheryteList). The list is read only with a character loaded and not between maps.
/// </summary>
public sealed class AetheryteListGuardTests
{
    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, false, false)] // the title screen, the character select
    [InlineData(true, false, false, false)]  // logging in: no character object yet
    [InlineData(true, true, true, false)]    // a loading screen
    public void GivenTheGameState_ExpectTheListReadOnlyWhenSafe(bool loggedIn, bool hasCharacter, bool betweenAreas, bool read) =>
        Assert.Equal(read, AetheryteListGuard.CanRead(loggedIn, hasCharacter, betweenAreas));

    [Fact]
    public void GivenTheReader_ExpectTheGuardBeforeTheList()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "AutoFateGrind", "Core", "Zones", "ZoneStateReader.cs"))) dir = Path.GetDirectoryName(dir)!;
        var source = File.ReadAllText(Path.Combine(dir, "AutoFateGrind", "Core", "Zones", "ZoneStateReader.cs"));
        var attunedSet = source[source.IndexOf("private static HashSet<uint> AttunedSet()", StringComparison.Ordinal)..];
        var guard = attunedSet.IndexOf("AetheryteListGuard.CanReadNow()", StringComparison.Ordinal);
        Assert.True(guard >= 0, "AttunedSet reads the list without the guard");
        Assert.True(guard < attunedSet.IndexOf("BuildAttunedSet()", StringComparison.Ordinal));
    }
}
