using UnityEngine;
using System.Reflection;

namespace ResearchSim
{
    /// <summary>
    /// Keeps the cockpit steering wheel visually aligned with the physical
    /// Fanatec wheel. VPP receives a sensitivity-scaled steering value for
    /// drivability, but the cockpit wheel should use the unscaled HID value.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class FanatecSteeringWheelVisualSync : MonoBehaviour
    {
        [Header("References")]
        public VppExternalInputBridge inputBridge;
        public FanatecHidInputProvider fanatecProvider;
        public Transform steeringWheel;

        [Header("Visual calibration")]
        public float realWheelRotationRangeDegrees = 900f;
        public Vector3 localRotationAxis = Vector3.forward;
        public bool invertRotation = true;
        public bool overrideOnlyWhenFanatecSteers = true;
        public bool readWheelRangeFromFanatecMapping = true;
        public bool readVisualSettingsFromVpp = true;
        public bool readWheelRangeFromVpp = true;

        private Quaternion baseLocalRotation = Quaternion.identity;
        private bool hasBaseRotation;

        private void Awake()
        {
            ResolveReferences();
            CacheBaseRotation();
        }

        private void LateUpdate()
        {
            ResolveReferences();

            if (steeringWheel == null || fanatecProvider == null)
                return;

            if (!hasBaseRotation)
                CacheBaseRotation();

            if (!fanatecProvider.ActiveDeviceFound || !fanatecProvider.LastSteeringControlFound)
                return;

            float fanatecSteering = Mathf.Clamp(fanatecProvider.LastSteeringNormalized, -1f, 1f);
            if (overrideOnlyWhenFanatecSteers && inputBridge != null && !inputBridge.LastFanatecSteeringActive && Mathf.Abs(fanatecSteering) < 0.0001f)
                return;

            Vector3 axis = localRotationAxis.sqrMagnitude > 0.001f ? localRotationAxis.normalized : Vector3.forward;
            float signedFactor = invertRotation ? -1f : 1f;
            float angle = fanatecSteering * signedFactor * realWheelRotationRangeDegrees * 0.5f;
            steeringWheel.localRotation = baseLocalRotation * Quaternion.AngleAxis(angle, axis);
        }

        private void ResolveReferences()
        {
            if (inputBridge == null)
                inputBridge = GetComponent<VppExternalInputBridge>();

            if (fanatecProvider == null)
                fanatecProvider = GetComponent<FanatecHidInputProvider>();

            if (readVisualSettingsFromVpp && steeringWheel == null)
                TryResolveVppVisualSettings();

            if (readWheelRangeFromFanatecMapping && fanatecProvider != null && fanatecProvider.mapping != null && fanatecProvider.mapping.steeringWheelVisualRangeDegrees > 1f)
                realWheelRotationRangeDegrees = fanatecProvider.mapping.steeringWheelVisualRangeDegrees;
            else if (readWheelRangeFromVpp && inputBridge != null && inputBridge.vehicleController != null)
                TryReadVppSteeringWheelRange(inputBridge.vehicleController);

            if (steeringWheel == null)
                steeringWheel = FindDescendant(transform, "SteeringWheel", "Steering_wheel");
        }

        private void TryResolveVppVisualSettings()
        {
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour == this || behaviour == inputBridge || behaviour == fanatecProvider)
                    continue;

                System.Type type = behaviour.GetType();
                FieldInfo steeringWheelField = type.GetField("steeringWheel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (steeringWheelField == null || !typeof(Transform).IsAssignableFrom(steeringWheelField.FieldType))
                    continue;

                Transform candidate = steeringWheelField.GetValue(behaviour) as Transform;
                if (candidate == null)
                    continue;

                steeringWheel = candidate;

                FieldInfo axisField = type.GetField("localRotationAxis", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (axisField != null)
                    localRotationAxis = AxisFromVppValue(axisField.GetValue(behaviour));

                return;
            }
        }

        private void TryReadVppSteeringWheelRange(MonoBehaviour vehicleController)
        {
            FieldInfo steeringField = vehicleController.GetType().GetField("steering", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object steering = steeringField != null ? steeringField.GetValue(vehicleController) : null;
            if (steering == null)
                return;

            FieldInfo rangeField = steering.GetType().GetField("steeringWheelRange", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (rangeField == null)
                return;

            object value = rangeField.GetValue(steering);
            if (value is float floatValue && floatValue > 1f)
                realWheelRotationRangeDegrees = floatValue;
        }

        private static Vector3 AxisFromVppValue(object value)
        {
            if (value == null)
                return Vector3.forward;

            int intValue;
            try
            {
                intValue = System.Convert.ToInt32(value);
            }
            catch
            {
                return Vector3.forward;
            }

            switch (intValue)
            {
                case 0: return Vector3.right;
                case 1: return Vector3.up;
                case 2: return Vector3.forward;
            }

            return Vector3.forward;
        }

        private void CacheBaseRotation()
        {
            if (steeringWheel == null)
                return;

            baseLocalRotation = steeringWheel.localRotation;
            hasBaseRotation = true;
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
    }
}
