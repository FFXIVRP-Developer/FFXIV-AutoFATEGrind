using AutoFateGrind.Core.Ipc;
using System.Numerics;

namespace AutoFateGrind.Core.Game.Fates;

// A FATE's position is the origin of its ring volume, not a floor point: The Imperious in Kozama'uka anchors
// its ring 9.6m under the plaza it covers (issue #82). A point handed to vnav unprojected makes a flight dive
// into the floor and seeds BossMod's obstacle map with nothing.
internal static class FateGround
{
    private const float HalfExtentXZ = 5f;
    private const float NearHalfExtentY = 5f;
    private const float RingHalfExtentY = 20f;

    public static Vector3? Project(Vector3 point)
    {
        var navmesh = NavmeshIPC.Instance;
        return navmesh.NearestPointReachable(point, HalfExtentXZ, NearHalfExtentY)
            ?? navmesh.NearestPointReachable(point, HalfExtentXZ, RingHalfExtentY);
    }

    public static float HorizontalDistance(Vector3 from, Vector3 to)
        => Vector2.Distance(new Vector2(from.X, from.Z), new Vector2(to.X, to.Z));
}
