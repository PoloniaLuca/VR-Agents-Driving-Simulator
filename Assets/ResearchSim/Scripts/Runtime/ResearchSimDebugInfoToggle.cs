using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace ResearchSim
{
    /// <summary>
    /// Runtime toggle for participant-facing debug overlays.
    /// Keeps the diagnostic panels available during setup without forcing them
    /// to stay visible while driving.
    /// </summary>
    public sealed class ResearchSimDebugInfoToggle : MonoBehaviour
    {
        public KeyCode toggleKey = KeyCode.F11;
        public bool debugInfoVisible;
        public bool logToggleState = true;

        [Tooltip("Left false by default because VPP telemetry was explicitly disabled for the experiment view.")]
        public bool includeVppTelemetry;

        private static bool sharedDebugInfoVisible;
        private static int lastToggleFrame = -1;

        private bool appliedInitialState;
        private DrivingDataLogger logger;
        private GUIStyle csvBoxStyle;
        private GUIStyle csvTitleStyle;
        private GUIStyle csvLabelStyle;

        public static bool DebugInfoVisible
        {
            get { return sharedDebugInfoVisible; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedState()
        {
            sharedDebugInfoVisible = false;
            lastToggleFrame = -1;
        }

        private void Awake()
        {
            debugInfoVisible = sharedDebugInfoVisible;
        }

        private void Start()
        {
            ApplyDebugInfoVisibility();
            appliedInitialState = true;
        }

        private void Update()
        {
            if (!appliedInitialState)
            {
                ApplyDebugInfoVisibility();
                appliedInitialState = true;
            }

            if (Input.GetKeyDown(toggleKey) && TryToggleSharedVisibility())
            {
                ApplyDebugInfoVisibility();

                if (logToggleState)
                    Debug.Log("[ResearchSimDebugInfoToggle] Debug info " + (debugInfoVisible ? "shown" : "hidden") + " with key " + toggleKey + ".");
            }
            else if (debugInfoVisible != sharedDebugInfoVisible)
            {
                ApplyDebugInfoVisibility();
            }
        }

        private void OnGUI()
        {
            if (!sharedDebugInfoVisible)
                return;

            if (logger == null)
                logger = FindAnyObjectByType<DrivingDataLogger>();
            if (logger == null)
                return;

            EnsureCsvHudStyles();
            DrivingDataLogger.PrimaryCsvStatusSnapshot status = logger.GetPrimaryCsvStatus(true);
            float width = Mathf.Min(760f, Screen.width - 32f);
            Rect area = new Rect(16f, 330f, width, 250f);
            GUILayout.BeginArea(area, csvBoxStyle);
            GUILayout.Label("CSV logging", csvTitleStyle);
            GUILayout.Label("Primary CSV: " + DisplayPath(status.primaryCsvPath), csvLabelStyle);
            GUILayout.Label(
                "Exists: " + (status.primaryCsvExists ? "yes" : "no") +
                "    Rows written: " + status.dataRowsWritten.ToString(CultureInfo.InvariantCulture) +
                "    CSV size: " + FormatBytes(status.primaryCsvBytes),
                csvLabelStyle);
            GUILayout.Label(
                "Writer: " + (status.writerIsOpen ? "open" : "closed") +
                "    Logging active: " + (status.loggingActive ? "yes" : "no"),
                csvLabelStyle);
            GUILayout.Label(
                "Last flush: " + FormatLastFlush(status) +
                "    Time: " + FormatFlushTime(status.lastFlushAttemptUtc),
                csvLabelStyle);
            GUILayout.Label(
                "Final flush: " + FormatAttempt(status.finalFlushAttempted, status.finalFlushSuccess) +
                "    Dispose: " + FormatAttempt(status.writerDisposeAttempted, status.writerDisposeSuccess) +
                "    Verified: " + (status.finalPrimaryCsvVerified ? "YES" : "no"),
                csvLabelStyle);
            GUILayout.Label(
                "Last error: " + (string.IsNullOrWhiteSpace(status.lastFlushError) ? "none" : status.lastFlushError),
                csvLabelStyle);
            GUILayout.EndArea();
        }

        private void ApplyDebugInfoVisibility()
        {
            debugInfoVisible = sharedDebugInfoVisible;

            VppExternalInputBridge[] bridges = FindObjectsByType<VppExternalInputBridge>();
            for (int i = 0; i < bridges.Length; i++)
            {
                if (bridges[i] != null)
                    bridges[i].showDriveModeHud = debugInfoVisible;
            }

            VppBuiltInVehicleControls[] builtInControls = FindObjectsByType<VppBuiltInVehicleControls>();
            for (int i = 0; i < builtInControls.Length; i++)
            {
                if (builtInControls[i] != null)
                    builtInControls[i].showAuxiliaryControlsHud = debugInfoVisible;
            }

            SimpleResearchVehicleController[] simpleControllers = FindObjectsByType<SimpleResearchVehicleController>();
            for (int i = 0; i < simpleControllers.Length; i++)
            {
                if (simpleControllers[i] != null)
                    simpleControllers[i].showDebugHud = debugInfoVisible;
            }

            DrivingExperimentManager[] experimentManagers = FindObjectsByType<DrivingExperimentManager>();
            for (int i = 0; i < experimentManagers.Length; i++)
            {
                if (experimentManagers[i] != null)
                    experimentManagers[i].showExperimentHud = debugInfoVisible;
            }

            CarFollowingExperimentController[] carFollowingControllers = FindObjectsByType<CarFollowingExperimentController>();
            for (int i = 0; i < carFollowingControllers.Length; i++)
            {
                if (carFollowingControllers[i] != null)
                    carFollowingControllers[i].showHud = debugInfoVisible;
            }

            ExperimentSessionController[] sessionControllers = FindObjectsByType<ExperimentSessionController>();
            for (int i = 0; i < sessionControllers.Length; i++)
            {
                if (sessionControllers[i] != null)
                    sessionControllers[i].showHud = debugInfoVisible;
            }

            SetKnownDebugPanelsActive(debugInfoVisible);

            if (includeVppTelemetry)
                SetVppTelemetryVisible(debugInfoVisible);
        }

        private void EnsureCsvHudStyles()
        {
            if (csvBoxStyle != null)
                return;

            csvBoxStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(12, 12, 10, 10)
            };
            csvBoxStyle.normal.textColor = Color.white;

            csvTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold
            };
            csvTitleStyle.normal.textColor = Color.white;

            csvLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                wordWrap = true
            };
            csvLabelStyle.normal.textColor = Color.white;
        }

        private static string DisplayPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? "unknown" : path;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0L)
                return "unknown";
            return (bytes / (1024d * 1024d)).ToString("F2", CultureInfo.InvariantCulture) + " MB";
        }

        private static string FormatFlushTime(string utcText)
        {
            if (string.IsNullOrWhiteSpace(utcText))
                return "never";
            if (DateTime.TryParse(
                utcText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime utc))
            {
                return utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            }
            return utcText;
        }

        private static string FormatAttempt(bool attempted, bool success)
        {
            return !attempted ? "pending" : success ? "OK" : "FAILED";
        }

        private static string FormatLastFlush(DrivingDataLogger.PrimaryCsvStatusSnapshot status)
        {
            if (string.IsNullOrWhiteSpace(status.lastFlushAttemptUtc))
                return "pending";
            return status.lastFlushSuccess ? "OK" : "FAILED";
        }

        private static bool TryToggleSharedVisibility()
        {
            if (lastToggleFrame == Time.frameCount)
                return false;

            sharedDebugInfoVisible = !sharedDebugInfoVisible;
            lastToggleFrame = Time.frameCount;
            return true;
        }

        private static void SetKnownDebugPanelsActive(bool visible)
        {
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject candidate = objects[i];
                if (candidate == null || !candidate.scene.IsValid() || !candidate.scene.isLoaded)
                    continue;

                if (IsKnownDebugPanelName(candidate.name))
                    candidate.SetActive(visible);
            }
        }

        private static bool IsKnownDebugPanelName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
                return false;

            return objectName.Equals("Device Debug Info", StringComparison.OrdinalIgnoreCase)
                || objectName.Equals("Device Debug Info Panel", StringComparison.OrdinalIgnoreCase)
                || objectName.Equals("Debug Info Panel", StringComparison.OrdinalIgnoreCase);
        }

        private static void SetVppTelemetryVisible(bool visible)
        {
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour.GetType().FullName != "VehiclePhysics.VPTelemetry")
                    continue;

                SetBoolField(behaviour, "showTelemetry", visible);
                SetBoolField(behaviour, "enableHotKey", visible);
            }
        }

        private static void SetBoolField(object target, string fieldName, bool value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(bool))
                field.SetValue(target, value);
        }
    }
}
