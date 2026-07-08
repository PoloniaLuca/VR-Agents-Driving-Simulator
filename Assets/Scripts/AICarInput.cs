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
        [SerializeField] private float waypointSpacing = 10f;

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

        [Header("Curve Speed Reduction")]
        [Tooltip("Enable automatic slow-down before sharp corners.")]
        [SerializeField] private bool enableCurveSlowdown = true;

        [Tooltip("Number of waypoints ahead used to detect an incoming curve.")]
        [SerializeField, Range(1, 8)] private int curveDetectionWaypoints = 4;

        [Tooltip("Speed factor at maximum sharpness (90° corner).\n" +
                 "0.4 = car slows to 40% of desired speed. 0.6 = 60%.")]
        [SerializeField, Range(0.2f, 0.9f)] private float minCurveSpeedFactor = 0.45f;

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
        [SerializeField] private float detectionRadius = 1.0f;

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
            float           maxSpeedKmhValue = 0f)
        {
            roadSpline       = spline;
            splineIndex      = splineIdx;
            laneOffset       = offset;
            desiredSpeedKmh  = desiredSpeedKmhValue;
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

            Debug.Log($"[AICarInput] '{name}': built {waypointCount} waypoints " +
                      $"(spacing ~{length / (waypointCount - 1):F1} m, " +
                      $"lane offset {laneOffset:F2} m).");
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

            // Reduce desired speed before sharp corners so the car can actually turn
            float desiredSpeedMs = baseDesiredSpeedMs * ComputeCurveSpeedFactor();

            // 1. Advance waypoint index
            AdvanceWaypoint();

            // 2. Obstacle detection (vehicles + statics)
            bool  hasObstacle    = DetectObstacle(out float gap, out float obstacleSpeedMs);
            float deltaV         = hasObstacle ? (ownSpeedMs - obstacleSpeedMs) : 0f;


            // 3. Fritzsche longitudinal control
            float accel = ComputeFritzscheAcceleration(
                hasObstacle, gap, deltaV, ownSpeedMs, desiredSpeedMs);

            // 4. Map accel → throttle / brake
            ApplyLongitudinalOutput(accel, ownSpeedMs);

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
        /// Inspects the next <see cref="curveDetectionWaypoints"/> waypoint segments
        /// and returns a speed multiplier in [minCurveSpeedFactor, 1.0].
        ///
        /// Sharp turn → small factor (slow down).
        /// Straight   → factor = 1.0  (full desired speed).
        /// </summary>
        private float ComputeCurveSpeedFactor()
        {
            if (!enableCurveSlowdown || waypointCount < 3) return 1f;

            float maxAngle = 0f;   // radians

            int end = Mathf.Min(currentWPIndex + curveDetectionWaypoints, waypointCount - 2);
            for (int i = currentWPIndex; i < end; i++)
            {
                Vector3 ab = (waypoints[i + 1] - waypoints[i]).normalized;
                Vector3 bc = (i + 2 < waypointCount)
                    ? (waypoints[i + 2] - waypoints[i + 1]).normalized
                    : ab;

                float dot   = Mathf.Clamp(Vector3.Dot(ab, bc), -1f, 1f);
                float angle = Mathf.Acos(dot);   // 0 = straight, π/2 = 90° turn
                maxAngle    = Mathf.Max(maxAngle, angle);
            }

            // Map [0, π/2] → [1, minCurveSpeedFactor]
            float t = Mathf.Clamp01(maxAngle / (Mathf.PI * 0.5f));
            return Mathf.Lerp(1f, minCurveSpeedFactor, t);
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
            gap              = float.MaxValue;
            obstacleSpeedMs  = 0f;

            // Offset origin past own front bumper so we never self-hit
            Vector3 origin = transform.position
                             + transform.up      * 0.3f
                             + transform.forward * (effectiveVehicleLength * 0.5f
                                                    + detectionRadius + 0.1f);

            float castDist = Mathf.Max(0.1f,
                detectionRange - effectiveVehicleLength * 0.5f - detectionRadius);

            RaycastHit[] hits = Physics.SphereCastAll(
                origin, detectionRadius, transform.forward, castDist,
                obstacleMask, QueryTriggerInteraction.Ignore);

            float   nearest     = float.MaxValue;
            bool    found       = false;
            Rigidbody nearestRb = null;

            // foreach (var h in hits)
            // {
            //     if (IsOwnCollider(h.collider)) continue;
            //     if (h.distance < nearest)
            //     {
            //         nearest   = h.distance;
            //         nearestRb = h.collider.attachedRigidbody;
            //         found     = true;
            //     }
            // }

            foreach (var h in hits)
            {
                if (IsOwnCollider(h.collider)) continue;

                // --- NUOVA LOGICA DI FILTRO CORSIA ---
                // Controlliamo se l'oggetto colpito ha un offset laterale simile al nostro
                AICarInput otherAI = h.collider.GetComponentInParent<AICarInput>();
                if (otherAI != null)
                {
                    // Se l'altra auto AI è in una corsia differente (differenza > 1 metro), ignorala
                    if (Mathf.Abs(otherAI.laneOffset - this.laneOffset) > 1.0f) continue;
                }
                
                // Per l'auto dell'utente (che non ha AICarInput), usiamo un controllo di distanza laterale
                if (h.collider.CompareTag("Player") || h.collider.GetComponentInParent<UserCarInput>() != null)
                {
                    // Calcola la posizione locale dell'utente rispetto a questa auto AI
                    Vector3 localPos = transform.InverseTransformPoint(h.collider.transform.position);
                    // Se l'utente è spostato lateralmente di più di 1.5m rispetto al centro di questa auto, ignoralo
                    if (Mathf.Abs(localPos.x) > 1.0f) continue; 
                }
                // -------------------------------------

                if (h.distance < nearest)
                {
                    nearest = h.distance;
                    nearestRb = h.collider.attachedRigidbody;
                    found = true;
                }
            }

            if (!found) return false;

            gap = Mathf.Max(0f, nearest);

            if (nearestRb != null)
                obstacleSpeedMs = Vector3.Dot(nearestRb.linearVelocity, transform.forward);
            // static object → obstacleSpeedMs stays 0

            return true;
        }

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
            float desiredSpeedMs)
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
                : detectionRange;
            float PTN = -(kPTN * Mathf.Pow(Mathf.Max(0f, distFactor - xf), 2f));
            float PTP =  (kPTP * Mathf.Pow(distFactor + xf, 2f));

            // Regime classification
            if (!hasObstacle || deltaX > detectionRange * 0.95f)
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
            // Waypoints
            if (waypoints != null)
            {
                for (int i = 0; i < waypointCount; i++)
                {
                    Gizmos.color = (i == currentWPIndex) ? Color.green : Color.grey;
                    Gizmos.DrawSphere(waypoints[i], 0.3f);
                    if (i < waypointCount - 1)
                    {
                        Gizmos.color = Color.grey;
                        Gizmos.DrawLine(waypoints[i], waypoints[i + 1]);
                    }
                }
            }

            // Look-ahead target (cyan)
            if (waypoints != null && Application.isPlaying)
            {
                float speedMs = rb != null ? rb.linearVelocity.magnitude : 0f;
                float L       = Mathf.Clamp(lookAheadBase + speedMs * speedLookAheadScale,
                                            lookAheadBase, maxLookAhead);
                Vector3 tgt   = GetPointAheadOnPath(L);
                Gizmos.color  = Color.cyan;
                Gizmos.DrawSphere(tgt, 0.4f);
                Gizmos.DrawLine(transform.position, tgt);
            }

            // Detection ray (yellow)
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(
                transform.position + transform.up * 0.3f,
                transform.position + transform.up * 0.3f + transform.forward * detectionRange);

            // Regime label
            if (Application.isPlaying)
            {
                UnityEditor.Handles.Label(
                    transform.position + Vector3.up * 3f,
                    $"{name}\nRegime:{currentRegime}  WP:{currentWPIndex}/{waypointCount - 1}\n" +
                    $"T:{Throttle:F2}  B:{Brake:F2}  S:{Steering:F2}");
            }

            
            // --- NUOVA VISUALIZZAZIONE CILINDRO DI VISIONE ---
            Gizmos.color = Color.yellow;
            
            // Calcoliamo l'origine del raggio (come nel codice di DetectObstacle)
            Vector3 origin = transform.position + transform.up * 0.3f + transform.forward * (effectiveVehicleLength * 0.5f + detectionRadius + 0.1f);
            Vector3 endPoint = origin + transform.forward * detectionRange;

            // Disegna il "tubo" (cilindro) dello SphereCast
            // Disegna il cerchio all'inizio e alla fine
            UnityEditor.Handles.color = Color.yellow;
            UnityEditor.Handles.DrawWireDisc(origin, transform.forward, detectionRadius);
            UnityEditor.Handles.DrawWireDisc(endPoint, transform.forward, detectionRadius);

            // Disegna le 4 linee laterali che collegano i cerchi per formare il tubo
            Gizmos.DrawLine(origin + transform.right * detectionRadius, endPoint + transform.right * detectionRadius);
            Gizmos.DrawLine(origin - transform.right * detectionRadius, endPoint - transform.right * detectionRadius);
            Gizmos.DrawLine(origin + transform.up * detectionRadius, endPoint + transform.up * detectionRadius);
            Gizmos.DrawLine(origin - transform.up * detectionRadius, endPoint - transform.up * detectionRadius);
            
            // Disegna una sfera alla fine del raggio per indicare la portata massima
            Gizmos.DrawWireSphere(endPoint, 0.5f);
        }

        private void OnDrawGizmos()
        {
            // Draw waypoints even when not selected (faint)
            if (waypoints == null) return;
            Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            for (int i = 0; i < waypointCount - 1; i++)
                Gizmos.DrawLine(waypoints[i], waypoints[i + 1]);
        }
#endif
    }
}
