using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System.Threading.Tasks;
using AutoFateGrind.Core.Game.Fates;
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

    private bool targetUnsynced;
    private long targetUnsyncWantSinceMs;
    private long targetResyncWantSinceMs;
    private long targetSyncCommandAtMs;
    private int  targetUnsyncAttempts;

    private void ResetTargetSync()
    {
        targetUnsynced = false;
        targetUnsyncWantSinceMs = 0;
        targetResyncWantSinceMs = 0;
        targetSyncCommandAtMs = 0;
        targetUnsyncAttempts = 0;
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
                if (targetUnsynced)
                {
                    targetUnsyncAttempts = 0;
                }
                return;
            }
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

            targetUnsyncAttempts++;
            targetSyncCommandAtMs = now;
            targetUnsynced = true;
            Diag(targetUnsyncAttempts >= MaxTargetUnsyncAttempts
                ? $"Fighting non-FATE {foe.Name} inside FATE {fateId}; /levelsync off (last try {targetUnsyncAttempts}/{MaxTargetUnsyncAttempts}, the earlier ones did not unsync)"
                : $"Fighting non-FATE {foe.Name} inside FATE {fateId}; /levelsync off to kill it unsynced (try {targetUnsyncAttempts}/{MaxTargetUnsyncAttempts})");
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
        if (IsSyncedTo(fateId))
        {
            Diag($"Synced to FATE {fateId} (non-FATE fight over)");
            targetUnsynced = false;
            targetResyncWantSinceMs = 0;
            return;
        }
        if (!IsUnsyncedLevelAbove(fateId))
        {
            // Never synced in the first place (the unsync request did not take), or the level no longer needs it.
            targetUnsynced = false;
            targetResyncWantSinceMs = 0;
            return;
        }
        if (now - targetSyncCommandAtMs < TargetSyncRetryMs)
        {
            return;
        }
        targetSyncCommandAtMs = now;
        SyncToFate(fateId);
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
