using UnityEngine;
using System.Collections.Generic;

namespace ResearchSim
{
    [RequireComponent(typeof(LeadVehicleController))]
    public sealed class LeadVehicleVisualSync : MonoBehaviour
    {
        private LeadVehicleController controller;
        private MaterialPropertyBlock materialBlock;
        private Transform[] spinningWheelVisuals;
        private Quaternion[] wheelInitialLocalRotations;
        private float wheelSpinDegrees;
        
        [Tooltip("The MeshRenderer containing the brake light material. E.g. Boot_light_brakes_Glow")]
        public MeshRenderer[] brakeLightRenderers;
        [Tooltip("Approximate visual wheel radius used only to spin the leader wheels.")]
        [Min(0.05f)] public float wheelRadiusMeters = 0.34f;
        public Vector3 wheelSpinAxis = Vector3.right;

        private void Awake()
        {
            controller = GetComponent<LeadVehicleController>();
            materialBlock = new MaterialPropertyBlock();
            NormalizeVisualMaterials();
            CacheWheelVisuals();

            AutoBindRearLights();
            ConfigureBrakeLightRenderers();
            
            SetBrakeLights(false);
        }

        private void OnEnable()
        {
            if (controller != null)
            {
                controller.OnDecelerationStart += HandleDecelerationStart;
                controller.OnCruiseRestored += HandleCruiseRestored;
            }
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.OnDecelerationStart -= HandleDecelerationStart;
                controller.OnCruiseRestored -= HandleCruiseRestored;
            }
        }
        
        private void Update()
        {
            // Fallback sync in case events are missed or state changes abruptly
            if (controller != null)
            {
                UpdateWheelSpin();
                SetBrakeLights(controller.IsDecelerating);
            }
        }

        private void HandleDecelerationStart(float time) => SetBrakeLights(true);
        private void HandleCruiseRestored(float time) => SetBrakeLights(false);

        private void NormalizeVisualMaterials()
        {
            MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                string lowerName = renderer.name.ToLowerInvariant();

                // The extracted Sport Coupe visual includes glow meshes that
                // look like flat white patches without the full VPP lighting
                // stack. Brake glow remains controlled by SetBrakeLights.
                if (lowerName.Contains("glow") && !lowerName.Contains("brake"))
                {
                    renderer.gameObject.SetActive(false);
                    continue;
                }

                if (lowerName.Contains("window") || lowerName.Contains("glass") || lowerName.Contains("mirror"))
                    ApplyTint(renderer, new Color(0.08f, 0.11f, 0.12f, 0.9f));
            }
        }

        private void AutoBindRearLights()
        {
            var brakeFound = new List<MeshRenderer>();

            if (brakeLightRenderers != null)
            {
                for (int i = 0; i < brakeLightRenderers.Length; i++)
                {
                    if (brakeLightRenderers[i] != null && !brakeFound.Contains(brakeLightRenderers[i]))
                        brakeFound.Add(brakeLightRenderers[i]);
                }
            }

            MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                string n = renderer.name.ToLowerInvariant();
                if (IsBrakeLightCandidate(n) && !brakeFound.Contains(renderer))
                    brakeFound.Add(renderer);
            }

            brakeLightRenderers = brakeFound.ToArray();
        }

        private void CacheWheelVisuals()
        {
            Transform[] transforms = GetComponentsInChildren<Transform>(true);
            var found = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == null)
                    continue;

                string lowerName = candidate.name.ToLowerInvariant();
                if (!lowerName.StartsWith("wheel_"))
                    continue;

                if (lowerName.Contains("pivot") || lowerName.Contains("brake"))
                    continue;

                found.Add(candidate);
            }

            spinningWheelVisuals = found.ToArray();
            wheelInitialLocalRotations = new Quaternion[spinningWheelVisuals.Length];
            for (int i = 0; i < spinningWheelVisuals.Length; i++)
                wheelInitialLocalRotations[i] = spinningWheelVisuals[i].localRotation;
        }

        private void UpdateWheelSpin()
        {
            if (spinningWheelVisuals == null || spinningWheelVisuals.Length == 0)
                return;

            float radius = Mathf.Max(0.05f, wheelRadiusMeters);
            float angularDegreesPerSecond = (controller.CurrentSpeedMps / radius) * Mathf.Rad2Deg;
            wheelSpinDegrees += angularDegreesPerSecond * Time.deltaTime;

            Vector3 axis = wheelSpinAxis.sqrMagnitude > 0.0001f ? wheelSpinAxis.normalized : Vector3.right;
            Quaternion spin = Quaternion.AngleAxis(wheelSpinDegrees, axis);

            for (int i = 0; i < spinningWheelVisuals.Length; i++)
            {
                if (spinningWheelVisuals[i] != null)
                    spinningWheelVisuals[i].localRotation = wheelInitialLocalRotations[i] * spin;
            }
        }

        private void ConfigureBrakeLightRenderers()
        {
            if (brakeLightRenderers == null || brakeLightRenderers.Length == 0)
                return;

            for (int i = 0; i < brakeLightRenderers.Length; i++)
            {
                MeshRenderer renderer = brakeLightRenderers[i];
                if (renderer == null)
                    continue;

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                Color tint = new Color(1f, 0.01f, 0.005f, 1f);
                Color emission = new Color(5f, 0f, 0f, 1f);

                ApplyTint(renderer, tint);
                ApplyEmission(renderer, emission);
            }

        }

        private static bool IsBrakeLightCandidate(string lowerName)
        {
            if (string.IsNullOrEmpty(lowerName))
                return false;

            if (lowerName.Contains("reverse") || lowerName.Contains("head"))
                return false;

            if (lowerName.Contains("brake"))
            {
                return lowerName.Contains("glow")
                    || lowerName.Contains("light")
                    || lowerName.Contains("rear_lights")
                    || lowerName.Contains("boot_light");
            }

            return false;
        }

        private void ApplyTint(Renderer renderer, Color color)
        {
            if (renderer == null || materialBlock == null)
                return;

            renderer.GetPropertyBlock(materialBlock);
            materialBlock.SetColor("_Color", color);
            materialBlock.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(materialBlock);
        }

        private void ApplyEmission(Renderer renderer, Color color)
        {
            if (renderer == null || materialBlock == null)
                return;

            renderer.GetPropertyBlock(materialBlock);
            materialBlock.SetColor("_EmissionColor", color);
            renderer.SetPropertyBlock(materialBlock);

            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material != null && material.HasProperty("_EmissionColor"))
                    material.EnableKeyword("_EMISSION");
            }
        }

        private void SetBrakeLights(bool state)
        {
            if (brakeLightRenderers != null)
            {
                for (int i = 0; i < brakeLightRenderers.Length; i++)
                {
                    if (brakeLightRenderers[i] != null && brakeLightRenderers[i].gameObject.activeSelf != state)
                        brakeLightRenderers[i].gameObject.SetActive(state);
                }
            }

        }
    }
}
