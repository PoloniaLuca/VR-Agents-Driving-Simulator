using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace ResearchSim
{
    /// <summary>
    /// Configures only the controls already implemented by Vehicle Physics Pro.
    /// Custom functions such as indicators, horn or wipers are intentionally
    /// left out until they are mapped explicitly.
    /// </summary>
    public sealed class VppBuiltInVehicleControls : MonoBehaviour
    {
        [Header("VPP built-in controls")]
        public KeyCode headLightsKey = KeyCode.L;
        public bool configureHeadLightsKey = true;
        public bool handleHeadLightsKeyDirectly = true;
        public bool logDetectedControls = true;
        public bool logHeadlightToggle = true;

        [Header("Auxiliary HID buttons")]
        public bool enableAuxiliaryHeadLightsButton = true;
        public string auxiliaryDeviceNameOrPathContains = "teensy;arduino";
        public string auxiliaryHeadLightsButtonControlPath;
        public string parkingLightsControlPath = "trigger";
        public string lowBeamModifierButtonPath = "button2";
        public string highBeamFlashButtonPath = "button6";
        public string leftIndicatorButtonPath = "button7";
        public string rightIndicatorButtonPath = "button5";
        public string wiperSpeed1ButtonPath = "button13";
        public string wiperSpeed2ButtonPath = "button8";
        public string wiperSpeed3ButtonPath = "button9";
        public bool showAuxiliaryControlsHud = true;

        private MonoBehaviour visualComponent;
        private GameObject[] headlightOnObjects = new GameObject[0];
        private GameObject[] headlightOffObjects = new GameObject[0];
        private bool localHeadLightsEnabled;
        private bool auxiliaryParkingLights;
        private bool auxiliaryLowBeams;
        private bool auxiliaryHighBeamFlash;
        private bool auxiliaryLeftIndicator;
        private bool auxiliaryRightIndicator;
        private int auxiliaryWiperSpeed;
        private bool lastAuxiliaryHeadLightsPressed;

        private void Awake()
        {
            DestroyOldRuntimeHeadlightArtifacts();

            visualComponent = FindVppVisualComponent();
            if (visualComponent == null)
            {
                if (logDetectedControls)
                    Debug.LogWarning("[VppBuiltInVehicleControls] VPP visual/light component not found. Built-in light controls are unavailable.");
                return;
            }

            if (configureHeadLightsKey)
                SetFieldValue(visualComponent, "headLightsToggleKey", headLightsKey);

            headlightOnObjects = FindNamedHeadlightObjects(true);
            headlightOffObjects = FindNamedHeadlightObjects(false);
            localHeadLightsEnabled = IsHeadLightsEnabled();
            ApplyHeadLightObjects(localHeadLightsEnabled);

            if (logDetectedControls)
                LogBuiltInControls(visualComponent);
        }

        private void DestroyOldRuntimeHeadlightArtifacts()
        {
            DestroyNamedObject("Runtime Left Headlight Beam");
            DestroyNamedObject("Runtime Right Headlight Beam");
            DestroyNamedObject("Runtime Headlight Road Glow");
        }

        private static void DestroyNamedObject(string objectName)
        {
            GameObject[] objects = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject found = objects[i];
                if (found != null && found.name == objectName)
                    Destroy(found);
            }
        }

        private void Update()
        {
            if (visualComponent == null)
                return;

            bool toggleRequested = handleHeadLightsKeyDirectly && Input.GetKeyDown(headLightsKey);
#if ENABLE_INPUT_SYSTEM
            UpdateAuxiliaryDashboardControls();
            toggleRequested |= WasAuxiliaryHeadLightsPressedThisFrame();
#endif

            if (!toggleRequested)
            {
#if ENABLE_INPUT_SYSTEM
                ApplyHeadLightsFromLocalAndAuxiliary();
#endif
                return;
            }

            SetHeadLightsEnabled(!localHeadLightsEnabled);

            if (logHeadlightToggle)
            {
                Debug.Log(
                    "[VppBuiltInVehicleControls] Headlights " + (localHeadLightsEnabled ? "ON" : "OFF") +
                    " via key/HID " + headLightsKey + "/" + auxiliaryHeadLightsButtonControlPath +
                    ". named on objects=" + headlightOnObjects.Length +
                    ", named off objects=" + headlightOffObjects.Length);
            }
        }

        private void LateUpdate()
        {
            if (visualComponent == null)
                return;

            bool enabled = localHeadLightsEnabled || auxiliaryParkingLights || auxiliaryLowBeams || auxiliaryHighBeamFlash || IsHeadLightsEnabled();
            ApplyHeadLightObjects(enabled);
        }

        private void OnGUI()
        {
            if (!showAuxiliaryControlsHud)
                return;

            GUIStyle style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 16
            };
            style.normal.textColor = Color.white;

            string label =
                "Arduino dashboard\n" +
                "Frecce: L=" + (auxiliaryLeftIndicator ? "ON" : "off") +
                "  R=" + (auxiliaryRightIndicator ? "ON" : "off") + "\n" +
                "Luci: pos=" + (auxiliaryParkingLights ? "ON" : "off") +
                "  anab=" + (auxiliaryLowBeams ? "ON" : "off") +
                "  flash=" + (auxiliaryHighBeamFlash ? "ON" : "off") + "\n" +
                "Tergicristallo: " + auxiliaryWiperSpeed + "\n" +
                "VPP support: headlights/brake/reverse/handbrake/stalled";

            GUI.Box(new Rect(16f, Screen.height - 130f, 460f, 114f), label, style);
        }

        private MonoBehaviour FindVppVisualComponent()
        {
            MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                Type type = behaviour.GetType();
                if (HasField(type, "headLightsOn") &&
                    HasField(type, "brakeLightsOn") &&
                    HasField(type, "reverseLightsOn"))
                    return behaviour;
            }

            return null;
        }

        private void LogBuiltInControls(MonoBehaviour target)
        {
            int headLights = CountFieldItems(target, "headLightsOn");
            int brakeLights = CountFieldItems(target, "brakeLightsOn");
            int reverseLights = CountFieldItems(target, "reverseLightsOn");
            bool hasHandbrakeLight = GetFieldValue(target, "handbrakeLightsOn") != null;
            bool hasStalledLight = GetFieldValue(target, "stalledLightsOn") != null;

            Debug.Log(
                "[VppBuiltInVehicleControls] VPP built-in controls: headlights key=" + headLightsKey +
                ", headlights=" + headLights +
                ", named on objects=" + headlightOnObjects.Length +
                ", named off objects=" + headlightOffObjects.Length +
                ", brake lights=" + brakeLights +
                ", reverse lights=" + reverseLights +
                ", handbrake light=" + hasHandbrakeLight +
                ", stalled light=" + hasStalledLight);
        }

        private void SetHeadLightsEnabled(bool enabled)
        {
            localHeadLightsEnabled = enabled;
            SetFieldValue(visualComponent, "headLightsEnabled", enabled);
            ApplyHeadLightObjects(enabled);
        }

        private bool IsHeadLightsEnabled()
        {
            object value = GetFieldValue(visualComponent, "headLightsEnabled");
            if (value is bool boolValue)
                return boolValue;
            if (value is int intValue)
                return intValue != 0;
            return false;
        }

        private void ApplyHeadLightObjects(bool enabled)
        {
            SetFieldObjectsActive(visualComponent, "headLightsOn", enabled);
            SetFieldObjectsActive(visualComponent, "headLightsOff", !enabled);
            SetObjectsActive(headlightOnObjects, enabled);
            SetObjectsActive(headlightOffObjects, !enabled);
        }

        private static bool HasField(Type type, string fieldName)
        {
            return type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
        }

        private static object GetFieldValue(object target, string fieldName)
        {
            if (target == null)
                return null;

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field != null ? field.GetValue(target) : null;
        }

        private static int CountFieldItems(object target, string fieldName)
        {
            object value = GetFieldValue(target, fieldName);
            if (value == null)
                return 0;

            if (value is ICollection collection)
                return collection.Count;

            if (value is IEnumerable enumerable)
            {
                int count = 0;
                foreach (object item in enumerable)
                {
                    if (item != null)
                        count++;
                }

                return count;
            }

            return 1;
        }

        private GameObject[] FindNamedHeadlightObjects(bool onObjects)
        {
            string[] names = onObjects
                ? new[] { "HeadlightOn", "LHeadLightGlow", "RHeadLightGlow" }
                : new[] { "HeadlightOff" };

            ArrayList matches = new ArrayList();
            for (int i = 0; i < names.Length; i++)
            {
                Transform found = FindDescendant(transform, names[i]);
                if (found != null)
                    matches.Add(found.gameObject);
            }

            GameObject[] result = new GameObject[matches.Count];
            for (int i = 0; i < matches.Count; i++)
                result[i] = matches[i] as GameObject;

            return result;
        }

        private static void SetObjectsActive(GameObject[] objects, bool active)
        {
            if (objects == null)
                return;

            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null)
                    objects[i].SetActive(active);
            }
        }

        private static void SetFieldObjectsActive(object target, string fieldName, bool active)
        {
            object value = GetFieldValue(target, fieldName);
            if (value == null)
                return;

            if (TrySetObjectActive(value, active))
                return;

            if (value is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                    TrySetObjectActive(item, active);
            }
        }

        private static bool TrySetObjectActive(object value, bool active)
        {
            if (value is GameObject gameObject)
            {
                gameObject.SetActive(active);
                return true;
            }

            if (value is Component component && component.gameObject != null)
            {
                component.gameObject.SetActive(active);
                return true;
            }

            return false;
        }

