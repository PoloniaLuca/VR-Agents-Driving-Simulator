using UnityEngine;

namespace DrivingSim
{
    /// <summary>
    /// Applies physics forces to the car based on the current ICarInput.
    ///
    /// Responsibilities:
    /// - Reads input from an ICarInput implementation.
    /// - Converts inputs to wheel collider forces (motor, brake, steering).
    /// - Synchronises visual wheel meshes with wheel colliders.
    ///
    /// It does NOT know how the input is produced (user, AI, replay, etc.).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public partial class CarController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Source of input for this car. Must implement ICarInput.")]
        [SerializeField] private MonoBehaviour inputSource;

        [Header("Wheel colliders")]
        [Tooltip("Front Left wheel collider.")]
        [SerializeField] private WheelCollider frontLeftWheelCollider;

        [Tooltip("Front Right wheel collider.")]
        [SerializeField] private WheelCollider frontRightWheelCollider;

        [Tooltip("Rear Left wheel collider.")]
        [SerializeField] private WheelCollider rearLeftWheelCollider;

        [Tooltip("Rear Right wheel collider.")]
        [SerializeField] private WheelCollider rearRightWheelCollider;

        [Header("Wheel visuals")]
        [Tooltip("Front Left wheel mesh transform.")]
        [SerializeField] private Transform frontLeftWheelMesh;

        [Tooltip("Front Right wheel mesh transform.")]
        [SerializeField] private Transform frontRightWheelMesh;

        [Tooltip("Rear Left wheel mesh transform.")]
        [SerializeField] private Transform rearLeftWheelMesh;

        [Tooltip("Rear Right wheel mesh transform.")]
        [SerializeField] private Transform rearRightWheelMesh;

        [Header("Engine & drivetrain")]
        [Tooltip("Maximum engine torque applied to drive wheels.")]
        [SerializeField] private float maxMotorTorque = 1500f;

        [Tooltip("Maximum brake torque applied to all wheels when fully braking.")]
        [SerializeField] private float maxBrakeTorque = 3000f;

        [Tooltip("Maximum brake torque applied to rear wheels when handbrake is fully engaged.")]
        [SerializeField] private float maxHandbrakeTorque = 4000f;

        [Tooltip("Maximum steering angle in degrees for the front wheels.")]
        [SerializeField] private float maxSteeringAngle = 30f;

        [Header("Drive configuration")]
        [Tooltip("If true, front wheels receive motor torque.")]
        [SerializeField] private bool frontWheelDrive = false;

        [Tooltip("If true, rear wheels receive motor torque.")]
        [SerializeField] private bool rearWheelDrive = true;

        [Header("Speed Limit")]
        [SerializeField] private bool enableSpeedLimit = true;
        [SerializeField] private float maxDisplaySpeedKmh = 90f;

        [Tooltip("How firmly the limiter enforces the cap each physics frame.\n" +
                 "0.05 = gentle engine-cut feel.  0.4 = firm rev-limiter feel.")]
        [SerializeField, Range(0.02f, 1f)] private float limitHardness = 0.15f;

        private ICarInput carInput;
        private Rigidbody rb;

