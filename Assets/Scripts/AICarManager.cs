using System.Collections.Generic;
using UnityEngine;

namespace DrivingSim
{
    /// <summary>
    /// Optional manager that holds references to all AI vehicles in the scene
    /// and exposes helper methods for runtime configuration and debug overlay.
    ///
    /// Usage:
    ///   1. Add this component to a dedicated "AIManager" GameObject.
    ///   2. Drag each AI vehicle root GameObject into the aiVehicles list.
    ///   3. Optionally configure presets (see AIVehiclePreset) and apply them
    ///      at Start via applyPresetsOnStart.
    /// </summary>
    public class AICarManager : MonoBehaviour
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Data structures
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A named set of parameters that can be applied to an AICarInput
        /// (and optionally its CarController) at runtime.
        /// </summary>
        [System.Serializable]
        public class AIVehiclePreset
        {
            [Tooltip("Human-readable name for this preset (e.g. 'Sports Car', 'Truck').")]
            public string presetName = "Default";

            [Header("Speed")]
            public float desiredSpeedKmh  = 120f;
            public float maxSpeedKmh      = 160f;

            [Header("Throttle / Brake aggressiveness [0-1]")]
            [Range(0.1f, 1f)] public float throttleAggressiveness = 0.8f;
            [Range(0.1f, 1f)] public float brakeAggressiveness    = 0.9f;

            [Header("Fritzsche parameters")]
            public float TD         = 1.8f;   // desired time gap
            public float Tr         = 0.5f;   // risky time gap
            public float Ts         = 1.0f;   // safe time gap
            public float aNormal    = 2.0f;   // normal acceleration [m/s²]
            public float aMaxBrake  = 6.0f;   // max deceleration [m/s²]

            [Header("CarController overrides (0 = keep current)")]
            public float maxMotorTorque  = 0f;
            public float maxBrakeTorque  = 0f;
            public float maxSteeringAngle = 0f;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Inspector fields
        // ─────────────────────────────────────────────────────────────────────

        [Header("AI Vehicle roots (must have AICarInput + CarController)")]
        [SerializeField] private List<GameObject> aiVehicles = new();

        [Header("Presets")]
        [SerializeField] private List<AIVehiclePreset> presets = new();

        [Tooltip("If true, assigns presets[i] to aiVehicles[i] on Start " +
                 "(pairs are matched by index; excess entries are ignored).")]
        [SerializeField] private bool applyPresetsOnStart = false;

        [Header("Debug")]
        [SerializeField] private bool showDebugGUI = true;

        // ─────────────────────────────────────────────────────────────────────
        //  Runtime
        // ─────────────────────────────────────────────────────────────────────
        private readonly List<AICarInput>    cachedInputs      = new();
        private readonly List<CarController> cachedControllers = new();

        private void Awake()
        {
            foreach (var go in aiVehicles)
            {
                if (go == null) continue;
                cachedInputs.Add(go.GetComponent<AICarInput>());
                cachedControllers.Add(go.GetComponent<CarController>());
            }
        }

        private void Start()
        {
            if (!applyPresetsOnStart) return;

            int count = Mathf.Min(aiVehicles.Count, presets.Count);
            for (int i = 0; i < count; i++)
                ApplyPreset(i, presets[i]);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Applies a preset to the AI vehicle at <paramref name="vehicleIndex"/>.
        /// Uses reflection-free SerializedField approach: directly sets public
        /// serialized fields via the cached component references.
        /// </summary>
        public void ApplyPreset(int vehicleIndex, AIVehiclePreset preset)
        {
            if (vehicleIndex < 0 || vehicleIndex >= cachedInputs.Count)
            {
                Debug.LogWarning($"[AICarManager] Vehicle index {vehicleIndex} out of range.");
                return;
            }

            AICarInput input = cachedInputs[vehicleIndex];
            if (input == null) return;

            // We expose the fields via a helper method on AICarInput
            input.ApplyPreset(
                preset.desiredSpeedKmh,
                preset.maxSpeedKmh,
                preset.throttleAggressiveness,
                preset.brakeAggressiveness,
                preset.TD, preset.Tr, preset.Ts,
                preset.aNormal, preset.aMaxBrake);

            // Optional CarController overrides
            CarController cc = cachedControllers[vehicleIndex];
            if (cc != null)
            {
                if (preset.maxMotorTorque  > 0f) cc.SetMaxMotorTorque(preset.maxMotorTorque);
                if (preset.maxBrakeTorque  > 0f) cc.SetMaxBrakeTorque(preset.maxBrakeTorque);
                if (preset.maxSteeringAngle > 0f) cc.SetMaxSteeringAngle(preset.maxSteeringAngle);
            }
        }

        /// <summary>Returns all registered AI input components (read-only).</summary>
        public IReadOnlyList<AICarInput> GetAllInputs() => cachedInputs;

        // ─────────────────────────────────────────────────────────────────────
        //  Debug overlay
        // ─────────────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (!showDebugGUI) return;

            GUILayout.BeginArea(new Rect(10, 10, 260, 30 + cachedInputs.Count * 22));
            GUILayout.Label("<b>AI Vehicles</b>", new GUIStyle(GUI.skin.label)
                { richText = true, fontSize = 13 });

            for (int i = 0; i < cachedInputs.Count; i++)
            {
                AICarInput inp = cachedInputs[i];
                if (inp == null) continue;
                GUILayout.Label(
                    $"[{i}] T:{inp.Throttle:F2} B:{inp.Brake:F2} S:{inp.Steering:F2}",
                    new GUIStyle(GUI.skin.label) { fontSize = 11 });
            }
            GUILayout.EndArea();
        }
    }
}
