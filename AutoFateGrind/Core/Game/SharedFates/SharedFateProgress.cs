using AutoFateGrind.Core.Zones;
using Dalamud.Hooking;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System.Text;

namespace AutoFateGrind.Core.Game.SharedFates;

// Ranks are read the way the game's own FATE Completion window reads them: a per-tab request to the server, whose
// reply lands in the achievement progress receiver, plus a direct progress request per achievement as a second path.
internal static unsafe class SharedFateProgress
{
    private struct Slot
    {
        public uint Current;
        public uint Max;
        public long ReceivedAtMs;
        public long RequestedAtMs;
        public long RefreshDueAtMs;
        public int LocalCompletions;
    }

    private const int RequestCooldownMs = 10_000;
    // The server-side counter moves shortly after the FATE ends, so the confirming read is deferred.
    private const int RefreshAfterFateMs = 3_000;

    private static readonly Slot[] slots = new Slot[SharedFateCatalog.Entries.Length];
    private static readonly long[] tabRequestedAtMs = new long[SharedFateCatalog.TabCount];
    private static Hook<Achievement.Delegates.ReceiveAchievementProgress>? receiveHook;

    public static bool Available => receiveHook is not null;

    public static void Initialize()
    {
        if (receiveHook is not null)
        {
            return;
        }

        try
        {
            receiveHook = Svc.Hook.HookFromAddress<Achievement.Delegates.ReceiveAchievementProgress>(
                Achievement.Addresses.ReceiveAchievementProgress.Value, ReceiveDetour);
            receiveHook.Enable();
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "Could not hook achievement progress; Shared FATE ranks stay unknown");
            receiveHook = null;
        }

