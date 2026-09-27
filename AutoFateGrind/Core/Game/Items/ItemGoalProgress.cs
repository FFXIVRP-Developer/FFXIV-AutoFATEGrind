using AutoFateGrind.Core.Zones;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System.Text;

namespace AutoFateGrind.Core.Game.Items;

internal readonly record struct ItemGoalDropStatus(uint ItemId, int Have, int Need)
{
    public bool Done => Have >= Need;

    public int Left => Math.Max(0, Need - Have);
}

internal enum ItemGoalBlocker : byte
{
    None,
    NeedQuest,
    NeedZenith,
    AllCollected,
}

internal static unsafe class ItemGoalProgress
{
    private const int RefreshIntervalMs = 500;
    private const int MainHandSlot = 0;
    private const int OffHandSlot = 1;
    private const uint ZenithRelicRow = 1;

    private static readonly Dictionary<uint, string> itemNames = new();
    private static readonly Dictionary<uint, int> counts = new();
    private static readonly List<ZoneInfo> zoneScratch = new();
    private static readonly StringBuilder describeBuilder = new();
    private static HashSet<uint>? zenithItemIds;
    private static long refreshedAtMs;

    public static int WeaponCount(Configuration cfg) => Math.Clamp(cfg.TargetRelicCount, 1, ItemGoalCatalog.MaxWeaponCount);

    public static ItemGoalDropStatus Status(ItemGoalDefinition goal, int dropIndex, Configuration cfg)
    {
        Refresh(goal);
        var drop = goal.Drops[dropIndex];
        counts.TryGetValue(drop.ItemId, out var have);
        return new ItemGoalDropStatus(drop.ItemId, have, drop.PerWeapon * WeaponCount(cfg));
    }

    public static (int Have, int Need) Totals(ItemGoalDefinition goal, Configuration cfg)
    {
        var have = 0;
        var need = 0;
        for (var index = 0; index < goal.Drops.Length; index++)
        {
            var status = Status(goal, index, cfg);
            have += Math.Min(status.Have, status.Need);
            need += status.Need;
        }

        return (have, need);
    }

    public static int ItemsLeft(ItemGoalDefinition goal, Configuration cfg)
    {
        var (have, need) = Totals(goal, cfg);
        return Math.Max(0, need - have);
    }

