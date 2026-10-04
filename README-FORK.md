# Auto FATE Grind local fork

This folder is a local clone of [XeldarAlz/FFXIV-AutoFATEGrind](https://github.com/XeldarAlz/FFXIV-AutoFATEGrind)
with the additions below on the branch `fork/vnav-engage`. It is not published anywhere. ForkWatch
tracks it against upstream `master`; rebase onto upstream when ForkWatch reports it behind.

Every change in the code is marked with a `// Fork:` comment.

## What is different from upstream

1. **Line of sight counts toward "in reach"** (`FateMobScanner.HasLineOfSight`, `AutoFate.Engage.cs`).
   Upstream treated a targeted FATE mob as engaged as soon as it was in combat and within reach by
   distance. A mob behind a rock or ledge therefore silenced every engage watchdog (idle reposition,
   preset bounce, stall bail) while each cast failed with "Target not in line of sight", and the
   character stood facing it indefinitely. The check is a level-collision raycast
   (`BGCollisionModule.RaycastMaterialFilter`) between points 2 y above both sets of feet.
2. **Sight-blocked target is walked to with vnav after 3 s** (`EngageSightStallMs`). The walk goes to
   the target itself (not the nearest mob) with the melee approach tolerance and stops as soon as the
   target is in reach *and* in sight. BossMod keeps the rotation running; its movement is parked for
   the walk, as upstream already does for its idle reposition. The engage diagnostics line marks the
   target `(no LoS)` when the ray is blocked.
3. **FATE progress resets the reposition count.** Upstream abandoned and blacklisted a FATE after three
   repositions toward mobs it never reached, even while the FATE was progressing. An escort FATE
   ("What Have You Done for Mead Lately", 2026-10-03) was dropped at 87 % this way because its mobs
   spawn ahead of the moving escort.

4. **Humanizer break location** (`AutoHumanize.Retreat.cs`, Settings → Humanizer → "Break location").
   City is upstream's wander. Inn room, Apartment, Private house and Free Company house are reached with
   Lifestream (`/li inn`, `apartment`, `home`, `fc`). Lifestream enters a private/FC house only when its
   house registration has the enter mode "Enter house". If Lifestream is missing, fails or times out
   (180 s), the break falls back to a city from the ticked list. With no city ticked that break is
   skipped and retried after the next FATE. Upstream required a ticked city for any break; with a
   location other than City none is needed (`AutoHumanize.HasBreakPlace`). The wander
   loop now **pauses before each hop** instead of after, and the pause range goes up to 999 minutes
   (double-click the field to type). A pause longer than the break means the character never moves
   during the break. No barracks: Lifestream cannot enter them (its `gc` stops at the Grand Company desk).

5. **Landing over water** (`AutoCommon.Landing.cs`). When no floor is found within 6 y under a flying
   character, upstream descended in place. Over water that freezes in the air, and the Yo-kai summon
   retried the same spot every 3 s forever (East Shroud, 2026-10-03, stuck at (-122,10,92)). Now it flies
   to the nearest walkable navmesh point within 60 y and lands there.

6. *(Removed 2026-10-03.)* **Resume run after reload** restarted a run on the next plugin load. It fought
   BoatRunner, which starts and stops AFG through item 8's IPC, so it is gone: AFG never starts a run on
   its own now. BoatRunner resumes its own cycle after a reload instead. A stale `ResumeRunPending` key in
   the saved config is ignored.

7. **Unsync for non-FATE mobs** (`AutoFate.TargetSync.cs`, Settings → Travel → "Unsync for non-FATE
   mobs", on by default). Upstream re-synced every engage tick, so a world mob that aggroed inside a
   FATE ring was fought at the FATE's level. Now, when the hard target is a live combatant with no FATE
   id that is already fighting, and that has lasted 1.5 s, the plugin sends `/levelsync off`. The sync
   comes back through `FateManager.LevelSync()` (upstream's call) right away when a FATE mob is targeted
   or combat ends, and after 1.5 s of no target mid-fight (so chaining two world mobs doesn't flicker).
   - FATE mobs need the sync: BossMod never casts at a FATE mob while unsynced.
   - BossMod's FATE helper (`MiscAI.FateUtils`, track `Sync`) is set to `Enable` in the bundled preset
     and re-syncs within a second. While unsynced, AFG overrides that track to `None` (a transient
     strategy, like the chocobo override) and clears it when it syncs again or the FATE ends.
   - Collect FATEs: AFG's pickup walk and hand-in trip don't start while the unsync is on, and the
     leftover hand-in at 100 % waits up to 5 s for the sync first.
   - Never toggles while mounted. If the game ignores `/levelsync off` three times in one FATE (e.g.
     refused in combat), it stops trying for that FATE. Every switch is a `Diag` line in `dalamud.log`.
   - No other plugin does this (checked 2026-10-03: Pandora's Box, Automaton, TwistOfFayte, Moirai,
     AutoFateSync, FrenRider, Henchman, autofate only sync on arrival).

8. **IPC for other local plugins** (`Core/Ipc/AfgIpcProvider.cs`). Upstream has none. Used by the local
   boat orchestrator to run AFG while it waits for an ocean fishing voyage:
   `AutoFateGrind.IsRunning() → bool`, `AutoFateGrind.Start() → bool` (like `/afg start`, true when a run
   is going after), `AutoFateGrind.StopWhenSafe()` (like `/afg stop soft`), `AutoFateGrind.Stop()`,
   `AutoFateGrind.Phase() → string`.

9. **Fight free before a zone teleport** (`AutoFate.cs` `GoToZone`, `AutoFate.Movement.cs` `ClearBlockingCombat`).
   Upstream's zone teleport only waited for combat to drop while nothing fought back: 2026-10-03 the character stood
   6+ minutes in South Shroud, in combat, bound for Upper La Noscea, every try "combat/casting (combat)". Now
   `GoToZone` runs `ClearBlockingCombat` (rotation on, up to 30 s) before every attempt and after a blocked one, and
   `ClearBlockingCombat`'s "a FATE started on top of us, let the state machine take it" exit only applies in the run's
   own zone (off-zone the state machine is in WrongZone and never engages that FATE). The "still in combat" line now
   names the target and the FATE.

10. **Yo-kai minion per zone** (`YokaiProgress.cs` fork block, `BuiltInModes.cs` `YokaiMedalsMode`,
    `AutoFateController.PlanYokaiZones`, `AutoFate.Yokai.cs` `YokaiTargetChanged`). Upstream farmed one minion at a time
    and planned only its three zones, handing off and replanning when it reached the goal. Every ARR zone pays for two
    minions, so now the run plans every zone some unfinished minion drops in (sorted by territory id, which groups the
    regions) and, in each zone, farms a minion that still needs medals there: the one out, else the last target, else
    the first in the roster. The switch happens in place (`Yo-kai in <zone>: farming X instead of Y`); the existing
    companion check before each FATE summons it. A zone where no unfinished minion drops is "done"
    (`IsZoneDone`) and rotated past; the run ends when the roster is done, as before. The Yo-kai hand-off
    (`PendingYokaiAdvance`) no longer fires.

11. **Ring-chase time limit** (`AutoFate.Engage.cs` `TickRingChase`, `RingChaseMaxMs = 45 s`). Upstream's chase of a
    target outside BossMod's FATE ring could stand still for good: its "target within goal" and "position frozen" exits
    report "still chasing" without walking, BossMod's movement stays parked, and the engagement watchdog is skipped
    while chasing (2026-10-03, FATE 312 In the Sac, Killer Mantis 13 m off, the character stood with no log for
    minutes). Chasing (any target) for 45 s while the FATE's progress does not move is given up: the target is cleared
    and marked as given up, movement goes back to BossMod, and no chase starts for 30 s, so the normal watchdogs run.
    Progress resets the clock. (The first version timed each target; switching Pugils restarted it, FATE 239, and a
    target within reach but not being hit never counted.) The clock and cooldown reset with each new FATE.

12. **Leave the water to summon a Yo-kai** (`AutoFate.Yokai.cs` `LeaveWaterForSummon`, `InWater` in
    `AutoCommon.Teleport.cs` became `private protected`). No minion can be summoned while swimming or diving, and the
    minion wait only retried the summon: 2026-10-03 the character floated in Upper La Noscea for minutes, "Could not
    summon Manjimutt (4 attempts, swim)", parked. Before summoning, a character in the water now moves on foot (swims)
    toward the nearest reachable navmesh point, then the zone's nearest aetheryte (always on land), then the zone's
    central landing, each move stopping once out of the water and not mounted. Upper La Noscea's central landing is in
    Bronze Lake, which is how the character got there. A mounted move flew off, counted the air as "out of the water"
    and dropped back in on the summon's dismount, hence on foot. Worked 15:46 (Camp Bronze Lake, ~448 m).

13. **City districts without an aetheryte** (`ZoneAetherytes.cs` `ResolveGateway`, `HubOfShardIn`). Upstream's two-leg
    gateway (teleport to the hub, ride the aethernet in) only served open-world zones; its note said Limsa Upper Decks
    "already works", but AutoRepair's trip to the Maelstrom Mender (territory 128) failed after every FATE with
    "territory 128 has no aetheryte to teleport to; giving up" (2026-10-03, gear at 13 %, NPC-only repair). Town
    territories (TerritoryIntendedUse 0) now use the gateway too; inns stay out. When the territory names no hub, the
    main aetheryte of the aethernet one of its shards belongs to is used (Upper Decks → Limsa Lower Decks).

See DRIFT.md for where each item hooks into upstream and how to recover from rebase conflicts.

14. **`AutoFateGrind.IsBusy`** (`Core/Ipc/AfgIpcProvider.cs`): the local plugins' standard status call, true while a
    run goes, finishing the FATE after `StopWhenSafe` included. Every local fork and own plugin has
    `<InternalName>.IsBusy`; BoatRunner waits on it.

15. **`AutoFateGrind.GoalReached`** (`AutoFateController.LastRunGoalReached`, set in `FinalizeRun` from the
    session's `CompletedByStopCondition`, reset when a run starts; `AfgIpcProvider.cs`): true while AFG is stopped and
    its last run ended because the goal was met (every Yo-kai zone done, item goal met), not a stop or a fault. BoatRunner
    then hands the wait to its next filler instead of restarting AFG every 2 minutes (each restart would end again at once).

16. **Multibox follow** (`Core/Multibox/MultiboxLink.cs`, `AutoFate.Multibox.cs`, two hooks: `MultiboxTick()` at the
    start of `ComputeState`, `LeaderFate()` at the start of `PickFate`; Settings → Travel → "Multibox role"). A Leader (the
    default) writes its world, zone, FATE and position about once a second to
    `%LOCALAPPDATA%\AutoFateGrind-fork\leader.json` (every client of the same Windows user reads it; no network). A
    Follower, while that file is fresh (20 s) and on its own world, switches its zone to the leader's when it is one of
    its run's zones (normal travel takes it there), and picks the leader's FATE when it exists in its own instance and
    is still going; otherwise it picks as usual. A FATE it is in is never left for the leader's. XIVProfiles sets every
    slave to Follower (`overrides/XIVLauncher/pluginConfigs/AutoFateGrind.json`), so MAIN is the only leader.
    Settings → **Multibox** (`MultiboxSettings.cs`): this client's role, the leader (who, zone, instance, FATE), and every
    client of this PC with zone, instance, FATE and status: leading, following, or blocked and why (leader not seen,
    another world, a zone not in my plan, another instance), like AutoDuty's multibox list. Every client writes a status
    card every 2 s while logged in (`MultiboxPresence.cs`, `clients\<content id>.json`); 30 s old = disconnected.
    The leader file also carries the instance. A follower in another instance of the leader's zone joins it: out of a
    FATE and out of combat (`GrindState.InstanceHop`), it teleports to the zone's aetheryte (TerritoryType.Aetheryte; a
    teleport inside the zone lands next to it) and calls `Lifestream.ChangeInstance` (needs an aetheryte in reach), then
    waits for the instance. At most one try every 90 s; the Multibox tab shows "changing to instance N" or the wait.

17. **Resume the run after a reload** (`Core/Fork/ReloadResume.cs`, a hook at the start of `Plugin.Dispose` and one after
    the IPC provider in the constructor; Settings → General, on by default, saved as `ResumeAfterReload`). A run going
    when AFG unloads (a rebuild, a crash, a game restart) starts again on the next load, once logged in, out of a loading
    screen and out of a duty (retried for ten minutes), once. Not while BoatRunner's phase is a boat one (StoppingFillers,
    BeforeBoat, OnBoat, AfterBoat): BoatRunner starts its fillers again itself. Item 6 was removed for fighting BoatRunner;
    this one only resumes a run that was really going and stays out of the boat. Off: AFG never starts on its own.

18. **"Follow the leader" mode** (`FollowLeaderMode` in `BuiltInModes.cs`, `Core/Multibox/MultiboxFollowerWatch.cs`; the two
    zone-swap checks in `AutoFate.cs` skip it). A slave's mode on AFG's first page: it plans every FATE zone (so the
    leader's is always in the plan), never rotates zones itself, has no goal, and acts as a follower (item 16) whatever
    the role setting says. It starts when the leader starts (or is already going when it loads), and when the leader
    stops (its file stale for 30 s) it finishes its FATE and parks at the humanizer break location (Inn / Apartment /
    Private house / FC house via Lifestream; City = stays). A follower stopped by hand stays stopped until the leader's
    next start. The leader now publishes whenever its run is going, humanizer breaks included. XIVProfiles sets every
    slave to this mode. The role decides the mode (`Configuration.ActiveMode`): a Leader / Slave switch tops the first
    page (`Windows/Sections/MultiboxPanel.cs`); a slave sees only its panel (leader, status, parking spot) and always
    runs Follow the leader, which is not among the goal cards; a leader picks its goal as usual and gets the slave list.

19. **Leader and slaves in one party** (`Core/Multibox/MultiboxParty.cs`, AutoDuty's game calls). While its run is
    going, the leader invites every connected slave that is not in the party (one try per slave every 20 s, up to the 8
    cap; same world `InviteToParty`, other world `InviteToPartyContentId`). A slave accepts an invite from the leader
    on the game's own Yes/No (through `AddonMaster.SelectYesno`, no key presses) when it is not in a party with others;
    the run's auto-decline (`PartyInviteWatcher`) skips the leader's invite. Following does not wait for the party.

## Verified in game

| Item | Status (2026-10-03) |
|---|---|
| 1-2 Line of sight | Not yet observed. Look for `(no LoS)` / "out of line of sight" lines in `dalamud.log`. |
| 3 Progress resets repositions | Not yet observed. |
| 4 Break location | Not yet observed. Look for `Humanize retreat ...: arrived in territory` in `dalamud.log`. |
| 5 Landing over water | Works: found ground 39 y away instead of hovering (East Shroud). |
| 6 Resume run after reload | Removed. |
| 7 Unsync for non-FATE mobs | Unsync works; the first build was re-synced by BossMod's FATE helper within a second (fixed with the override). Not yet re-observed. Look for `/levelsync off` and `Synced to FATE ... (non-FATE fight over)` in `dalamud.log`; "last try" lines mean the game refused the unsync. |
| 8 IPC | Works: BoatRunner starts and soft-stops runs. |
| 13 City districts without an aetheryte | Not yet observed. Look for `reached ...; riding its aethernet into territory 128`. |
| 12 Leave the water to summon | Works: swam out of Bronze Lake toward Camp Bronze Lake, FATEs resumed (2026-10-03 15:46). |
| 11 Ring-chase time limit | Per-target version did not fire (target switching). Now progress-based; not yet observed. Look for `Chased outside the FATE ring`. |
| 10 Yo-kai minion per zone | Not yet observed. Look for `Yo-kai in <zone>: farming`. |
| 9 Fight free before a zone teleport | Not yet observed. Look for `In combat outside a FATE; enabling rotation to fight free` before `Off-zone ... teleporting`. |

## Using the humanizer rest

Settings → Humanizer:
1. Turn the humanizer on and set "FATEs between breaks" and "Break length".
2. "Break location" (under Cities): Inn room, Apartment, Private house or Free Company house. No city
   needs to be ticked. Ticked cities only serve as the fallback when Lifestream can't get there.
3. "Pause between hops": set both ends to 999 minutes (double-click a field to type). The character
   then stands still for the whole break.

For a private or FC house, register it in Lifestream with the enter mode "Enter house" first.

Not changed: BossMod Reborn still drives in-ring movement (it dodges AoEs; vnav would not).

## Submodule

`ECommons` points at upstream's pinned commit plus one local commit on `fork/excelpage-alias`: a
`using ExcelPage = Lumina.Excel.ExcelPage;` alias in `ExcelServices/Sheets/QuestDialogueText.cs`. The
Dalamud dev build of 2026-10-02 added `FFXIVClientStructs.FFXIV.Component.Excel.ExcelPage`, which made
the name ambiguous and broke the build (ECommons master had not fixed it yet). Drop the local commit
once upstream ECommons has the fix.

## Rebuild

Commit first (ForkWatch reads the commit stamped in the DLL), then:

```
$env:DALAMUD_HOME = '<Me>\XIVLauncher\addon\Hooks\dev\'
dotnet build AutoFateGrind/AutoFateGrind.csproj -c Release -p:DalamudLibPath=$env:DALAMUD_HOME
```

Output: `AutoFateGrind\bin\Release\AutoFateGrind.dll`. For a compile check without touching the loaded
build add `-p:OutDir=<scratch>`; the packager step then fails on the missing `bin\Release` DLL, which is
expected and harmless.

## Loading it

Add `AutoFateGrind\bin\Release\AutoFateGrind.dll` as a dev plugin location and disable the
repository-installed Auto FATE Grind (both share the internal name). Settings are shared with the
installed plugin (`pluginConfigs\AutoFateGrind.json`).

Currently loaded in the `Me` profile only (since 2026-10-03). Slaves still get the store build through
`SLAVE_PLUGINS`. Dalamud hot-reloads the dev plugin on every build into `bin\Release`. That stops a
running run; BoatRunner starts it again at its next waiting phase, or start it with `/afg start`.
