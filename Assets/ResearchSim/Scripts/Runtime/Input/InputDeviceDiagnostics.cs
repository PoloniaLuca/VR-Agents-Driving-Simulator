using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace ResearchSim
{
    /// <summary>
    /// Runtime helper for mapping real HID controls. Enable OnGUI in Play Mode,
    /// move one control at a time, then copy the changing control path into
    /// FanatecInputMapping.
    /// </summary>
    public sealed class InputDeviceDiagnostics : MonoBehaviour
    {
        public bool logDevicesOnStart = true;
        public bool showOnGUI;
        public string deviceNameOrPathContains = "fanatec;teensy;arduino";
        public string additionalDeviceNameOrPathContains = "teensy;arduino";
        [Range(0f, 1f)] public float valueThreshold = 0.02f;
        public int maxVisibleControls = 28;
        public KeyCode toggleKey = KeyCode.F10;
        public bool logToFileWhenVisible = true;
        public float fileLogChangeThreshold = 0.02f;
        public float fileLogIntervalSeconds = 0.05f;
        [Header("Steering axis discovery")]
        public bool showAllAxes = true;
        public int maxVisibleAxes = 80;
        public float axisDisplayThreshold = 0.0001f;
        public float axisChangeLogThreshold = 0.0001f;
        public bool showAllButtons = true;
        public int maxVisibleButtons = 80;

        private Vector2 scroll;
        private readonly Dictionary<string, float> lastLoggedValues = new Dictionary<string, float>();
        private readonly Dictionary<string, float> lastGuiValues = new Dictionary<string, float>();
        private readonly Dictionary<string, float> lastGuiUnprocessedValues = new Dictionary<string, float>();
        private readonly Dictionary<string, float> peakGuiDeltas = new Dictionary<string, float>();
        private readonly Dictionary<string, float> peakGuiUnprocessedDeltas = new Dictionary<string, float>();
        private string logFilePath;
        private float nextFileLogTime;
        private GUIStyle diagnosticsBoxStyle;
        private GUIStyle diagnosticsLabelStyle;
        private GUIStyle diagnosticsTitleStyle;
        private bool lastShowOnGUI;

        private void Start()
        {
            if (logDevicesOnStart)
                LogDevices();
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                showOnGUI = !showOnGUI;

            if (showOnGUI != lastShowOnGUI)
            {
                if (showOnGUI)
                    BeginFileLog();
                else
                    Debug.Log("[InputDeviceDiagnostics] Diagnostics disabled. Log file: " + logFilePath);

                lastShowOnGUI = showOnGUI;
            }

            if (showOnGUI && logToFileWhenVisible)
                LogChangedControlsToFile();
        }

        [ContextMenu("Log Input Devices")]
        public void LogDevices()
        {
#if ENABLE_INPUT_SYSTEM
            foreach (InputDevice device in InputSystem.devices)
            {
                Debug.Log(BuildDeviceSummary(device, includeControls: true));
            }
#else
            Debug.LogWarning("[InputDeviceDiagnostics] Unity Input System is not enabled.");
#endif
        }

        public string CurrentLogFilePath
        {
            get { return logFilePath; }
        }

        private void OnGUI()
        {
            if (!showOnGUI)
                return;

#if ENABLE_INPUT_SYSTEM
            EnsureGuiStyles();

            GUILayout.BeginArea(new Rect(12f, 120f, 760f, Screen.height - 140f), diagnosticsBoxStyle);
            GUILayout.Label("Input Device Diagnostics", diagnosticsTitleStyle);
            GUILayout.Label("Toggle: " + toggleKey + "    Log: " + (string.IsNullOrEmpty(logFilePath) ? "not started" : logFilePath), diagnosticsLabelStyle);
            scroll = GUILayout.BeginScrollView(scroll);

            foreach (InputDevice device in InputSystem.devices)
            {
                if (!ShouldShowDevice(device))
                    continue;

                GUILayout.Label(FormatDeviceLine(device), diagnosticsLabelStyle);
                GUILayout.Label("  Capabilities: " + device.description.capabilities, diagnosticsLabelStyle);
                DrawChangingControls(device);
                if (showAllAxes)
                    DrawAxisDiscovery(device);
                if (showAllButtons)
                    DrawButtonDiscovery(device);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
#else
            EnsureGuiStyles();
            GUI.Label(new Rect(12f, 120f, 620f, 34f), "Input System not enabled.", diagnosticsLabelStyle);
#endif
        }

        private void EnsureGuiStyles()
        {
            if (diagnosticsBoxStyle == null)
            {
                diagnosticsBoxStyle = new GUIStyle(GUI.skin.box)
                {
                    padding = new RectOffset(14, 14, 12, 12)
                };
                diagnosticsBoxStyle.normal.background = Texture2D.whiteTexture;
                diagnosticsBoxStyle.normal.textColor = Color.black;
            }

            if (diagnosticsLabelStyle == null)
            {
                diagnosticsLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                    wordWrap = false,
                    clipping = TextClipping.Clip
                };
                diagnosticsLabelStyle.normal.textColor = Color.black;
            }

            if (diagnosticsTitleStyle == null)
            {
                diagnosticsTitleStyle = new GUIStyle(diagnosticsLabelStyle)
                {
                    fontSize = 20,
                    fontStyle = FontStyle.Bold
                };
                diagnosticsTitleStyle.normal.textColor = Color.black;
            }
        }

        private void BeginFileLog()
        {
            if (!logToFileWhenVisible)
                return;

            string folder = Path.Combine(ResearchDataPaths.ProjectRoot, ResearchDataPaths.DataRootFolderName, "InputDiagnostics");
            Directory.CreateDirectory(folder);
            logFilePath = Path.Combine(folder, "input_diagnostics_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            lastLoggedValues.Clear();

            AppendLine("Input diagnostics started");
            AppendLine("Build data path: " + Application.dataPath);
            AppendLine("Project root: " + ResearchDataPaths.ProjectRoot);
            AppendLine("Filter: " + (string.IsNullOrWhiteSpace(deviceNameOrPathContains) ? "<all>" : deviceNameOrPathContains));
#if ENABLE_INPUT_SYSTEM
            foreach (InputDevice device in InputSystem.devices)
                AppendLine(BuildDeviceSummary(device, includeControls: true));
#else
            AppendLine("Unity Input System is not enabled.");
#endif
            Debug.Log("[InputDeviceDiagnostics] Diagnostics enabled. Log file: " + logFilePath);
        }

        private void LogChangedControlsToFile()
        {
#if ENABLE_INPUT_SYSTEM
            if (string.IsNullOrEmpty(logFilePath) || Time.unscaledTime < nextFileLogTime)
                return;

            nextFileLogTime = Time.unscaledTime + fileLogIntervalSeconds;

            foreach (InputDevice device in InputSystem.devices)
            {
                if (!ShouldShowDevice(device))
                    continue;

                foreach (InputControl control in device.allControls)
                {
                    if (!TryReadFloat(control, out float value))
                        continue;

                    float unprocessed = TryReadUnprocessedFloat(control, out float rawValue) ? rawValue : value;
                    string key = device.path + "|" + control.path;
                    lastLoggedValues.TryGetValue(key, out float previous);
                    bool activeEnough = Mathf.Abs(value) >= valueThreshold;
                    float effectiveChangeThreshold = control is AxisControl
                        ? Mathf.Min(fileLogChangeThreshold, axisChangeLogThreshold)
                        : fileLogChangeThreshold;
                    bool changedEnough = Mathf.Abs(value - previous) >= effectiveChangeThreshold ||
                                         Mathf.Abs(unprocessed - previous) >= effectiveChangeThreshold;
                    if (!activeEnough && !changedEnough)
                        continue;

                    lastLoggedValues[key] = unprocessed;
                    AppendLine(
                        Time.realtimeSinceStartup.ToString("F3") + "\t" +
                        FormatDeviceLine(device) + "\tcontrol=" + control.path +
                        "\tname=" + control.name +
                        "\tlayout=" + control.layout +
                        "\tprocessed=" + value.ToString("F6") +
                        "\tunprocessed=" + unprocessed.ToString("F6") +
                        "\tstate=" + FormatStateBlock(control) +
                        "\tusages=" + FormatUsages(control));
                }
            }
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private void DrawChangingControls(InputDevice device)
        {
            int shown = 0;
            foreach (InputControl control in device.allControls)
            {
                if (shown >= maxVisibleControls)
                    break;

                if (!TryReadFloat(control, out float value) || Mathf.Abs(value) < valueThreshold)
                    continue;

                GUILayout.Label("  active " + control.path + " = " + value.ToString("F4"), diagnosticsLabelStyle);
                shown++;
            }
        }

        private void DrawAxisDiscovery(InputDevice device)
        {
            List<ControlSnapshot> axes = new List<ControlSnapshot>();
            foreach (InputControl control in device.allControls)
            {
                if (!(control is AxisControl) || control is ButtonControl)
                    continue;

                if (!TryReadFloat(control, out float value))
                    continue;

                float unprocessed = TryReadUnprocessedFloat(control, out float rawValue) ? rawValue : value;
                string key = device.path + "|" + control.path;
                lastGuiValues.TryGetValue(key, out float previous);
                float delta = value - previous;
                float absDelta = Mathf.Abs(delta);
                lastGuiValues[key] = value;

                lastGuiUnprocessedValues.TryGetValue(key, out float previousUnprocessed);
                float unprocessedDelta = unprocessed - previousUnprocessed;
                float absUnprocessedDelta = Mathf.Abs(unprocessedDelta);
                lastGuiUnprocessedValues[key] = unprocessed;

                peakGuiDeltas.TryGetValue(key, out float peak);
                if (absDelta > peak)
                {
                    peak = absDelta;
                    peakGuiDeltas[key] = peak;
                }

                peakGuiUnprocessedDeltas.TryGetValue(key, out float unprocessedPeak);
                if (absUnprocessedDelta > unprocessedPeak)
                {
                    unprocessedPeak = absUnprocessedDelta;
                    peakGuiUnprocessedDeltas[key] = unprocessedPeak;
                }

                axes.Add(new ControlSnapshot(
                    control.path,
                    control.name,
                    control.layout,
                    FormatStateBlock(control),
                    FormatUsages(control),
                    value,
                    unprocessed,
                    delta,
                    unprocessedDelta,
                    peak,
                    unprocessedPeak));
            }

            axes.Sort(CompareSnapshots);

            GUILayout.Label("  Axis discovery: proc/unproc/delta/peak. Use the path whose unproc changes with 1-2 degrees.", diagnosticsLabelStyle);
            int shown = 0;
            for (int i = 0; i < axes.Count && shown < maxVisibleAxes; i++)
            {
                ControlSnapshot axis = axes[i];
                GUILayout.Label(
                    "  axis " + axis.Path +
                    "  proc=" + axis.Value.ToString("F6") +
                    "  unproc=" + axis.UnprocessedValue.ToString("F6") +
                    "  d=" + axis.Delta.ToString("+0.000000;-0.000000; 0.000000") +
                    "  rawD=" + axis.UnprocessedDelta.ToString("+0.000000;-0.000000; 0.000000") +
                    "  peak=" + axis.PeakDelta.ToString("F6") +
                    "  rawPeak=" + axis.UnprocessedPeakDelta.ToString("F6") +
                    "  name=" + axis.Name +
                    "  layout=" + axis.Layout +
                    "  " + axis.StateBlock +
                    "  usages=" + axis.Usages,
                    diagnosticsLabelStyle);
                shown++;
            }
        }

        private static int CompareSnapshots(ControlSnapshot left, ControlSnapshot right)
        {
            int peakComparison = Mathf.Abs(right.UnprocessedPeakDelta).CompareTo(Mathf.Abs(left.UnprocessedPeakDelta));
            if (peakComparison != 0)
                return peakComparison;

            int deltaComparison = Mathf.Abs(right.UnprocessedDelta).CompareTo(Mathf.Abs(left.UnprocessedDelta));
            if (deltaComparison != 0)
                return deltaComparison;

            int processedPeakComparison = Mathf.Abs(right.PeakDelta).CompareTo(Mathf.Abs(left.PeakDelta));
            if (processedPeakComparison != 0)
                return processedPeakComparison;

            return string.CompareOrdinal(left.Path, right.Path);
        }

        private void DrawButtonDiscovery(InputDevice device)
        {
            List<ButtonSnapshot> buttons = new List<ButtonSnapshot>();
            foreach (InputControl control in device.allControls)
            {
                ButtonControl button = control as ButtonControl;
                if (button == null)
                    continue;

                float processed = button.ReadValue();
                float unprocessed = button.ReadUnprocessedValue();
                bool pressed = button.isPressed;

                string key = device.path + "|" + control.path;
                lastGuiValues.TryGetValue(key, out float previous);
                float delta = processed - previous;
                lastGuiValues[key] = processed;

                lastGuiUnprocessedValues.TryGetValue(key, out float previousUnprocessed);
                float unprocessedDelta = unprocessed - previousUnprocessed;
                lastGuiUnprocessedValues[key] = unprocessed;

                peakGuiUnprocessedDeltas.TryGetValue(key, out float unprocessedPeak);
                float absUnprocessedDelta = Mathf.Abs(unprocessedDelta);
                if (absUnprocessedDelta > unprocessedPeak)
                {
                    unprocessedPeak = absUnprocessedDelta;
                    peakGuiUnprocessedDeltas[key] = unprocessedPeak;
                }

                buttons.Add(new ButtonSnapshot(
                    control.path,
                    control.name,
                    control.layout,
                    FormatStateBlock(control),
                    FormatUsages(control),
                    processed,
                    unprocessed,
                    delta,
                    unprocessedDelta,
                    unprocessedPeak,
                    pressed,
                    button.wasPressedThisFrame));
            }

            buttons.Sort(CompareButtons);

            GUILayout.Label("  Button / gear discovery: pressed buttons first. Use button paths for reverse/gears.", diagnosticsLabelStyle);
            int shown = 0;
            for (int i = 0; i < buttons.Count && shown < maxVisibleButtons; i++)
            {
                ButtonSnapshot button = buttons[i];
                GUILayout.Label(
                    "  button " + button.Path +
                    "  pressed=" + (button.Pressed ? "YES" : "no") +
                    "  down=" + (button.PressedThisFrame ? "YES" : "no") +
                    "  proc=" + button.Value.ToString("F3") +
                    "  unproc=" + button.UnprocessedValue.ToString("F3") +
                    "  rawD=" + button.UnprocessedDelta.ToString("+0.000;-0.000; 0.000") +
                    "  rawPeak=" + button.UnprocessedPeakDelta.ToString("F3") +
                    "  name=" + button.Name +
                    "  layout=" + button.Layout +
                    "  " + button.StateBlock +
                    "  usages=" + button.Usages,
                    diagnosticsLabelStyle);
                shown++;
            }
        }

        private static int CompareButtons(ButtonSnapshot left, ButtonSnapshot right)
        {
            int pressedComparison = right.Pressed.CompareTo(left.Pressed);
            if (pressedComparison != 0)
                return pressedComparison;

            int downComparison = right.PressedThisFrame.CompareTo(left.PressedThisFrame);
            if (downComparison != 0)
                return downComparison;

            int peakComparison = Mathf.Abs(right.UnprocessedPeakDelta).CompareTo(Mathf.Abs(left.UnprocessedPeakDelta));
            if (peakComparison != 0)
                return peakComparison;

            return string.CompareOrdinal(left.Path, right.Path);
        }
#endif

        private void AppendLine(string line)
        {
            if (string.IsNullOrEmpty(logFilePath))
                return;

            try
            {
                File.AppendAllText(logFilePath, line + System.Environment.NewLine);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[InputDeviceDiagnostics] Cannot write log: " + exception.Message);
            }
        }

#if ENABLE_INPUT_SYSTEM
        private bool ShouldShowDevice(InputDevice device)
        {
            if (device == null)
                return false;

            if (string.IsNullOrWhiteSpace(deviceNameOrPathContains))
                return true;

            string identity = (device.name + " " + device.displayName + " " + device.layout + " " + device.path + " " +
                               device.description.product + " " + device.description.manufacturer).ToLowerInvariant();
            return MatchesFilter(identity, deviceNameOrPathContains) ||
                   MatchesFilter(identity, additionalDeviceNameOrPathContains);
        }

        private static bool MatchesFilter(string identity, string filterText)
        {
            if (string.IsNullOrWhiteSpace(filterText))
                return false;

            string[] filters = filterText.ToLowerInvariant().Split(';', ',');
            for (int i = 0; i < filters.Length; i++)
            {
                string filter = filters[i].Trim();
                if (filter.Length > 0 && identity.Contains(filter))
                    return true;
            }

            return false;
        }

        private static string BuildDeviceSummary(InputDevice device, bool includeControls)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("[InputDeviceDiagnostics] " + FormatDeviceLine(device));

            if (!includeControls)
                return builder.ToString();

            foreach (InputControl control in device.allControls)
            {
                string kind = control is ButtonControl ? "button" : control is AxisControl ? "axis" : control.valueType.Name;
                builder.AppendLine("  " + kind + " | " + control.path + " | " + control.layout);
            }

            return builder.ToString();
        }

        private static string FormatDeviceLine(InputDevice device)
        {
            return device.displayName + " | name=" + device.name + " | layout=" + device.layout + " | path=" + device.path;
        }

        private static bool TryReadFloat(InputControl control, out float value)
        {
            value = 0f;

            if (control is AxisControl axis)
            {
                value = axis.ReadValue();
                return true;
            }

            if (control is ButtonControl button)
            {
                value = button.ReadValue();
                return true;
            }

            return false;
        }

        private static bool TryReadUnprocessedFloat(InputControl control, out float value)
        {
            value = 0f;

            if (control is AxisControl axis)
            {
                value = axis.ReadUnprocessedValue();
                return true;
            }

            if (control is ButtonControl button)
            {
                value = button.ReadUnprocessedValue();
                return true;
            }

            return false;
        }

        private static string FormatStateBlock(InputControl control)
        {
            if (control == null)
                return "state=<none>";

            return "state=format:" + control.stateBlock.format +
                   " byte:" + control.stateBlock.byteOffset +
                   " bit:" + control.stateBlock.bitOffset +
                   " bits:" + control.stateBlock.sizeInBits;
        }

        private static string FormatUsages(InputControl control)
        {
            if (control == null || control.usages.Count == 0)
                return "-";

            string result = control.usages[0].ToString();
            for (int i = 1; i < control.usages.Count; i++)
                result += "," + control.usages[i];

            return result;
        }

        private readonly struct ControlSnapshot
        {
            public readonly string Path;
            public readonly string Name;
            public readonly string Layout;
            public readonly string StateBlock;
            public readonly string Usages;
            public readonly float Value;
            public readonly float UnprocessedValue;
            public readonly float Delta;
            public readonly float UnprocessedDelta;
            public readonly float PeakDelta;
            public readonly float UnprocessedPeakDelta;

            public ControlSnapshot(
                string path,
                string name,
                string layout,
                string stateBlock,
                string usages,
                float value,
                float unprocessedValue,
                float delta,
                float unprocessedDelta,
                float peakDelta,
                float unprocessedPeakDelta)
            {
                Path = path;
                Name = name;
                Layout = layout;
                StateBlock = stateBlock;
                Usages = usages;
                Value = value;
                UnprocessedValue = unprocessedValue;
                Delta = delta;
                UnprocessedDelta = unprocessedDelta;
                PeakDelta = peakDelta;
                UnprocessedPeakDelta = unprocessedPeakDelta;
            }
        }

        private readonly struct ButtonSnapshot
        {
            public readonly string Path;
            public readonly string Name;
            public readonly string Layout;
            public readonly string StateBlock;
            public readonly string Usages;
            public readonly float Value;
            public readonly float UnprocessedValue;
            public readonly float Delta;
            public readonly float UnprocessedDelta;
            public readonly float UnprocessedPeakDelta;
            public readonly bool Pressed;
            public readonly bool PressedThisFrame;

            public ButtonSnapshot(
                string path,
                string name,
                string layout,
                string stateBlock,
                string usages,
                float value,
                float unprocessedValue,
                float delta,
                float unprocessedDelta,
                float unprocessedPeakDelta,
                bool pressed,
                bool pressedThisFrame)
            {
                Path = path;
                Name = name;
                Layout = layout;
                StateBlock = stateBlock;
                Usages = usages;
                Value = value;
                UnprocessedValue = unprocessedValue;
                Delta = delta;
                UnprocessedDelta = unprocessedDelta;
                UnprocessedPeakDelta = unprocessedPeakDelta;
                Pressed = pressed;
                PressedThisFrame = pressedThisFrame;
            }
        }
#endif
    }
}