    public static bool AllCollected(ItemGoalDefinition goal, Configuration cfg)
    {
        for (var index = 0; index < goal.Drops.Length; index++)
        {
            if (!Status(goal, index, cfg).Done)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsAvailable(ItemGoalDefinition goal) => goal.Gate switch
    {
        ItemGoalGate.QuestAccepted => QuestManager.Instance()->IsQuestAccepted(goal.QuestId),
        ItemGoalGate.ZenithEquipped => IsZenithEquipped(),
        _ => true,
    };

    public static ItemGoalBlocker Blocker(ItemGoalDefinition goal, Configuration cfg)
    {
        if (!IsAvailable(goal))
        {
            return goal.Gate == ItemGoalGate.ZenithEquipped ? ItemGoalBlocker.NeedZenith : ItemGoalBlocker.NeedQuest;
        }

        return AllCollected(goal, cfg) ? ItemGoalBlocker.AllCollected : ItemGoalBlocker.None;
    }

    // Zones where at least one drop is still short, in registry order so the plan reads like the library.
    public static IReadOnlyList<ZoneInfo> PlanZones(ItemGoalDefinition goal, Configuration cfg)
    {
        zoneScratch.Clear();
        var registry = ZoneRegistry.Zones;
        for (var zoneIndex = 0; zoneIndex < registry.Length; zoneIndex++)
        {
            var zone = registry[zoneIndex];
            if (DropsHere(goal, zone.TerritoryId) && !IsZoneDone(goal, zone.TerritoryId, cfg))
            {
                zoneScratch.Add(zone);
            }
        }

        return [.. zoneScratch];
    }

    public static bool DropsHere(ItemGoalDefinition goal, uint territoryId)
    {
        for (var index = 0; index < goal.Drops.Length; index++)
        {
            if (Array.IndexOf(goal.Drops[index].ZoneIds, territoryId) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsZoneDone(ItemGoalDefinition goal, uint territoryId, Configuration cfg)
    {
        var listed = false;
        for (var index = 0; index < goal.Drops.Length; index++)
        {
            if (Array.IndexOf(goal.Drops[index].ZoneIds, territoryId) < 0)
            {
                continue;
            }

            listed = true;
            if (!Status(goal, index, cfg).Done)
            {
                return false;
            }
        }

        return listed;
    }

    public static string ItemName(uint itemId)
    {
        if (itemNames.TryGetValue(itemId, out var cached))
        {
            return cached;
        }

        var name = Svc.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId)?.Name.ExtractText();
        name = string.IsNullOrWhiteSpace(name) ? itemId.ToString() : name;
        itemNames[itemId] = name;
        return name;
    }

    public static string QuestName(uint questId)
    {
        var name = Svc.Data.GetExcelSheet<Quest>().GetRowOrDefault(questId)?.Name.ExtractText();
        return string.IsNullOrWhiteSpace(name) ? questId.ToString() : name.Trim();
    }

    public static string ZoneNames(ItemGoalDefinition goal, int dropIndex)
    {
        var zoneIds = goal.Drops[dropIndex].ZoneIds;
        describeBuilder.Clear();
        for (var index = 0; index < zoneIds.Length; index++)
        {
            if (FindZone(zoneIds[index]) is not { } zone)
            {
                continue;
            }

            if (describeBuilder.Length > 0)
            {
                describeBuilder.Append(", ");
            }

            describeBuilder.Append(zone.Name);
        }

        return describeBuilder.ToString();
    }

    public static string Describe(ItemGoalDefinition goal, Configuration cfg)
    {
        describeBuilder.Clear();
        for (var index = 0; index < goal.Drops.Length; index++)
        {
            var status = Status(goal, index, cfg);
            if (index > 0)
            {
                describeBuilder.Append(" | ");
            }

            describeBuilder.Append(ItemName(status.ItemId)).Append(' ').Append(status.Have).Append('/').Append(status.Need);
        }

        return describeBuilder.ToString();
    }

    public static void Invalidate() => refreshedAtMs = 0;

    private static void Refresh(ItemGoalDefinition goal)
    {
        var now = Environment.TickCount64;
        if (now - refreshedAtMs < RefreshIntervalMs && counts.Count > 0)
        {
            return;
        }

        var inventory = InventoryManager.Instance();
        if (inventory is null)
        {
            return;
        }

        refreshedAtMs = now;
        var definitions = ItemGoalCatalog.Definitions;
        for (var goalIndex = 0; goalIndex < definitions.Length; goalIndex++)
        {
            var drops = definitions[goalIndex].Drops;
            for (var dropIndex = 0; dropIndex < drops.Length; dropIndex++)
            {
                counts[drops[dropIndex].ItemId] = inventory->GetInventoryItemCount(drops[dropIndex].ItemId);
            }
        }
    }

    private static bool IsZenithEquipped()
    {
        var inventory = InventoryManager.Instance();
        if (inventory is null)
        {
            return false;
        }

        var equipped = inventory->GetInventoryContainer(InventoryType.EquippedItems);
        if (equipped is null)
        {
            return false;
        }

        var zeniths = zenithItemIds ??= ResolveZenithItemIds();
        return zeniths.Contains(equipped->GetInventorySlot(MainHandSlot)->ItemId)
            || zeniths.Contains(equipped->GetInventorySlot(OffHandSlot)->ItemId);
    }

    private static HashSet<uint> ResolveZenithItemIds()
    {
        var ids = new HashSet<uint>(12);
        if (Svc.Data.GetExcelSheet<RelicItem>().GetRowOrDefault(ZenithRelicRow) is not { } row)
        {
            return ids;
        }

        ids.Add(row.GladiatorItem.RowId);
        ids.Add(row.PugilistItem.RowId);
        ids.Add(row.MarauderItem.RowId);
        ids.Add(row.LancerItem.RowId);
        ids.Add(row.ArcherItem.RowId);
        ids.Add(row.ConjurerItem.RowId);
        ids.Add(row.ThaumaturgeItem.RowId);
        ids.Add(row.ArcanistSMNItem.RowId);
        ids.Add(row.ArcanistSCHItem.RowId);
        ids.Add(row.ShieldItem.RowId);
        ids.Add(row.RogueItem.RowId);
        ids.Remove(0);
        return ids;
    }

    private static ZoneInfo? FindZone(uint territoryId)
    {
        var registry = ZoneRegistry.Zones;
        for (var index = 0; index < registry.Length; index++)
        {
            if (registry[index].TerritoryId == territoryId)
            {
                return registry[index];
            }
        }

        return null;
    }
}
