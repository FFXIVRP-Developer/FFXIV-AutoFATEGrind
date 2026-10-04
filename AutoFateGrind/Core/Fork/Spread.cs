using System.Globalization;
using System.Numerics;
using AutoFateGrind.Core.Game.Fates;
using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.DalamudServices;

namespace AutoFateGrind.Core.Fork;

// Fork (README-FORK item 20): characters spread out naturally in a FATE, like BOCCHI's critical encounter parking.
// Every character rolls its own values from its content id, so the leader and each slave keep the same personal
// side and distances (stable, not re-rolled every walk) while being different from each other:
//   1. stand angle   its own side of the FATE / mob: the arrival point, the walk to a mob, the walk to the centre.
//                    With other clients of this PC connected, the sides are spaced evenly (the group sorted by content id,
//                    360°/n apart; re-checked every 10 s); alone, its own random side.
//   2. crowd nudge   after arriving, someone within 6 m → a nearby spot with more room (BOCCHI's scoring)
//   3. dodge margin  BossMod's ForbiddenZoneCushion: Small or Medium per character (was none)
//   4. combat range  BossMod's StayCloseToTarget range: melee on the hitbox, ranged/healers 12-20 m per character
//   5. idle nudge    the crowd nudge also every 10 s out of combat (between FATEs, waiting for mobs)
//   6. fight step    a ranged job or healer with someone within 3 m steps to its own side of its target at its own
//                    distance, only while nothing is casting nearby and BossMod is not moving it (no dodge going on)
internal static class Spread
{
    private const float CrowdRadius = 6f;       // a nudge when another player stands this close
    private const float ScoreCrowdRadius = 8f;  // BOCCHI: -5 per player within 8 m
    private const float NearestCap = 25f;
    private const float CrowdPenalty = 5f;
    private const int NudgeSamples = 30;
    private const float NudgeMinStep = 4f, NudgeMaxStep = 12f;

    private static ulong seededFor;
    private static float angle;            // radians, this character's side
    private static string cushion = "Small";
    private static float rangedRange = 15f;
    private static readonly Random jitter = new();
    private static long groupCheckedMs;
    private static float? groupAngle;
    private static string groupKey = "";

    /// <summary>This character's place in the connected group (0-based), or -1 when alone.</summary>
    public static int GroupIndex { get { Side(); return groupIndexValue; } }
    private static int groupIndexValue = -1;

    /// <summary>This character's side: an even share of the circle within the connected group, else its own random side.</summary>
    private static float Side()
    {
        EnsureSeed();
        if (Environment.TickCount64 < groupCheckedMs) return groupAngle ?? angle;
        groupCheckedMs = Environment.TickCount64 + 10_000;
        var ids = Multibox.MultiboxLink.Clients()
            .Where(c => DateTime.UtcNow - c.UpdatedUtc <= Multibox.MultiboxLink.ClientFresh && c.ContentId != 0)
            .Select(c => c.ContentId).Distinct().OrderBy(id => id).ToList();
        var index = ids.IndexOf(seededFor);
        groupIndexValue = ids.Count < 2 ? -1 : index;
        if (index < 0 || ids.Count < 2) { groupAngle = null; groupKey = ""; return angle; }
        var key = string.Join(',', ids);
        if (key != groupKey)
        {
            // The group's turn of the circle comes from its lowest id: every client computes the same one.
            var turn = (ids[0] % 360UL) * MathF.PI / 180f;
            groupAngle = turn + index * MathF.Tau / ids.Count;
            groupKey = key;
            Svc.Log.Info($"[AFG] Spread: {index + 1} of {ids.Count} in the group; side {(groupAngle.Value * 180f / MathF.PI) % 360f:F0}° ({360f / ids.Count:F0}° apart)");
        }
        return groupAngle ?? angle;
    }

    private static void EnsureSeed()
    {
        var cid = ECommons.GameHelpers.Player.CID;
        if (cid == 0 || cid == seededFor) return;
        seededFor = cid;
        var rng = new Random(unchecked((int)(cid ^ (cid >> 32))));
        angle = rng.NextSingle() * MathF.Tau;
        cushion = rng.Next(2) == 0 ? "Small" : "Medium";
        rangedRange = MathF.Round(12f + rng.NextSingle() * 8f, 1);
        Svc.Log.Info($"[AFG] Spread: side {angle * 180f / MathF.PI:F0}°, dodge margin {cushion}, ranged distance {rangedRange:F1} m");
    }

    private static Vector3 Dir(float radians) => new(MathF.Cos(radians), 0f, MathF.Sin(radians));

