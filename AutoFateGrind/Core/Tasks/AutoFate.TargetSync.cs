using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System.Threading.Tasks;
using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Ipc;
using CSFateManager = FFXIVClientStructs.FFXIV.Client.Game.Fate.FateManager;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using DalamudStatusFlags = Dalamud.Game.ClientState.Objects.Enums.StatusFlags;

namespace AutoFateGrind.Core.Tasks;

// Fork: inside a FATE, a fight with a mob that is not part of any FATE is fought unsynced, and the sync comes
// back for the FATE's own mobs (BossMod never casts at a FATE mob while unsynced), Collect pickups and hand-ins.
// Unsync goes through the game's "/levelsync off"; sync through FateManager.LevelSync() as upstream does.
public sealed partial class AutoFate
{
    // A non-FATE fight has to last this long before the unsync, so a target that flickers past does not toggle it.
    private const int TargetUnsyncDelayMs = 1_500;
    // Between two non-FATE kills the target is briefly empty; still in combat, the sync waits this long for the next one.
    private const int TargetResyncGraceMs = 1_500;
    // A sync command gets this long to land before it is sent again.
    private const int TargetSyncRetryMs = 3_000;
    // Unsync requests the game ignored (e.g. refused in combat) before giving up on unsyncing in this FATE.
    private const int MaxTargetUnsyncAttempts = 3;
    private const int TargetSyncWaitMs = 5_000;

    // BossMod's FATE helper track; the bundled preset sets it to "Enable" (always sync), which undoes "/levelsync off"
    // within a second (seen 2026-10-03: unsync every 3 s, re-synced in between). "None" leaves the sync alone.
    private const string FateHelperSyncTrack = "Sync";
    private const string FateHelperSyncNoneOption = "None";

    private bool targetUnsynced;
    private bool targetUnsyncTook;
    private long targetUnsyncWantSinceMs;
    private long targetResyncWantSinceMs;
    private long targetSyncCommandAtMs;
    private int  targetUnsyncAttempts;
    private string? bossModSyncHeldPreset;

    private void ResetTargetSync()
    {
        ReleaseBossModSync();
        targetUnsynced = false;
        targetUnsyncTook = false;
        targetUnsyncWantSinceMs = 0;
        targetResyncWantSinceMs = 0;
        targetSyncCommandAtMs = 0;
        targetUnsyncAttempts = 0;
    }

    // Keeps BossMod's FATE helper from re-syncing while the non-FATE fight runs. False only when the override could
    // not be undone later; a preset without the FATE helper needs no override (nothing in BossMod re-syncs then).
    private bool HoldBossModSync()
    {
        if (bossModSyncHeldPreset is not null) return true;
        if (!BossModIPC.Instance.CanClearTransientStrategy) return false;
        var preset = Plugin.Cfg.CombatPresetName;
        if (BossModIPC.Instance.AddTransientStrategy(preset, BossModFateHelper.Module, FateHelperSyncTrack, FateHelperSyncNoneOption))
        {
            bossModSyncHeldPreset = preset;
        }
        return true;
    }

    private void ReleaseBossModSync()
    {
        if (bossModSyncHeldPreset is null) return;
        BossModIPC.Instance.ClearTransientStrategy(bossModSyncHeldPreset, BossModFateHelper.Module, FateHelperSyncTrack);
        bossModSyncHeldPreset = null;
    }

