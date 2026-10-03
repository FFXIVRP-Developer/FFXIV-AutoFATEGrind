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
