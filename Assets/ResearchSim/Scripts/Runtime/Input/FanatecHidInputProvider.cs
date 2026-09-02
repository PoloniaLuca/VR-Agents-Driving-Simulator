using System;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace ResearchSim
{
    /// <summary>
    /// Configurable Unity Input System reader for Fanatec/HID wheel rigs. It
    /// does not use vendor SDKs and remains inactive when no matching device is
    /// connected or when a mapping entry is left empty.
    /// </summary>
    public sealed class FanatecHidInputProvider : MonoBehaviour, IDrivingInputProvider
    {
        [Header("Mapping")]
        public FanatecInputMapping mapping;
        public bool enableProvider = true;
        public bool logDevicesOnStart = true;

        [Header("Auto activation")]
        public float inputActivationThreshold = 0.0001f;
        public float quietAxisThreshold = 0.03f;

        public string ProviderName => activeDeviceName;
        public bool IsAvailable => enableProvider && mapping != null && ActiveDeviceFound;
        public bool ActiveDeviceFound { get; private set; }
        public string LastDevicePath { get; private set; } = string.Empty;
        public float LastSteeringRaw { get; private set; }
        public float LastSteeringNormalized { get; private set; }
        public bool LastSteeringControlFound { get; private set; }
        public int LastDiagnosticGear { get; private set; } = int.MinValue;
        public string LastGearDebug { get; private set; } = "-";

        private string activeDeviceName = "Fanatec/HID";
        private bool warnedNoMapping;

        private void Start()
        {
            if (logDevicesOnStart)
                LogMatchingDevices();
        }

        public bool TryGetInput(out DrivingInputState state)
        {
            state = default;
            state.SourceName = activeDeviceName;

            if (!enableProvider || mapping == null)
            {
                if (!warnedNoMapping && enableProvider)
                {
                    warnedNoMapping = true;
                    Debug.LogWarning("[FanatecHidInputProvider] No FanatecInputMapping assigned. Provider is inactive.");
                }
                ActiveDeviceFound = false;
                ClearDiagnostics();
                return false;
            }

#if ENABLE_INPUT_SYSTEM
            InputDevice[] devices = FindDevices();
            if (devices.Length == 0)
            {
                ActiveDeviceFound = false;
                ClearDiagnostics();
                return false;
            }

            ActiveDeviceFound = true;
            LastDevicePath = FormatDevicePaths(devices);
            activeDeviceName = FormatDeviceNames(devices);

            state.SourceName = activeDeviceName;
            state.Steering = ReadSignedAxis(devices, mapping.steering, 0f, out float steeringRaw, out bool steeringFound);
            LastSteeringRaw = steeringRaw;
            LastSteeringNormalized = state.Steering;
            LastSteeringControlFound = steeringFound;
            state.Throttle = ReadPedal(devices, mapping.throttle, 0f);
            state.Brake = ReadPedal(devices, mapping.brake, 0f);
            state.Clutch = ReadPedal(devices, mapping.clutch, 0f);
            state.Handbrake = ReadPedal(devices, mapping.handbrakeAxis, 0f);

            if (state.Handbrake <= quietAxisThreshold && ReadButton(devices, mapping.handbrakeButton))
                state.Handbrake = 1f;

            state.HasIgnition = HasButtonPath(mapping.ignition);
            state.Ignition = state.HasIgnition && ReadButton(devices, mapping.ignition);
            state.GearUp = ReadButton(devices, mapping.gearUp);
            state.GearDown = ReadButton(devices, mapping.gearDown);
            ReadGear(devices, ref state);
            state.Clamp();

            return state.HasDrivingInput(inputActivationThreshold);
#else
            ActiveDeviceFound = false;
            ClearDiagnostics();
            return false;
#endif
        }

        private void ClearDiagnostics()
        {
            LastSteeringRaw = 0f;
            LastSteeringNormalized = 0f;
            LastSteeringControlFound = false;
            LastDiagnosticGear = int.MinValue;
            LastGearDebug = "-";
        }

        public void LogMatchingDevices()
        {
#if ENABLE_INPUT_SYSTEM
            foreach (InputDevice device in InputSystem.devices)
                Debug.Log("[Input] Device: " + FormatDeviceName(device) + " path=" + device.path + " layout=" + device.layout);
#else
            Debug.LogWarning("[FanatecHidInputProvider] Unity Input System is not enabled in this build.");
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private InputDevice[] FindDevices()
        {
            List<InputDevice> matches = new List<InputDevice>();
            bool hasManualFilter = !string.IsNullOrWhiteSpace(mapping.manualDeviceNameOrPathContains);

            foreach (InputDevice device in InputSystem.devices)
            {
                if (device == null || device is Keyboard || device is Mouse || device is Gamepad)
                    continue;

                string identity = GetDeviceIdentity(device);
                bool manualMatch = hasManualFilter && identity.Contains(mapping.manualDeviceNameOrPathContains.ToLowerInvariant());
                bool automaticMatch = !hasManualFilter && MatchesKeywords(identity);

                if (manualMatch || automaticMatch)
                    matches.Add(device);
            }

            return matches.ToArray();
        }

        private bool MatchesKeywords(string identity)
        {
            if (mapping.deviceMatchKeywords == null)
                return false;

            for (int i = 0; i < mapping.deviceMatchKeywords.Length; i++)
            {
                string keyword = mapping.deviceMatchKeywords[i];
                if (!string.IsNullOrWhiteSpace(keyword) && identity.Contains(keyword.ToLowerInvariant()))
                    return true;
            }

            return false;
        }

        private static string GetDeviceIdentity(InputDevice device)
        {
            return (device.name + " " +
                    device.displayName + " " +
                    device.layout + " " +
                    device.path + " " +
                    device.description.product + " " +
                    device.description.manufacturer + " " +
                    device.description.interfaceName).ToLowerInvariant();
        }

        private static string FormatDeviceName(InputDevice device)
        {
            return string.IsNullOrEmpty(device.displayName) ? device.name : device.displayName;
        }

        private static string FormatDeviceNames(InputDevice[] devices)
        {
            if (devices == null || devices.Length == 0)
                return "Fanatec/HID";

            string result = FormatDeviceName(devices[0]);
            for (int i = 1; i < devices.Length; i++)
                result += " + " + FormatDeviceName(devices[i]);

            return result;
        }

        private static string FormatDevicePaths(InputDevice[] devices)
        {
            if (devices == null || devices.Length == 0)
                return string.Empty;

            string result = devices[0].path;
            for (int i = 1; i < devices.Length; i++)
                result += ";" + devices[i].path;

            return result;
        }

        private float ReadSignedAxis(InputDevice[] devices, FanatecInputMapping.AxisBinding binding, float fallback, out float raw, out bool found)
        {
            raw = 0f;
            found = TryReadControlValue(devices, binding.controlPath, out raw);
            if (!found)
                return fallback;

            return DrivingInputState.NormalizeSignedAxis(raw, binding.rawMin, binding.rawMax, binding.invert, binding.deadzone);
        }

        private float ReadPedal(InputDevice[] devices, FanatecInputMapping.AxisBinding binding, float fallback)
        {
            if (!TryReadControlValue(devices, binding.controlPath, out float raw))
                return fallback;

            return DrivingInputState.NormalizePedal(raw, binding.rawMin, binding.rawMax, binding.invert, binding.deadzone);
        }

        private bool ReadButton(InputDevice[] devices, FanatecInputMapping.ButtonBinding binding)
        {
            if (!HasButtonPath(binding))
                return false;

            InputControl control = ResolveControl(devices, binding.controlPath);
            if (control is ButtonControl button)
                return button.isPressed;

            if (control is AxisControl axis)
                return axis.ReadValue() > 0.5f;

            return false;
        }

        private bool TryReadControlValue(InputDevice[] devices, string controlPath, out float value)
        {
            value = 0f;
            InputControl control = ResolveControl(devices, controlPath);
            if (control == null)
                return false;

            if (control is AxisControl axis)
            {
                // Fanatec DD1 can expose a processed axis that stays at zero
                // for large steering angles while the unprocessed HID value
                // changes immediately. Use the unprocessed value here and let
                // FanatecInputMapping handle normalization/deadzone explicitly.
                value = axis.ReadUnprocessedValue();
                return true;
            }

            if (control is ButtonControl button)
            {
                value = button.ReadValue();
                return true;
            }

            object rawObject = control.ReadValueAsObject();
            if (rawObject is float rawFloat)
            {
                value = rawFloat;
                return true;
            }

            if (rawObject is double rawDouble)
            {
                value = (float)rawDouble;
                return true;
            }

            if (rawObject is int rawInt)
            {
                value = rawInt;
                return true;
            }

            return false;
        }

        private InputControl ResolveControl(InputDevice[] devices, string controlPath)
        {
            if (devices == null || string.IsNullOrWhiteSpace(controlPath))
                return null;

            for (int i = 0; i < devices.Length; i++)
            {
                InputControl control = ResolveControl(devices[i], controlPath);
                if (control != null)
                    return control;
            }

            return null;
        }

        private InputControl ResolveControl(InputDevice device, string controlPath)
        {
            if (device == null || string.IsNullOrWhiteSpace(controlPath))
                return null;

            try
            {
                string trimmed = controlPath.Trim();
                if (trimmed.StartsWith("<", StringComparison.Ordinal))
                {
                    int devicePrefixEnd = trimmed.IndexOf(">/", StringComparison.Ordinal);
                    if (devicePrefixEnd >= 0)
                        trimmed = trimmed.Substring(devicePrefixEnd + 2);
                }

                InputControl direct = device.TryGetChildControl<InputControl>(trimmed);
                if (direct != null)
                    return direct;

                foreach (InputControl control in device.allControls)
                {
                    if (control.path.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase) ||
                        control.name.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
                        return control;
                }

                return null;
            }
            catch (Exception exception)
            {
                if (mapping.logWarningsForMissingControls)
                    Debug.LogWarning("[FanatecHidInputProvider] Cannot resolve control '" + controlPath + "': " + exception.Message);
                return null;
            }
        }

        private void ReadGear(InputDevice[] devices, ref DrivingInputState state)
        {
            LastDiagnosticGear = int.MinValue;
            LastGearDebug = "-";
            if (ReadButton(devices, mapping.reverse)) { SetGear(ref state, DrivingGear.Reverse, "R", mapping.reverse); return; }
            if (ReadButton(devices, mapping.neutral)) { SetGear(ref state, DrivingGear.Neutral, "N", mapping.neutral); return; }
            if (ReadButton(devices, mapping.gear1)) { SetGear(ref state, DrivingGear.Gear1, "1", mapping.gear1); return; }
            if (ReadButton(devices, mapping.gear2)) { SetGear(ref state, DrivingGear.Gear2, "2", mapping.gear2); return; }
            if (ReadButton(devices, mapping.gear3)) { SetGear(ref state, DrivingGear.Gear3, "3", mapping.gear3); return; }
            if (ReadButton(devices, mapping.gear4)) { SetGear(ref state, DrivingGear.Gear4, "4", mapping.gear4); return; }
            if (ReadButton(devices, mapping.gear5)) { SetGear(ref state, DrivingGear.Gear5, "5", mapping.gear5); return; }
            if (ReadButton(devices, mapping.gear6)) { SetGear(ref state, DrivingGear.Gear6, "6", mapping.gear6); return; }
            // The H-shifter's possible seventh position is useful diagnostic
            // information, but the current VPP vehicle prefab is not configured
            // for a seventh gear, so do not apply it to the drivetrain.
            if (ReadButton(devices, mapping.gear7))
            {
                LastDiagnosticGear = 7;
                LastGearDebug = FormatGearDebug("7 diagnostic", mapping.gear7);
                return;
            }

            if (mapping != null && mapping.HasAnyHPatternBinding())
                SetGear(ref state, DrivingGear.Neutral, "N fallback", mapping.neutral);
        }

        private void SetGear(ref DrivingInputState state, DrivingGear gear, string label, FanatecInputMapping.ButtonBinding binding)
        {
            state.Gear = gear;
            state.HasGear = true;
            LastGearDebug = FormatGearDebug(label, binding);
            LastDiagnosticGear = (int)gear;
        }

        private static string FormatGearDebug(string label, FanatecInputMapping.ButtonBinding binding)
        {
            string path = binding != null && !string.IsNullOrWhiteSpace(binding.controlPath) ? binding.controlPath : "<no path>";
            return label + " @ " + path;
        }

        private static bool HasButtonPath(FanatecInputMapping.ButtonBinding binding)
        {
            return binding != null && !string.IsNullOrWhiteSpace(binding.controlPath);
        }
#endif
    }
}
