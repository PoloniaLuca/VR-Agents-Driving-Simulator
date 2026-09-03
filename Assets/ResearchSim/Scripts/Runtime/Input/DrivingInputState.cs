using UnityEngine;

namespace ResearchSim
{
    public enum DrivingGear
    {
        Unset = -100,
        Reverse = -1,
        Neutral = 0,
        Gear1 = 1,
        Gear2 = 2,
        Gear3 = 3,
        Gear4 = 4,
        Gear5 = 5,
        Gear6 = 6,
        Gear7 = 7
    }

    /// <summary>
    /// Device-independent driving input. Providers convert keyboard, gamepad,
    /// wheel pedals or HID controls into this normalized shape before the
    /// vehicle bridge applies it to Vehicle Physics Pro.
    /// </summary>
    public struct DrivingInputState
    {
        public string SourceName;
        public float Steering;
        public float Throttle;
        public float Brake;
        public float Clutch;
        public float Handbrake;
        public DrivingGear Gear;
        public bool HasGear;
        public bool GearUp;
        public bool GearDown;
        public bool Ignition;
        public bool HasIgnition;

        public bool HasDrivingInput(float axisThreshold)
        {
            return Mathf.Abs(Steering) > axisThreshold ||
                   Throttle > axisThreshold ||
                   Brake > axisThreshold ||
                   Clutch > axisThreshold ||
                   Handbrake > axisThreshold ||
                   HasGear ||
                   GearUp ||
                   GearDown ||
                   HasIgnition;
        }

        public void Clamp()
        {
            Steering = Mathf.Clamp(Steering, -1f, 1f);
            Throttle = Mathf.Clamp01(Throttle);
            Brake = Mathf.Clamp01(Brake);
            Clutch = Mathf.Clamp01(Clutch);
            Handbrake = Mathf.Clamp01(Handbrake);
        }

        public static float NormalizeAxis(float raw, float rawMin, float rawMax, bool invert)
        {
            if (Mathf.Approximately(rawMin, rawMax))
                return 0f;

            float value = Mathf.InverseLerp(rawMin, rawMax, raw);
            value = invert ? 1f - value : value;
            return Mathf.Clamp01(value);
        }

        public static float NormalizeSignedAxis(float raw, float rawMin, float rawMax, bool invert, float deadzone)
        {
            float value = NormalizeAxis(raw, rawMin, rawMax, invert) * 2f - 1f;
            return Mathf.Clamp(ApplyDeadzone(value, deadzone), -1f, 1f);
        }

        public static float NormalizePedal(float raw, float rawMin, float rawMax, bool invert, float deadzone)
        {
            float value = NormalizeAxis(raw, rawMin, rawMax, invert);
            return Mathf.Clamp01(ApplyDeadzone(value, deadzone));
        }

        public static float ApplyDeadzone(float value, float deadzone)
        {
            deadzone = Mathf.Clamp01(deadzone);
            if (Mathf.Abs(value) <= deadzone)
                return 0f;

            if (value > 0f)
                return Mathf.InverseLerp(deadzone, 1f, value);

            return -Mathf.InverseLerp(deadzone, 1f, -value);
        }
    }
}