    /// <summary>1. Arrival: a point on this character's side, 20-50 % of the radius out; null when off.</summary>
    public static Vector3? ArrivalPoint(Vector3 centre, float radius)
    {
        if (!Plugin.Cfg.SpreadStandAngle) return null;
        EnsureSeed();
        var a = Side() + (jitter.NextSingle() * 2f - 1f) * 0.45f; // ±25°
        var r = radius * (0.2f + jitter.NextSingle() * 0.3f);
        return centre + Dir(a) * r;
    }

    /// <summary>1. In the fight: this character's side of a point (a mob, the FATE centre), standoff metres out.</summary>
    public static Vector3 AroundPoint(Vector3 point, float standoff)
    {
        if (!Plugin.Cfg.SpreadStandAngle || standoff <= 0f) return point;
        EnsureSeed();
        var a = Side() + (jitter.NextSingle() * 2f - 1f) * 0.35f; // ±20°
        var p = point + Dir(a) * standoff;
        return FateGround.Project(p) ?? point;
    }

    /// <summary>2. A nearby spot with more room when another player stands within 6 m; null = stay.</summary>
    public static Vector3? CrowdNudge(Vector3 me, Vector3 fateCentre, float fateRadius)
    {
        if (!Plugin.Cfg.SpreadCrowdNudge) return null;
        var others = OtherPlayers();
        if (!others.Any(o => Vector3.Distance(o, me) < CrowdRadius)) return null;

        var current = Score(me, others);
        Vector3? best = null;
        var bestScore = current;
        for (var i = 0; i < NudgeSamples; i++)
        {
            var step = NudgeMinStep + jitter.NextSingle() * (NudgeMaxStep - NudgeMinStep);
            var candidate = me + Dir(jitter.NextSingle() * MathF.Tau) * step;
            if (FateGround.HorizontalDistance(candidate, fateCentre) > fateRadius * 0.85f) continue;
            if (FateGround.Project(candidate) is not { } onMesh || Vector3.Distance(onMesh, candidate) > 2f) continue;
            var score = Score(onMesh, others);
            if (score > bestScore) { bestScore = score; best = onMesh; }
        }
        return best;
    }

    private static float Score(Vector3 at, List<Vector3> others)
    {
        if (others.Count == 0) return NearestCap;
        var nearest = others.Min(o => Vector3.Distance(o, at));
        var crowd = others.Count(o => Vector3.Distance(o, at) < ScoreCrowdRadius);
        return MathF.Min(nearest, NearestCap) - CrowdPenalty * crowd;
    }

    private static List<Vector3> OtherPlayers()
    {
        var me = Svc.Objects.LocalPlayer;
        var list = new List<Vector3>();
        foreach (var obj in Svc.Objects)
        {
            if (obj is IPlayerCharacter pc && (me is null || pc.GameObjectId != me.GameObjectId)) list.Add(pc.Position);
        }
        return list;
    }

    /// <summary>The ranged distance this character holds (6: where its fight step goes).</summary>
    public static float RangedDistance() { EnsureSeed(); return rangedRange; }

    /// <summary>A crowd nudge with no FATE ring to stay in (between FATEs): anywhere within 40 m of here.</summary>
    public static Vector3? CrowdNudge(Vector3 me) => CrowdNudge(me, me, 40f / 0.85f);

    /// <summary>Another player stands within this many metres.</summary>
    public static bool SomeoneWithin(Vector3 me, float metres) => OtherPlayers().Any(o => Vector3.Distance(o, me) < metres);

    /// <summary>Fork (item 22): BossMod GoToPositional for a melee DPS (Rear or Flank by its place in the group), else null.</summary>
    public static string? MeleePositional(byte role)
    {
        if (!Plugin.Cfg.SpreadMeleePositional || role != 2) return null;
        EnsureSeed();
        var slot = GroupIndex >= 0 ? GroupIndex : (int)(seededFor % 2);
        return slot % 2 == 0 ? "Rear" : "Flank";
    }

    /// <summary>3. BossMod ForbiddenZoneCushion for this character, or null when off.</summary>
    public static string? DodgeMargin()
    {
        // Upstream's Combat movement (Settings → Humanizer, v2.18) rolls the same cushion per FATE: when it is on, it decides.
        if (!Plugin.Cfg.SpreadDodgeMargin || Plugin.Cfg.CombatMovementEnabled) return null;
        EnsureSeed();
        return cushion;
    }

    /// <summary>4. BossMod StayCloseToTarget range for this character's role, or null when off.</summary>
    public static string? CombatRange(byte role)
    {
        if (!Plugin.Cfg.SpreadCombatRange) return null;
        EnsureSeed();
        // ClassJob.Role: 1 tank, 2 melee, 3 ranged, 4 healer.
        return role is 1 or 2 ? "OnHitbox" : rangedRange.ToString(CultureInfo.InvariantCulture);
    }
}
