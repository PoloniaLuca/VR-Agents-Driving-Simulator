using UnityEngine;

namespace ResearchSim
{
    public sealed class SimpleEngineAudio : MonoBehaviour
    {
        public SimpleResearchVehicleController vehicleController;
        public HybridVehicleInput inputSource;
        public AudioClip idleClip;
        public AudioClip runClip;
        public AudioSource engineSource;
        public AudioSource transmissionSource;
        public AudioSource windSource;
        public float engineVolume = 0.42f;
        public float transmissionVolume = 0.18f;
        public float windVolume = 0.16f;
        public float minPitch = 0.82f;
        public float maxPitch = 1.35f;

        private AudioSource fallbackSource;

        private void Awake()
        {
            if (vehicleController == null)
                vehicleController = GetComponentInParent<SimpleResearchVehicleController>();
            if (inputSource == null)
                inputSource = GetComponentInParent<HybridVehicleInput>();

            engineSource = engineSource != null ? engineSource : FindSource("Engine");
            transmissionSource = transmissionSource != null ? transmissionSource : FindSource("Transmission");
            windSource = windSource != null ? windSource : FindSource("Wind");

            if (engineSource == null)
            {
                fallbackSource = GetComponent<AudioSource>();
                if (fallbackSource == null)
                    fallbackSource = gameObject.AddComponent<AudioSource>();

                engineSource = fallbackSource;
                ConfigureSource(engineSource, runClip != null ? runClip : idleClip, engineVolume, minPitch);
            }

            ConfigureExistingSource(engineSource, engineVolume, minPitch);
            ConfigureExistingSource(transmissionSource, 0f, minPitch);
            ConfigureExistingSource(windSource, 0f, 1f);
        }

        private void OnEnable()
        {
            PlayIfReady(engineSource);
            PlayIfReady(transmissionSource);
            PlayIfReady(windSource);
        }

        private void Update()
        {
            if (vehicleController == null)
                return;

            float speedT = Mathf.InverseLerp(0f, Mathf.Max(1f, vehicleController.maxSpeedKmh), vehicleController.CurrentSpeedKmh);
            float throttle = inputSource != null ? inputSource.Throttle : 0f;
            float load = Mathf.Clamp01(speedT * 0.55f + throttle * 0.35f);

            if (engineSource != null)
            {
                engineSource.volume = Mathf.Lerp(engineVolume * 0.45f, engineVolume, load);
                engineSource.pitch = Mathf.Lerp(minPitch, maxPitch, load);
            }

            if (transmissionSource != null)
            {
                transmissionSource.volume = Mathf.Lerp(0f, transmissionVolume, speedT);
                transmissionSource.pitch = Mathf.Lerp(0.75f, 1.18f, speedT);
            }

            if (windSource != null)
            {
                float windT = Mathf.SmoothStep(0f, 1f, speedT);
                windSource.volume = Mathf.Lerp(0f, windVolume, windT);
                windSource.pitch = Mathf.Lerp(0.85f, 1.12f, windT);
            }
        }

        private void ConfigureSource(AudioSource source, AudioClip clip, float volume, float pitch)
        {
            if (source == null)
                return;

            source.clip = clip;
            source.loop = true;
            source.playOnAwake = true;
            source.spatialBlend = 0.15f;
            source.volume = volume;
            source.pitch = pitch;
            source.minDistance = 1f;
            source.maxDistance = 35f;
        }

        private void ConfigureExistingSource(AudioSource source, float volume, float pitch)
        {
            if (source == null)
                return;

            source.loop = true;
            source.playOnAwake = true;
            source.spatialBlend = 0.2f;
            source.volume = volume;
            source.pitch = pitch;
            source.minDistance = 1f;
            source.maxDistance = 35f;
        }

        private AudioSource FindSource(string objectName)
        {
            Transform[] transforms = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null && transforms[i].name == objectName)
                    return transforms[i].GetComponent<AudioSource>();
            }

            return null;
        }

        private static void PlayIfReady(AudioSource source)
        {
            if (source != null && source.clip != null && !source.isPlaying)
                source.Play();
        }
    }
}
