using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ResearchSim
{
    /// <summary>
    /// Legacy standalone CSV logger kept for older non-UXF scenes. The active
    /// experiment uses CarTelemetryTracker through UXF, so this component should
    /// normally remain disabled.
    /// </summary>
    public sealed class TelemetryLogger : MonoBehaviour
    {
        private const string CsvHeader = "Timestamp,ParticipantID,PositionX,PositionZ,Speed_KMH,SteeringInput,ThrottleInput,BrakeInput,DistanceFromCenterLine";

        [Header("References")]
        public CenterlinePath centerline;
        public HybridVehicleInput inputSource;
        public Rigidbody vehicleRigidbody;
        public MonoBehaviour rccControllerOverride;

        [Header("File Output")]
        public bool beginLoggingOnStart;
        public string outputSubfolder = "LegacyTelemetry";
        public string participantIDOverride;
        public float flushIntervalSeconds = 1f;

        public string CurrentFilePath { get; private set; }
        public bool IsLogging => writer != null;

        private readonly StringBuilder lineBuilder = new StringBuilder(256);
        private StreamWriter writer;
        private MonoBehaviour rccController;
        private SimpleResearchVehicleController simpleController;
        private double nextFlushTime;

        private void Awake()
        {
            if (centerline == null)
                centerline = FindAnyObjectByType<CenterlinePath>();

            if (inputSource == null)
                inputSource = GetComponentInParent<HybridVehicleInput>();

            if (vehicleRigidbody == null)
                vehicleRigidbody = GetComponentInParent<Rigidbody>();

            simpleController = GetComponentInParent<SimpleResearchVehicleController>();
        }

        private void Start()
        {
            if (beginLoggingOnStart)
                BeginLogging();
        }

        private void FixedUpdate()
        {
            if (writer == null)
                return;

            if (inputSource != null)
                inputSource.RefreshInputValues();

            Vector3 position = transform.position;
            InputSnapshot inputs = ReadInputSnapshot();
            float speedKmh = ReadSpeedKmh();
            float distanceFromCenterLine = centerline != null
                ? centerline.GetDistanceFromCenterLine(position)
                : float.NaN;

            WriteCsvLine(Time.fixedTimeAsDouble, position, speedKmh, inputs, distanceFromCenterLine);

            if (Time.realtimeSinceStartupAsDouble >= nextFlushTime)
            {
                writer.Flush();
                nextFlushTime = Time.realtimeSinceStartupAsDouble + Mathf.Max(0.1f, flushIntervalSeconds);
            }
        }

        private void OnApplicationQuit()
        {
            StopLogging();
        }

        private void OnDestroy()
        {
            StopLogging();
        }

        public void BeginLogging()
        {
            if (writer != null)
                return;

            string participantID = string.IsNullOrWhiteSpace(participantIDOverride)
                ? ExperimentSession.GetFileSafeParticipantID()
                : MakeFileSafe(participantIDOverride.Trim());

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string outputFolder = Path.Combine(ResearchDataPaths.ProjectRoot, ResearchDataPaths.DataRootFolderName, outputSubfolder);
            Directory.CreateDirectory(outputFolder);

            CurrentFilePath = Path.Combine(outputFolder, participantID + "_" + timestamp + ".csv");
            writer = new StreamWriter(CurrentFilePath, false, new UTF8Encoding(false));
            writer.WriteLine(CsvHeader);
            writer.Flush();

            nextFlushTime = Time.realtimeSinceStartupAsDouble + Mathf.Max(0.1f, flushIntervalSeconds);
            Debug.Log("Telemetry logging started: " + CurrentFilePath);
        }

        public void StopLogging()
        {
            if (writer == null)
                return;

            writer.Flush();
            writer.Dispose();
            writer = null;
            Debug.Log("Telemetry logging stopped: " + CurrentFilePath);
        }

        private void WriteCsvLine(double timestamp, Vector3 position, float speedKmh, InputSnapshot inputs, float centerlineDistance)
        {
            string participantID = string.IsNullOrWhiteSpace(participantIDOverride)
                ? ExperimentSession.GetParticipantID()
                : participantIDOverride.Trim();

            lineBuilder.Clear();
            Append(timestamp);
            Append(participantID);
            Append(position.x);
            Append(position.z);
            Append(speedKmh);
            Append(inputs.steering);
            Append(inputs.throttle);
            Append(inputs.brake);
            AppendLast(centerlineDistance);

            writer.WriteLine(lineBuilder.ToString());
        }

        private InputSnapshot ReadInputSnapshot()
        {
            if (inputSource != null)
            {
                return new InputSnapshot
                {
                    steering = inputSource.Steering,
                    throttle = inputSource.Throttle,
                    brake = inputSource.Brake
                };
            }

            MonoBehaviour rcc = ResolveRccController();
            return new InputSnapshot
            {
                steering = ReadFloatMember(rcc, 0f, "steerInput", "steeringInput", "horizontalInput"),
                throttle = Mathf.Clamp01(ReadFloatMember(rcc, 0f, "throttleInput", "gasInput", "accelInput", "accelerationInput", "verticalInput")),
                brake = Mathf.Clamp01(ReadFloatMember(rcc, 0f, "brakeInput", "footBrakeInput"))
            };
        }

        private float ReadSpeedKmh()
        {
            MonoBehaviour rcc = ResolveRccController();
            float rccSpeed = ReadFloatMember(rcc, float.NaN, "speed", "Speed", "speed_KPH", "speedKPH", "speedKmh", "SpeedKmh", "currentSpeed", "vehicleSpeed");
            if (!float.IsNaN(rccSpeed))
                return Mathf.Abs(rccSpeed);

            if (simpleController == null)
                simpleController = GetComponentInParent<SimpleResearchVehicleController>();

            if (simpleController != null)
                return simpleController.CurrentSpeedKmh;

            if (vehicleRigidbody != null)
                return GetVelocity(vehicleRigidbody).magnitude * 3.6f;

            return 0f;
        }

        private MonoBehaviour ResolveRccController()
        {
            if (rccController != null)
                return rccController;

            if (rccControllerOverride != null)
            {
                rccController = rccControllerOverride;
                return rccController;
            }

            MonoBehaviour[] behaviours = GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour == this || behaviour == inputSource)
                    continue;

                string typeName = behaviour.GetType().FullName;
                if (typeName.Contains("RCC") || typeName.Contains("RealisticCarController") || typeName.Contains("BoneCracker"))
                {
                    rccController = behaviour;
                    return rccController;
                }
            }

            return null;
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

        private static string MakeFileSafe(string value)
        {
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
                value = value.Replace(invalidChar, '_');

            return value;
        }

        private void Append(double value)
        {
            lineBuilder.Append(value.ToString("F4", CultureInfo.InvariantCulture));
            lineBuilder.Append(',');
        }

        private void Append(float value)
        {
            lineBuilder.Append(value.ToString("F4", CultureInfo.InvariantCulture));
            lineBuilder.Append(',');
        }

        private void Append(string value)
        {
            lineBuilder.Append(EscapeCsv(value));
            lineBuilder.Append(',');
        }

        private void AppendLast(float value)
        {
            lineBuilder.Append(value.ToString("F4", CultureInfo.InvariantCulture));
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            bool mustQuote = value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r");
            if (!mustQuote)
                return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private struct InputSnapshot
        {
            public float steering;
            public float throttle;
            public float brake;
        }
    }
}
