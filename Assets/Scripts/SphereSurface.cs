using UnityEngine;

/// Shared helpers for constraining positions/movement to a sphere's surface.
public static class SphereSurface
{
    public static Vector3 RandomPointOnSphere(Vector3 center, float radius)
    {
        return center + Random.onUnitSphere * radius;
    }

    public static Vector3 ProjectToSurface(Vector3 point, Vector3 center, float radius)
    {
        return center + (point - center).normalized * radius;
    }

    /// Surface (great-circle) distance between two points on the same sphere.
    public static float SurfaceDistance(Vector3 a, Vector3 b, Vector3 center, float radius)
    {
        Vector3 da = (a - center).normalized;
        Vector3 db = (b - center).normalized;
        float angle = Vector3.Angle(da, db) * Mathf.Deg2Rad;
        return angle * radius;
    }

    /// Moves a point along the sphere surface, tangent to its current normal, by worldDelta,
    /// then re-projects onto the sphere so it doesn't drift inward/outward.
    public static Vector3 MoveAlongSurface(Vector3 point, Vector3 tangentDirection, float distance, Vector3 center, float radius)
    {
        Vector3 moved = point + tangentDirection.normalized * distance;
        return ProjectToSurface(moved, center, radius);
    }

    /// Direction tangent to the sphere at 'from', pointing toward 'to' (projected, normalized).
    public static Vector3 TangentDirectionTo(Vector3 from, Vector3 to, Vector3 center)
    {
        Vector3 normal = (from - center).normalized;
        Vector3 toVec = to - from;
        Vector3 tangent = toVec - Vector3.Dot(toVec, normal) * normal;
        return tangent.normalized;
    }
}
