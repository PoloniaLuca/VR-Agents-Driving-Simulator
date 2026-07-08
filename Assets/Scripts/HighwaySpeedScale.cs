using UnityEngine;
using UnityEngine.Splines;

namespace DrivingSim
{
    /// <summary>
    /// Central authority for speed scaling between Unity physics and
    /// real-world km/h equivalent values.
    ///
    /// Problem it solves
    /// -----------------
    /// A Unity spline may not be at 1:1 real-world scale.
    /// If the spline is 500 m long but represents a 5 km highway, a car
    /// that physically moves at 2.22 m/s in Unity traverses the route in
    /// the same time as a real car doing 80 km/h on a real 5 km road.
    /// This component performs that conversion transparently.
    ///
    /// If your spline IS at 1:1 scale (5000 Unity units = 5 km),
    /// SpeedScale will equal 1.0 and all values pass through unchanged.
    ///
    /// Usage
    /// -----
    ///   Physics speed [m/s]  = RealKmhToPhysicsMs(displayKmh)
    ///   Display speed [km/h] = PhysicsMsToDisplayKmh(physicsMs)
    /// </summary>
    public class HighwaySpeedScale : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Singleton
        // ─────────────────────────────────────────────────────────────────────
        public static HighwaySpeedScale Instance { get; private set; }

        // ─────────────────────────────────────────────────────────────────────
        //  Inspector
        // ─────────────────────────────────────────────────────────────────────
        [Header("Highway Definition")]
        [Tooltip("The same SplineContainer used by HighwayBuilder.")]
        [SerializeField] private SplineContainer roadSpline;

        [Tooltip("The real-world equivalent length of this highway [km].\n" +
                 "e.g. 5 means the spline represents a 5 km highway.\n" +
                 "All km/h values in the simulation are relative to this distance.")]
        [SerializeField] private float realHighwayLengthKm = 5f;

        // ─────────────────────────────────────────────────────────────────────
        //  Public read-only properties
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Actual arc-length of the spline [m] in Unity units.</summary>
        public float SplineLength { get; private set; }

        /// <summary>
        /// Ratio of Unity metres to real-world metres.
        /// = SplineLength / (realHighwayLengthKm × 1000).
        /// If the spline is at 1:1 scale this equals 1.0.
        /// </summary>
        public float SpeedScale { get; private set; }

        /// <summary>The real-world highway length in km as set in the Inspector.</summary>
        public float RealHighwayLengthKm => realHighwayLengthKm;

        // ─────────────────────────────────────────────────────────────────────
        //  Lifecycle
        // ─────────────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (roadSpline == null)
            {
                Debug.LogError("[HighwaySpeedScale] roadSpline not assigned — using Scale=1.", this);
                SplineLength = realHighwayLengthKm * 1000f;
                SpeedScale   = 1f;
                return;
            }

            SplineLength = roadSpline.CalculateLength();
            float realLengthM = realHighwayLengthKm * 1000f;
            SpeedScale   = SplineLength / realLengthM;

            Debug.Log(
                $"[HighwaySpeedScale] Spline = {SplineLength:F0} m | " +
                $"Real = {realHighwayLengthKm} km | " +
                $"Scale = {SpeedScale:F4}\n" +
                $"  90 km/h display → {RealKmhToPhysicsMs(90f):F3} m/s physics\n" +
                $"  90 km/h display → {RealKmhToPhysicsMs(90f):F3} m/s physics\n" +
                $" 100 km/h display → {RealKmhToPhysicsMs(100f):F3} m/s physics");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Conversion API
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Converts a display km/h speed to the physics [m/s] value that produces
        /// an equivalent travel time on this spline vs a real-world highway.
        /// </summary>
        public float RealKmhToPhysicsMs(float displayKmh)
            => displayKmh / 3.6f * SpeedScale;

        /// <summary>
        /// Converts a physics [m/s] speed to the display km/h equivalent.
        /// This is what the dashboard should show.
        /// </summary>
        public float PhysicsMsToDisplayKmh(float physicsMs)
            => SpeedScale > 0f ? physicsMs * 3.6f / SpeedScale : 0f;
    }
}
