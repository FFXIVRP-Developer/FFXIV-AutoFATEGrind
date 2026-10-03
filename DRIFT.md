# Drift and conflict recovery

How to bring this fork back onto upstream (`origin/master`) when ForkWatch says it is behind, and how
to re-apply each change by hand when a rebase conflicts. README-FORK.md says *what* each item does;
this file says *where it hooks into upstream* and *what must stay true* after a merge.

The full diff against upstream is kept in `docs/fork-vs-upstream.patch` (regenerate it after every fork
commit, see the end). Every fork line in the code carries a `// Fork:` comment, so
`git grep -n "Fork:"` lists every hook point.

## Rebase procedure

```
git fetch origin
git rebase origin/master            # fork/vnav-engage onto upstream
# conflicts: resolve with the per-item notes below, then: git add <file>; git rebase --continue
git submodule update                # if upstream moved the ECommons pin, see "ECommons" below
dotnet build ... -p:OutDir=<scratch>  # compile check (README-FORK "Rebuild")
```

If a rebase gets messy, abort it (`git rebase --abort`), start a fresh branch from `origin/master`, and
re-apply the items one by one from this file. Every item is small and self-contained. The new files
(`AutoHumanize.Retreat.cs`, `AutoResume.cs`) can be copied as-is.

## Footprint per item

### 1-2. Line of sight in engage (README-FORK items 1, 2)

| File | Hook | Must stay true |
|---|---|---|
| `Core/Game/Fates/FateMobScanner.cs` | `using ...BGCollision;` + `HasLineOfSight()` + `SightHeightMeters` appended at the end of the class | Pure addition. Only conflicts if upstream adds its own LoS helper: then delete ours and call theirs. |
| `Core/Tasks/AutoFate.Engage.cs` `HasTargetInReach` | body uses `TryGetTarget` and adds `&& HasLineOfSight(...)` | Every caller of "target in reach" must also require sight. Upstream callers: the combat-stall bounce, `TickEngagementWatchdog`, `RepositionToFateMob.EngagedOrGone`. |
| same, `TryGetSightBlockedTarget` | new helper next to `HasTargetInReach` | Pure addition. |
| same, `TickEngagementWatchdog` | after `idle.MarkOutOfReach();`: compute `sightBlocked`, pass the stall time to `idle.Stalled(pos, ms)` and `sightBlocked` to `RepositionToFateMob` | If upstream rewrites the watchdog, re-insert "sight-blocked → shorter stall → walk to that target". |
| same, `RepositionToFateMob` | extra parameter `FateMobTarget? sightBlocked`; goal/hitbox/distance come from it when set; `closeIn` is true when set; separate Diag text | If upstream changes the tolerance maths, keep: blocked → walk to the target itself with the melee tolerance. |
| same, `EngageIdleTracker.Stalled` | takes `int stallMs` instead of reading `EngageIdleStallMs` | Callers pass `EngageIdleStallMs` for the normal case. |
| same, `DescribeEngageSituation` | appends `(no LoS)` to the target text | Cosmetic. Drop it if it conflicts. |
| `Core/Tasks/AutoFate.cs` | `EngageSightStallMs = 3_000` after `EngageIdleStallMs` | Pure addition. |

### 3. Progress resets repositions (README-FORK item 3)

| File | Hook | Must stay true |
|---|---|---|
| `Core/Tasks/AutoFate.Engage.cs` engage loop | `idle.ForgetRepositions();` inside `if (fate.Progress != lastProgress)` | Has to run wherever upstream notices progress. |
| same, `EngageIdleTracker` | `ForgetRepositions()` method | Pure addition. |

### 4. Humanizer break location (README-FORK item 4)

