using System;
using System.Reflection;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace ResearchSim
{
    /// <summary>
    /// Adapter between Unity input devices and Vehicle Physics Pro. It keeps
    /// the default keyboard behaviour available, adds gamepad/wheel fallback
    /// input, and exposes a simple automatic/manual mode switch without
    /// replacing VPP's physics model.
    /// </summary>
    public sealed class VppExternalInputBridge : MonoBehaviour
    {
        public enum DriveMode
        {
            Automatic,
            ManualPhysical
        }

        public enum ExternalController
        {
            Auto,
            Fanatec,
            G29
        }

        [Header("External Controller")]
        public ExternalController externalController = ExternalController.Auto;
        public bool enableG29HidInput = true;
        public G29HidInputProvider g29Provider;

        [Header("VPP")]
        public MonoBehaviour standardInput;
        public MonoBehaviour vehicleController;

        [Header("Drive mode")]
        public DriveMode driveMode = DriveMode.Automatic;
        public bool showDriveModeHud = true;

        [Header("Transmission mode")]
        public TransmissionModeManager transmissionModeManager;
        public TransmissionMode fallbackTransmissionMode = TransmissionMode.Automatic;

        [Header("Generic wheel fallback")]
        public bool enableGenericWheelInput;
        public bool mapFirstWheelButtonsToHPattern = true;
        public bool invertThrottlePedal;
        public bool invertBrakePedal;
        public bool invertClutchPedal;
        [Range(1f, 2.5f)] public float externalSteeringSensitivity = 1.65f;

        [Header("Fanatec / HID")]
        public bool enableFanatecHidInput = true;
        public FanatecHidInputProvider fanatecProvider;
        public bool preferMappedFanatecProviderOverGenericWheel = true;
        public bool autoSelectHPatternModeOnShifterInput = false;
        [Range(1f, 3f)] public float fanatecSteeringSensitivity = 2.05f;

        [Header("Auxiliary HID / Arduino")]
        public bool enableAuxiliaryIgnitionInput = true;
        public string auxiliaryDeviceNameOrPathContains = "teensy;arduino";
        public string auxiliaryIgnitionRunControlPath = "button18";
        public string auxiliaryIgnitionStartControlPath = "button17";

        [Header("Experiment ignition gate")]
        public bool experimentHoldIgnitionOff;
        public bool experimentThrottleDisabled;
        public KeyCode keyboardIgnitionReleaseKey = KeyCode.K;
        [Min(0.1f)] public float experimentStartPulseSeconds = 2.0f;

        public float sourceSwitchDebounceSeconds = 0.25f;
        public float externalInputThreshold = 0.0001f;
        public string activeExternalInputSource = "None";
        public string requestedGearStatus = "N";
        public string appliedGearStatus = "N";
        public string ignitionStatus = "AUTO";
        public float LastFanatecSteering { get; private set; }
        public bool LastFanatecSteeringActive { get; private set; }

        private const int VppGearModeManual = 0;
        private const int VppGearModeReverse = 2;
        private const int VppGearModeNeutral = 3;
        private const int VppGearModeDrive = 4;
        private const int VppGearboxManual = 0;
        private const int VppGearboxAutomatic = 1;
        private const int VppForceAutoShift = 1;
        private const int VppForceManualShift = 2;
        private const float ManualClutchMaxTorqueTransfer = 280f;

        private FieldInfo externalSteerField;
        private FieldInfo externalThrottleField;
        private FieldInfo externalBrakeField;
        private FieldInfo externalClutchField;
        private FieldInfo externalHandbrakeField;
        private FieldInfo externalIgnitionField;
        private MethodInfo setGearMethod;
        private MonoBehaviour vehicleBase;
        private object vehicleDataBus;
        private MethodInfo vehicleDataSetMethod;
        private MethodInfo vehicleDataGetMethod;
        private int channelInput = int.MinValue;
        private int channelSettings = int.MinValue;
        private int inputAutomaticGear = int.MinValue;
        private int inputManualGear = int.MinValue;
        private int channelVehicle = int.MinValue;
        private int vehicleGearboxGear = int.MinValue;
        private int vehicleGearboxMode = int.MinValue;
        private int settingsAutoShiftOverride = int.MinValue;
        private float startTimer = 1.2f;
        private bool previousAuxiliaryIgnitionStart;
        private bool auxiliaryIgnitionStateInitialized;
        private DriveMode lastAppliedDriveMode = (DriveMode)(-1);
        private TransmissionMode lastAppliedTransmissionMode = (TransmissionMode)(-1);
        private float lastExternalSourceSwitchTime;
        private string lastExternalInputSource = "None";
        private int lastAppliedHPatternGear = int.MinValue;
        private int lastRequestedHPatternGear = int.MinValue;
        private float hudSteering;
        private float hudThrottle;
        private float hudBrake;
        private float hudClutch;
        private float hudHandbrake;
        private float hudVppSteering;
        private float hudFanatecRawSteering;
        private float hudFanatecMappedSteering;
        private bool hudFanatecSteeringFound;
        private int hudFanatecDiagnosticGear = int.MinValue;
        private string hudFanatecGearDebug = "-";
        private int hudVppInputAutomaticGear = int.MinValue;
        private int hudVppInputManualGear = int.MinValue;
        private int hudVppVehicleGear = int.MinValue;
        private int hudVppVehicleGearMode = int.MinValue;
        private int hudVppForwardGearCount = int.MinValue;
        private bool warnedMissingHPatternMapping;
        private GUIStyle hudBoxStyle;
        private GUIStyle hudLabelStyle;

        private void Awake()
        {
            ResolveReferences();
            ResolveTransmissionModeManager();
        }

        private void Update()
        {
            if (standardInput == null || vehicleController == null)
                ResolveReferences();

            TransmissionMode transmissionMode = CurrentTransmissionMode;

            // The bridge only writes VPP's external input fields when an
            // external device is active. Keyboard input remains VPP-native.
            ApplyTransmissionModeIfNeeded(transmissionMode);
            ApplyIgnitionInput();

#if ENABLE_INPUT_SYSTEM
            ExternalInputSample selected = ExternalInputSample.Inactive;
            ExternalInputSample fanatecSample = ExternalInputSample.Inactive;

            if (TryReadGamepad(out float steer, out float throttle, out float brake, out float clutch))
                selected = ExternalInputSample.Merge(selected, new ExternalInputSample("Gamepad", steer, throttle, brake, clutch, 0f));

            bool fanatecActive = false;
            bool g29Active = false;

            if ((externalController == ExternalController.Auto || externalController == ExternalController.Fanatec) &&
                enableFanatecHidInput &&
                ResolveFanatecProvider() != null &&
                fanatecProvider.TryGetInput(out DrivingInputState fanatecState))
            {
                LastFanatecSteering = Mathf.Clamp(fanatecState.Steering, -1f, 1f);
                LastFanatecSteeringActive = true;
                fanatecState.Steering = ScaleFanatecSteeringForVpp(fanatecState.Steering);
                fanatecSample = ExternalInputSample.FromState(fanatecState);
                fanatecActive = fanatecSample.Active;
                selected = ExternalInputSample.Merge(selected, fanatecSample);
            }
            else
            {
                LastFanatecSteering = 0f;
                LastFanatecSteeringActive = false;
            }

            ExternalInputSample g29Sample = ExternalInputSample.Inactive;
            if (!fanatecActive &&
                (externalController == ExternalController.Auto || externalController == ExternalController.G29) &&
                enableG29HidInput &&
                ResolveG29Provider() != null &&
                g29Provider.TryGetInput(out DrivingInputState g29State))
            {
                g29Sample = ExternalInputSample.FromState(g29State);
                g29Active = g29Sample.Active;
                selected = ExternalInputSample.Merge(selected, g29Sample);
            }

            bool skipGenericWheel =
                preferMappedFanatecProviderOverGenericWheel &&
                (fanatecSample.Active || g29Sample.Active);
            if (!skipGenericWheel && enableGenericWheelInput && TryReadWheel(out float wheelSteer, out float wheelThrottle, out float wheelBrake, out float wheelClutch))
                selected = ExternalInputSample.Merge(selected, new ExternalInputSample("Generic HID wheel", wheelSteer, wheelThrottle, wheelBrake, wheelClutch, 0f));

            selected = DebounceExternalSource(selected);
            activeExternalInputSource = selected.Active ? selected.SourceName : "None";
            UpdateInputHud(selected);

            if (selected.Active)
            {
                float vppSteering = Mathf.Clamp(selected.Steering * externalSteeringSensitivity, -1f, 1f);
                hudVppSteering = vppSteering;
                SetInputField(externalSteerField, vppSteering);
                SetInputField(externalThrottleField, experimentThrottleDisabled ? 0f : Mathf.Clamp01(selected.Throttle));
                SetInputField(externalBrakeField, Mathf.Clamp01(selected.Brake));
                SetInputField(externalClutchField, UsesPhysicalClutch(transmissionMode) ? Mathf.Clamp01(selected.Clutch) : 0f);
                SetInputField(externalHandbrakeField, Mathf.Clamp01(selected.Handbrake));

                if (!enableAuxiliaryIgnitionInput && selected.HasIgnition)
                {
                    ignitionStatus = selected.Ignition ? "ON" : "OFF";
                    SetEnumField(externalIgnitionField, selected.Ignition ? 1 : 0);
                }

                ApplyExternalGearInput(selected, transmissionMode);
            }
            else
            {
                hudVppSteering = 0f;
                ClearExternalInputFields();
            }

            ReadGenericWheelHPatternButtons(transmissionMode);
#endif

            if (experimentThrottleDisabled)
                ForceThrottleZero();
        }

        private void LateUpdate()
        {
            if (experimentThrottleDisabled)
                ForceThrottleZero();
        }

        private void ResolveReferences()
        {
            if (standardInput == null)
                standardInput = FindComponentByTypeName("VehiclePhysics.VPStandardInput");
            if (vehicleController == null)
                vehicleController = FindComponentByTypeName("VehiclePhysics.VPVehicleController");
            if (vehicleBase == null)
                vehicleBase = FindComponentByTypeName("VehiclePhysics.VehicleBase") ?? FindComponentAssignableToTypeName("VehiclePhysics.VehicleBase");

            if (standardInput != null)
            {
                Type inputType = standardInput.GetType();
                externalSteerField = inputType.GetField("externalSteer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                externalThrottleField = inputType.GetField("externalThrottle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                externalBrakeField = inputType.GetField("externalBrake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                externalClutchField = inputType.GetField("externalClutch", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                externalHandbrakeField = inputType.GetField("externalHandbrake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                                         inputType.GetField("externalHandBrake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                                         inputType.GetField("externalHandbrakeInput", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                externalIgnitionField = inputType.GetField("externalIgnition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            if (vehicleController != null)
            {
                Type vehicleType = vehicleController.GetType();
                setGearMethod = FindMethod(vehicleType, "SetGear", 1);
            }

            ResolveVppDataBus();
        }

        private void ApplyTransmissionModeIfNeeded(TransmissionMode transmissionMode)
        {
            if (vehicleController == null || lastAppliedTransmissionMode == transmissionMode)
                return;

            lastAppliedTransmissionMode = transmissionMode;
            driveMode = transmissionMode == TransmissionMode.Automatic ? DriveMode.Automatic : DriveMode.ManualPhysical;
            lastAppliedDriveMode = driveMode;

            bool automatic = transmissionMode == TransmissionMode.Automatic;
            bool physicalClutch = UsesPhysicalClutch(transmissionMode);
            SetNestedField(vehicleController, "gearbox", "type", automatic ? VppGearboxAutomatic : VppGearboxManual);
            SetNestedField(vehicleController, "gearbox", "autoShift", automatic);
            SetNestedField(vehicleController, "clutch", "type", physicalClutch ? 1 : 3);
            SetNestedField(vehicleController, "clutch", "maxTorqueTransfer", physicalClutch ? ManualClutchMaxTorqueTransfer : 280f);
            SetInputField(externalClutchField, 0f);
            SetVppAutomaticGearMode(automatic ? VppGearModeDrive : VppGearModeManual);
            SetVppAutoShiftOverride(automatic ? 0 : VppForceManualShift);
            lastAppliedHPatternGear = int.MinValue;
            lastRequestedHPatternGear = int.MinValue;

            if (IsHPatternMode(transmissionMode))
            {
                requestedGearStatus = "N";
                appliedGearStatus = "N";
                ApplyVppManualGear(0);
            }
            else
            {
                requestedGearStatus = "AUTO D";
                appliedGearStatus = "AUTO D";
            }

            WarnIfHPatternMappingMissing(transmissionMode);
        }

        private TransmissionModeManager ResolveTransmissionModeManager()
        {
            if (transmissionModeManager == null)
                transmissionModeManager = FindAnyObjectByType<TransmissionModeManager>();

            return transmissionModeManager;
        }

        private TransmissionMode CurrentTransmissionMode
        {
            get
            {
                if (ResolveTransmissionModeManager() != null)
                    return transmissionModeManager.CurrentMode;

                return fallbackTransmissionMode;
            }
        }

        private MonoBehaviour FindComponentByTypeName(string fullName)
        {
            MonoBehaviour[] components = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < components.Length; i++)
            {
                MonoBehaviour component = components[i];
                if (component != null && component.GetType().FullName == fullName)
                    return component;
            }

            return null;
        }

        private MonoBehaviour FindComponentAssignableToTypeName(string fullName)
        {
            MonoBehaviour[] components = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < components.Length; i++)
            {
                MonoBehaviour component = components[i];
                if (component != null && IsTypeOrBaseNamed(component.GetType(), fullName))
                    return component;
            }

            return null;
        }

        private static bool IsTypeOrBaseNamed(Type type, string fullName)
        {
            while (type != null)
            {
                if (type.FullName == fullName)
                    return true;
                type = type.BaseType;
            }

            return false;
        }

        private MethodInfo FindMethod(Type type, string name, int parameterCount)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name == name && method.GetParameters().Length == parameterCount)
                    return method;
            }

            return null;
        }

        public void SetExternalController(ExternalController controller)
        {
            externalController = controller;
            lastExternalInputSource = "None";
            lastExternalSourceSwitchTime = 0f;
            activeExternalInputSource = "None";
        }

        private G29HidInputProvider ResolveG29Provider()
        {
            if (g29Provider == null)
                g29Provider = GetComponentInChildren<G29HidInputProvider>(true);

            return g29Provider;
        }

        private FanatecHidInputProvider ResolveFanatecProvider()
        {
            if (fanatecProvider == null)
                fanatecProvider = GetComponentInChildren<FanatecHidInputProvider>(true);

            return fanatecProvider;
        }

        private float ScaleFanatecSteeringForVpp(float steering)
        {
            float baseSensitivity = Mathf.Max(0.001f, externalSteeringSensitivity);
            float fanatecSensitivity = Mathf.Max(baseSensitivity, fanatecSteeringSensitivity);
            return Mathf.Clamp(steering * (fanatecSensitivity / baseSensitivity), -1f, 1f);
        }

        private void ApplyIgnitionInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (enableAuxiliaryIgnitionInput && TryReadAuxiliaryIgnition(out bool auxRun, out bool auxStart))
            {
                if (!auxiliaryIgnitionStateInitialized)
                    startTimer = 0f;

                bool startPressedThisFrame = auxiliaryIgnitionStateInitialized &&
                                             auxStart &&
                                             !previousAuxiliaryIgnitionStart;
                previousAuxiliaryIgnitionStart = auxStart;
                auxiliaryIgnitionStateInitialized = true;
                ApplyAuxiliaryIgnitionInput(auxRun, auxStart, startPressedThisFrame);
                return;
            }

            if (auxiliaryIgnitionStateInitialized)
                startTimer = 0f;

            previousAuxiliaryIgnitionStart = false;
            auxiliaryIgnitionStateInitialized = false;
#endif

            if (experimentHoldIgnitionOff)
            {
                if (Input.GetKeyDown(keyboardIgnitionReleaseKey))
                {
                    ReleaseExperimentIgnitionHold(true);
                }
                else
                {
                    ignitionStatus = "OFF (experiment wait)";
                    SetEnumField(externalIgnitionField, 0);
                    return;
                }
            }

            KeepEngineStartedFallback();
        }

