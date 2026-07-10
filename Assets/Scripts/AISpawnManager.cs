using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace DrivingSim
{
    /// <summary>
    /// Spawns AI vehicles along a spline, distributes them across configurable
    /// lanes, and continuously recycles them (spawn at start, destroy at end).
    ///
    /// Setup:
    ///   1. Add this component to an empty "AISpawnManager" GameObject.
    ///   2. Assign roadSpline and at least one carPrefab.
    ///   3. Configure lanes and density in the Inspector.
    ///   4. Press Play – cars are placed immediately and keep spawning.
    /// </summary>
    public class AISpawnManager : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Inspector – Spline
        // ─────────────────────────────────────────────────────────────────────
        [Header("Spline")]
        [Tooltip("The SplineContainer that defines the road centre-line.")]
        [SerializeField] private SplineContainer roadSpline;

        [Tooltip("Index of the spline inside the SplineContainer (usually 0).")]
        [SerializeField] private int splineIndex = 0;

        // ─────────────────────────────────────────────────────────────────────
        //  Inspector – Prefabs
        // ─────────────────────────────────────────────────────────────────────
        [Header("Car Prefabs")]
        [Tooltip("One or more AI car prefabs. Each must have AICarInput + CarController.\n" +
                 "A random prefab is chosen each time a car is spawned.")]
        [SerializeField] private GameObject[] carPrefabs;

        // ─────────────────────────────────────────────────────────────────────
        //  Inspector – Lanes
        // ─────────────────────────────────────────────────────────────────────
        [Header("Lanes")]
        [Tooltip("Number of lanes to use (1–4).")]
        [SerializeField, Range(1, 4)] private int laneCount = 2;

        [Tooltip("Width of each lane [m]. Used to auto-compute lane offsets from the\n" +
                 "spline centre-line. e.g. 3.75 m gives a standard road lane.")]
        [SerializeField] private float laneWidth = 3.75f;

        [Tooltip("If true, lane offsets are computed automatically from laneCount and laneWidth.\n" +
                 "If false, the manualLaneOffsets array below is used instead.")]
        [SerializeField] private bool autoLaneOffsets = true;

        [Tooltip("Manual lane offsets [m] from spline centre-line.\n" +
                 "Only used when autoLaneOffsets = false.\n" +
                 "Positive = left of direction of travel, negative = right.")]
        [SerializeField] private float[] manualLaneOffsets = { -1.875f, 1.875f };

        // ─────────────────────────────────────────────────────────────────────
        //  Inspector – Density
        // ─────────────────────────────────────────────────────────────────────
        [Header("Density")]
        [Tooltip("Target number of cars per kilometre PER LANE during initial population.\n" +
                 "Higher = denser traffic. Effective range: 2–20.")]
        [SerializeField, Range(1f, 30f)] private float carsPerKmPerLane = 8f;

        [Tooltip("Absolute minimum bumper-to-bumper gap between cars on the same lane [m].\n" +
                 "Overrides density if the computed spacing would be smaller than this.")]
        [SerializeField] private float minGapMetres = 12f;

        [Tooltip("Random extra spacing added on top of the base spacing [m].\n" +
                 "Prevents all cars from being perfectly equidistant.")]
        [SerializeField] private float spacingJitter = 6f;

        // ─────────────────────────────────────────────────────────────────────
        //  Inspector – Spawn limits & timing
        // ─────────────────────────────────────────────────────────────────────
        [Header("Spawn Limits")]
        [Tooltip("Maximum number of AI cars alive at the same time (all lanes combined).")]
        [SerializeField] private int maxTotalCars = 40;

        [Tooltip("Seconds between spawn attempts at the start of each lane.")]
        [SerializeField] private float spawnInterval = 3f;

        [Tooltip("Distance [m] from the very start of the spline where cars are spawned.\n" +
                 "Increase this if the spline starts at the edge of the road and cars fall off.\n" +
                 "20-40 m is usually enough.")]
        [SerializeField] private float spawnOffsetMetres = 30f;

        [Tooltip("Radius [m] of the sphere overlap-check at the spawn gate.\n" +
                 "A new car is only spawned if no other vehicle is within this radius.")]
        [SerializeField] private float spawnClearanceRadius = 8f;

        // ─────────────────────────────────────────────────────────────────────
        //  Inspector – Per-car variety (randomised each spawn)
        // ─────────────────────────────────────────────────────────────────────
        [Header("Lane Speed Limits (right → left, display km/h)")]
        [Tooltip("Maximum speed per lane in display km/h, ordered right-to-left.\n" +
                 "Index 0 = rightmost lane (slowest), last index = leftmost (fastest).\n" +
                 "e.g. 2 lanes: [80, 100]  |  3 lanes: [80, 90, 100]\n" +
                 "If the array is shorter than laneCount, the last value is reused.")]
                 
        // [SerializeField] private float[] laneMaxSpeedsKmh = { 80f, 90f, 100f };
        [SerializeField] private float[] laneMaxSpeedsKmh = { 90f, 100f };

        [Header("Car Variety")]
        [Tooltip("Desired speed is randomised between (laneMax - speedVariance) and laneMax.\n" +
                 "e.g. laneMax=80, variance=10 → desired speed 70–80 km/h.")]
        [SerializeField] private float speedVarianceKmh = 10f;

        [Tooltip("Minimum motor torque applied to the CarController.")]
        [SerializeField] private float minMotorTorque = 1000f;

        [Tooltip("Maximum motor torque applied to the CarController.")]
        [SerializeField] private float maxMotorTorque = 2000f;

        // ─────────────────────────────────────────────────────────────────────
        //  Inspector – Debug
        // ─────────────────────────────────────────────────────────────────────
        [Header("Debug")]
        [SerializeField] private bool showGizmos = true;

        // ─────────────────────────────────────────────────────────────────────
        //  Runtime state
        // ─────────────────────────────────────────────────────────────────────
        private float[]          laneOffsets;
        private float            splineLength;
        private List<GameObject> activeCars  = new();
        private float            spawnTimer;
        private float[]          laneSpawnTimers;   // per-lane cooldown

        // ─────────────────────────────────────────────────────────────────────
        //  Unity lifecycle
        // ─────────────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (roadSpline == null)
            {
                Debug.LogError("[AISpawnManager] roadSpline is not assigned.", this);
                enabled = false;
                return;
            }

            if (carPrefabs == null || carPrefabs.Length == 0)
            {
                Debug.LogError("[AISpawnManager] No car prefabs assigned.", this);
                enabled = false;
                return;
            }

            splineLength = roadSpline.Splines[splineIndex].GetLength();
            BuildLaneOffsets();
        }

        private void Start()
        {
            laneSpawnTimers = new float[laneOffsets.Length];
            PopulateInitial();
            spawnTimer = spawnInterval;
        }

        private void Update()
        {
            // Destroy cars that have reached the end of the spline
            CleanupFinishedCars();

            // Tick per-lane cooldowns
            if (laneSpawnTimers != null)
                for (int i = 0; i < laneSpawnTimers.Length; i++)
                    if (laneSpawnTimers[i] > 0f)
                        laneSpawnTimers[i] -= Time.deltaTime;

            // Periodically try to spawn new cars at the beginning
            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f)
            {
                spawnTimer = spawnInterval;
                TrySpawnAtStart();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Lane offset computation
        // ─────────────────────────────────────────────────────────────────────

        private void BuildLaneOffsets()
        {
            if (!autoLaneOffsets
                && manualLaneOffsets != null
                && manualLaneOffsets.Length > 0)
            {
                laneOffsets = manualLaneOffsets;
                laneCount   = laneOffsets.Length;
                return;
            }

            laneOffsets = new float[laneCount];

            // Centre the lanes symmetrically around the spline centre-line.
            // e.g. 2 lanes at 3.75 m:  offsets = [-1.875, +1.875]
            // e.g. 3 lanes at 3.75 m:  offsets = [-3.75,  0,  +3.75]
            float totalSpan = (laneCount - 1) * laneWidth;
            for (int i = 0; i < laneCount; i++)
                laneOffsets[i] = -totalSpan * 0.5f + i * laneWidth;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Initial population
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Places cars along the full spline length at the start of the simulation.
        /// Each lane is populated independently with a random phase offset so
        /// cars in adjacent lanes are not perfectly aligned.
        /// </summary>
        private void PopulateInitial()
        {
            // Spacing in metres derived from density
            float baseSpacing = 1000f / Mathf.Max(carsPerKmPerLane, 0.01f);
            baseSpacing = Mathf.Max(baseSpacing, minGapMetres);

            int placed = 0;

            // Convert the metre offset to a normalised t value
            float spawnStartT = Mathf.Clamp01(spawnOffsetMetres / splineLength);

            for (int laneIdx = 0; laneIdx < laneOffsets.Length; laneIdx++)
            {
                float offset = laneOffsets[laneIdx];

                // Random phase starting from the spawn offset
                float t = spawnStartT + Random.Range(0f, baseSpacing / splineLength);

                while (t < 1f)
                {
                    if (activeCars.Count >= maxTotalCars) break;

                    SpawnCarAt(t, offset, laneIdx);
                    placed++;

                    float spacing = baseSpacing + Random.Range(0f, spacingJitter);
                    t += spacing / splineLength;
                }
            }

            Debug.Log($"[AISpawnManager] Initial population complete: {placed} cars on " +
                      $"{laneOffsets.Length} lanes (spline length {splineLength:F0} m).");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Continuous spawning
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called periodically. Picks a random lane order so every lane gets
        /// an equal chance to spawn. Each lane has its own cooldown timer so
        /// spawning one lane doesn't block the others.
        /// Uses a forward-only clearance cast so adjacent-lane cars don't
        /// prevent spawning (the old OverlapSphere was the cause of left-lane-only).
        /// </summary>
        private void TrySpawnAtStart()
        {
            if (laneOffsets == null || laneOffsets.Length == 0) return;

            float spawnT = Mathf.Clamp01(spawnOffsetMetres / splineLength);

            // Build a shuffled copy of lane indices
            int n = laneOffsets.Length;
            int[] order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            foreach (int laneIdx in order)
            {
                if (activeCars.Count >= maxTotalCars) return;
                if (laneSpawnTimers[laneIdx] > 0f) continue;

                float offset = laneOffsets[laneIdx];

                if (IsLaneClearForSpawn(spawnT, offset))
                {
                    SpawnCarAt(spawnT, offset, laneIdx);
                    laneSpawnTimers[laneIdx] = spawnInterval;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Spawn single car
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Spawns a car at position t on the spline, in the lane at laneOffsets[laneIndex].
        /// Speed is taken from laneMaxSpeedsKmh[laneIndex] (clamped to array bounds).
        /// </summary>
        private void SpawnCarAt(float t, float offset, int laneIndex)
        {
            // Pick a random prefab
            int        idx    = Random.Range(0, carPrefabs.Length);
            GameObject prefab = carPrefabs[idx];
            if (prefab == null) return;

            // Spawn position and orientation aligned to the spline tangent
            Vector3    pos = SplineLaneWorldPoint(t, offset);
            Quaternion rot = SplineWorldRotation(t);

            GameObject car = Instantiate(prefab, pos, rot);
            car.name = $"AICar_L{laneIndex}_{activeCars.Count}";

            // Configure AICarInput – must happen before Start() runs on the new GO
            AICarInput ai = car.GetComponent<AICarInput>();
            ai.myLaneIndex = laneIndex; 
            if (ai == null)
            {
                Debug.LogError($"[AISpawnManager] Prefab '{prefab.name}' has no AICarInput.", this);
                Destroy(car);
                return;
            }

            // Per-lane speed limits (display km/h – AICarInput scales via HighwaySpeedScale)
            float laneMax     = GetLaneMaxSpeed(laneIndex);
            float desiredSpeed = Mathf.Max(10f, laneMax - Random.Range(0f, speedVarianceKmh));
            float maxSpeed     = laneMax;

            ai.Configure(roadSpline, splineIndex, offset, desiredSpeed, maxSpeed, laneIndex); 

            // Randomise motor torque for variety
            CarController cc = car.GetComponent<CarController>();
            if (cc != null)
                cc.SetMaxMotorTorque(Random.Range(minMotorTorque, maxMotorTorque));

            activeCars.Add(car);
        }

        /// <summary>
        /// Returns the max speed [km/h] for a given lane index.
        /// Clamps to the last element if the index exceeds the array length.
        /// </summary>
        private float GetLaneMaxSpeed(int laneIndex)
        {
            if (laneMaxSpeedsKmh == null || laneMaxSpeedsKmh.Length == 0)
                return 100f;

            int clampedIdx = Mathf.Clamp(laneIndex, 0, laneMaxSpeedsKmh.Length - 1);
            return laneMaxSpeedsKmh[clampedIdx];
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Cleanup
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Iterates over all active cars and destroys any that have signalled
        /// they reached the end of the spline (AICarInput.ReachedEnd == true)
        /// or whose GameObject was destroyed externally (null check).
        /// </summary>
        private void CleanupFinishedCars()
        {
            for (int i = activeCars.Count - 1; i >= 0; i--)
            {
                GameObject car = activeCars[i];

                // Destroyed externally (e.g. collision)
                if (car == null)
                {
                    activeCars.RemoveAt(i);
                    continue;
                }

                AICarInput ai = car.GetComponent<AICarInput>();
                if (ai != null && ai.ReachedEnd)
                {
                    activeCars.RemoveAt(i);
                    Destroy(car);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Spline helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// World-space lane position at normalised spline parameter t.
        /// Same formula used in AICarInput.SplineLanePoint().
        /// </summary>
        private Vector3 SplineLaneWorldPoint(float t, float laneOff)
        {
            var     spline  = roadSpline.Splines[splineIndex];
            Vector3 localP  = spline.EvaluatePosition(t);
            Vector3 localTg = spline.EvaluateTangent(t);

            Vector3 worldP  = roadSpline.transform.TransformPoint(localP);
            Vector3 worldTg = roadSpline.transform.TransformDirection(localTg).normalized;
            Vector3 right   = Vector3.Cross(Vector3.up, worldTg).normalized;

            return worldP + right * laneOff;
        }

        /// <summary>
        /// World-space rotation aligned to the spline tangent at t.
        /// Used to orient spawned cars correctly along the road.
        /// </summary>
        private Quaternion SplineWorldRotation(float t)
        {
            var     spline = roadSpline.Splines[splineIndex];
            Vector3 tg     = roadSpline.transform.TransformDirection(
                                 spline.EvaluateTangent(t)).normalized;

            if (tg.sqrMagnitude < 0.001f) return Quaternion.identity;
            return Quaternion.LookRotation(tg, Vector3.up);
        }

        /// <summary>
        /// Returns true if no vehicle is within <see cref="spawnClearanceRadius"/>
        /// metres AHEAD along the spawn lane.
        ///
        /// Uses a forward SphereCast (not OverlapSphere) so cars in adjacent
        /// lanes don't block this lane's spawn point.
        /// The cast radius is half the lane width so it stays within one lane.
        /// </summary>
        private bool IsLaneClearForSpawn(float spawnT, float laneOffset)
        {
            Vector3 origin  = SplineLaneWorldPoint(spawnT, laneOffset) + Vector3.up * 0.3f;
            Vector3 forward = SplineWorldRotation(spawnT) * Vector3.forward;
            float   radius  = Mathf.Min(spawnClearanceRadius * 0.4f, laneWidth * 0.45f);

            RaycastHit[] hits = Physics.SphereCastAll(
                origin, radius, forward, spawnClearanceRadius,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);

            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                Rigidbody colRb = h.collider.attachedRigidbody;
                if (colRb == null) continue;

                if (colRb.GetComponent<AICarInput>()  != null) return false;
                if (colRb.GetComponent<UserCarInput>() != null) return false;
            }
            return true;
        }

        // Keep old name as alias so any external callers still compile
        private bool IsPositionClear(Vector3 pos)
            => IsLaneClearForSpawn(
                FindClosestT(pos),
                0f);   // approximate – prefer IsLaneClearForSpawn directly

        private float FindClosestT(Vector3 worldPos)
        {
            const int S = 32;
            float bestT = 0f, bestD = float.MaxValue;
            for (int i = 0; i <= S; i++)
            {
                float t = (float)i / S;
                float d = (SplineLaneWorldPoint(t, 0f) - worldPos).sqrMagnitude;
                if (d < bestD) { bestD = d; bestT = t; }
            }
            return bestT;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Current number of AI cars alive.</summary>
        public int ActiveCarCount => activeCars.Count;

        /// <summary>
        /// Changes the density at runtime and rebuilds the population.
        /// Destroys all current AI cars and respawns with the new settings.
        /// </summary>
        public void SetDensityAndRespawn(float newCarsPerKmPerLane)
        {
            carsPerKmPerLane = Mathf.Max(0.1f, newCarsPerKmPerLane);

            // Destroy all current cars
            foreach (var car in activeCars)
                if (car != null) Destroy(car);
            activeCars.Clear();

            PopulateInitial();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Gizmos
        // ─────────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGizmos || roadSpline == null) return;

            // Rebuild offsets in editor without Play mode
            if (laneOffsets == null) BuildLaneOffsets();

            foreach (float offset in laneOffsets)
            {
                // Spawn gate (green) – shown at the actual spawn t, not t=0
                float   spawnGizT = Application.isPlaying
                    ? Mathf.Clamp01(spawnOffsetMetres / splineLength)
                    : Mathf.Clamp01(spawnOffsetMetres /
                        Mathf.Max(roadSpline.Splines[splineIndex].GetLength(), 1f));
                Vector3 spawnPt = SplineLaneWorldPoint(spawnGizT, offset);
                Gizmos.color    = Color.green;
                Gizmos.DrawWireSphere(spawnPt, spawnClearanceRadius);
                UnityEditor.Handles.Label(spawnPt + Vector3.up * 2f,
                    $"SPAWN\noffset {offset:F2}m");

                // End gate (red)
                Vector3 endPt = SplineLaneWorldPoint(1f, offset);
                Gizmos.color  = Color.red;
                Gizmos.DrawWireSphere(endPt, 2f);
                UnityEditor.Handles.Label(endPt + Vector3.up * 2f, "END");

                // Lane line (faint blue dots every 50 m)
                Gizmos.color = new Color(0.3f, 0.5f, 1f, 0.4f);
                int steps = Mathf.Max(2, Mathf.RoundToInt(
                    roadSpline.Splines[splineIndex].GetLength() / 50f));
                Vector3 prev = SplineLaneWorldPoint(0f, offset);
                for (int i = 1; i <= steps; i++)
                {
                    float   t    = (float)i / steps;
                    Vector3 next = SplineLaneWorldPoint(t, offset);
                    Gizmos.DrawLine(prev, next);
                    prev = next;
                }
            }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying) return;
            GUILayout.BeginArea(new Rect(10, 10, 220, 50));
            GUILayout.Box($"AI Cars: {activeCars.Count} / {maxTotalCars}");
            GUILayout.EndArea();
        }
#endif
    }
}