    /// <summary>Replaces the engage loop's per-tick SyncToFate.</summary>
    private void TickTargetSync(uint fateId)
    {
        var now = Environment.TickCount64;
        if (Plugin.Cfg.UnsyncForNonFateMobs && TryGetNonFateFoe(out var foe))
        {
            targetResyncWantSinceMs = 0;
            if (!IsSyncedTo(fateId))
            {
                targetUnsyncTook |= targetUnsynced;
                return;
            }
            // Synced again mid-fight after an unsync that took: something else re-synced. Counts as a failed try.
            if (targetUnsyncAttempts >= MaxTargetUnsyncAttempts)
            {
                return;
            }
            if (targetUnsyncWantSinceMs == 0)
            {
                targetUnsyncWantSinceMs = now;
            }
            if (now - targetUnsyncWantSinceMs < TargetUnsyncDelayMs || now - targetSyncCommandAtMs < TargetSyncRetryMs)
            {
                return;
            }

            if (!HoldBossModSync())
            {
                targetUnsyncAttempts = MaxTargetUnsyncAttempts;
                Diag($"Fighting non-FATE {foe.Name} inside FATE {fateId}; not unsyncing: BossMod's FATE-helper sync could not be paused (no transient strategies), it would re-sync at once");
                return;
            }

            targetUnsyncAttempts++;
            targetSyncCommandAtMs = now;
            Diag(targetUnsyncAttempts >= MaxTargetUnsyncAttempts
                ? $"Fighting non-FATE {foe.Name} inside FATE {fateId}; /levelsync off (last try {targetUnsyncAttempts}/{MaxTargetUnsyncAttempts}; {(targetUnsyncTook ? "something re-synced after the earlier ones" : "the earlier ones did not unsync")})"
                : $"Fighting non-FATE {foe.Name} inside FATE {fateId}; /levelsync off to kill it unsynced (try {targetUnsyncAttempts}/{MaxTargetUnsyncAttempts}{(targetUnsyncTook ? ", re-synced by something else since the last one" : "")})");
            targetUnsynced = true;
            Chat.ExecuteCommand("/levelsync off");
            return;
        }

        targetUnsyncWantSinceMs = 0;
        if (!targetUnsynced)
        {
            SyncToFate(fateId);
            return;
        }

        // A FATE mob or the end of the fight needs the sync now; an empty target mid-fight is likely the next non-FATE kill.
        if (Svc.Condition[ConditionFlag.InCombat] && !FateMobScanner.IsTargetingMobOf(fateId))
        {
            if (targetResyncWantSinceMs == 0)
            {
                targetResyncWantSinceMs = now;
            }
            if (now - targetResyncWantSinceMs < TargetResyncGraceMs)
            {
                return;
            }
        }
        RestoreFateSync(fateId);
    }

    private void RestoreFateSync(uint fateId)
    {
        var now = Environment.TickCount64;
        // BossMod's own FATE sync may help from here on.
        ReleaseBossModSync();
        if (IsSyncedTo(fateId))
        {
            Diag($"Synced to FATE {fateId} (non-FATE fight over)");
            EndNonFateFight();
            return;
        }
        if (!IsUnsyncedLevelAbove(fateId))
        {
            // Never synced in the first place (the unsync request did not take), or the level no longer needs it.
            EndNonFateFight();
            return;
        }
        if (now - targetSyncCommandAtMs < TargetSyncRetryMs)
        {
            return;
        }
        targetSyncCommandAtMs = now;
        SyncToFate(fateId);
    }

    // A fight whose unsync took starts the next one with fresh tries; one the game refused keeps its count for this FATE.
    private void EndNonFateFight()
    {
        if (targetUnsyncTook)
        {
            targetUnsyncAttempts = 0;
        }
        targetUnsynced = false;
        targetUnsyncTook = false;
        targetResyncWantSinceMs = 0;
    }

    /// <summary>Before a Collect wrap-up (leftover hand-in) that starts while the non-FATE unsync is still on.</summary>
    private async Task WaitForFateSync(uint fateId)
    {
        if (!targetUnsynced)
        {
            return;
        }
        var deadline = Environment.TickCount64 + TargetSyncWaitMs;
        while (targetUnsynced && Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested)
        {
            RestoreFateSync(fateId);
            await NextFrame(10);
        }
        if (targetUnsynced)
        {
            Diag($"Still not synced to FATE {fateId} {TargetSyncWaitMs / 1000}s after the non-FATE fight; handing in anyway");
            targetUnsynced = false;
        }
    }

    private static unsafe bool IsSyncedTo(uint fateId)
    {
        var mgr = CSFateManager.Instance();
        return mgr is not null && mgr->SyncedFateId == fateId;
    }

    private static unsafe bool IsUnsyncedLevelAbove(uint fateId)
    {
        var mgr = CSFateManager.Instance();
        return mgr is not null
            && mgr->CurrentFate is not null
            && mgr->CurrentFate->FateId == fateId
            && Svc.Objects.LocalPlayer is { } player
            && player.Level > mgr->CurrentFate->MaxLevel;
    }

    // The hard target is a live enemy that belongs to no FATE and is already fighting.
    private static unsafe bool TryGetNonFateFoe(out IBattleNpc foe)
    {
        foe = null!;
        if (!Svc.Condition[ConditionFlag.InCombat] || Svc.Condition[ConditionFlag.Mounted])
        {
            return false;
        }
        if (Svc.Targets.Target is not IBattleNpc npc || !npc.IsTargetable || npc.CurrentHp == 0)
        {
            return false;
        }
        if ((npc.StatusFlags & DalamudStatusFlags.InCombat) == 0)
        {
            return false;
        }
        var native = (CSGameObject*)npc.Address;
        if (native->FateId != 0 || native->BattleNpcSubKind != BattleNpcSubKind.Combatant)
        {
            return false;
        }
        foe = npc;
        return true;
    }
}
