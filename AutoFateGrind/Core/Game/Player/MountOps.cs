using Dalamud.Game;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System.Globalization;
using Mount = Lumina.Excel.Sheets.Mount;
using TerritoryType = Lumina.Excel.Sheets.TerritoryType;

namespace AutoFateGrind.Core.Game.Player;

internal readonly record struct MountOption(uint Id, string Name);

internal static unsafe class MountOps
{
    public const uint Roulette = 0;

    public static bool HasPreferred => IsUsable(Plugin.Cfg.PreferredMountId);

    // clib's own Mount() gate: inns and similar interiors forbid mounts outright, so there is nothing to wait for.
    public static bool TerritoryAllowsMount()
        => Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(Svc.ClientState.TerritoryType) is { Mount: true };

    // Only Mount Guide rows (Order >= 0) that fly are offered, so a chosen mount never strands vnav's flying route.
    public static bool IsUsable(uint mountId)
    {
        if (mountId == Roulette)
        {
            return false;
        }
        if (Svc.Data.GetExcelSheet<Mount>().GetRowOrDefault(mountId) is not { } row || !IsSelectable(row))
        {
            return false;
        }
        var playerState = PlayerState.Instance();
        return playerState is not null && playerState->IsMountUnlocked(mountId);
    }

    public static bool TrySummonPreferred()
    {
        var mountId = Plugin.Cfg.PreferredMountId;
        if (!IsUsable(mountId))
        {
            return false;
        }
        var actionManager = ActionManager.Instance();
        if (actionManager is null || actionManager->GetActionStatus(ActionType.Mount, mountId) != 0)
        {
            return false;
        }
        return actionManager->UseAction(ActionType.Mount, mountId);
    }

    public static MountOption[] OwnedMounts()
    {
        var playerState = PlayerState.Instance();
        if (playerState is null)
        {
            return [];
        }

        var owned = new List<MountOption>();
        foreach (var row in Svc.Data.GetExcelSheet<Mount>())
        {
            if (!IsSelectable(row) || !playerState->IsMountUnlocked(row.RowId))
            {
                continue;
            }
            var name = row.Singular.ExtractText();
            if (name.Length == 0)
            {
                continue;
            }
            owned.Add(new MountOption(row.RowId, DisplayName(name)));
        }

        var options = owned.ToArray();
        Array.Sort(options, static (left, right) => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase));
        return options;
    }

    public static string NameOf(uint mountId)
    {
        if (Svc.Data.GetExcelSheet<Mount>().GetRowOrDefault(mountId) is not { } row)
        {
            return string.Empty;
        }
        return DisplayName(row.Singular.ExtractText());
    }

    private static bool IsSelectable(in Mount row)
        => row.Order >= 0 && row.IsFlying != 0 && !row.IsImmobile;

    // English sheet names are lower case ("company chocobo"); the Mount Guide shows them title cased.
    private static string DisplayName(string name)
    {
        if (name.Length == 0)
        {
            return name;
        }
        if (Svc.ClientState.ClientLanguage == ClientLanguage.English)
        {
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
        }
        return char.IsLower(name[0]) ? string.Concat(char.ToUpperInvariant(name[0]).ToString(), name.AsSpan(1)) : name;
    }
}