#if ENABLE_INPUT_SYSTEM
        private void ApplyAuxiliaryIgnitionInput(bool run, bool start, bool startPressedThisFrame)
        {
            if (!run && !start)
            {
                startTimer = 0f;
                ignitionStatus = experimentHoldIgnitionOff ? "OFF (experiment wait)" : "OFF";
                SetEnumField(externalIgnitionField, 0);
                return;
            }

            if (experimentHoldIgnitionOff)
            {
                if (startPressedThisFrame)
                    ReleaseExperimentIgnitionHold(true);
                else if (run)
                    ReleaseExperimentIgnitionHold(false);
                else
                {
                    ignitionStatus = "OFF (experiment wait)";
                    SetEnumField(externalIgnitionField, 0);
                    return;
                }
            }
            else if (startPressedThisFrame)
            {
                ReleaseExperimentIgnitionHold(true);
            }

            startTimer = Mathf.Max(0f, startTimer - Time.deltaTime);
            bool pulseActive = startTimer > 0f;
            ignitionStatus = pulseActive ? "START" : "ON";
            SetEnumField(externalIgnitionField, pulseActive ? 2 : 1);
        }
#endif

        public void HoldIgnitionOffForExperiment()
        {
            experimentHoldIgnitionOff = true;
            startTimer = 0f;
            ignitionStatus = "OFF (experiment wait)";
            SetEnumField(externalIgnitionField, 0);
            ClearExternalInputFields();
        }

        public void ReleaseExperimentIgnitionHold(bool requestStart)
        {
            experimentHoldIgnitionOff = false;
            startTimer = requestStart ? Mathf.Max(0.1f, experimentStartPulseSeconds) : 0f;
            if (requestStart)
            {
                ignitionStatus = "AUTO START";
                SetEnumField(externalIgnitionField, 2);
            }
        }

        public void SetExperimentThrottleDisabled(bool disabled)
        {
            experimentThrottleDisabled = disabled;
            if (disabled)
                ForceThrottleZero();
        }

        private void ForceThrottleZero()
        {
            SetInputField(externalThrottleField, 0f);
            SetFloatMember(standardInput, 0f, "throttleInput", "gasInput", "accelInput", "accelerationInput", "verticalInput");
            hudThrottle = 0f;
        }

        private void KeepEngineStartedFallback()
        {
            // VPP needs a short start command at scene load, then normal running
            // ignition. This preserves the working "press accelerator and go"
            // baseline behaviour.
            if (externalIgnitionField == null || standardInput == null)
                return;

            startTimer -= Time.deltaTime;
            ignitionStatus = startTimer > 0f ? "AUTO START" : "AUTO ON";
            SetEnumField(externalIgnitionField, startTimer > 0f ? 2 : 1);
        }

        private static bool IsHPatternMode(TransmissionMode mode)
        {
            return mode == TransmissionMode.ManualHPatternEasy || mode == TransmissionMode.ManualHPatternRealistic;
        }

        private static bool UsesPhysicalClutch(TransmissionMode mode)
        {
            return mode == TransmissionMode.ManualHPatternRealistic;
        }

        private void WarnIfHPatternMappingMissing(TransmissionMode mode)
        {
            if (!IsHPatternMode(mode))
            {
                warnedMissingHPatternMapping = false;
                return;
            }

            FanatecHidInputProvider provider = ResolveFanatecProvider();
            if (provider == null || provider.mapping == null || provider.mapping.HasAnyHPatternBinding())
                return;

            if (warnedMissingHPatternMapping)
                return;

            warnedMissingHPatternMapping = true;
            Debug.LogWarning("[VppExternalInputBridge] H-pattern mode selected, but no H-pattern gear buttons are configured in FanatecInputMapping.");
        }

        private void SetInputField(FieldInfo field, float value)
        {
            try
            {
                if (field != null && standardInput != null)
                    field.SetValue(standardInput, value);
            }
            catch (Exception)
            {
            }
        }

        private static void SetFloatMember(object target, float value, params string[] names)
        {
            if (target == null || names == null)
                return;

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            Type type = target.GetType();
            for (int i = 0; i < names.Length; i++)
            {
                PropertyInfo property = type.GetProperty(names[i], Flags);
                if (property != null && property.CanWrite && IsFloatCompatible(property.PropertyType))
                {
                    try
                    {
                        property.SetValue(target, Convert.ChangeType(value, property.PropertyType));
                    }
                    catch (Exception)
                    {
                    }
                }

                FieldInfo field = type.GetField(names[i], Flags);
                if (field != null && IsFloatCompatible(field.FieldType))
                {
                    try
                    {
                        field.SetValue(target, Convert.ChangeType(value, field.FieldType));
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private static bool IsFloatCompatible(Type type)
        {
            return type == typeof(float) || type == typeof(double) || type == typeof(int);
        }

        private void ClearExternalInputFields()
        {
            SetInputField(externalSteerField, 0f);
            SetInputField(externalThrottleField, 0f);
            SetInputField(externalBrakeField, 0f);
            SetInputField(externalClutchField, 0f);
            SetInputField(externalHandbrakeField, 0f);
        }

        private void SetEnumField(FieldInfo field, int enumValue)
        {
            if (field == null || standardInput == null)
                return;

            Type fieldType = field.FieldType;
            object boxedValue = fieldType.IsEnum ? Enum.ToObject(fieldType, enumValue) : enumValue;
            try
            {
                field.SetValue(standardInput, boxedValue);
            }
            catch (Exception)
            {
            }
        }

        private void SetNestedField(MonoBehaviour target, string parentFieldName, string childFieldName, object value)
        {
            try
            {
                if (target == null)
                    return;

                Type targetType = target.GetType();
                FieldInfo parentField = targetType.GetField(parentFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (parentField == null)
                    return;

                object parentValue = parentField.GetValue(target);
                if (parentValue == null)
                    return;

                FieldInfo childField = parentValue.GetType().GetField(childFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (childField == null)
                    return;

                object convertedValue = value;
                if (childField.FieldType.IsEnum)
                    convertedValue = Enum.ToObject(childField.FieldType, Convert.ToInt32(value));
                else if (childField.FieldType == typeof(float))
                    convertedValue = Convert.ToSingle(value);
                else if (childField.FieldType == typeof(bool))
                    convertedValue = Convert.ToBoolean(value);

                childField.SetValue(parentValue, convertedValue);
                parentField.SetValue(target, parentValue);
            }
            catch (Exception)
            {
            }
        }

        private void ResolveVppDataBus()
        {
            if (vehicleDataBus != null &&
                vehicleDataSetMethod != null &&
                channelInput != int.MinValue &&
                channelSettings != int.MinValue &&
                channelVehicle != int.MinValue &&
                inputAutomaticGear != int.MinValue &&
                inputManualGear != int.MinValue &&
                vehicleGearboxGear != int.MinValue &&
                vehicleGearboxMode != int.MinValue &&
                settingsAutoShiftOverride != int.MinValue)
                return;

            object dataOwner = vehicleBase != null ? vehicleBase : vehicleController;
            if (dataOwner == null)
                return;

            Type ownerType = dataOwner.GetType();
            FieldInfo dataField = ownerType.GetField("data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            PropertyInfo dataProperty = ownerType.GetProperty("data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            vehicleDataBus = dataField != null ? dataField.GetValue(dataOwner) : dataProperty != null ? dataProperty.GetValue(dataOwner, null) : null;
            if (vehicleDataBus == null)
                return;

            vehicleDataSetMethod = FindMethod(vehicleDataBus.GetType(), "Set", 3);
            vehicleDataGetMethod = FindMethod(vehicleDataBus.GetType(), "Get", 2);
            channelInput = ReadStaticInt("VehiclePhysics.Channel", "Input", channelInput);
            channelSettings = ReadStaticInt("VehiclePhysics.Channel", "Settings", channelSettings);
            channelVehicle = ReadStaticInt("VehiclePhysics.Channel", "Vehicle", channelVehicle);
            inputAutomaticGear = ReadStaticInt("VehiclePhysics.InputData", "AutomaticGear", inputAutomaticGear);
            inputManualGear = ReadStaticInt("VehiclePhysics.InputData", "ManualGear", inputManualGear);
            vehicleGearboxGear = ReadStaticInt("VehiclePhysics.VehicleData", "GearboxGear", vehicleGearboxGear);
            vehicleGearboxMode = ReadStaticInt("VehiclePhysics.VehicleData", "GearboxMode", vehicleGearboxMode);
            settingsAutoShiftOverride = ReadStaticInt("VehiclePhysics.SettingsData", "AutoShiftOverride", settingsAutoShiftOverride);
        }

        private int ReadStaticInt(string typeName, string fieldName, int fallback)
        {
            Type type = FindType(typeName);
            if (type == null)
                return fallback;

            FieldInfo field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                return fallback;

            try
            {
                return Convert.ToInt32(field.GetValue(null));
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private static Type FindType(string fullName)
        {
            Type type = Type.GetType(fullName);
            if (type != null)
                return type;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(fullName);
                if (type != null)
                    return type;
            }

            return null;
        }

        private void SetVppAutomaticGearMode(int gearMode)
        {
            SetVppData(channelInput, inputAutomaticGear, gearMode);
        }

        private void SetVppAutoShiftOverride(int overrideMode)
        {
            SetVppData(channelSettings, settingsAutoShiftOverride, overrideMode);
        }

        private void SetVppManualGearInput(int gear)
        {
            SetVppData(channelInput, inputManualGear, gear);
        }

        private void SetVppData(int channel, int dataId, int value)
        {
            try
            {
                ResolveVppDataBus();
                if (vehicleDataBus == null || vehicleDataSetMethod == null || channel == int.MinValue || dataId == int.MinValue)
                    return;

                vehicleDataSetMethod.Invoke(vehicleDataBus, new object[] { channel, dataId, value });
            }
            catch (Exception)
            {
            }
        }

        private int GetVppData(int channel, int dataId, int fallback)
        {
            try
            {
                ResolveVppDataBus();
                if (vehicleDataBus == null || vehicleDataGetMethod == null || channel == int.MinValue || dataId == int.MinValue)
                    return fallback;

                object value = vehicleDataGetMethod.Invoke(vehicleDataBus, new object[] { channel, dataId });
                return Convert.ToInt32(value);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

#if ENABLE_INPUT_SYSTEM
        private ExternalInputSample DebounceExternalSource(ExternalInputSample sample)
        {
            if (!sample.Active)
            {
                lastExternalInputSource = "None";
                return sample;
            }

            if (sample.Activity < externalInputThreshold)
                return ExternalInputSample.Inactive;

            if (lastExternalInputSource != "None" &&
                lastExternalInputSource != sample.SourceName &&
                Time.unscaledTime - lastExternalSourceSwitchTime < sourceSwitchDebounceSeconds)
                return ExternalInputSample.Inactive;

            if (lastExternalInputSource != sample.SourceName)
            {
                lastExternalInputSource = sample.SourceName;
                lastExternalSourceSwitchTime = Time.unscaledTime;
                Debug.Log("[VppExternalInputBridge] Active external input source: " + sample.SourceName);
            }

            return sample;
        }

        private bool TryReadGamepad(out float steer, out float throttle, out float brake, out float clutch)
        {
            steer = 0f;
            throttle = 0f;
            brake = 0f;
            clutch = 0f;

            Gamepad gamepad = Gamepad.current;
            if (gamepad == null)
                return false;

            steer = gamepad.leftStick.x.ReadValue();
            throttle = gamepad.rightTrigger.ReadValue();
            brake = gamepad.leftTrigger.ReadValue();
            clutch = gamepad.buttonWest.isPressed ? 1f : 0f;

            return Mathf.Abs(steer) > 0.02f || throttle > 0.02f || brake > 0.02f || clutch > 0.02f ||
                   gamepad.leftShoulder.isPressed || gamepad.rightShoulder.isPressed;
        }

        private bool TryReadWheel(out float steer, out float throttle, out float brake, out float clutch)
        {
            // Generic HID fallback for wheels such as Fanatec. Exact axis names
            // can vary by driver, so matching is intentionally broad.
            steer = 0f;
            throttle = 0f;
            brake = 0f;
            clutch = 0f;

            InputDevice[] wheels = FindWheelLikeDevices();
            if (wheels.Length == 0)
                return false;

            steer = ReadAxis(wheels, new[] { "steer", "steering", "wheel", "x" }, 0f, false);
            throttle = ReadAxis(wheels, new[] { "accelerator", "accel", "gas", "throttle", "pedal0", "z" }, 0f, invertThrottlePedal);
            brake = ReadAxis(wheels, new[] { "brake", "pedal1", "rz" }, 0f, invertBrakePedal);
            clutch = ReadAxis(wheels, new[] { "clutch", "pedal2", "slider" }, 0f, invertClutchPedal);

            return Mathf.Abs(steer) > 0.02f || throttle > 0.02f || brake > 0.02f || clutch > 0.02f || AnyButtonPressed(wheels);
        }

        private InputDevice[] FindWheelLikeDevices()
        {
            System.Collections.Generic.List<InputDevice> matches = new System.Collections.Generic.List<InputDevice>();
            foreach (InputDevice device in InputSystem.devices)
            {
                if (device is Keyboard || device is Mouse || device is Gamepad)
                    continue;

                string identity = (device.name + " " + device.displayName + " " + device.layout).ToLowerInvariant();
                if (identity.Contains("fanatec") || identity.Contains("wheel") || identity.Contains("steering") ||
                    identity.Contains("joystick") || identity.Contains("hid"))
                    matches.Add(device);
            }

            return matches.ToArray();
        }

        private float ReadAxis(InputDevice[] devices, string[] nameFragments, float fallback, bool invert)
        {
            AxisControl axis = FindAxis(devices, nameFragments);
            if (axis == null)
                return fallback;

            float value = axis.ReadValue();
            bool isSteering = Array.IndexOf(nameFragments, "x") >= 0 || Array.IndexOf(nameFragments, "steer") >= 0;
            if (isSteering)
                return Mathf.Clamp(value, -1f, 1f);

            float normalized = value < -0.05f ? (value + 1f) * 0.5f : Mathf.Clamp01(value);
            return invert ? 1f - normalized : normalized;
        }

        private AxisControl FindAxis(InputDevice[] devices, string[] fragments)
        {
            for (int i = 0; i < devices.Length; i++)
            {
                AxisControl axis = FindAxis(devices[i], fragments);
                if (axis != null)
                    return axis;
            }

            return null;
        }

        private AxisControl FindAxis(InputDevice device, string[] fragments)
        {
            foreach (InputControl control in device.allControls)
            {
                AxisControl axis = control as AxisControl;
                if (axis == null)
                    continue;

                string identity = (axis.name + " " + axis.displayName + " " + axis.path).ToLowerInvariant();
                for (int i = 0; i < fragments.Length; i++)
                {
                    if (identity.Contains(fragments[i]))
                        return axis;
                }
            }

            return null;
        }

        private bool AnyButtonPressed(InputDevice[] devices)
        {
            for (int i = 0; i < devices.Length; i++)
            {
                if (AnyButtonPressed(devices[i]))
                    return true;
            }

            return false;
        }

        private bool AnyButtonPressed(InputDevice device)
        {
            foreach (InputControl control in device.allControls)
            {
                ButtonControl button = control as ButtonControl;
                if (button != null && button.isPressed)
                    return true;
            }

            return false;
        }

        private bool TryReadAuxiliaryIgnition(out bool run, out bool start)
        {
            run = false;
            start = false;

            InputDevice device = FindAuxiliaryDevice();
            if (device == null)
                return false;

            bool hasRun = TryReadButton(device, auxiliaryIgnitionRunControlPath, out run);
            bool hasStart = TryReadButton(device, auxiliaryIgnitionStartControlPath, out start);
            return hasRun || hasStart;
        }

        private InputDevice FindAuxiliaryDevice()
        {
            foreach (InputDevice device in InputSystem.devices)
            {
                if (device is Keyboard || device is Mouse || device is Gamepad)
                    continue;

                string identity = (device.name + " " + device.displayName + " " + device.layout + " " + device.path + " " +
                                   device.description.product + " " + device.description.manufacturer).ToLowerInvariant();
                if (MatchesAnyFilter(identity, auxiliaryDeviceNameOrPathContains))
                    return device;
            }

            return null;
        }

        private static bool TryReadButton(InputDevice device, string controlPath, out bool pressed)
        {
            pressed = false;
            if (device == null || string.IsNullOrWhiteSpace(controlPath))
                return false;

            InputControl control = device.TryGetChildControl(controlPath) ?? device[controlPath];
            ButtonControl button = control as ButtonControl;
            if (button == null)
                return false;

            pressed = button.isPressed;
            return true;
        }

        private static bool MatchesAnyFilter(string identity, string filters)
        {
            if (string.IsNullOrWhiteSpace(filters))
                return true;

            string[] parts = filters.ToLowerInvariant().Split(';', ',');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length > 0 && identity.Contains(part))
                    return true;
            }

            return false;
        }

        private void ReadGenericWheelHPatternButtons(TransmissionMode transmissionMode)
        {
            if (!IsHPatternMode(transmissionMode))
                return;

            if (!mapFirstWheelButtonsToHPattern)
                return;

            if (preferMappedFanatecProviderOverGenericWheel &&
                (((externalController == ExternalController.Fanatec || externalController == ExternalController.Auto) &&
                  enableFanatecHidInput && ResolveFanatecProvider() != null && fanatecProvider.ActiveDeviceFound) ||
                 ((externalController == ExternalController.G29 || externalController == ExternalController.Auto) &&
                  enableG29HidInput && ResolveG29Provider() != null && g29Provider.IsAvailable)))
                return;

            InputDevice[] wheels = FindWheelLikeDevices();
            if (wheels.Length == 0)
                return;

            for (int deviceIndex = 0; deviceIndex < wheels.Length; deviceIndex++)
            {
                foreach (InputControl control in wheels[deviceIndex].allControls)
                {
                    ButtonControl button = control as ButtonControl;
                    if (button == null || !button.wasPressedThisFrame)
                        continue;

                    int gear = GearFromButtonName(button.name);
                    if (gear != int.MinValue)
                        ApplyRequestedHPatternGear(gear, transmissionMode, 1f);
                }
            }
        }

        private void ApplyExternalGearInput(ExternalInputSample selected, TransmissionMode transmissionMode)
        {
            if (!selected.HasGear)
                return;

            if (!IsHPatternMode(transmissionMode))
            {
                if (selected.Gear == DrivingGear.Reverse)
                {
                    requestedGearStatus = "R";
                    appliedGearStatus = "AUTO R";
                    ApplyVppAutomaticGear(VppGearModeReverse);
                    return;
                }

                if (selected.Gear == DrivingGear.Neutral)
                {
                    // In Automatic, ignore Fanatec inferred Neutral fallback so a connected or unresolved
                    // H-shifter cannot override AUTO D during participant collection.
                    FanatecHidInputProvider fanatecProv = ResolveFanatecProvider();
                    bool inferredFanatecNeutral =
                        transmissionMode == TransmissionMode.Automatic &&
                        fanatecProv != null &&
                        fanatecProv.mapping != null &&
                        (fanatecProv.mapping.neutral == null || string.IsNullOrWhiteSpace(fanatecProv.mapping.neutral.controlPath)) &&
                        fanatecProv.LastGearDebug.StartsWith("N fallback", StringComparison.Ordinal);

                    G29HidInputProvider g29Prov = ResolveG29Provider();
                    bool inferredG29Neutral =
                        transmissionMode == TransmissionMode.Automatic &&
                        g29Prov != null &&
                        g29Prov.mapping != null &&
                        (g29Prov.mapping.neutral == null || string.IsNullOrWhiteSpace(g29Prov.mapping.neutral.controlPath)) &&
                        g29Prov.LastGearDebug.StartsWith("N fallback", StringComparison.Ordinal);

                    if (inferredFanatecNeutral || inferredG29Neutral)
                    {
                        requestedGearStatus = "AUTO D";
                        appliedGearStatus = "AUTO D";
                        return;
                    }

                    requestedGearStatus = "N";
                    appliedGearStatus = "AUTO N";
                    ApplyVppAutomaticGear(VppGearModeNeutral);
                    return;
                }

                if (autoSelectHPatternModeOnShifterInput && ResolveTransmissionModeManager() != null)
                {
                    transmissionModeManager.SetMode(TransmissionMode.ManualHPatternEasy, true);
                    transmissionMode = transmissionModeManager.CurrentMode;
                }
            }

            if (!IsHPatternMode(transmissionMode))
                return;

            ApplyRequestedHPatternGear((int)selected.Gear, transmissionMode, selected.Clutch);
        }

        private void ApplyRequestedHPatternGear(int requestedGear, TransmissionMode transmissionMode, float clutch)
        {
            requestedGearStatus = FormatGear(requestedGear);

            int gearToApply = requestedGear;
            bool requestedGearChanged = requestedGear != lastRequestedHPatternGear;
            bool canRetryRejectedGear = !requestedGearChanged && requestedGear != lastAppliedHPatternGear;
            lastRequestedHPatternGear = requestedGear;

            if (!requestedGearChanged && !canRetryRejectedGear)
            {
                MaintainAppliedManualGear();
                return;
            }

            if (transmissionMode == TransmissionMode.ManualHPatternRealistic && requestedGear != 0 && clutch < RequiredClutchThreshold)
            {
                appliedGearStatus = FormatGear(lastAppliedHPatternGear) + " (clutch)";
                MaintainAppliedManualGear();
                return;
            }

            if (gearToApply == lastAppliedHPatternGear)
            {
                MaintainAppliedManualGear();
                return;
            }

            lastAppliedHPatternGear = gearToApply;
            appliedGearStatus = FormatGear(gearToApply);
            ApplyVppManualGear(gearToApply);
        }

        private void ApplyVppManualGear(int gear)
        {
            SetVppAutomaticGearMode(gear < 0 ? VppGearModeReverse : gear == 0 ? VppGearModeNeutral : VppGearModeManual);
            SetVppAutoShiftOverride(VppForceManualShift);
            SetVppManualGearInput(gear);
            InvokeSetGear(gear);
        }

        private void ApplyVppAutomaticGear(int gearMode)
        {
            SetVppAutomaticGearMode(gearMode);
            SetVppAutoShiftOverride(VppForceAutoShift);
        }

        private void MaintainAppliedManualGear()
        {
            int gear = lastAppliedHPatternGear == int.MinValue ? 0 : lastAppliedHPatternGear;
            SetVppAutomaticGearMode(gear < 0 ? VppGearModeReverse : gear == 0 ? VppGearModeNeutral : VppGearModeManual);
            SetVppAutoShiftOverride(VppForceManualShift);
            SetVppManualGearInput(gear);
        }

        private float RequiredClutchThreshold
        {
            get
            {
                return transmissionModeManager != null ? transmissionModeManager.clutchPressedThreshold : 0.75f;
            }
        }

        private int GearFromButtonName(string buttonName)
        {
            string lower = buttonName.ToLowerInvariant();
            if (lower == "button0" || lower == "button14" || lower.Contains("gear1")) return 1;
            if (lower == "button1" || lower == "button15" || lower.Contains("gear2")) return 2;
            if (lower == "button2" || lower == "button16" || lower.Contains("gear3")) return 3;
            if (lower == "button3" || lower == "button17" || lower.Contains("gear4")) return 4;
            if (lower == "button4" || lower == "button18" || lower.Contains("gear5")) return 5;
            if (lower == "button5" || lower == "button19" || lower.Contains("gear6")) return 6;
            if (lower == "button13" || lower == "button6" || lower.Contains("reverse")) return -1;
            if (lower == "button7" || lower.Contains("neutral")) return 0;
            return int.MinValue;
        }

        private void UpdateInputHud(ExternalInputSample selected)
        {
            if (!selected.Active)
            {
                hudSteering = 0f;
                hudThrottle = 0f;
                hudBrake = 0f;
                hudClutch = 0f;
                hudHandbrake = 0f;
                hudVppSteering = 0f;
                UpdateFanatecHudValues();
                return;
            }

            hudSteering = selected.Steering;
            hudThrottle = selected.Throttle;
            hudBrake = selected.Brake;
            hudClutch = selected.Clutch;
            hudHandbrake = selected.Handbrake;
            UpdateFanatecHudValues();

            if (selected.HasGear)
                requestedGearStatus = FormatGear((int)selected.Gear);
        }

        private void UpdateFanatecHudValues()
        {
            FanatecHidInputProvider provider = ResolveFanatecProvider();
            if (provider == null)
            {
                hudFanatecRawSteering = 0f;
                hudFanatecMappedSteering = 0f;
                hudFanatecSteeringFound = false;
                hudFanatecDiagnosticGear = int.MinValue;
                hudFanatecGearDebug = "-";
                return;
            }

            hudFanatecRawSteering = provider.LastSteeringRaw;
            hudFanatecMappedSteering = provider.LastSteeringNormalized;
            hudFanatecSteeringFound = provider.LastSteeringControlFound;
            hudFanatecDiagnosticGear = provider.LastDiagnosticGear;
            hudFanatecGearDebug = provider.LastGearDebug;
            UpdateVppGearHudValues();
        }

        private void UpdateVppGearHudValues()
        {
            hudVppInputAutomaticGear = GetVppData(channelInput, inputAutomaticGear, int.MinValue);
            hudVppInputManualGear = GetVppData(channelInput, inputManualGear, int.MinValue);
            hudVppVehicleGear = GetVppData(channelVehicle, vehicleGearboxGear, int.MinValue);
            hudVppVehicleGearMode = GetVppData(channelVehicle, vehicleGearboxMode, int.MinValue);
            hudVppForwardGearCount = CountVppForwardGears();
        }

        private int CountVppForwardGears()
        {
            try
            {
                if (vehicleController == null)
                    return int.MinValue;

                FieldInfo gearboxField = vehicleController.GetType().GetField("gearbox", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (gearboxField == null)
                    return int.MinValue;

                object gearbox = gearboxField.GetValue(vehicleController);
                if (gearbox == null)
                    return int.MinValue;

                FieldInfo ratiosField = gearbox.GetType().GetField("forwardGearRatios", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object ratios = ratiosField != null ? ratiosField.GetValue(gearbox) : null;
                if (ratios is Array array)
                    return array.Length;

                if (ratios is System.Collections.ICollection collection)
                    return collection.Count;

                return int.MinValue;
            }
            catch (Exception)
            {
                return int.MinValue;
            }
        }

        private static string FormatGear(int gear)
        {
            if (gear == int.MinValue)
                return "-";
            if (gear < 0)
                return "R";
            if (gear == 0)
                return "N";
            return gear.ToString();
        }

        private readonly struct ExternalInputSample
        {
            public static readonly ExternalInputSample Inactive = new ExternalInputSample();

            public readonly bool Active;
            public readonly string SourceName;
            public readonly float Steering;
            public readonly float Throttle;
            public readonly float Brake;
            public readonly float Clutch;
            public readonly float Handbrake;
            public readonly DrivingGear Gear;
            public readonly bool HasGear;
            public readonly bool GearUp;
            public readonly bool GearDown;
            public readonly bool Ignition;
            public readonly bool HasIgnition;
            public readonly float Activity;

            public ExternalInputSample(string sourceName, float steering, float throttle, float brake, float clutch, float handbrake)
                : this(sourceName, steering, throttle, brake, clutch, handbrake, DrivingGear.Unset, false, false, false, false, false)
            {
            }

            private ExternalInputSample(
                string sourceName,
                float steering,
                float throttle,
                float brake,
                float clutch,
                float handbrake,
                DrivingGear gear,
                bool hasGear,
                bool gearUp,
                bool gearDown,
                bool ignition,
                bool hasIgnition)
            {
                Active = true;
                SourceName = sourceName;
                Steering = Mathf.Clamp(steering, -1f, 1f);
                Throttle = Mathf.Clamp01(throttle);
                Brake = Mathf.Clamp01(brake);
                Clutch = Mathf.Clamp01(clutch);
                Handbrake = Mathf.Clamp01(handbrake);
                Gear = gear;
                HasGear = hasGear;
                GearUp = gearUp;
                GearDown = gearDown;
                Ignition = ignition;
                HasIgnition = hasIgnition;
                Activity = Mathf.Max(Mathf.Abs(Steering), Throttle, Brake, Clutch, Handbrake, HasGear ? 1f : 0f, GearUp ? 1f : 0f, GearDown ? 1f : 0f, HasIgnition ? 1f : 0f);
            }

            private ExternalInputSample(DrivingInputState state)
                : this(
                    state.SourceName,
                    state.Steering,
                    state.Throttle,
                    state.Brake,
                    state.Clutch,
                    state.Handbrake,
                    state.Gear,
                    state.HasGear,
                    state.GearUp,
                    state.GearDown,
                    state.Ignition,
                    state.HasIgnition)
            {
            }

            public static ExternalInputSample FromState(DrivingInputState state)
            {
                return new ExternalInputSample(state);
            }

            public static ExternalInputSample Merge(ExternalInputSample current, ExternalInputSample incoming)
            {
                if (!incoming.Active)
                    return current;
                if (!current.Active)
                    return incoming;

                string sourceName = current.SourceName == incoming.SourceName
                    ? current.SourceName
                    : current.SourceName + " + " + incoming.SourceName;

                float steering = Mathf.Abs(incoming.Steering) >= Mathf.Abs(current.Steering) ? incoming.Steering : current.Steering;
                DrivingGear gear = incoming.HasGear ? incoming.Gear : current.Gear;
                bool hasGear = current.HasGear || incoming.HasGear;

                return new ExternalInputSample(
                    sourceName,
                    steering,
                    Mathf.Max(current.Throttle, incoming.Throttle),
                    Mathf.Max(current.Brake, incoming.Brake),
                    Mathf.Max(current.Clutch, incoming.Clutch),
                    Mathf.Max(current.Handbrake, incoming.Handbrake),
                    gear,
                    hasGear,
                    current.GearUp || incoming.GearUp,
                    current.GearDown || incoming.GearDown,
                    incoming.HasIgnition ? incoming.Ignition : current.Ignition,
                    current.HasIgnition || incoming.HasIgnition);
            }
        }
#endif

        private void InvokeSetGear(int gear)
        {
            if (setGearMethod == null || vehicleController == null)
                return;

            ParameterInfo parameter = setGearMethod.GetParameters()[0];
            object value = parameter.ParameterType.IsEnum ? Enum.ToObject(parameter.ParameterType, gear) : gear;
            try
            {
                setGearMethod.Invoke(vehicleController, new[] { value });
            }
            catch (Exception)
            {
            }
        }

        private void OnGUI()
        {
            if (!showDriveModeHud)
                return;

            EnsureHudStyles();

            string label = driveMode == DriveMode.Automatic
                ? "Guida: AUTOMATICA  |  1-3: modalita cambio"
                : "Guida: " + CurrentTransmissionMode + "  |  1-3: modalita cambio";

            float width = Mathf.Min(560f, Screen.width - 32f);
            Rect boxRect = new Rect(Screen.width - width - 16f, 16f, width, 280f);
            GUILayout.BeginArea(boxRect, hudBoxStyle);
            GUILayout.Label(label, hudLabelStyle);

#if ENABLE_INPUT_SYSTEM
            string gearLabel = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Input: {0}\nMarcia richiesta: {1}    Applicata: {2}",
                activeExternalInputSource,
                requestedGearStatus,
                appliedGearStatus);
            string pedalsLabel = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Sterzo selezionato: {0:0.0000}    VPP: {1:0.0000}\nFanatec raw: {2:0.0000}    mapped: {3:0.0000}    ctrl: {4}\nGear diag: {5}    Fanatec gear: {6}\nVPP input auto/manual: {7}/{8}    VPP vehicle mode/gear: {9}/{10}    forward gears: {11}\nGas: {12:0.000}    Freno: {13:0.000}\nFrizione: {14:0.000}    Freno mano: {15:0.000}",
                hudSteering,
                hudVppSteering,
                hudFanatecRawSteering,
                hudFanatecMappedSteering,
                hudFanatecSteeringFound ? "OK" : "NO",
                hudFanatecDiagnosticGear == int.MinValue ? "-" : hudFanatecDiagnosticGear.ToString(System.Globalization.CultureInfo.InvariantCulture),
                hudFanatecGearDebug,
                hudVppInputAutomaticGear == int.MinValue ? "-" : hudVppInputAutomaticGear.ToString(System.Globalization.CultureInfo.InvariantCulture),
                hudVppInputManualGear == int.MinValue ? "-" : hudVppInputManualGear.ToString(System.Globalization.CultureInfo.InvariantCulture),
                hudVppVehicleGearMode == int.MinValue ? "-" : hudVppVehicleGearMode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                hudVppVehicleGear == int.MinValue ? "-" : hudVppVehicleGear.ToString(System.Globalization.CultureInfo.InvariantCulture),
                hudVppForwardGearCount == int.MinValue ? "-" : hudVppForwardGearCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                hudThrottle,
                hudBrake,
                hudClutch,
                hudHandbrake);
            GUILayout.Label(gearLabel, hudLabelStyle);
            GUILayout.Label(pedalsLabel, hudLabelStyle);
            GUILayout.Label("Accensione: " + ignitionStatus, hudLabelStyle);
#endif
            GUILayout.EndArea();
        }

        private void EnsureHudStyles()
        {
            if (hudBoxStyle == null)
            {
                hudBoxStyle = new GUIStyle(GUI.skin.box);
                hudBoxStyle.alignment = TextAnchor.UpperLeft;
                hudBoxStyle.normal.textColor = Color.white;
                hudBoxStyle.padding = new RectOffset(12, 12, 10, 10);
            }

            if (hudLabelStyle == null)
            {
                hudLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Normal,
                    wordWrap = true,
                    clipping = TextClipping.Clip,
                    alignment = TextAnchor.UpperLeft
                };
                hudLabelStyle.normal.textColor = Color.white;
            }
        }
    }
}
