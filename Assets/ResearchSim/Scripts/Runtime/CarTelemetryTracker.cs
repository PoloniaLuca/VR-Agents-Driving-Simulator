using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UXF;

namespace ResearchSim
{
    /// <summary>
    /// UXF tracker attached to the research vehicle. It writes the behavioural
    /// variables needed for analysis: position, lane offset, speed, driver
    /// inputs, current gear and engine speed over time.
    /// </summary>
    public sealed class CarTelemetryTracker : Tracker
    {
        public override string MeasurementDescriptor => "telemetry";

        public override IEnumerable<string> CustomHeader => new[]
        {
            "trial_elapsed_time",
            "position_x",
            "position_y",
            "position_z",
            "heading_yaw_deg",
            "lateral_position_m",
            "abs_lateral_position_m",
            "speed_mps",
            "speed_kmh",
            "engine_rpm",
            "gear_index",
            "gear_mode",
            "gear_label",
            "steering_input",
            "throttle_input",
            "brake_input"
        };

        [Header("Vehicle")]
        public Rigidbody vehicleRigidbody;
        public HybridVehicleInput hybridInput;
        public MonoBehaviour vppStandardInput;
        public MonoBehaviour vppVehicleController;
        public MonoBehaviour vppVehicleBase;

        [Header("Lane Reference")]
        public CenterlinePath centerline;

        private float recordingStartTime;

        private void Awake()
        {
            // Auto-fill common references so the tracker survives prefab and
            // scene rewiring, while still allowing explicit Inspector setup.
            if (vehicleRigidbody == null)
                vehicleRigidbody = GetComponentInParent<Rigidbody>();

            if (hybridInput == null)
                hybridInput = GetComponentInParent<HybridVehicleInput>();

            if (centerline == null)
                centerline = FindAnyObjectByType<CenterlinePath>();

            ResolveVppReferences();
            updateType = TrackerUpdateType.FixedUpdate;
            objectName = "research_vehicle";
        }

        protected override UXFDataRow GetCurrentValues()
        {
            // UXF calls this at the configured tracker rate. Keep this method
            // allocation-light because it runs throughout every trial.
            if (Data.CountRows() == 0)
                recordingStartTime = Time.time;

            Vector3 position = transform.position;
            float lateralPosition = centerline != null ? centerline.GetSignedDistanceFromCenterLine(position) : float.NaN;
            float speedMps = vehicleRigidbody != null ? GetVelocity(vehicleRigidbody).magnitude : 0f;
            InputSnapshot input = ReadInputSnapshot();
            VehicleSnapshot vehicle = ReadVehicleSnapshot();

            return new UXFDataRow()
            {
                ("trial_elapsed_time", Time.time - recordingStartTime),
                ("position_x", position.x),
                ("position_y", position.y),
                ("position_z", position.z),
                ("heading_yaw_deg", transform.eulerAngles.y),
                ("lateral_position_m", lateralPosition),
                ("abs_lateral_position_m", Mathf.Abs(lateralPosition)),
                ("speed_mps", speedMps),
                ("speed_kmh", speedMps * 3.6f),
                ("engine_rpm", vehicle.engineRpm),
                ("gear_index", vehicle.gearIndex),
                ("gear_mode", vehicle.gearMode),
                ("gear_label", vehicle.gearLabel),
                ("steering_input", input.steering),
                ("throttle_input", input.throttle),
                ("brake_input", input.brake)
            };
        }

