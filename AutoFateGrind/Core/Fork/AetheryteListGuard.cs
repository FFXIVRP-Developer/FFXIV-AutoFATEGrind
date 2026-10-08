using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Fork;

/// <summary>
///     README-FORK item 25: the game's aetheryte list (Svc.AetheryteList, Telepo.UpdateAetheryteList) is read only with a
///     character loaded and not between maps; read while logging in, it crashed the game (access violation).
/// </summary>
internal static class AetheryteListGuard
{
    public static bool CanRead(bool loggedIn, bool hasCharacter, bool betweenAreas) => loggedIn && hasCharacter && !betweenAreas;

    public static bool CanReadNow() =>
        CanRead(Svc.ClientState.IsLoggedIn, Svc.Objects.LocalPlayer is not null,
                Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51]);
}
