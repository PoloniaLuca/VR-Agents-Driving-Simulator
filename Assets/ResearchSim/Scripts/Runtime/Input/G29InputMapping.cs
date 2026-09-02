using System;
using UnityEngine;

namespace ResearchSim
{
    /// <summary>
    /// Inspector-editable Unity Input System mapping for a Logitech G29.
    /// This is completely separate from FanatecInputMapping.
    ///
    /// The default axis paths are common G29 DirectInput paths, but should be
    /// verified with InputDeviceDiagnostics on the target PC.
    /// </summary>
    [CreateAssetMenu(menuName = "Research Sim/Input/G29 HID Mapping", fileName = "G29InputMapping")]
    public sealed class G29InputMapping : ScriptableObject
    {
        [Header("Device matching")]
        public string[] deviceMatchKeywords =
        {
            "g29",
            "driving force",
            "logitech"
        };

        public string manualDeviceNameOrPathContains = "G29 Driving Force Racing Wheel";
        public bool logWarningsForMissingControls = true;

        [Header("Visual calibration")]
        public float steeringWheelVisualRangeDegrees = 900f;

        [Header("Axes")]
        public AxisBinding steering =
            new AxisBinding("Steering", "stick/x", -1f, 1f, false, 0.01f);

        public AxisBinding throttle =
            new AxisBinding("Throttle", "z", -1f, 1f, false, 0.02f);

        public AxisBinding brake =
            new AxisBinding("Brake", "rz", -1f, 1f, false, 0.02f);

        public AxisBinding clutch =
            new AxisBinding("Clutch", "slider", -1f, 1f, false, 0.02f);

        public AxisBinding handbrakeAxis =
            new AxisBinding("Handbrake Axis", string.Empty, 0f, 1f, false, 0.02f);

        [Header("Optional buttons")]
        public ButtonBinding gearUp =
            new ButtonBinding("Gear Up / Right Paddle", "button5");

        public ButtonBinding gearDown =
            new ButtonBinding("Gear Down / Left Paddle", "button4");

        public ButtonBinding handbrakeButton =
            new ButtonBinding("Handbrake Button", string.Empty);

        public ButtonBinding ignition =
            new ButtonBinding("Ignition / Key Switch", string.Empty);

        [Header("H-pattern shifter")]
        public ButtonBinding reverse =
            new ButtonBinding("Reverse", string.Empty);

        public ButtonBinding neutral =
            new ButtonBinding("Neutral", string.Empty);

        public ButtonBinding gear1 =
            new ButtonBinding("Gear 1", string.Empty);

        public ButtonBinding gear2 =
            new ButtonBinding("Gear 2", string.Empty);

        public ButtonBinding gear3 =
            new ButtonBinding("Gear 3", string.Empty);

        public ButtonBinding gear4 =
            new ButtonBinding("Gear 4", string.Empty);

        public ButtonBinding gear5 =
            new ButtonBinding("Gear 5", string.Empty);

        public ButtonBinding gear6 =
            new ButtonBinding("Gear 6", string.Empty);

        public ButtonBinding gear7 =
            new ButtonBinding("Gear 7 diagnostic only", string.Empty);

        public bool HasAnyHPatternBinding()
        {
            return HasPath(reverse) || HasPath(neutral) ||
                   HasPath(gear1) || HasPath(gear2) ||
                   HasPath(gear3) || HasPath(gear4) ||
                   HasPath(gear5) || HasPath(gear6) ||
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

            [Range(0f, 0.5f)]
            public float deadzone;

            public AxisBinding(
                string label,
                string controlPath,
                float rawMin,
                float rawMax,
                bool invert,
                float deadzone)
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
            return binding != null &&
                   !string.IsNullOrWhiteSpace(binding.controlPath);
        }
    }
}
