using System;
using UnityEngine;

namespace ResearchSim
{
    /// <summary>
    /// Inspector-editable HID mapping for Fanatec or generic wheel devices.
    /// Control paths intentionally stay as strings because Windows/HID drivers
    /// may expose the DD1, pedals, shifter and key switch with different names.
    /// Use InputDeviceDiagnostics to discover the real paths on the lab PC.
    /// </summary>
    [CreateAssetMenu(menuName = "Research Sim/Input/Fanatec HID Mapping", fileName = "FanatecInputMapping")]
    public sealed class FanatecInputMapping : ScriptableObject
    {
        [Header("Device matching")]
        public string[] deviceMatchKeywords =
        {
            "fanatec",
            "podium",
            "dd1",
            "wheel",
            "clubsport"
        };
        public string manualDeviceNameOrPathContains;
        public bool logWarningsForMissingControls = true;

        [Header("Visual calibration")]
        public float steeringWheelVisualRangeDegrees = 900f;

        [Header("Axes")]
        public AxisBinding steering = new AxisBinding("Steering", "stick/x", -1f, 1f, false, 0f);
        public AxisBinding throttle = new AxisBinding("Throttle", "trigger", 0f, 1f, false, 0.02f);
        public AxisBinding brake = new AxisBinding("Brake", "rz", 0f, 1f, false, 0.02f);
        public AxisBinding clutch = new AxisBinding("Clutch", "slider", 0f, 1f, false, 0.02f);
        public AxisBinding handbrakeAxis = new AxisBinding("Handbrake Axis", string.Empty, 0f, 1f, false, 0.02f);

        [Header("H-pattern shifter buttons")]
        public ButtonBinding reverse = new ButtonBinding("Reverse", "button13");
        public ButtonBinding neutral = new ButtonBinding("Neutral", string.Empty);
        public ButtonBinding gear1 = new ButtonBinding("Gear 1", "button14");
        public ButtonBinding gear2 = new ButtonBinding("Gear 2", "button15");
        public ButtonBinding gear3 = new ButtonBinding("Gear 3", "button16");
        public ButtonBinding gear4 = new ButtonBinding("Gear 4", "button17");
        public ButtonBinding gear5 = new ButtonBinding("Gear 5", "button18");
        public ButtonBinding gear6 = new ButtonBinding("Gear 6", "button19");
        public ButtonBinding gear7 = new ButtonBinding("Gear 7 diagnostic only", "button20");

        [Header("Optional buttons")]
        public ButtonBinding gearUp = new ButtonBinding("Gear Up / Right Paddle", string.Empty);
        public ButtonBinding gearDown = new ButtonBinding("Gear Down / Left Paddle", string.Empty);
        public ButtonBinding handbrakeButton = new ButtonBinding("Handbrake Button", string.Empty);
        public ButtonBinding ignition = new ButtonBinding("Ignition / Key Switch", string.Empty);

        public bool HasAnyHPatternBinding()
        {
            return HasPath(reverse) || HasPath(neutral) || HasPath(gear1) || HasPath(gear2) ||
                   HasPath(gear3) || HasPath(gear4) || HasPath(gear5) || HasPath(gear6) ||
                   HasPath(gear7);
        }

        [Serializable]
        public sealed class AxisBinding
        {
            public string label;
            public string controlPath;
            public float rawMin;
            public float rawMax = 1f;
            public bool invert;
            [Range(0f, 0.5f)] public float deadzone;

            public AxisBinding(string label, string controlPath, float rawMin, float rawMax, bool invert, float deadzone)
            {
                this.label = label;
                this.controlPath = controlPath;
                this.rawMin = rawMin;
                this.rawMax = rawMax;
                this.invert = invert;
                this.deadzone = deadzone;
            }
        }

        [Serializable]
        public sealed class ButtonBinding
        {
            public string label;
            public string controlPath;

            public ButtonBinding(string label, string controlPath)
            {
                this.label = label;
                this.controlPath = controlPath;
            }
        }

        private static bool HasPath(ButtonBinding binding)
        {
            return binding != null && !string.IsNullOrWhiteSpace(binding.controlPath);
        }
    }
}
