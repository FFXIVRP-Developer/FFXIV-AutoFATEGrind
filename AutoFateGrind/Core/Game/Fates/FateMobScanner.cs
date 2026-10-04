using DalamudObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using DalamudStatusFlags = Dalamud.Game.ClientState.Objects.Enums.StatusFlags;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using System;
using System.Numerics;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace AutoFateGrind.Core.Game.Fates;

internal readonly record struct FateMobSurvey(
    int LiveCount,
    Vector3 NearestPosition,
    float NearestHitboxRadius,
    float NearestDistanceToHitbox,
    float NearestVerticalDelta)
{
    public bool Any => LiveCount > 0;

    public static readonly FateMobSurvey Empty = new(0, default, 0f, float.MaxValue, 0f);
}

internal readonly record struct FateMobTarget(
    ulong GameObjectId,
    Vector3 Position,
    float HitboxRadius,
    float DistanceToHitbox);

internal static unsafe class FateMobScanner
{
    public static FateMobSurvey Survey(uint fateId, Vector3 from)
    {
        var liveCount = 0;
        var nearestPosition = default(Vector3);
        var nearestHitbox = 0f;
        var nearestDistance = float.MaxValue;
        var nearestVerticalDelta = 0f;

        var objects = Svc.Objects;
        for (var objectIndex = 0; objectIndex < objects.Length; objectIndex++)
        {
            if (objects[objectIndex] is not IBattleNpc npc)
            {
                continue;
            }
            if (!IsLiveMobOfFate(npc, fateId))
            {
                continue;
            }

            liveCount++;
            var candidate = DistanceToHitbox(from, npc);
            if (candidate >= nearestDistance)
            {
                continue;
            }

            nearestDistance = candidate;
            nearestHitbox = npc.HitboxRadius;
            nearestPosition = npc.Position;
            nearestVerticalDelta = npc.Position.Y - from.Y;
        }

        return liveCount == 0
            ? FateMobSurvey.Empty
            : new FateMobSurvey(liveCount, nearestPosition, nearestHitbox, nearestDistance, nearestVerticalDelta);
    }

    /// <summary>Fork (item 21): FATE mobs a tank should pull, within reach and in sight: first those fighting nobody yet
    /// (nearest first), then "wrong pulls", mobs attacking a player who is not a tank (nearest first).</summary>
    public static List<(IBattleNpc Mob, bool WrongPull)> PullCandidates(uint fateId, Vector3 from, float maxDistanceToHitbox)
    {
        var me = Svc.Objects.LocalPlayer?.GameObjectId ?? 0;
        var fresh = new List<IBattleNpc>();
        var wrong = new List<IBattleNpc>();
        foreach (var obj in Svc.Objects)
        {
            if (obj is not IBattleNpc npc || !IsLiveMobOfFate(npc, fateId)) continue;
            if (DistanceToHitbox(from, npc) > maxDistanceToHitbox || !HasLineOfSight(from, npc.Position)) continue;
            if ((npc.StatusFlags & DalamudStatusFlags.InCombat) == 0) { fresh.Add(npc); continue; }
            if (npc.TargetObjectId == me || npc.TargetObject is not Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter victim) continue;
            if (victim.ClassJob.Value.Role != 1) wrong.Add(npc);
        }
        fresh.Sort((a, b) => DistanceToHitbox(from, a).CompareTo(DistanceToHitbox(from, b)));
        wrong.Sort((a, b) => DistanceToHitbox(from, a).CompareTo(DistanceToHitbox(from, b)));
        return [.. fresh.Select(m => (m, false)), .. wrong.Select(m => (m, true))];
    }

    public static bool TryGetTargetedMob(uint fateId, Vector3 from, out float distanceToHitbox)
    {
        var found = TryGetTarget(fateId, from, out var target);
        distanceToHitbox = found ? target.DistanceToHitbox : float.MaxValue;
        return found;
    }

    public static bool IsTargetingMobOf(uint fateId)
        => Svc.Targets.Target is IBattleNpc npc && IsLiveMobOfFate(npc, fateId);

