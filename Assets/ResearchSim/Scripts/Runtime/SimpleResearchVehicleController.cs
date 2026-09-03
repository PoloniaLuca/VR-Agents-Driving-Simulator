using UnityEngine;

namespace ResearchSim
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(HybridVehicleInput))]
    public sealed class SimpleResearchVehicleController : MonoBehaviour
    {
        [Header("Longitudinal")]
        public float acceleration = 9f;
        public float brakeDeceleration = 18f;
        public float dragAtRest = 2.5f;
        public float maxSpeedKmh = 130f;

        [Header("Lateral")]
        public float maxSteeringAngle = 22f;
        public float highSpeedSteeringAngle = 4.5f;
        public float highSpeedSteeringKmh = 30f;
        public float wheelbase = 2.7f;
        public float steeringResponse = 2.2f;
        public float steeringReturnResponse = 3.8f;
        public float yawRateResponse = 90f;
        public float maxYawRateDegreesPerSecond = 36f;
        public float rearAxleToCenter = 1.25f;

        [Header("Grounding")]
        public bool snapToDriveSurface = true;
        public float rideHeight = 0.45f;
        public float groundProbeHeight = 6f;
        public float groundProbeDistance = 80f;

        [Header("Visuals")]
        public float visualBodyRollDegrees = 1.4f;
        public float wheelVisualRadius = 0.34f;
        public Transform visualBody;
        public Transform frontLeftWheel;
        public Transform frontRightWheel;
        public Transform rearLeftWheel;
        public Transform rearRightWheel;
        public Transform steeringWheelVisual;
        public float steeringWheelMaxRotationDegrees = 70f;
        public Vector3 steeringWheelRotationAxis = Vector3.forward;
        public Transform speedGaugeRoot;
        public Transform rpmGaugeRoot;
        public float speedGaugeMaxKmh = 220f;
        public float analogGaugeNeedleLength = 0.036f;
        public float analogGaugeNeedleWidth = 0.0035f;

        [Header("Debug")]
        public bool showDebugHud = true;
        public float CurrentSpeedKmh => currentForwardSpeed * 3.6f;
        public Vector3 CurrentVelocity { get; private set; }

        private Rigidbody rb;
        private HybridVehicleInput input;
        private float currentForwardSpeed;
        private float currentSteering;
        private float currentWheelAngle;
        private float currentYawRate;
        private float headingDegrees;
        private Vector3 rearAxlePosition;
        private float wheelSpinDegrees;
        private bool stateInitialized;
        private bool visualsCached;
        private Quaternion visualBodyBaseLocalRotation = Quaternion.identity;
        private Quaternion steeringWheelBaseLocalRotation = Quaternion.identity;
        private Transform speedNeedle;
        private Transform rpmNeedle;
        private Quaternion speedNeedleBaseLocalRotation = Quaternion.identity;
        private Quaternion rpmNeedleBaseLocalRotation = Quaternion.identity;
        private WheelVisual[] wheelVisuals = new WheelVisual[0];

        private struct WheelVisual
        {
            public Transform Transform;
            public Quaternion BaseLocalRotation;
            public bool Steer;
        }

        private void Awake()
        {
            if (name.Contains("Placeholder") && Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, 0f)) < 1f)
                transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            rb = GetComponent<Rigidbody>();
            input = GetComponent<HybridVehicleInput>();
            visualBody = visualBody != null ? visualBody : transform.Find("RCC Prototype Visual");

            rb.mass = 1200f;
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.detectCollisions = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            SyncStateFromTransform();
            CacheVisuals();
        }

        private void FixedUpdate()
        {
            if (!stateInitialized || NeedsStateResync())
                SyncStateFromTransform();

            input.RefreshInputValues();

            if (!rb.isKinematic && currentForwardSpeed <= 0.01f)
            {
                Vector3 velocity = GetVelocity(rb);
                currentForwardSpeed = Mathf.Max(0f, Vector3.Dot(velocity, transform.forward));
            }

            currentForwardSpeed += input.Throttle * acceleration * Time.fixedDeltaTime;
            currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, 0f, input.Brake * brakeDeceleration * Time.fixedDeltaTime);

            if (input.Throttle <= 0.01f && input.Brake <= 0.01f)
                currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, 0f, dragAtRest * Time.fixedDeltaTime);

            float maxSpeed = maxSpeedKmh / 3.6f;
            currentForwardSpeed = Mathf.Clamp(currentForwardSpeed, 0f, maxSpeed);

            float targetSteering = Mathf.Clamp(input.Steering, -1f, 1f);
            float response = Mathf.Abs(targetSteering) > 0.01f ? steeringResponse : steeringReturnResponse;
            currentSteering = Mathf.MoveTowards(currentSteering, targetSteering, response * Time.fixedDeltaTime);

            float speedKmh = currentForwardSpeed * 3.6f;
            float speedT = Mathf.InverseLerp(0f, highSpeedSteeringKmh, speedKmh);
            float allowedSteeringAngle = Mathf.Lerp(maxSteeringAngle, highSpeedSteeringAngle, speedT);
            currentWheelAngle = currentSteering * allowedSteeringAngle;

            float targetYawRate = Mathf.Abs(currentWheelAngle) > 0.01f && wheelbase > 0.01f
                ? currentForwardSpeed / wheelbase * Mathf.Tan(currentWheelAngle * Mathf.Deg2Rad) * Mathf.Rad2Deg
                : 0f;
            targetYawRate = Mathf.Clamp(targetYawRate, -maxYawRateDegreesPerSecond, maxYawRateDegreesPerSecond);

            currentYawRate = Mathf.MoveTowards(currentYawRate, targetYawRate, yawRateResponse * Time.fixedDeltaTime);

            float yawDelta = currentYawRate * Time.fixedDeltaTime;
            Quaternion travelRotation = Quaternion.Euler(0f, headingDegrees + yawDelta * 0.5f, 0f);
            rearAxlePosition += travelRotation * Vector3.forward * currentForwardSpeed * Time.fixedDeltaTime;

            headingDegrees += yawDelta;
            Quaternion nextRotation = Quaternion.Euler(0f, headingDegrees, 0f);

            Vector3 previousPosition = rb.position;
            Vector3 nextPosition = rearAxlePosition + nextRotation * Vector3.forward * rearAxleToCenter;
            if (TryGetGroundedPosition(nextPosition, out Vector3 groundedPosition))
                nextPosition = groundedPosition;

            rearAxlePosition = nextPosition - nextRotation * Vector3.forward * rearAxleToCenter;
            CurrentVelocity = (nextPosition - previousPosition) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);

            rb.MoveRotation(nextRotation);
            rb.MovePosition(nextPosition);
            if (!rb.isKinematic)
                SetVelocity(rb, CurrentVelocity);

            UpdateVisuals();
        }

        private void OnGUI()
        {
            if (!showDebugHud || input == null || rb == null)
                return;

            float speedKmh = currentForwardSpeed * 3.6f;

            GUIStyle panel = new GUIStyle(GUI.skin.box);
            panel.normal.textColor = Color.white;

            GUIStyle small = new GUIStyle(GUI.skin.label);
            small.fontSize = 15;
            small.normal.textColor = Color.white;

            GUIStyle speedNumber = new GUIStyle(GUI.skin.label);
            speedNumber.fontSize = 42;
            speedNumber.fontStyle = FontStyle.Bold;
            speedNumber.alignment = TextAnchor.MiddleRight;
            speedNumber.normal.textColor = new Color(0.92f, 1f, 1f);

            GUIStyle speedUnit = new GUIStyle(GUI.skin.label);
            speedUnit.fontSize = 15;
            speedUnit.alignment = TextAnchor.UpperRight;
            speedUnit.normal.textColor = new Color(0.72f, 0.9f, 1f);

            GUI.Box(new Rect(12f, 12f, 330f, 74f), string.Empty, panel);
            GUI.Label(new Rect(24f, 20f, 300f, 22f), "W/S accelera/frena   A/D sterza", small);
            GUI.Label(new Rect(24f, 46f, 300f, 22f), "Sterzo: " + currentSteering.ToString("F2") + "   Angolo: " + currentWheelAngle.ToString("F1") + " deg", small);

            Rect speedRect = new Rect(Screen.width - 208f, Screen.height - 112f, 184f, 88f);
            GUI.Box(speedRect, string.Empty, panel);
            GUI.Label(new Rect(speedRect.x + 12f, speedRect.y + 8f, 148f, 52f), Mathf.RoundToInt(speedKmh).ToString("000"), speedNumber);
            GUI.Label(new Rect(speedRect.x + 12f, speedRect.y + 60f, 148f, 20f), "KM/H", speedUnit);
        }

        private void SyncStateFromTransform()
        {
            headingDegrees = transform.eulerAngles.y;
            Vector3 position = transform.position;
            if (TryGetGroundedPosition(position, out Vector3 groundedPosition))
            {
                position = groundedPosition;
                transform.position = position;
            }

            rearAxlePosition = position - transform.forward * rearAxleToCenter;
            CurrentVelocity = Vector3.zero;
            stateInitialized = true;
        }

        private bool TryGetGroundedPosition(Vector3 position, out Vector3 groundedPosition)
        {
            groundedPosition = position;
            if (!snapToDriveSurface)
                return false;

            Vector3 origin = position + Vector3.up * groundProbeHeight;
            float maxDistance = groundProbeHeight + groundProbeDistance;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float bestY = float.NegativeInfinity;

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                    continue;

                if (hit.normal.y < 0.35f)
                    continue;

                if (hit.point.y > bestY)
                    bestY = hit.point.y;
            }

            if (float.IsNegativeInfinity(bestY))
                return false;

            groundedPosition.y = bestY + rideHeight;
            return true;
        }

        private bool NeedsStateResync()
        {
            if (currentForwardSpeed > 0.01f)
                return false;

            Quaternion currentRotation = Quaternion.Euler(0f, headingDegrees, 0f);
            Vector3 estimatedPosition = rearAxlePosition + currentRotation * Vector3.forward * rearAxleToCenter;
            estimatedPosition.y = rb.position.y;
            return (rb.position - estimatedPosition).sqrMagnitude > 0.25f;
        }

        private void CacheVisuals()
        {
            visualBody = visualBody != null ? visualBody : transform.Find("RCC Prototype Visual");
            if (visualBody == null)
                return;

            visualBodyBaseLocalRotation = visualBody.localRotation;

            frontLeftWheel = frontLeftWheel != null ? frontLeftWheel : FindDescendant(visualBody, "Wheel_FL", "Wheel_FrontLeft", "Wheel_LF");
            frontRightWheel = frontRightWheel != null ? frontRightWheel : FindDescendant(visualBody, "Wheel_FR", "Wheel_FrontRight", "Wheel_RF");
            rearLeftWheel = rearLeftWheel != null ? rearLeftWheel : FindDescendant(visualBody, "Wheel_RL", "Wheel_RearLeft", "Wheel_LR");
            rearRightWheel = rearRightWheel != null ? rearRightWheel : FindDescendant(visualBody, "Wheel_RR", "Wheel_RearRight");
            steeringWheelVisual = steeringWheelVisual != null ? steeringWheelVisual : FindDescendant(visualBody, "SteeringWheel", "Steering_wheel");
            steeringWheelBaseLocalRotation = steeringWheelVisual != null ? steeringWheelVisual.localRotation : Quaternion.identity;
            speedGaugeRoot = speedGaugeRoot != null ? speedGaugeRoot : FindDescendant(visualBody, "Speed");
            rpmGaugeRoot = rpmGaugeRoot != null ? rpmGaugeRoot : FindDescendant(visualBody, "Rpm", "RPM");
            speedNeedle = CreateOrFindGaugeNeedle(speedGaugeRoot, "Runtime Speed Needle", new Color(1f, 0.08f, 0.02f));
            rpmNeedle = CreateOrFindGaugeNeedle(rpmGaugeRoot, "Runtime RPM Needle", new Color(1f, 0.08f, 0.02f));
            speedNeedleBaseLocalRotation = speedNeedle != null ? speedNeedle.localRotation : Quaternion.identity;
            rpmNeedleBaseLocalRotation = rpmNeedle != null ? rpmNeedle.localRotation : Quaternion.identity;

            wheelVisuals = new[]
            {
                CreateWheelVisual(frontLeftWheel, true),
                CreateWheelVisual(frontRightWheel, true),
                CreateWheelVisual(rearLeftWheel, false),
                CreateWheelVisual(rearRightWheel, false)
            };

            visualsCached = true;
        }

        private void UpdateVisuals()
        {
            if (!visualsCached)
                CacheVisuals();

            if (visualBody != null)
            {
                float roll = -Mathf.Sign(currentYawRate) * Mathf.InverseLerp(0f, maxYawRateDegreesPerSecond, Mathf.Abs(currentYawRate)) * visualBodyRollDegrees;
                visualBody.localRotation = visualBodyBaseLocalRotation * Quaternion.Euler(0f, 0f, roll);
            }

            if (steeringWheelVisual != null)
            {
                Vector3 axis = steeringWheelRotationAxis.sqrMagnitude > 0.001f ? steeringWheelRotationAxis.normalized : Vector3.forward;
                float wheelAngle = -currentSteering * steeringWheelMaxRotationDegrees;
                steeringWheelVisual.localRotation = steeringWheelBaseLocalRotation * Quaternion.AngleAxis(wheelAngle, axis);
            }

            UpdateGaugeNeedle(speedNeedle, speedNeedleBaseLocalRotation, Mathf.InverseLerp(0f, speedGaugeMaxKmh, CurrentSpeedKmh));
            float pseudoRpmT = Mathf.Clamp01(0.04f + Mathf.InverseLerp(0f, maxSpeedKmh, CurrentSpeedKmh) * 0.42f + (input != null ? input.Throttle : 0f) * 0.22f);
            UpdateGaugeNeedle(rpmNeedle, rpmNeedleBaseLocalRotation, pseudoRpmT);

            if (wheelVisuals == null || wheelVisuals.Length == 0 || wheelVisualRadius <= 0.01f)
                return;

            wheelSpinDegrees += currentForwardSpeed * Time.fixedDeltaTime / wheelVisualRadius * Mathf.Rad2Deg;
            for (int i = 0; i < wheelVisuals.Length; i++)
            {
                WheelVisual wheel = wheelVisuals[i];
                if (wheel.Transform == null)
                    continue;

                float steerAngle = wheel.Steer ? currentWheelAngle : 0f;
                wheel.Transform.localRotation = wheel.BaseLocalRotation *
                                                Quaternion.Euler(0f, steerAngle, 0f) *
                                                Quaternion.Euler(wheelSpinDegrees, 0f, 0f);
            }
        }

        private static WheelVisual CreateWheelVisual(Transform wheel, bool steer)
        {
            return new WheelVisual
            {
                Transform = wheel,
                BaseLocalRotation = wheel != null ? wheel.localRotation : Quaternion.identity,
                Steer = steer
            };
        }

        private Transform CreateOrFindGaugeNeedle(Transform gaugeRoot, string needleName, Color color)
        {
            if (gaugeRoot == null)
                return null;

            Transform existing = gaugeRoot.Find(needleName);
            if (existing != null)
                return existing;

            GameObject pivot = new GameObject(needleName);
            pivot.transform.SetParent(gaugeRoot, false);
            pivot.transform.localPosition = new Vector3(0f, 0f, -0.014f);
            pivot.transform.localRotation = Quaternion.Euler(0f, 0f, 130f);
            pivot.transform.localScale = Vector3.one;

            GameObject needle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            needle.name = "Needle Bar";
            needle.transform.SetParent(pivot.transform, false);
            needle.transform.localPosition = new Vector3(0f, analogGaugeNeedleLength * 0.5f, 0f);
            needle.transform.localRotation = Quaternion.identity;
            needle.transform.localScale = new Vector3(analogGaugeNeedleWidth, analogGaugeNeedleLength, 0.004f);

            Collider collider = needle.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            Renderer renderer = needle.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = CreateNeedleMaterial(color);

            return pivot.transform;
        }

        private static void UpdateGaugeNeedle(Transform needle, Quaternion baseRotation, float normalizedValue)
        {
            if (needle == null)
                return;

            float angle = Mathf.Lerp(130f, -125f, Mathf.Clamp01(normalizedValue));
            needle.localRotation = baseRotation * Quaternion.Euler(0f, 0f, angle - 130f);
        }

        private static Material CreateNeedleMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            Material material = new Material(shader);
            material.name = "Runtime Gauge Needle";
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * 0.7f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.25f);
            return material;
        }

        private static Transform FindDescendant(Transform root, params string[] names)
        {
            if (root == null)
                return null;

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == null)
                    continue;

                for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
                {
                    if (candidate.name == names[nameIndex])
                        return candidate;
                }
            }

            return null;
        }

        private static Vector3 GetVelocity(Rigidbody body)
        {
#if UNITY_6000_0_OR_NEWER
            return body.linearVelocity;
#else
            return body.velocity;
#endif
        }

        private static void SetVelocity(Rigidbody body, Vector3 velocity)
        {
#if UNITY_6000_0_OR_NEWER
            body.linearVelocity = velocity;
#else
            body.velocity = velocity;
#endif
        }
    }
}
