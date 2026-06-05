// ──────────────────────────────────────────────────────────────────────────────
// AICarInput_Extensions.cs
//
// Adds the ApplyPreset() runtime helper to AICarInput so that AICarManager
// can reconfigure AI vehicles without requiring public serialized fields.
//
// This file is a PARTIAL CLASS of AICarInput and must live in the same
// namespace and assembly.  In Unity this simply means placing both files
// in the same project folder – Unity compiles them together.
// ──────────────────────────────────────────────────────────────────────────────

using UnityEngine;

namespace DrivingSim
{
    public partial class AICarInput
    {
        /// <summary>
        /// Applies a complete behavioural preset at runtime (called by AICarManager).
        /// All values are validated and clamped to safe ranges before assignment.
        /// </summary>
        public void ApplyPreset(
            float desiredSpeedKmhVal,
            float maxSpeedKmhVal,
            float throttleAggressivenessVal,
            float brakeAggressivenessVal,
            float tdVal,
            float trVal,
            float tsVal,
            float aNormalVal,
            float aMaxBrakeVal)
        {
            desiredSpeedKmh       = Mathf.Max(10f,  desiredSpeedKmhVal);
            maxSpeedKmh           = Mathf.Max(desiredSpeedKmh, maxSpeedKmhVal);
            throttleAggressiveness = Mathf.Clamp01(throttleAggressivenessVal);
            brakeAggressiveness    = Mathf.Clamp01(brakeAggressivenessVal);

            // Fritzsche time-gap constraints: TD > Ts > Tr  (Section 3.5)
            Tr  = Mathf.Max(0.1f,  trVal);
            Ts  = Mathf.Max(Tr + 0.1f,  tsVal);
            TD  = Mathf.Max(Ts + 0.1f,  tdVal);

            aNormal   = Mathf.Max(0.5f, aNormalVal);
            aMaxBrake = Mathf.Max(1.0f, aMaxBrakeVal);
        }
    }
}
