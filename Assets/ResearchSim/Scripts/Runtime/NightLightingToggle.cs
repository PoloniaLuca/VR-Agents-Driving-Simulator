using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace ResearchSim
{
    /// <summary>
    /// Temporary lighting toggle for checking VPP headlights in a dark scene.
    /// It only changes runtime lighting and restores the original scene values.
    /// </summary>
    public sealed class NightLightingToggle : MonoBehaviour
    {
        public KeyCode toggleKey = KeyCode.F12;
        public bool nightModeEnabled;
        public bool logToggleState = true;

        [Header("Night lighting")]
        public Color nightAmbientColor = new Color(0.008f, 0.008f, 0.012f);
        public Color nightSkyColor = new Color(0.002f, 0.004f, 0.012f);
        public Color nightFogColor = new Color(0.015f, 0.015f, 0.02f);
        [Range(0f, 1f)] public float directionalLightMultiplier = 0.01f;
        public bool enableFogAtNight;
        public float nightFogDensity = 0.004f;

        private AmbientMode originalAmbientMode;
        private Color originalAmbientLight;
        private float originalAmbientIntensity;
        private bool originalFog;
        private Color originalFogColor;
        private float originalFogDensity;
        private Light[] directionalLights;
        private float[] originalDirectionalIntensities;
        private Camera[] cameras;
        private CameraClearFlags[] originalCameraClearFlags;
        private Color[] originalCameraBackgrounds;

        private void Awake()
        {
            CaptureOriginalLighting();
        }

        private void Update()
        {
            if (!Input.GetKeyDown(toggleKey) || IsTextEntryActive())
                return;

            nightModeEnabled = !nightModeEnabled;
            ApplyLighting();

            if (logToggleState)
                Debug.Log("[NightLightingToggle] Night lighting " + (nightModeEnabled ? "enabled" : "disabled") + " with key " + toggleKey + ".");
        }

        private void OnDisable()
        {
            if (!nightModeEnabled)
                return;

            nightModeEnabled = false;
            ApplyLighting();
        }

        private static bool IsTextEntryActive()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || eventSystem.currentSelectedGameObject == null)
                return false;

            GameObject selected = eventSystem.currentSelectedGameObject;
            if (selected.GetComponent<UnityEngine.UI.InputField>() != null)
                return true;

            Component[] components = selected.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null && component.GetType().Name.Contains("InputField"))
                    return true;
            }

            return false;
        }

        private void CaptureOriginalLighting()
        {
            originalAmbientMode = RenderSettings.ambientMode;
            originalAmbientLight = RenderSettings.ambientLight;
            originalAmbientIntensity = RenderSettings.ambientIntensity;
            originalFog = RenderSettings.fog;
            originalFogColor = RenderSettings.fogColor;
            originalFogDensity = RenderSettings.fogDensity;

            Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            int directionalCount = 0;
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].type == LightType.Directional)
                    directionalCount++;
            }

            directionalLights = new Light[directionalCount];
            originalDirectionalIntensities = new float[directionalCount];

            int index = 0;
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null || light.type != LightType.Directional)
                    continue;

                directionalLights[index] = light;
                originalDirectionalIntensities[index] = light.intensity;
                index++;
            }

            cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
            originalCameraClearFlags = new CameraClearFlags[cameras.Length];
            originalCameraBackgrounds = new Color[cameras.Length];
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera == null)
                    continue;

                originalCameraClearFlags[i] = camera.clearFlags;
                originalCameraBackgrounds[i] = camera.backgroundColor;
            }
        }

        private void ApplyLighting()
        {
            if (nightModeEnabled)
                ApplyNightLighting();
            else
                RestoreOriginalLighting();
        }

        private void ApplyNightLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = nightAmbientColor;
            RenderSettings.ambientIntensity = 0f;
            RenderSettings.fog = enableFogAtNight;
            RenderSettings.fogColor = nightFogColor;
            RenderSettings.fogDensity = nightFogDensity;

            for (int i = 0; i < directionalLights.Length; i++)
            {
                if (directionalLights[i] != null)
                    directionalLights[i].intensity = originalDirectionalIntensities[i] * directionalLightMultiplier;
            }

            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera == null)
                    continue;

                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = nightSkyColor;
            }
        }

        private void RestoreOriginalLighting()
        {
            RenderSettings.ambientMode = originalAmbientMode;
            RenderSettings.ambientLight = originalAmbientLight;
            RenderSettings.ambientIntensity = originalAmbientIntensity;
            RenderSettings.fog = originalFog;
            RenderSettings.fogColor = originalFogColor;
            RenderSettings.fogDensity = originalFogDensity;

            for (int i = 0; i < directionalLights.Length; i++)
            {
                if (directionalLights[i] != null)
                    directionalLights[i].intensity = originalDirectionalIntensities[i];
            }

            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera == null)
                    continue;

                camera.clearFlags = originalCameraClearFlags[i];
                camera.backgroundColor = originalCameraBackgrounds[i];
            }
        }
    }
}