        // Simple gear / reverse state. Can be replaced by a richer gearbox system later.
        private bool isInReverse = false;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            InitializeInput();
            ValidateSetup();
        }

        private void InitializeInput()
        {
            carInput = inputSource as ICarInput;
            if (carInput == null && inputSource != null)
            {
                Debug.LogError(
                    $"Input source on '{name}' does not implement ICarInput. " +
                    "Please assign a component that implements ICarInput.",
                    this);
            }
        }

        private void ValidateSetup()
        {
            if (frontLeftWheelCollider == null ||
                frontRightWheelCollider == null ||
                rearLeftWheelCollider == null ||
                rearRightWheelCollider == null)
            {
                Debug.LogWarning("One or more wheel colliders are not assigned on CarController.", this);
            }
        }

        private void Update()
        {
            // Handle non-physics state (e.g., gear changes).
            if (carInput == null)
                return;

            HandleGearInput();
        }

        private void FixedUpdate()
        {
            if (carInput == null)
                return;

            ApplySteering();
            ApplyMotorAndBrakes();
            UpdateWheelVisuals();
            EnforceSpeedLimit();
        }

        /// <summary>
        /// Handles gear / reverse toggling based on input.
        /// </summary>
        private void HandleGearInput()
        {
            if (carInput.ToggleReversePressed)
            {
                isInReverse = !isInReverse;
            }

            // Placeholder for future manual gearbox:
            // Use carInput.ShiftUpPressed and carInput.ShiftDownPressed here.
        }

        /// <summary>
        /// Applies steering to the front wheel colliders based on input.
        /// </summary>
        private void ApplySteering()
        {
            float targetSteerAngle = carInput.Steering * maxSteeringAngle;

            if (frontLeftWheelCollider != null)
                frontLeftWheelCollider.steerAngle = targetSteerAngle;

            if (frontRightWheelCollider != null)
                frontRightWheelCollider.steerAngle = targetSteerAngle;
        }

        /// <summary>
        /// Applies motor torque, brake, and handbrake to the wheel colliders.
        /// </summary>
        private void ApplyMotorAndBrakes()
        {
            float throttleInput = Mathf.Clamp01(carInput.Throttle);
            float brakeInput = Mathf.Clamp01(carInput.Brake);
            float handbrakeInput = Mathf.Clamp01(carInput.Handbrake);

            float direction = isInReverse ? -1f : 1f;
            float motorTorque = throttleInput * maxMotorTorque * direction;
            float brakeTorque = brakeInput * maxBrakeTorque;
            float handbrakeTorque = handbrakeInput * maxHandbrakeTorque;

            // Reset all torques before applying new ones.
            ResetWheelTorques();

            // Apply motor torque based on drive configuration.
            if (frontWheelDrive)
            {
                if (frontLeftWheelCollider != null)
                    frontLeftWheelCollider.motorTorque += motorTorque * 0.5f;
                if (frontRightWheelCollider != null)
                    frontRightWheelCollider.motorTorque += motorTorque * 0.5f;
            }

            if (rearWheelDrive)
            {
                if (rearLeftWheelCollider != null)
                    rearLeftWheelCollider.motorTorque += motorTorque * 0.5f;
                if (rearRightWheelCollider != null)
                    rearRightWheelCollider.motorTorque += motorTorque * 0.5f;
            }

            // Apply brake torque to all wheels.
            ApplyBrakeTorqueToAll(brakeTorque);

            // Apply handbrake to rear wheels only.
            ApplyHandbrakeTorque(handbrakeTorque);
        }

        /// <summary>
        /// Clears motor and brake torques on all wheels before applying new values.
        /// </summary>
        private void ResetWheelTorques()
        {
            WheelCollider[] colliders =
            {
                frontLeftWheelCollider,
                frontRightWheelCollider,
                rearLeftWheelCollider,
                rearRightWheelCollider
            };

            foreach (var col in colliders)
            {
                if (col == null) continue;
                col.motorTorque = 0f;
                col.brakeTorque = 0f;
            }
        }

        /// <summary>
        /// Applies the given brake torque to all wheels.
        /// </summary>
        private void ApplyBrakeTorqueToAll(float brakeTorque)
        {
            if (brakeTorque <= 0f)
                return;

            if (frontLeftWheelCollider != null)
                frontLeftWheelCollider.brakeTorque += brakeTorque;
            if (frontRightWheelCollider != null)
                frontRightWheelCollider.brakeTorque += brakeTorque;
            if (rearLeftWheelCollider != null)
                rearLeftWheelCollider.brakeTorque += brakeTorque;
            if (rearRightWheelCollider != null)
                rearRightWheelCollider.brakeTorque += brakeTorque;
        }

        /// <summary>
        /// Applies handbrake torque to rear wheels only.
        /// </summary>
        private void ApplyHandbrakeTorque(float handbrakeTorque)
        {
            if (handbrakeTorque <= 0f)
                return;

            if (rearLeftWheelCollider != null)
                rearLeftWheelCollider.brakeTorque += handbrakeTorque;
            if (rearRightWheelCollider != null)
                rearRightWheelCollider.brakeTorque += handbrakeTorque;
        }

        /// <summary>
        /// Synchronises the wheel meshes' positions and rotations
        /// with their corresponding wheel colliders.
        /// </summary>
        private void UpdateWheelVisuals()
        {
            UpdateSingleWheelVisual(frontLeftWheelCollider, frontLeftWheelMesh);
            UpdateSingleWheelVisual(frontRightWheelCollider, frontRightWheelMesh);
            UpdateSingleWheelVisual(rearLeftWheelCollider, rearLeftWheelMesh);
            UpdateSingleWheelVisual(rearRightWheelCollider, rearRightWheelMesh);
        }

        /// <summary>
        /// Updates a single wheel mesh transform from its collider.
        /// </summary>
        private void UpdateSingleWheelVisual(WheelCollider collider, Transform mesh)
        {
            if (collider == null || mesh == null)
                return;

            collider.GetWorldPose(out Vector3 pos, out Quaternion rot);
            mesh.position = pos;
            mesh.rotation = rot;
        }

        /// <summary>
        /// Restituisce la velocità lineare dell'auto in metri al secondo.
        /// </summary>
        public float GetSpeedMetersPerSecond()
        {
            if (rb == null) return 0f;
            return rb.linearVelocity.magnitude;
        }

        /// <summary>
        /// Restituisce la velocità in km/h.
        /// </summary>
        public float GetSpeedKmh()
        {
            return GetSpeedMetersPerSecond() * 3.6f;
        }

        /// <summary>
        /// Restituisce la velocità in mph.
        /// </summary>
        public float GetSpeedMph()
        {
            return GetSpeedMetersPerSecond() * 2.23694f;
        }

        /// <summary>
        /// Returns the speed in display km/h, scaled by HighwaySpeedScale so the
        /// value represents real-world equivalent km/h on the configured highway.
        /// Use this for dashboard displays instead of GetSpeedKmh().
        /// </summary>
        public float GetDisplaySpeedKmh()
        {
            float physicsMs = GetSpeedMetersPerSecond();
            return HighwaySpeedScale.Instance != null
                ? HighwaySpeedScale.Instance.PhysicsMsToDisplayKmh(physicsMs)
                : physicsMs * 3.6f;
        }

        /// <summary>
        /// Clamps the Rigidbody velocity to maxDisplaySpeedKmh each physics frame.
        /// Uses a soft lerp so the speed reduction feels like a rev-limiter, not
        /// an invisible wall.
        /// </summary>
        private void EnforceSpeedLimit()
        {
            if (!enableSpeedLimit || rb == null || maxDisplaySpeedKmh <= 0f) return;

            float physicsMaxMs = HighwaySpeedScale.Instance != null
                ? HighwaySpeedScale.Instance.RealKmhToPhysicsMs(maxDisplaySpeedKmh)
                : maxDisplaySpeedKmh / 3.6f;

            if (rb.linearVelocity.magnitude <= physicsMaxMs) return;

            Vector3 capped = rb.linearVelocity.normalized * physicsMaxMs;
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, capped, limitHardness);
        }
    }
}