        Svc.ClientState.Logout += OnLogout;
    }

    public static void Shutdown()
    {
        Svc.ClientState.Logout -= OnLogout;
        receiveHook?.Disable();
        receiveHook?.Dispose();
        receiveHook = null;
        Forget();
    }

    public static bool TryGet(uint territoryId, out SharedFateRank rank)
    {
        var index = SharedFateCatalog.IndexOfTerritory(territoryId);
        if (index < 0 || slots[index].ReceivedAtMs == 0)
        {
            rank = default;
            return false;
        }

        rank = Resolve(index);
        return true;
    }

    public static bool IsMaxed(uint territoryId) => TryGet(territoryId, out var rank) && rank.IsMaxed;

    public static void Request(uint territoryId, bool force = false)
    {
        var index = SharedFateCatalog.IndexOfTerritory(territoryId);
        if (index < 0 || !CanRequest())
        {
            return;
        }

        var now = Environment.TickCount64;
        RequestTab(SharedFateCatalog.Entries[index].TabIndex, now, force);
        RequestAchievement(index, now, force);
    }

    public static void Request(IReadOnlyList<ZoneInfo> zones, bool force = false)
    {
        for (var index = 0; index < zones.Count; index++)
        {
            Request(zones[index].TerritoryId, force);
        }
    }

    public static void Request(IReadOnlyList<uint> territoryIds, bool force = false)
    {
        for (var index = 0; index < territoryIds.Count; index++)
        {
            Request(territoryIds[index], force);
        }
    }

    public static void NoteCompletion(uint territoryId)
    {
        var index = SharedFateCatalog.IndexOfTerritory(territoryId);
        if (index < 0)
        {
            return;
        }

        ref var slot = ref slots[index];
        if (slot.ReceivedAtMs != 0 && slot.Current + slot.LocalCompletions < Math.Max(slot.Max, 1u))
        {
            slot.LocalCompletions++;
        }

        slot.RefreshDueAtMs = Environment.TickCount64 + RefreshAfterFateMs;
    }

    public static void Tick()
    {
        var now = Environment.TickCount64;
        for (var index = 0; index < slots.Length; index++)
        {
            ref var slot = ref slots[index];
            if (slot.RefreshDueAtMs == 0 || now < slot.RefreshDueAtMs)
            {
                continue;
            }

            slot.RefreshDueAtMs = 0;
            if (!CanRequest())
            {
                continue;
            }

            RequestTab(SharedFateCatalog.Entries[index].TabIndex, now, force: true);
            RequestAchievement(index, now, force: true);
        }
    }

    public static bool AllKnown(IReadOnlyList<ZoneInfo> zones)
    {
        for (var index = 0; index < zones.Count; index++)
        {
            var entryIndex = SharedFateCatalog.IndexOfTerritory(zones[index].TerritoryId);
            if (entryIndex >= 0 && slots[entryIndex].ReceivedAtMs == 0)
            {
                return false;
            }
        }

        return true;
    }

    public static bool AllMaxed(IReadOnlyList<ZoneInfo> zones)
    {
        var ranked = 0;
        for (var index = 0; index < zones.Count; index++)
        {
            if (!SharedFateCatalog.HasRanks(zones[index].TerritoryId))
            {
                continue;
            }

            ranked++;
            if (!IsMaxed(zones[index].TerritoryId))
            {
                return false;
            }
        }

        return ranked > 0;
    }

    public static int CountUnmaxed(IReadOnlyList<ZoneInfo> zones)
    {
        var count = 0;
        for (var index = 0; index < zones.Count; index++)
        {
            if (SharedFateCatalog.HasRanks(zones[index].TerritoryId) && !IsMaxed(zones[index].TerritoryId))
            {
                count++;
            }
        }

        return count;
    }

    public static (int Maxed, int Ranked) CountMaxed(IReadOnlyList<uint> territoryIds)
    {
        var maxed = 0;
        var ranked = 0;
        for (var index = 0; index < territoryIds.Count; index++)
        {
            if (!SharedFateCatalog.HasRanks(territoryIds[index]))
            {
                continue;
            }

            ranked++;
            if (IsMaxed(territoryIds[index]))
            {
                maxed++;
            }
        }

        return (maxed, ranked);
    }

    public static string Describe(ZoneInfo zone)
    {
        var index = SharedFateCatalog.IndexOfTerritory(zone.TerritoryId);
        if (index < 0)
        {
            return $"{zone.Name}: no ranks";
        }

        if (!TryGet(zone.TerritoryId, out var rank))
        {
            return $"{zone.Name}: unknown";
        }

        var unconfirmed = slots[index].LocalCompletions;
        var suffix = unconfirmed > 0 ? $" (+{unconfirmed} unconfirmed)" : string.Empty;
        return rank.IsMaxed
            ? $"{zone.Name}: rank {rank.Rank}/{rank.MaxRank} maxed{suffix}"
            : $"{zone.Name}: rank {rank.Rank}/{rank.MaxRank}, {rank.RankProgress}/{rank.RankSize} to next, {rank.Completed}/{rank.Total} total{suffix}";
    }

    public static string Describe(IReadOnlyList<ZoneInfo> zones)
    {
        var builder = new StringBuilder(zones.Count * 48);
        for (var index = 0; index < zones.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(Describe(zones[index]));
        }

        return builder.ToString();
    }

    private static SharedFateRank Resolve(int index)
    {
        ref var slot = ref slots[index];
        return SharedFateCatalog.Resolve(index, (int)slot.Current + slot.LocalCompletions, (int)slot.Max);
    }

    private static bool CanRequest() => receiveHook is not null && Svc.ClientState.IsLoggedIn && UIState.Instance() is not null;

    private static void RequestTab(byte tabIndex, long now, bool force)
    {
        if (!force && now - tabRequestedAtMs[tabIndex] < RequestCooldownMs)
        {
            return;
        }

        tabRequestedAtMs[tabIndex] = now;
        try
        {
            UIState.Instance()->Achievement.RequestFateProgressTab(tabIndex);
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, $"Shared FATE tab {tabIndex} request failed");
        }
    }

    private static void RequestAchievement(int index, long now, bool force)
    {
        ref var slot = ref slots[index];
        if (!force && now - slot.RequestedAtMs < RequestCooldownMs)
        {
            return;
        }

        slot.RequestedAtMs = now;
        try
        {
            GameMain.ExecuteCommand((int)clib.Enums.CommandFlag.RequestAchievement, (int)SharedFateCatalog.Entries[index].AchievementId, 0, 0, 0);
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, $"Shared FATE achievement {SharedFateCatalog.Entries[index].AchievementId} request failed");
        }
    }

    private static void ReceiveDetour(Achievement* thisPtr, uint id, uint current, uint max)
    {
        receiveHook!.Original(thisPtr, id, current, max);
        try
        {
            var index = SharedFateCatalog.IndexOfAchievement(id);
            if (index < 0)
            {
                return;
            }

            ref var slot = ref slots[index];
            slot.Current = current;
            slot.Max = max;
            slot.ReceivedAtMs = Environment.TickCount64;
            slot.LocalCompletions = 0;
            RunLog.Debug($"Shared FATE progress received for territory {SharedFateCatalog.Entries[index].TerritoryId}: {current}/{max}");
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "Shared FATE progress receive failed");
        }
    }

    private static void OnLogout(int type, int code) => Forget();

    private static void Forget()
    {
        Array.Clear(slots);
        Array.Clear(tabRequestedAtMs);
    }
}