        private VehicleSnapshot ReadVehicleSnapshot()
        {
            ResolveVppReferences();
            MonoBehaviour vehicle = vppVehicleBase != null ? vppVehicleBase : vppVehicleController;
            if (vehicle == null || !vehicle.isActiveAndEnabled)
                return VehicleSnapshot.Missing;

            try
            {
                object dataBus = ReadObjectMember(vehicle, "data");
                if (dataBus == null)
                    return VehicleSnapshot.Missing;

                int channelVehicle = ReadStaticInt("VehiclePhysics.Channel", "Vehicle");
                int engineRpmId = ReadStaticInt("VehiclePhysics.VehicleData", "EngineRpm");
                int gearboxGearId = ReadStaticInt("VehiclePhysics.VehicleData", "GearboxGear");
                int gearboxModeId = ReadStaticInt("VehiclePhysics.VehicleData", "GearboxMode");
                int gearboxShiftingId = ReadStaticInt("VehiclePhysics.VehicleData", "GearboxShifting");

                int[] vehicleData = InvokeGetChannel(dataBus, channelVehicle);
                if (vehicleData == null)
                    return VehicleSnapshot.Missing;

                int gearIndex = SafeRead(vehicleData, gearboxGearId, 0);
                int gearMode = SafeRead(vehicleData, gearboxModeId, -1);
                bool shifting = SafeRead(vehicleData, gearboxShiftingId, 0) != 0;
                float engineRpm = ReadVehicleDataValue(dataBus, channelVehicle, engineRpmId) / 1000f;

                return new VehicleSnapshot(engineRpm, gearIndex, gearMode, FormatGearLabel(gearIndex, gearMode, shifting));
            }
            catch (Exception)
            {
                return VehicleSnapshot.Missing;
            }
        }

        private InputSnapshot ReadInputSnapshot()
        {
            // Prefer the local hybrid input wrapper when present; otherwise use
            // reflection to read VPP input fields without taking control away
            // from VPP itself.
            if (hybridInput != null)
            {
                hybridInput.RefreshInputValues();
                return new InputSnapshot(hybridInput.Steering, hybridInput.Throttle, hybridInput.Brake);
            }

            ResolveVppReferences();
            return new InputSnapshot(
                ReadFloatMember(vppStandardInput, 0f, "externalSteer", "steerInput", "steeringInput", "horizontalInput"),
                Mathf.Clamp01(ReadFloatMember(vppStandardInput, 0f, "externalThrottle", "throttleInput", "gasInput", "accelInput")),
                Mathf.Clamp01(ReadFloatMember(vppStandardInput, 0f, "externalBrake", "brakeInput", "footBrakeInput"))
            );
        }

        private void ResolveVppReferences()
        {
            // VPP classes may not be available at compile time in every project
            // state, so references are located by full type name.
            if (vppStandardInput == null)
                vppStandardInput = FindComponentByFullName("VehiclePhysics.VPStandardInput");

            if (vppVehicleController == null)
                vppVehicleController = FindComponentByFullName("VehiclePhysics.VPVehicleController");

            if (vppVehicleBase == null)
                vppVehicleBase = FindComponentByFullName("VehiclePhysics.VehicleBase");
        }

        private MonoBehaviour FindComponentByFullName(string fullName)
        {
            MonoBehaviour[] behaviours = GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour != null && behaviour.GetType().FullName == fullName)
                    return behaviour;
            }

