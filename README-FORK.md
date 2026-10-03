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
   (180 s), the break falls back to a city from the ticked list, so keep at least one ticked. The wander
   loop now **pauses before each hop** instead of after, and the pause range goes up to 999 minutes
   (double-click the field to type). A pause longer than the break means the character never moves
   during the break. No barracks: Lifestream cannot enter them (its `gc` stops at the Grand Company desk).

5. **Landing over water** (`AutoCommon.Landing.cs`). When no floor is found within 6 y under a flying
   character, upstream descended in place. Over water that freezes in the air, and the Yo-kai summon
   retried the same spot every 3 s forever (East Shroud, 2026-10-03, stuck at (-122,10,92)). Now it flies
   to the nearest walkable navmesh point within 60 y and lands there.

6. **Resume run after reload** (`AutoResume.cs`, no setting). Starting a run saves
   `ResumeRunPending = true` in the config. Stop, a soft stop or a run that ends on its own (goal met,
   nothing left) clears it. A plugin unload (reload, game exit, crash) does not. The next load waits
   until you are in game and settled for 15 s, then starts the run like `/afg start`. A paused run
   counts as going, so it restarts too.

See DRIFT.md for where each item hooks into upstream and how to recover from rebase conflicts.

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
