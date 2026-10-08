namespace AutoFateGrind.Core.Fork;

/// <summary>
///     README-FORK item 26: where a multibox follower parks. Never a trip while another plugin uses the character (it goes
///     dormant where it stands), and always an inn of the world it is on: Lifestream's "inn N" returns to the home world first
///     on a visited one, so a visiting character takes Lifestream's own "inn" there.
/// </summary>
public static class FollowerPark
{
    /// <summary>A trip to an inn only when nothing else is busy (<paramref name="busyElsewhere" /> null).</summary>
    public static bool Trip(string? busyElsewhere) => busyElsewhere is null;

    /// <summary>
    ///     The Lifestream command: the Grand Company city's inn on the home world ("inn N" counts its InnData, sorted by territory:
    ///     1 Limsa Lominsa, 2 Ul'dah, 3 Gridania), plain "inn" without a company or on a visited world.
    /// </summary>
    public static string InnCommand(byte grandCompany, bool visitingAnotherWorld) =>
        visitingAnotherWorld ? "inn" : grandCompany switch
        {
            1 => "inn 1", // Maelstrom: Limsa Lominsa, The Mizzenmast
            2 => "inn 3", // Order of the Twin Adder: Gridania, The Roost
            3 => "inn 2", // Immortal Flames: Ul'dah, The Hourglass
            _ => "inn",
        };
}