| File | Hook | Must stay true |
|---|---|---|
| `Core/Tasks/AutoHumanize.Retreat.cs` | **new file**: `HumanizerRetreat` enum, `RetreatLabels`, `ReachRetreat()` (Lifestream IPC `ExecuteCommand`, `IsBusy`, `Abort`) | Copy as-is. Only breaks if Lifestream renames its IPC or its `/li` keywords (`inn`, `apartment`, `home`, `fc`). |
| `Core/Tasks/AutoHumanize.cs` class line | `sealed class` → `sealed partial class` | Needed for the new file. |
| same, `Execute()` top | `var territory = await ReachRetreat() ?? cityTerritoryId;` then the cancel check and label swap; the teleport `if` compares with `territory` | Upstream teleports to `cityTerritoryId`. Keep that call as it is: the teleport only runs when no retreat was reached, so `territory == cityTerritoryId` there. |
| same, wander loop | territory check and `o.Move(territory, ...)` use `territory`; **the pause moved from after the walk to before it** | The point of the change: the first thing after arriving is the pause, so a 999-minute pause means no movement at all. If upstream restructures the loop, keep pause-first. |
| `Configuration.cs` | `HumanizerRetreat` property after `HumanizerWanderMaxMeters` | The saved JSON is shared with the store build. The store build ignores the extra key. |
| `Windows/Sections/Config/HumanizerSettings.cs` | `MaxPauseSec = 999 * 60` used as the pause range max (upstream: `60`); "Break location" row at the top of `DrawCitiesGroup` | The UI text is literal English, not localised, on purpose: no `L.cs` / `Localization/*.json` changes to conflict. |
| `Core/Tasks/AutoFate.Engage.cs` `QueueHandoffIfDue` | `HumanizerCities.Count > 0` → `AutoHumanize.HasBreakPlace(Plugin.Cfg)` | A retreat alone must be enough to queue a break. |
| `Core/Tasks/AutoFateController.Handoffs.cs` `ResumeGrindOrHumanize` | the `Count == 0` skip uses `!HasBreakPlace(cfg)`; the "no catalog city" skip only applies with `HumanizerRetreat.City`; `cityId` is `0` when no city is ticked | `0` means "no city fallback". `AutoHumanize.Execute` aborts the break (BreakTaken stays false) when the retreat fails and the city is `0`. Never teleport to territory 0. |
| `Windows/Sections/Config/HumanizerSettings.cs` | the "No cities selected" warning only shows with `HumanizerRetreat.City` | Cosmetic. |

The controller still picks a city (when any is ticked) and passes it in as the fallback for a failed retreat.

### 5. Landing over water (README-FORK item 5)

| File | Hook | Must stay true |
|---|---|---|
| `Core/Tasks/AutoCommon.Landing.cs` `LandAndDismount` | right after `FindLandingSpots(...)`: if nothing was found, add `NearestPointReachable(around, 60, 60)` as the only spot | A floorless spot must never lead to a descent in place. If upstream adds its own wider search, drop ours. |
| same, constants | `LandingFarSearchMeters = 60f` | Pure addition. |

### 6. Resume run after reload (README-FORK item 6)

| File | Hook | Must stay true |
|---|---|---|
| `Core/Tasks/AutoResume.cs` | **new file**: `Unloading` flag, `MarkStarted()`/`MarkEnded()`, framework-tick starter | Copy as-is. |
| `Configuration.cs` | `ResumeRunPending` after `AutoShowOnLogin` | Pure addition. No on/off setting by design: whether a run was going is the only switch. |
| `Core/Tasks/AutoFateController.cs` `RunAll` | `AutoResume.MarkStarted();` right after `session = s;` | Must run only once a run really starts (after every "Start aborted" return). |
| same, `Stop()` | `AutoResume.MarkEnded();` at the top | Every user stop goes through `Stop()`. |
| `Core/Tasks/AutoFateController.RunLifecycle.cs` `EndRun` | `AutoResume.MarkEnded();` after `FinalizeRun` | `EndRun` is the choke point for runs ending on their own. If upstream adds an end path that skips `EndRun`, hook it too. |
| `Plugin.cs` | ctor end: `autoResume = new AutoResume();` + field; `Dispose` first lines: `AutoResume.Unloading = true; autoResume.Dispose();`; `StartFromCommand` `private` → `internal` | **`Unloading` must be set before anything else in `Dispose`.** The grind task's completion callback fires *after* unload and calls `EndRun`, which would otherwise clear the memory on every reload (seen in the log: "FATE grind ended ... Run ends" after "Finished unloading"). |

