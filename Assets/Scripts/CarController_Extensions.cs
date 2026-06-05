// ──────────────────────────────────────────────────────────────────────────────
// CarController_Extensions.cs
//
// Adds runtime setter methods to CarController so AICarManager can override
// physics parameters without modifying the original CarController.cs.
//
// Partial class – compiled together with CarController.cs by Unity.
// ──────────────────────────────────────────────────────────────────────────────

using UnityEngine;

namespace DrivingSim
{
    public partial class CarController
    {
        /// <summary>Override max motor torque at runtime (e.g. for preset application).</summary>
        public void SetMaxMotorTorque(float value)   => maxMotorTorque   = Mathf.Max(0f, value);

        /// <summary>Override max brake torque at runtime.</summary>
        public void SetMaxBrakeTorque(float value)   => maxBrakeTorque   = Mathf.Max(0f, value);

        /// <summary>Override max steering angle at runtime.</summary>
        public void SetMaxSteeringAngle(float value) => maxSteeringAngle = Mathf.Clamp(value, 1f, 90f);
    }
}
