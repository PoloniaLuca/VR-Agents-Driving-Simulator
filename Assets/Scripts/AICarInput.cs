using UnityEngine;
using UnityEngine.Splines;

namespace DrivingSim
{
    /// <summary>
    /// AI input provider for DrivingSim.
    ///
    /// Longitudinal  →  Fritzsche (1994) psycho-physical car-following model.
    ///                  Brakes for any obstacle (vehicles + static objects).
    /// Lateral       →  Waypoint-based Pure-Pursuit.
    ///                  Waypoints are sampled automatically from the spline at
    ///                  a fixed arc-length interval; no manual placement needed.
    ///                  No lane-change logic.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public partial class AICarInput : MonoBehaviour, ICarInput
    {
        // ─────────────────────────────────────────────────────────────────────
        //  ICarInput
        // ─────────────────────────────────────────────────────────────────────
        public float Steering           { get; private set; }
        public float Throttle           { get; private set; }
        public float Brake              { get; private set; }
        public float Handbrake          { get; private set; }
        public bool  ToggleReversePressed => false;

        
        public int myLaneIndex;

        // ─────────────────────────────────────────────────────────────────────
        //  Inspector – Spline & waypoints
        // ─────────────────────────────────────────────────────────────────────
        [Header("Spline")]
        [Tooltip("SplineContainer that defines the road centre-line.")]
        [SerializeField] private SplineContainer roadSpline;

        [Tooltip("Index of the spline inside the SplineContainer (usually 0).")]
        [SerializeField] private int splineIndex = 0;

        [Tooltip("Lateral offset from the spline centre-line [m].\n" +
                 "Positive = left of direction of travel.\n" +
                 "Negative = right of direction of travel.\n" +
                 "e.g. left lane = +1.875, right lane = -1.875")]
        [SerializeField] private float laneOffset = -1.875f;

        [Tooltip("Arc-length interval [m] between auto-generated waypoints.\n" +
                 "Smaller = follows curves more precisely but costs marginally more.\n" +
                 "Good default: 15–25 m.")]
        [SerializeField] private float waypointSpacing = 5f;

        // ─────────────────────────────────────────────────────────────────────
        //  Inspector – Pure-Pursuit steering
        // ─────────────────────────────────────────────────────────────────────
        [Header("Pure-Pursuit Steering")]
        [Tooltip("Base look-ahead distance [m] at low/zero speed.\n" +
                 "Keep small (4-8 m) so the car reacts to curves in time.")]
        [SerializeField] private float lookAheadBase = 10f;

        [Tooltip("Maximum look-ahead distance [m] at high speed.\n" +
                 "Prevents over-steering on straights. 12-20 m.")]
        [SerializeField] private float maxLookAhead = 20f;

        [Tooltip("Speed-based look-ahead scaling [m per m/s].\n" +
                 "LookAhead = lookAheadBase + speed × this. 0.3-0.5.")]
        [SerializeField] private float speedLookAheadScale = 0.4f;

        [Tooltip("Must match CarController.maxSteeringAngle [degrees].\n" +
                 "Used to normalise the heading error into a [-1, 1] output.\n" +
                 "Wrong value = under- or over-steer. Default: 30°.")]
        [SerializeField] private float maxSteeringAngleDeg = 30f;

        [Tooltip("Multiplier on top of the geometric steering output.\n" +
                 "1.0 = geometrically correct. Increase (1.1-1.4) if the car still\n" +
                 "under-steers; decrease if it oscillates on straights.")]
        [SerializeField] private float steeringGain = 1.1f;

        [Tooltip("Maximum steering output magnitude [0,1].")]
        [SerializeField, Range(0.1f, 1f)] private float maxSteeringOutput = 1f;

        [Header("Curve Speed Control")]
        [Tooltip("Enable automatic slow-down before curves (speed limit computed from the curve radius).")]
        [SerializeField] private bool enableCurveSlowdown = true;

        [Tooltip("Maximum lateral acceleration [m/s²] the AI accepts in a curve (PHYSICS units).\n" +
                 "Curve speed = sqrt(maxLateralAccel x radius).\n" +
                 "LOWER = slower in curves (use if cars still slide/crash). 2.5-3.5 is typical;\n" +
                 "HIGHER = faster in curves (use if cars crawl).")]
        [SerializeField] private float maxLateralAccel = 3.0f;

        [Tooltip("Deceleration [m/s²] assumed when planning the slow-down BEFORE a curve.\n" +
                 "Lower = starts braking earlier and more gently. 2-3 m/s² is comfortable.")]
        [SerializeField] private float curvePlanningDecel = 2.5f;

        [Tooltip("Extra look-ahead [m] added on top of the braking distance.")]
        [SerializeField] private float curveLookAheadMargin = 30f;

        [Tooltip("Minimum / maximum distance [m] scanned ahead for curves.")]
        [SerializeField] private float curveLookAheadMin = 50f;
        [SerializeField] private float curveLookAheadMax = 250f;

        [Tooltip("Never slow below this speed [real km/h] because of a curve (avoids crawling/stalling).")]
        [SerializeField] private float minCurveSpeedKmh = 90f;

        [Tooltip("Brake pedal [0-1] applied per m/s of speed ABOVE the curve limit (closed loop).\n" +
                 "0.25 = 4 m/s over the limit -> full brake. Raise if cars still arrive too fast.")]
        [SerializeField] private float curveBrakeGain = 0.25f;

        [Tooltip("Overspeed [m/s] tolerated before the curve brake kicks in.")]
        [SerializeField] private float curveBrakeDeadband = 0.5f;

        // ─────────────────────────────────────────────────────────────────────
        //  Inspector – Speed profile
        // ─────────────────────────────────────────────────────────────────────
        [Header("Speed Profile")]
        [Tooltip("Target cruising speed [km/h] on a clear road.")]
        [SerializeField] private float desiredSpeedKmh = 100f;

        [Tooltip("Hard speed cap [km/h]. Vehicle never exceeds this.")]
        [SerializeField] private float maxSpeedKmh = 110f;

        [Tooltip("Throttle aggressiveness [0-1].")]
        [SerializeField, Range(0.1f, 1f)] private float throttleAggressiveness = 0.8f;

        [Tooltip("Brake aggressiveness [0-1].")]
        [SerializeField, Range(0.1f, 1f)] private float brakeAggressiveness = 1f;

        // ─────────────────────────────────────────────────────────────────────
        //  Inspector – Fritzsche car-following  (Olstam & Tapani 2004, §3.5)
        // ─────────────────────────────────────────────────────────────────────
        [Header("Fritzsche Car-Following Parameters")]
        [Tooltip("Desired time gap TD [s]. (Paper default: 1.8)")]
        [SerializeField] private float TD = 2.5f;

        [Tooltip("Risky time gap Tr [s]. Below this the driver brakes hard. (Paper: 0.5)")]
        [SerializeField] private float Tr = 0.8f;

        [Tooltip("Safe time gap Ts [s]. Min gap to accept positive accel. (Paper: 1.0)")]
        [SerializeField] private float Ts = 1.0f;

        [Tooltip("Braking distance parameter Δb_m [m/s²]. (Paper: 0.4)")]
        [SerializeField] private float deltaBm = 0.4f;

        [Tooltip("k_PTP: perception threshold constant for positive Δv. (Paper: 0.001)")]
        [SerializeField] private float kPTP = 0.001f;

        [Tooltip("k_PTN: perception threshold constant for negative Δv. (Paper: 0.002)")]
        [SerializeField] private float kPTN = 0.002f;

        [Tooltip("x_f [m]: offset in PTP/PTN threshold formula. (Paper: 0.5)")]
        [SerializeField] private float xf = 0.5f;

        [Tooltip("b_null [m/s²]: micro-accel in Following-I regime. (Paper: 0.2)")]
        [SerializeField] private float bNull = 0.2f;

        [Tooltip("a+ [m/s²]: normal free-driving acceleration. (Paper: 2.0)")]
        [SerializeField] private float aNormal = 2.0f;

        [Tooltip("a- [m/s²]: maximum deceleration in Danger regime.")]
        [SerializeField] private float aMaxBrake = 6.0f;

        [Tooltip("s_{n-1} [m]: effective vehicle length incl. standstill gap. (Paper: 6)")]
        [SerializeField] private float effectiveVehicleLength = 8.0f;

        // ─────────────────────────────────────────────────────────────────────
        //  Inspector – Obstacle detection
        // ─────────────────────────────────────────────────────────────────────
        [Header("Obstacle Detection")]
        [Tooltip("Maximum look-ahead distance [m] for the forward SphereCast.\n" +
                 "Covers both vehicles and static obstacles.")]
        [SerializeField] private float detectionRange = 60f;

        [Tooltip("Radius [m] of the forward SphereCast.\n" +
                 "Keep slightly narrower than the lane half-width (~1.0–1.4 m).")]
        [SerializeField] private float detectionRadius = 1.5f;

        [Tooltip("Layer mask for objects treated as obstacles (vehicles + statics).\n" +
                 "Exclude the road surface layer to avoid false positives.")]
        [SerializeField] private LayerMask obstacleMask = ~0;

        // ─────────────────────────────────────────────────────────────────────
        //  Private – runtime state
        // ─────────────────────────────────────────────────────────────────────
        private Rigidbody  rb;
        private Collider[] ownColliders;

        // Waypoints
        private Vector3[]  waypoints;       // world-space lane positions
        private int        waypointCount;
        private int        currentWPIndex;  // index of the next target waypoint
        private bool       reachedEnd;
        private float[]    wpCurvature;           // curvature [1/m] at each waypoint
        private float      lastCurveCeilingMs = float.MaxValue; // debug: current curve speed ceiling (physics m/s)

        // Fritzsche debug
        private FritzscheRegime currentRegime = FritzscheRegime.FreeDriving;

        private enum FritzscheRegime
        {
            FreeDriving, FollowingI, FollowingII, ClosingIn, Danger
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Awake / Start
        // ─────────────────────────────────────────────────────────────────────
        private void Awake()
        {
            rb           = GetComponent<Rigidbody>();
            ownColliders = GetComponentsInChildren<Collider>(includeInactive: true);
            // Spline init is deferred to Start so that Configure() can be
            // called between Instantiate() and the first Update frame.
        }

        private void Start()
        {
            if (roadSpline == null)
            {
                Debug.LogError($"[AICarInput] '{name}': roadSpline is not assigned.", this);
                enabled = false;
                return;
            }

            InitialiseSpline();
        }

        /// <summary>
        /// Builds the waypoint array and resets tracking state.
        /// Called from Start and from Configure() if the component is already running.
        /// </summary>
        private void InitialiseSpline()
        {
            BuildWaypoints();
            currentWPIndex = FindClosestWaypointAhead();
            reachedEnd     = false;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Runtime configuration (called by AISpawnManager after Instantiate)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Configures the AI vehicle at runtime. Call this immediately after
        /// Instantiate and before the first FixedUpdate frame (i.e. in the same
        /// frame as Instantiate – Start has not run yet).
        /// </summary>
        public void Configure(
            SplineContainer spline,
            int             splineIdx,
            float           offset,
            float           desiredSpeedKmhValue,
            float           maxSpeedKmhValue = 0f,
            int laneIdx = -1)
        {
            roadSpline       = spline;
            splineIndex      = splineIdx;
            laneOffset       = offset;
            desiredSpeedKmh  = desiredSpeedKmhValue;
            myLaneIndex      = laneIdx;
            
            if (maxSpeedKmhValue > 0f)
                maxSpeedKmh  = Mathf.Max(desiredSpeedKmhValue, maxSpeedKmhValue);

            // If Start() already ran (hot-reconfigure), reinitialise immediately.
            if (waypoints != null)
                InitialiseSpline();
        }

        /// <summary>True once the vehicle has passed the last waypoint.</summary>
        public bool ReachedEnd => reachedEnd;

        // ─────────────────────────────────────────────────────────────────────
        //  Waypoint generation
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Samples the spline at every <see cref="waypointSpacing"/> metres and
        /// offsets each point laterally to the assigned lane.
        /// Result is stored in <see cref="waypoints"/>.
        /// </summary>
        private void BuildWaypoints()
        {
            var   spline = roadSpline.Splines[splineIndex];
            float length = spline.GetLength();

            waypointSpacing = Mathf.Max(1f, waypointSpacing);
            waypointCount   = Mathf.Max(2, Mathf.FloorToInt(length / waypointSpacing) + 1);
            waypoints       = new Vector3[waypointCount];

            for (int i = 0; i < waypointCount; i++)
            {
                float t          = (float)i / (waypointCount - 1);
                waypoints[i]     = SplineLanePoint(t);
            }

            BuildCurvature();
        }

        /// <summary>
        /// Returns the world-space lane position at normalised spline parameter t.
        /// </summary>
        private Vector3 SplineLanePoint(float t)
        {
            var     spline  = roadSpline.Splines[splineIndex];
            Vector3 localP  = spline.EvaluatePosition(t);
            Vector3 localTg = spline.EvaluateTangent(t);

            Vector3 worldP  = roadSpline.transform.TransformPoint(localP);
            Vector3 worldTg = roadSpline.transform.TransformDirection(localTg).normalized;

            // right = Cross(up, forward) → points LEFT of direction of travel in Unity
            Vector3 right = Vector3.Cross(Vector3.up, worldTg).normalized;
            return worldP + right * laneOffset;
        }

        /// <summary>
        /// Finds the waypoint index closest to the vehicle's current position
        /// that is also ahead of the vehicle (dot product with forward > 0).
        /// Falls back to the absolute closest if none are ahead.
        /// </summary>
        private int FindClosestWaypointAhead()
        {
            int   bestIndex = 0;
            float bestDist  = float.MaxValue;

            for (int i = 0; i < waypointCount; i++)
            {
                Vector3 toWP = waypoints[i] - transform.position;
                if (Vector3.Dot(toWP, transform.forward) < 0f) continue; // behind us

                float d = toWP.sqrMagnitude;
                if (d < bestDist) { bestDist = d; bestIndex = i; }
            }

            return bestIndex;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  FixedUpdate
        // ─────────────────────────────────────────────────────────────────────
        private void FixedUpdate()
        {
            if (roadSpline == null || reachedEnd) return;

            float ownSpeedMs     = rb.linearVelocity.magnitude;
            // Convert display km/h to physics m/s.
            // HighwaySpeedScale adjusts for spline-vs-real-world length ratio.
            float baseDesiredSpeedMs = HighwaySpeedScale.Instance != null
                ? HighwaySpeedScale.Instance.RealKmhToPhysicsMs(desiredSpeedKmh)
                : desiredSpeedKmh / 3.6f;

            // 1. Advance waypoint index (before the curve scan so it uses the updated waypoint)
            AdvanceWaypoint();

            // Speed ceiling imposed by the curves ahead (physics m/s), including the braking distance needed
            float curveCeilingMs = ComputeCurveSpeedCeiling(ownSpeedMs);
            float desiredSpeedMs = Mathf.Min(baseDesiredSpeedMs, curveCeilingMs);
            
            // CALCOLO DINAMICO DELLA VISTA (circa 3.5 secondi di anticipo in base alla velocità)
            float dynamicRange = Mathf.Max(detectionRange, ownSpeedMs * 3.5f);

            // 2. Obstacle detection (vehicles + statics)
            bool  hasObstacle    = DetectObstacle(out float gap, out float obstacleSpeedMs);
            float deltaV         = hasObstacle ? (ownSpeedMs - obstacleSpeedMs) : 0f;


            // 3. Fritzsche longitudinal control
            float accel = ComputeFritzscheAcceleration(
                hasObstacle, gap, deltaV, ownSpeedMs, desiredSpeedMs, dynamicRange);

            // 4. Map accel → throttle / brake
            ApplyLongitudinalOutput(accel, ownSpeedMs);

            // 4b. Closed-loop brake if the car is faster than the curve allows (Fritzsche free-driving only brakes at ~0.8 m/s²)
            ApplyCurveBraking(ownSpeedMs, curveCeilingMs);

            // 5. Pure-Pursuit steering toward next waypoint
            ComputePurePursuitSteering(ownSpeedMs);

            Handbrake = 0f;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Waypoint tracking
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Advances <see cref="currentWPIndex"/> when the vehicle is close enough
        /// to the current target waypoint (uses XZ distance to ignore height).
        /// </summary>
        private void AdvanceWaypoint()
        {
            if (currentWPIndex >= waypointCount - 1)
            {
                reachedEnd = true;
                Throttle   = 0f;
                Brake      = 1f;
                Steering   = 0f;
                return;
            }

            Vector3 toWP   = waypoints[currentWPIndex] - transform.position;
            toWP.y         = 0f;   // ignore vertical offset
            float distXZ   = toWP.magnitude;

            // Acceptance radius: larger at high speed to avoid overshooting
            float acceptRadius = Mathf.Max(4f, rb.linearVelocity.magnitude * 0.4f);

            if (distXZ < acceptRadius)
                currentWPIndex++;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Pure-Pursuit steering
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Heading-error Pure-Pursuit controller.
        ///
        /// Instead of computing curvature (which gives tiny values needing large
        /// gain), we compute the heading error directly:
        ///
        ///   headingError = atan2(x_local, z_local)   [radians]
        ///   Steering     = clamp(headingError / maxSteerRad × gain, ±1)
        ///
        /// This maps naturally: 30° error on a 30° wheel → full lock.
        /// Requires maxSteeringAngleDeg to match CarController.maxSteeringAngle.
        /// </summary>
        private void ComputePurePursuitSteering(float speedMs)
        {
            // Speed-adaptive look-ahead distance
            float L = Mathf.Clamp(
                lookAheadBase + speedMs * speedLookAheadScale,
                lookAheadBase, maxLookAhead);

            Vector3 targetWorld = GetPointAheadOnPath(L);
            Vector3 localTarget = transform.InverseTransformPoint(targetWorld);

            // Heading error: angle from car's forward to the target direction
            // atan2(x, z) gives signed angle in radians; positive = turn right
            float headingError = Mathf.Atan2(localTarget.x,
                                              Mathf.Max(localTarget.z, 0.01f));

            // Normalise by the wheel's physical max angle
            float maxSteerRad  = maxSteeringAngleDeg * Mathf.Deg2Rad;
            float normalised   = headingError / Mathf.Max(maxSteerRad, 0.001f);

            Steering = Mathf.Clamp(normalised * steeringGain,
                                   -maxSteeringOutput, maxSteeringOutput);
        }

        /// <summary>
        /// Returns a world-space point approximately <paramref name="distance"/>
        /// metres ahead along the waypoint path, starting from currentWPIndex.
        /// Interpolates along segments so the target moves smoothly rather than
        /// snapping between distant waypoints.
        /// </summary>
        private Vector3 GetPointAheadOnPath(float distance)
        {
            int   idx       = currentWPIndex;
            float remaining = distance;

            // Start from the current waypoint position
            Vector3 prev = waypoints[idx];

            while (idx < waypointCount - 1)
            {
                Vector3 next    = waypoints[idx + 1];
                float   segLen  = Vector3.Distance(prev, next);

                if (segLen >= remaining)
                {
                    float frac = remaining / Mathf.Max(segLen, 0.001f);
                    return Vector3.Lerp(prev, next, frac);
                }

                remaining -= segLen;
                prev       = next;
                idx++;
            }

            // Ran out of path → return last waypoint
            return waypoints[waypointCount - 1];
        }

        /// <summary>
        /// Computes the path curvature [1/m] at every waypoint from the turning angle
        /// between consecutive segments divided by their real length (so it does NOT
        /// depend on waypointSpacing). The max over 3 neighbours is kept (conservative).
        /// </summary>
        private void BuildCurvature()
        {
            wpCurvature = new float[waypointCount];
            if (waypointCount < 3) return;

            float[] raw = new float[waypointCount];
            for (int i = 1; i < waypointCount - 1; i++)
            {
                Vector3 a = waypoints[i]     - waypoints[i - 1]; a.y = 0f;
                Vector3 b = waypoints[i + 1] - waypoints[i];     b.y = 0f;
                float la = a.magnitude, lb = b.magnitude;
                if (la < 0.01f || lb < 0.01f) continue;

                float angleRad = Vector3.Angle(a, b) * Mathf.Deg2Rad;
                raw[i] = angleRad / (0.5f * (la + lb));
            }

            for (int i = 0; i < waypointCount; i++)
            {
                float k = raw[i];
                if (i > 0)                 k = Mathf.Max(k, raw[i - 1]);
                if (i < waypointCount - 1) k = Mathf.Max(k, raw[i + 1]);
                wpCurvature[i] = k;
            }
        }

        /// <summary>Maximum cornering speed [physics m/s] at waypoint i.</summary>
        private float CurveSpeedAt(int i)
        {
            float k = wpCurvature[i];
            if (k < 1e-5f) return 1000f; // straight
            return Mathf.Sqrt(Mathf.Max(0.1f, maxLateralAccel) / k);
        }

        /// <summary>
        /// Speed ceiling [physics m/s] for the current position: for every curve within the
        /// look-ahead, the speed we may still have NOW so that we can brake (at curvePlanningDecel)
        /// down to that curve's speed by the time we reach it:  v_allowed = sqrt(v_curve² + 2·a·s).
        /// </summary>
        private float ComputeCurveSpeedCeiling(float ownSpeedMs)
        {
            lastCurveCeilingMs = float.MaxValue;
            if (!enableCurveSlowdown || wpCurvature == null || waypointCount < 3)
                return float.MaxValue;

            float decel     = Mathf.Max(0.5f, curvePlanningDecel);
            float lookAhead = Mathf.Clamp(
                ownSpeedMs * ownSpeedMs / (2f * decel) + curveLookAheadMargin,
                curveLookAheadMin, curveLookAheadMax);

            float floorMs = HighwaySpeedScale.Instance != null
                ? HighwaySpeedScale.Instance.RealKmhToPhysicsMs(minCurveSpeedKmh)
                : minCurveSpeedKmh / 3.6f;

            float ceiling = float.MaxValue;
            float s = 0f;
            int   cur = currentWPIndex;

            // Start from the waypoint just passed (the curve we are in right now), s = 0
            for (int i = Mathf.Max(0, cur - 1); i < waypointCount - 1; i++)
            {
                if (i == cur)
                {
                    Vector3 d = waypoints[i] - transform.position; d.y = 0f;
                    s = d.magnitude;
                }
                else if (i > cur)
                {
                    Vector3 d = waypoints[i] - waypoints[i - 1]; d.y = 0f;
                    s += d.magnitude;
                }

                if (s > lookAhead) break;

                float vCurve   = Mathf.Max(CurveSpeedAt(i), floorMs);
                float vAllowed = Mathf.Sqrt(vCurve * vCurve + 2f * decel * s);
                if (vAllowed < ceiling) ceiling = vAllowed;
            }

            lastCurveCeilingMs = ceiling;
            return ceiling;
        }

        /// <summary>
        /// Closed-loop brake: if the car is faster than the curve ceiling, brake in proportion to the excess
        /// and cut the throttle. Works whatever the real deceleration of the brake pedal is.
        /// </summary>
        private void ApplyCurveBraking(float ownSpeedMs, float curveCeilingMs)
        {
            if (!enableCurveSlowdown) return;

            float overspeed = ownSpeedMs - curveCeilingMs;
            if (overspeed <= curveBrakeDeadband) return;

            Brake    = Mathf.Max(Brake, Mathf.Clamp01(overspeed * curveBrakeGain));
            Throttle = 0f;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Obstacle detection  (vehicles + static objects, ignores own colliders)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// SphereCastAll forward, skip own colliders, return the nearest hit.
        /// Static obstacles return obstacleSpeedMs = 0.
        /// </summary>
        private bool DetectObstacle(out float gap, out float obstacleSpeedMs)
        {
            gap = float.MaxValue;
            obstacleSpeedMs = 0f;

            bool found = false;
            Rigidbody nearestRb = null;

            // Partiamo dal muso dell'auto
            Vector3 origin = transform.position + transform.up * 0.3f + transform.forward * 1.0f;
            Vector3 prevPoint = origin;
            
            // Spezzettiamo la visione in segmenti da 10m per "curvare" con la strada
            float step = 10f; 

            // CALCOLO DINAMICO DEL RANGE: Più l'auto va veloce, più guarda lontano (minimo detectionRange, massimo 4 secondi di previsione)
            float currentSpeed = rb != null ? rb.linearVelocity.magnitude : 0f;
            float dynamicRange = Mathf.Max(detectionRange, currentSpeed * 8.0f);

            for (float d = step; d <= dynamicRange; d += step)
            {
                // Troviamo il punto sulla nostra corsia a distanza 'd'
                Vector3 nextPoint = GetPointAheadOnPath(d) + transform.up * 0.3f;
                Vector3 dir = nextPoint - prevPoint;
                float segmentLen = dir.magnitude;

                if (segmentLen < 0.1f) break;

                // Lanciamo il raggio per questo singolo segmento curvo
                RaycastHit[] hits = Physics.SphereCastAll(prevPoint, detectionRadius, dir.normalized, segmentLen, obstacleMask, QueryTriggerInteraction.Ignore);
                
                foreach (var h in hits)
                {
                    if (IsOwnCollider(h.collider)) continue;

                    // IL SEGRETO: Poiché il raggio "curva" seguendo esattamente la nostra corsia,
                    // chiunque entri in questo tubo è FISICAMENTE nella nostra traiettoria.
                    // Nessun filtro matematico necessario: se tocca il raggio (es. raggio 1.5m), è un ostacolo.
                    
                    // Gestione di sicurezza se gli oggetti sono già compenetrati
                    Vector3 contactPoint = h.distance == 0f ? h.collider.ClosestPoint(origin) : h.point;
                    float distToCar = Vector3.Distance(origin, contactPoint);

                    if (distToCar < gap)
                    {
                        gap = distToCar;
                        nearestRb = h.collider.attachedRigidbody;
                        found = true;
                    }
                }

                // Se abbiamo trovato l'ostacolo più vicino in questo segmento, ci fermiamo
                if (found) break;

                prevPoint = nextPoint;
            }

            if (!found) return false;
            
            gap = Mathf.Max(0f, gap);
            if (nearestRb != null) obstacleSpeedMs = Vector3.Dot(nearestRb.linearVelocity, transform.forward);
            return true;
        }

        /// <summary>
        /// Calcola la distanza laterale esatta di un punto nel mondo rispetto alla Spline stradale.
        /// Restituisce un valore compatibile con 'laneOffset'.
        /// </summary>
        // private float GetDynamicLateralOffset(Vector3 worldPos)
        // {
        //     var spline = roadSpline.Splines[splineIndex];
            
        //     // Convertiamo la posizione del mondo nello spazio locale della SplineContainer
        //     Vector3 localPos = roadSpline.transform.InverseTransformPoint(worldPos);

        //     // Troviamo il punto più vicino sulla curva della spline
        //     UnityEngine.Splines.SplineUtility.GetNearestPoint(spline, localPos, out Unity.Mathematics.float3 nearestPos, out float t);

        //     // Calcoliamo il vettore 'Destra' locale (la stessa matematica usata in SplineLanePoint)
        //     Vector3 localTangent = spline.EvaluateTangent(t);
        //     Vector3 localRight = Vector3.Cross(Vector3.up, localTangent).normalized;

        //     // Vettore dal centro della strada verso l'oggetto
        //     Vector3 centerToObj = localPos - (Vector3)nearestPos;

        //     // Il Prodotto Scalare (Dot) ci dà la distanza con il segno corretto (+ a sinistra, - a destra)
        //     float offset = Vector3.Dot(centerToObj, localRight);
            
        //     return offset;
        // }

        private bool IsOwnCollider(Collider col)
        {
            if (ownColliders == null) return false;
            foreach (var c in ownColliders)
                if (c == col) return true;
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Fritzsche (1994) longitudinal model  (Olstam & Tapani 2004, §3.5)
        // ─────────────────────────────────────────────────────────────────────
        private float ComputeFritzscheAcceleration(
            bool  hasObstacle,
            float deltaX,
            float deltaV,
            float ownSpeedMs,
            float desiredSpeedMs,
            float dynamicRange)
        {
            // Distance thresholds
            float AD = effectiveVehicleLength + TD * ownSpeedMs;
            float AR = effectiveVehicleLength + Tr * ownSpeedMs;
            float AS = effectiveVehicleLength + Ts * ownSpeedMs;
            float AB = AR + (deltaV > 0f
                ? (deltaV * deltaV) / (2f * Mathf.Max(deltaBm, 0.01f))
                : 0f);

            // Speed-difference perception thresholds
            float distFactor = hasObstacle
                ? Mathf.Max(0f, deltaX - effectiveVehicleLength)
                : dynamicRange;
            float PTN = -(kPTN * Mathf.Pow(Mathf.Max(0f, distFactor - xf), 2f));
            float PTP =  (kPTP * Mathf.Pow(distFactor + xf, 2f));

            // Regime classification
            if (!hasObstacle || deltaX > dynamicRange * 0.95f)
            {
                currentRegime = FritzscheRegime.FreeDriving;
            }
            else if (deltaX <= AR)
            {
                currentRegime = FritzscheRegime.Danger;
            }
            else if (deltaV > PTN && (deltaX <= AB || deltaX <= AD))
            {
                currentRegime = FritzscheRegime.ClosingIn;
            }
            else if ((deltaV >= PTN && deltaV <= PTP && deltaX >= AR && deltaX <= AD)
                  || (deltaV >  PTP && deltaX >= AR  && deltaX <= AS))
            {
                currentRegime = FritzscheRegime.FollowingI;
            }
            else if (deltaV > PTN && deltaX > Mathf.Max(AB, AD))
            {
                currentRegime = FritzscheRegime.FollowingII;
            }
            else
            {
                currentRegime = FritzscheRegime.FreeDriving;
            }

            float accel;
            switch (currentRegime)
            {
                case FritzscheRegime.Danger:
                    accel = -aMaxBrake;
                    break;

                case FritzscheRegime.ClosingIn:
                    float dc = Mathf.Max(0.1f,
                        deltaX - AR + deltaV * Time.fixedDeltaTime);
                    accel = -(deltaV * deltaV) / (2f * dc);
                    accel = Mathf.Max(accel, -aMaxBrake);
                    break;

                case FritzscheRegime.FollowingI:
                    accel = (deltaX - AD) > 0f ? bNull : -bNull;
                    break;

                case FritzscheRegime.FollowingII:
                    accel = -bNull * 0.5f;
                    break;

                case FritzscheRegime.FreeDriving:
                default:
                    float speedError = desiredSpeedMs - ownSpeedMs;
                    if      (speedError >  0.1f) accel =  aNormal;
                    else if (speedError < -0.5f) accel = -aNormal * 0.4f;
                    else                         accel =  bNull * Mathf.Sign(speedError + 0.001f);
                    break;
            }

            // Hard speed cap (uses same scale as desiredSpeedMs)
            float physicsMaxMs = HighwaySpeedScale.Instance != null
                ? HighwaySpeedScale.Instance.RealKmhToPhysicsMs(maxSpeedKmh)
                : maxSpeedKmh / 3.6f;
            if (ownSpeedMs >= physicsMaxMs && accel > 0f)
                accel = 0f;

            return accel;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Throttle / Brake mapping
        // ─────────────────────────────────────────────────────────────────────
        private void ApplyLongitudinalOutput(float desiredAccel, float ownSpeedMs)
        {
            if (desiredAccel >= 0f)
            {
                Throttle = Mathf.Clamp01(desiredAccel / Mathf.Max(aNormal, 0.01f))
                           * throttleAggressiveness;
                Brake    = 0f;
            }
            else
            {
                Throttle = 0f;
                Brake    = Mathf.Clamp01(-desiredAccel / Mathf.Max(aMaxBrake, 0.01f))
                           * brakeAggressiveness;
            }

            // Hold-brake only when genuinely blocked at standstill
            if (ownSpeedMs < 0.1f && currentRegime == FritzscheRegime.Danger)
            {
                Throttle = 0f;
                Brake    = 0.3f;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Gizmos
        // ─────────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // 1. DISEGNO WAYPOINTS (Percorso che l'auto sta seguendo)
            if (waypoints != null)
            {
                for (int i = 0; i < waypointCount; i++)
                {
                    Gizmos.color = (i == currentWPIndex) ? Color.green : new Color(0.7f, 0.7f, 0.7f, 0.5f);
                    Gizmos.DrawSphere(waypoints[i], 0.3f);
                    if (i < waypointCount - 1)
                    {
                        Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                        Gizmos.DrawLine(waypoints[i], waypoints[i + 1]);
                    }
                }
            }

            // 2. TARGET PURE-PURSUIT (Dove l'auto punta per sterzare)
            if (waypoints != null && Application.isPlaying)
            {
                float speedMs = rb != null ? rb.linearVelocity.magnitude : 0f;
                float L = Mathf.Clamp(lookAheadBase + speedMs * speedLookAheadScale, lookAheadBase, maxLookAhead);
                Vector3 tgt = GetPointAheadOnPath(L);
                
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(tgt, 0.5f);
                Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, tgt);
            }

            // 3. CILINDRO DI VISIONE CURVO (Si piega con la strada in Play Mode)
            if (Application.isPlaying && waypoints != null)
            {
                float startOffset = 1.0f;
                Vector3 prevPoint = transform.position + transform.up * 0.3f + transform.forward * startOffset;
                float step = 10f;

                float currentSpeed = rb != null ? rb.linearVelocity.magnitude : 0f;
                float dynamicRange = Mathf.Max(detectionRange, currentSpeed * 3.5f);

                Gizmos.color = Color.yellow;
                UnityEditor.Handles.color = Color.yellow;
                
                UnityEditor.Handles.DrawWireDisc(prevPoint, transform.forward, detectionRadius);

                for (float d = step; d <= dynamicRange; d += step)
                {
                    Vector3 nextPoint = GetPointAheadOnPath(d) + transform.up * 0.3f;
                    Vector3 dir = nextPoint - prevPoint;
                    if (dir.magnitude < 0.1f) break;

                    UnityEditor.Handles.DrawWireDisc(nextPoint, dir.normalized, detectionRadius);
                    
                    Vector3 upOffset = Vector3.up * detectionRadius;
                    Vector3 rightOffset = Vector3.Cross(dir.normalized, Vector3.up).normalized * detectionRadius;

                    Gizmos.DrawLine(prevPoint + upOffset, nextPoint + upOffset);
                    Gizmos.DrawLine(prevPoint - upOffset, nextPoint - upOffset);
                    Gizmos.DrawLine(prevPoint + rightOffset, nextPoint + rightOffset);
                    Gizmos.DrawLine(prevPoint - rightOffset, nextPoint - rightOffset);

                    prevPoint = nextPoint;
                }
            }
            else 
            {
                // Fallback statico per l'Editor
                Vector3 origin = transform.position + transform.up * 0.3f + transform.forward * 1.0f;
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(origin, detectionRadius);
            }

            // 4. ETICHETTE DI STATO (Testo sopra l'auto)
            if (Application.isPlaying)
            {
                string info = $"{name}\n" +
                              $"Regime: <color=yellow>{currentRegime}</color>\n" +
                              $"Speed: {(rb.linearVelocity.magnitude * 3.6f):F1} km/h\n" +
                              $"Lane Offset: {laneOffset:F2}m\n" +
                              $"Curve ceiling: {(lastCurveCeilingMs > 900f ? "-" : (lastCurveCeilingMs * 3.6f).ToString("F0") + " km/h (phys)")}\n" +
                              $"T:{Throttle:F2} B:{Brake:F2} S:{Steering:F2}";

                UnityEditor.Handles.BeginGUI();
                GUIStyle style = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12 };
                Vector3 labelPos = transform.position + Vector3.up * 3.5f;
                Vector2 guiPoint = UnityEditor.HandleUtility.WorldToGUIPoint(labelPos);
                GUI.Label(new Rect(guiPoint.x - 50, guiPoint.y - 60, 200, 100), info, style);
                UnityEditor.Handles.EndGUI();
            }
        }

        private void OnDrawGizmos()
        {
            // Disegna la spline della corsia in modo molto tenue anche quando non selezionata
            if (waypoints == null) return;
            Gizmos.color = new Color(0.3f, 0.5f, 1f, 0.1f);
            for (int i = 0; i < waypointCount - 1; i++)
                Gizmos.DrawLine(waypoints[i], waypoints[i + 1]);
        }
#endif
    }
}