#if ENABLE_INPUT_SYSTEM
        private bool WasAuxiliaryHeadLightsPressedThisFrame()
        {
            if (!enableAuxiliaryHeadLightsButton || string.IsNullOrWhiteSpace(auxiliaryHeadLightsButtonControlPath))
            {
                lastAuxiliaryHeadLightsPressed = false;
                return false;
            }

            InputDevice device = FindAuxiliaryDevice();
            bool pressed = device != null && TryReadButton(device, auxiliaryHeadLightsButtonControlPath);
            bool pressedThisFrame = pressed && !lastAuxiliaryHeadLightsPressed;
            lastAuxiliaryHeadLightsPressed = pressed;
            return pressedThisFrame;
        }

        private void UpdateAuxiliaryDashboardControls()
        {
            InputDevice device = FindAuxiliaryDevice();
            if (device == null)
            {
                auxiliaryParkingLights = false;
                auxiliaryLowBeams = false;
                auxiliaryHighBeamFlash = false;
                auxiliaryLeftIndicator = false;
                auxiliaryRightIndicator = false;
                auxiliaryWiperSpeed = 0;
                return;
            }

            bool trigger = TryReadButton(device, parkingLightsControlPath);
            bool lowModifier = TryReadButton(device, lowBeamModifierButtonPath);
            bool highModifier = TryReadButton(device, highBeamFlashButtonPath);

            auxiliaryParkingLights = trigger;
            auxiliaryLowBeams = trigger && lowModifier;
            auxiliaryHighBeamFlash = trigger && lowModifier && highModifier;
            auxiliaryLeftIndicator = TryReadButton(device, leftIndicatorButtonPath);
            auxiliaryRightIndicator = TryReadButton(device, rightIndicatorButtonPath);

            if (TryReadButton(device, wiperSpeed3ButtonPath))
                auxiliaryWiperSpeed = 3;
            else if (TryReadButton(device, wiperSpeed2ButtonPath))
                auxiliaryWiperSpeed = 2;
            else if (TryReadButton(device, wiperSpeed1ButtonPath))
                auxiliaryWiperSpeed = 1;
            else
                auxiliaryWiperSpeed = 0;
        }

        private void ApplyHeadLightsFromLocalAndAuxiliary()
        {
            if (visualComponent == null)
                return;

            bool enabled = localHeadLightsEnabled || auxiliaryParkingLights || auxiliaryLowBeams || auxiliaryHighBeamFlash;
            SetFieldValue(visualComponent, "headLightsEnabled", enabled);
            ApplyHeadLightObjects(enabled);
        }

        private InputDevice FindAuxiliaryDevice()
        {
            foreach (InputDevice device in InputSystem.devices)
            {
                if (device == null || device is Keyboard || device is Mouse)
                    continue;

                string identity = (device.name + " " + device.displayName + " " + device.layout + " " + device.path + " " +
                                   device.description.product + " " + device.description.manufacturer + " " +
                                   device.description.interfaceName).ToLowerInvariant();
                if (MatchesAnyFilter(identity, auxiliaryDeviceNameOrPathContains))
                    return device;
            }

            return null;
        }

        private static bool TryReadButton(InputDevice device, string controlPath)
        {
            if (device == null || string.IsNullOrWhiteSpace(controlPath))
                return false;

            string trimmed = controlPath.Trim();
            InputControl control = device.TryGetChildControl<InputControl>(trimmed);
            if (control == null)
            {
                foreach (InputControl candidate in device.allControls)
                {
                    if (candidate.path.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase) ||
                        candidate.name.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        control = candidate;
                        break;
                    }
                }
            }

            if (control is ButtonControl button)
                return button.isPressed;
            if (control is AxisControl axis)
                return axis.ReadUnprocessedValue() > 0.5f;

            return false;
        }

        private static bool MatchesAnyFilter(string identity, string filters)
        {
            if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(filters))
                return false;

            string[] parts = filters.ToLowerInvariant().Split(';', ',');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length > 0 && identity.Contains(part))
                    return true;
            }

            return false;
        }
#endif

        private static Transform FindDescendant(Transform root, params string[] names)
        {
            if (root == null || names == null)
                return null;

            for (int i = 0; i < names.Length; i++)
            {
                Transform direct = root.Find(names[i]);
                if (direct != null)
                    return direct;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDescendant(root.GetChild(i), names);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static void SetFieldValue(object target, string fieldName, object value)
        {
            if (target == null)
                return;

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                return;

            try
            {
                object convertedValue = value;
                if (field.FieldType.IsEnum)
                    convertedValue = Enum.ToObject(field.FieldType, Convert.ToInt32(value));
                else if (field.FieldType == typeof(int))
                    convertedValue = value is bool boolValue ? (boolValue ? 1 : 0) : Convert.ToInt32(value);

                field.SetValue(target, convertedValue);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[VppBuiltInVehicleControls] Cannot configure '" + fieldName + "': " + exception.Message);
            }
        }
    }
}