    public static bool TryGetTarget(uint fateId, Vector3 from, out FateMobTarget target)
    {
        target = default;
        if (Svc.Targets.Target is not IBattleNpc npc)
        {
            return false;
        }
        if (!IsLiveMobOfFate(npc, fateId))
        {
            return false;
        }

        target = new FateMobTarget(npc.GameObjectId, npc.Position, npc.HitboxRadius, DistanceToHitbox(from, npc));
        return true;
    }

    // BossMod's AutoTarget only waits for a target the player has not pulled yet; one already in a fight stays.
    public static void DropUnpulledTarget(uint fateId)
    {
        if (Svc.Targets.Target is not IBattleNpc npc || !IsLiveMobOfFate(npc, fateId))
        {
            return;
        }
        if ((npc.StatusFlags & DalamudStatusFlags.InCombat) != 0)
        {
            return;
        }
        Svc.Targets.Target = null;
    }

    public static bool HasPickupWithin(uint fateId, Vector3 from, float maxMeters)
    {
        var maxSquared = maxMeters * maxMeters;
        var objects = Svc.Objects;
        for (var objectIndex = 0; objectIndex < objects.Length; objectIndex++)
        {
            var candidate = objects[objectIndex];
            if (!IsPickupOfFate(candidate, fateId))
            {
                continue;
            }
            if (Vector3.DistanceSquared(from, candidate!.Position) <= maxSquared)
            {
                return true;
            }
        }
        return false;
    }

    // Same pick as BossMod's FATE helper: the pickup nearest by flat distance to its hitbox.
    public static bool TryFindNearestPickup(uint fateId, Vector3 from, out IGameObject pickup, out float distanceToHitbox)
    {
        pickup = null!;
        distanceToHitbox = float.MaxValue;
        var objects = Svc.Objects;
        for (var objectIndex = 0; objectIndex < objects.Length; objectIndex++)
        {
            var candidate = objects[objectIndex];
            if (!IsPickupOfFate(candidate, fateId))
            {
                continue;
            }
            var distance = FlatDistanceToHitbox(from, candidate!);
            if (distance >= distanceToHitbox)
            {
                continue;
            }
            distanceToHitbox = distance;
            pickup = candidate!;
        }
        return distanceToHitbox < float.MaxValue;
    }

    public static bool TryGetPickup(uint fateId, ulong gameObjectId, out IGameObject pickup)
    {
        var candidate = Svc.Objects.SearchById(gameObjectId);
        pickup = candidate!;
        return IsPickupOfFate(candidate, fateId);
    }

    private static bool IsPickupOfFate(IGameObject? candidate, uint fateId)
        => candidate is not null
        && candidate.ObjectKind == DalamudObjectKind.EventObj
        && candidate.IsTargetable
        && ((CSGameObject*)candidate.Address)->FateId == fateId;

    private static float FlatDistanceToHitbox(Vector3 from, IGameObject target)
        => MathF.Max(0f, Vector2.Distance(new Vector2(from.X, from.Z), new Vector2(target.Position.X, target.Position.Z)) - target.HitboxRadius);

    private static bool IsLiveMobOfFate(IBattleNpc npc, uint fateId)
    {
        if (!npc.IsTargetable)
        {
            return false;
        }
        if (npc.CurrentHp == 0)
        {
            return false;
        }

        var native = (CSGameObject*)npc.Address;
        if (native->FateId != fateId)
        {
            return false;
        }
        return native->BattleNpcSubKind == BattleNpcSubKind.Combatant;
    }

    private static float DistanceToHitbox(Vector3 from, IBattleNpc npc)
        => MathF.Max(0f, Vector3.Distance(from, npc.Position) - npc.HitboxRadius);

    // Fork: positions are at the feet; the ray runs between chest heights so ground bumps don't count as walls.
    private const float SightHeightMeters = 2f;

    // Fork: true when no level collision lies between the two points; the game refuses casts ("Target not in line of sight") otherwise.
    public static bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        var lift = new Vector3(0f, SightHeightMeters, 0f);
        var origin = from + lift;
        var offset = to + lift - origin;
        var distance = offset.Length();
        if (distance < 0.01f)
        {
            return true;
        }
        return !BGCollisionModule.RaycastMaterialFilter(origin, offset / distance, out _, distance);
    }
}