### 7. Unsync for non-FATE mobs (README-FORK item 7)

| File | Hook | Must stay true |
|---|---|---|
| `Core/Tasks/AutoFate.TargetSync.cs` | **new file**: `TickTargetSync`, `RestoreFateSync`, `WaitForFateSync`, `ResetTargetSync`, `TryGetNonFateFoe` | Copy as-is. Uses upstream's `SyncToFate` for the sync back; breaks only if that is renamed. |
| `Core/Tasks/AutoFate.Engage.cs` `EngageCurrentFate` | `ResetTargetSync();` before the first `SyncToFate(fateId);` | State is per FATE. |
| same, engage loop | per-tick `SyncToFate(fateId);` → `TickTargetSync(fateId);` | **Nothing else in the loop may call `SyncToFate` every tick**, or it undoes the unsync. If upstream moves the per-tick sync, move `TickTargetSync` with it. |
| same, Collect block | `!targetUnsynced && (` around the hand-in / pickup-walk call | Items need the sync. Any new upstream item step in the loop gets the same guard. |
| same, Collect wrap-up | `await WaitForFateSync(fateId);` before `WrapUpCollectFate` (the `if` got braces) | Leftovers are handed in synced. |
| same, engage `finally` | `ReleaseBossModSync();` after `ReleaseCollectPullHold(preset);` | The FateUtils `Sync` override must never outlive the FATE. If upstream changes the bundled preset's FateUtils track or option names, update `FateHelperSyncTrack` / `FateHelperSyncNoneOption`. |
| `Configuration.cs` | `UnsyncForNonFateMobs` after `KeepTwistOfFate` | Pure addition; the store build ignores the key. |
| `Windows/Sections/Config/TravelSettings.cs` `DrawFatePlayGroup` | literal-English toggle row after "Keep Twist of Fate" | Cosmetic. No `L.cs` changes. |

### ECommons submodule

The local commit on `ECommons` branch `fork/excelpage-alias` adds one line
(`using ExcelPage = Lumina.Excel.ExcelPage;` in `ExcelServices/Sheets/QuestDialogueText.cs`).

- Upstream bumps the ECommons pin: `cd ECommons; git fetch; git checkout <new pin>`. Build. If the
  `ExcelPage` ambiguity error is back, `git cherry-pick fork/excelpage-alias`. If it builds clean,
  ECommons fixed it and the local commit is obsolete. Then commit the new pointer in the fork.
- If `git submodule update` complains that the local commit is unreachable, it lives only in this
  clone's `ECommons/.git`. Re-create it from the one-line description above.

## What "too far" looks like

Signs that re-applying beats rebasing:
- upstream split or renamed `AutoFate.Engage.cs` / `AutoHumanize.cs`
- `EngageIdleTracker` was replaced
- the humanizer stopped using a wander loop
- the controller no longer ends runs through `Stop()` / `EndRun()` (auto-resume hooks)
- `AutoCommon.Landing.cs` no longer has the `FindLandingSpots` ring

In that case, start from upstream and re-implement the "Must stay true" lines above in the new shape.
Each item is under ~40 lines of real code.

## Regenerate the patch

```
git diff origin/master -- . ":(exclude)*.md" ":(exclude)docs/**" > docs/fork-vs-upstream.patch
```

Commit it separately ("Regenerate fork-vs-upstream.patch"), as in the AutoDuty fork.
