using UnityEngine;

namespace ResearchSim
{
    /// <summary>
    /// Reference line used for lane-keeping measurements and route start/end
    /// detection. Waypoints should follow the intended driving path, not the
    /// roadside scenery.
    /// </summary>
    public sealed class CenterlinePath : MonoBehaviour
    {
        [Tooltip("Ordered waypoints describing the reference line. Enable Closed Loop only for looped routes.")]
        public Transform[] waypoints;

        public bool closedLoop = true;
        public bool drawGizmos = true;
        public float gizmoRadius = 0.6f;

        public int Count => waypoints == null ? 0 : waypoints.Length;

        public float GetDistanceFromCenterLine(Vector3 worldPosition)
        {
            return Mathf.Abs(GetSignedDistanceFromCenterLine(worldPosition));
        }

        public float GetSignedDistanceFromCenterLine(Vector3 worldPosition)
        {
            // Project the vehicle onto each waypoint segment and keep the
            // nearest projection. The sign tells whether the vehicle is left or
            // right of the path direction.
            if (waypoints == null || waypoints.Length < 2)
                return float.NaN;

            Vector3 closestPoint = Vector3.zero;
            Vector3 closestDirection = Vector3.forward;
            float closestSqrDistance = float.PositiveInfinity;

            int segmentCount = closedLoop ? waypoints.Length : waypoints.Length - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                Transform startTransform = waypoints[i];
                Transform endTransform = waypoints[(i + 1) % waypoints.Length];

                if (startTransform == null || endTransform == null)
                    continue;

                Vector3 start = Flatten(startTransform.position, worldPosition.y);
                Vector3 end = Flatten(endTransform.position, worldPosition.y);
                Vector3 segment = end - start;
                float segmentSqrMagnitude = segment.sqrMagnitude;

                if (segmentSqrMagnitude <= Mathf.Epsilon)
                    continue;

                Vector3 point = Flatten(worldPosition, worldPosition.y);
                float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / segmentSqrMagnitude);
                Vector3 projectedPoint = start + segment * t;
                float sqrDistance = (point - projectedPoint).sqrMagnitude;

                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    closestPoint = projectedPoint;
                    closestDirection = segment.normalized;
                }
            }

            if (float.IsPositiveInfinity(closestSqrDistance))
                return float.NaN;

            Vector3 lateralVector = Flatten(worldPosition, worldPosition.y) - closestPoint;
            float sign = Mathf.Sign(Vector3.Cross(closestDirection, lateralVector).y);
            if (Mathf.Approximately(sign, 0f))
                sign = 1f;

            return Mathf.Sqrt(closestSqrDistance) * sign;
        }

        private static Vector3 Flatten(Vector3 value, float y)
        {
            return new Vector3(value.x, y, value.z);
        }

        private void OnDrawGizmos()
        {
            // Editor-only visual aid. Disable Draw Gizmos in the Inspector for
            // participant-facing tests if Game view gizmos are visible.
            if (!drawGizmos || waypoints == null || waypoints.Length == 0)
                return;

            Gizmos.color = Color.cyan;
            for (int i = 0; i < waypoints.Length; i++)
            {
                Transform waypoint = waypoints[i];
                if (waypoint == null)
                    continue;

                Gizmos.DrawSphere(waypoint.position, gizmoRadius);

                bool shouldDrawSegment = closedLoop || i < waypoints.Length - 1;
                if (!shouldDrawSegment)
                    continue;

                Transform next = waypoints[(i + 1) % waypoints.Length];
                if (next != null)
                    Gizmos.DrawLine(waypoint.position, next.position);
            }
        }
    }
}