            return null;
        }

        private static object ReadObjectMember(object target, string memberName)
        {
            if (target == null)
                return null;

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(memberName, Flags);
            if (property != null)
                return property.GetValue(target);

            FieldInfo field = type.GetField(memberName, Flags);
            return field != null ? field.GetValue(target) : null;
        }

        private static int ReadStaticInt(string typeName, string memberName)
        {
            Type type = FindType(typeName);
            if (type == null)
                return -1;

            const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo field = type.GetField(memberName, Flags);
            if (field != null)
                return Convert.ToInt32(field.GetValue(null), CultureInfo.InvariantCulture);

            PropertyInfo property = type.GetProperty(memberName, Flags);
            return property != null ? Convert.ToInt32(property.GetValue(null), CultureInfo.InvariantCulture) : -1;
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

        private static int[] InvokeGetChannel(object dataBus, int channel)
        {
            MethodInfo method = FindGetMethod(dataBus, 1);
            if (method == null)
                return null;

            object value = method.Invoke(dataBus, new object[] { ConvertArgument(channel, method.GetParameters()[0].ParameterType) });
            return value as int[];
        }

        private static int ReadVehicleDataValue(object dataBus, int channel, int dataId)
        {
            MethodInfo method = FindGetMethod(dataBus, 2);
            if (method == null)
                return 0;

            ParameterInfo[] parameters = method.GetParameters();
            object value = method.Invoke(dataBus, new object[]
            {
                ConvertArgument(channel, parameters[0].ParameterType),
                ConvertArgument(dataId, parameters[1].ParameterType)
            });
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static MethodInfo FindGetMethod(object target, int parameterCount)
        {
            if (target == null)
                return null;

            MethodInfo[] methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name == "Get" && method.GetParameters().Length == parameterCount)
                    return method;
            }

            return null;
        }

        private static object ConvertArgument(int value, Type targetType)
        {
            if (targetType.IsEnum)
                return Enum.ToObject(targetType, value);
            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        private static int SafeRead(int[] values, int index, int fallback)
        {
            return values != null && index >= 0 && index < values.Length ? values[index] : fallback;
        }

        private static float ReadFloatMember(object target, float fallback, params string[] memberNames)
        {
            if (target == null)
                return fallback;

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            Type type = target.GetType();

            for (int i = 0; i < memberNames.Length; i++)
            {
                PropertyInfo property = type.GetProperty(memberNames[i], Flags);
                if (property != null && IsNumericType(property.PropertyType))
                    return Convert.ToSingle(property.GetValue(target), CultureInfo.InvariantCulture);

                FieldInfo field = type.GetField(memberNames[i], Flags);
                if (field != null && IsNumericType(field.FieldType))
                    return Convert.ToSingle(field.GetValue(target), CultureInfo.InvariantCulture);
            }

            return fallback;
        }

        private static bool IsNumericType(Type type)
        {
            return type == typeof(float) || type == typeof(double) || type == typeof(int);
        }

        private static Vector3 GetVelocity(Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        private static string FormatGearLabel(int gearIndex, int gearMode, bool shifting)
        {
            switch (gearMode)
            {
                case 0:
                    if (gearIndex == 0)
                        return shifting ? string.Empty : "N";
                    if (gearIndex > 0)
                        return gearIndex.ToString(CultureInfo.InvariantCulture);
                    return gearIndex == -1 ? "R" : "R" + (-gearIndex).ToString(CultureInfo.InvariantCulture);
                case 1:
                    return "P";
                case 2:
                    return gearIndex < -1 ? "R" + (-gearIndex).ToString(CultureInfo.InvariantCulture) : "R";
                case 3:
                    return "N";
                case 4:
                    return gearIndex > 0 ? "D" + gearIndex.ToString(CultureInfo.InvariantCulture) : "D";
                case 5:
                    return gearIndex > 0 ? "L" + gearIndex.ToString(CultureInfo.InvariantCulture) : "L";
                default:
                    return string.Empty;
            }
        }

        private readonly struct InputSnapshot
        {
            public readonly float steering;
            public readonly float throttle;
            public readonly float brake;

            public InputSnapshot(float steering, float throttle, float brake)
            {
                this.steering = steering;
                this.throttle = throttle;
                this.brake = brake;
            }
        }

        private readonly struct VehicleSnapshot
        {
            public static readonly VehicleSnapshot Missing = new VehicleSnapshot(float.NaN, 0, -1, string.Empty);

            public readonly float engineRpm;
            public readonly int gearIndex;
            public readonly int gearMode;
            public readonly string gearLabel;

            public VehicleSnapshot(float engineRpm, int gearIndex, int gearMode, string gearLabel)
            {
                this.engineRpm = engineRpm;
                this.gearIndex = gearIndex;
                this.gearMode = gearMode;
                this.gearLabel = gearLabel;
            }
        }
    }
}
