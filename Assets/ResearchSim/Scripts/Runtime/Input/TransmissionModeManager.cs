using System;
using UnityEngine;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ResearchSim
{
    /// <summary>
    /// Owns the selected transmission mode, persists it with PlayerPrefs, and
    /// handles simple runtime selection keys. Vehicle code reads CurrentMode.
    /// </summary>
    public sealed class TransmissionModeManager : MonoBehaviour
    {
        public const string PlayerPrefsKey = "Driving.TransmissionMode";
        public const string PlayerPrefsVersionKey = "Driving.TransmissionMode.Version";
        private const int CurrentPlayerPrefsVersion = 2;

        [Header("Mode")]
        public TransmissionMode defaultMode = TransmissionMode.Automatic;
        public bool allowParticipantModeChange = true;
        public bool saveModeBetweenSessions = true;

        [Header("Overlay")]
        public TransmissionModeOverlay overlay;
        public float modeMessageDuration = 3.0f;
        public bool showInitialModeMessage = true;

        [Header("Clutch / realistic H-pattern")]
        [Range(0f, 1f)] public float clutchPressedThreshold = 0.65f;
        [Range(0f, 1f)] public float clutchReleasedThreshold = 0.20f;
        public bool enableEngineStall = false;
        public float stallSpeedKmh = 2.0f;
        public float stallRpmThreshold = 900.0f;
        public float stallThrottleThreshold = 0.15f;

        public TransmissionMode CurrentMode { get; private set; }
        public event Action<TransmissionMode> ModeChanged;

        private void Awake()
        {
            overlay = overlay != null ? overlay : GetComponent<TransmissionModeOverlay>();
            CurrentMode = LoadInitialMode();
        }

        private void Start()
        {
            if (showInitialModeMessage && overlay != null)
                overlay.Show(CurrentMode, modeMessageDuration);
        }

        private void Update()
        {
            if (!allowParticipantModeChange || IsTextEntryActive())
                return;

            if (WasModeKeyPressed(TransmissionMode.Automatic))
                SetMode(TransmissionMode.Automatic, true);
            else if (WasModeKeyPressed(TransmissionMode.ManualHPatternEasy))
                SetMode(TransmissionMode.ManualHPatternEasy, true);
            else if (WasModeKeyPressed(TransmissionMode.ManualHPatternRealistic))
                SetMode(TransmissionMode.ManualHPatternRealistic, true);
        }

        public void SetMode(TransmissionMode mode, bool showMessage)
        {
            if (CurrentMode == mode)
                return;

            CurrentMode = mode;
            SaveMode(mode);

            if (showMessage && overlay != null)
                overlay.Show(mode, modeMessageDuration);

            Debug.Log("[TransmissionModeManager] Transmission mode: " + mode);
            ModeChanged?.Invoke(mode);
        }

        public void ForceAutomaticForSessionStart()
        {
            bool changed = CurrentMode != TransmissionMode.Automatic;
            CurrentMode = TransmissionMode.Automatic;

            if (changed)
                ModeChanged?.Invoke(CurrentMode);

            Debug.Log("[ResearchSim] Transmission forced to Automatic for session start.");
        }

        private TransmissionMode LoadInitialMode()
        {
            if (saveModeBetweenSessions && PlayerPrefs.HasKey(PlayerPrefsKey))
            {
                int saved = PlayerPrefs.GetInt(PlayerPrefsKey, (int)defaultMode);
                int version = PlayerPrefs.GetInt(PlayerPrefsVersionKey, 1);
                if (version < CurrentPlayerPrefsVersion)
                {
                    if (saved == 2)
                        return TransmissionMode.ManualHPatternEasy;
                    if (saved == 3)
                        return TransmissionMode.ManualHPatternRealistic;
                }

                if (Enum.IsDefined(typeof(TransmissionMode), saved))
                    return (TransmissionMode)saved;
            }

            return defaultMode;
        }

        private void SaveMode(TransmissionMode mode)
        {
            if (!saveModeBetweenSessions)
                return;

            PlayerPrefs.SetInt(PlayerPrefsKey, (int)mode);
            PlayerPrefs.SetInt(PlayerPrefsVersionKey, CurrentPlayerPrefsVersion);
            PlayerPrefs.Save();
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

        private static bool WasModeKeyPressed(TransmissionMode mode)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return false;

            switch (mode)
            {
                case TransmissionMode.Automatic:
                    return keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame;
                case TransmissionMode.ManualHPatternEasy:
                    return keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame;
                case TransmissionMode.ManualHPatternRealistic:
                    return keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame;
                default:
                    return false;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            switch (mode)
            {
                case TransmissionMode.Automatic: return Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1);
                case TransmissionMode.ManualHPatternEasy: return Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2);
                case TransmissionMode.ManualHPatternRealistic: return Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3);
                default: return false;
            }
#else
            return false;
#endif
        }
    }
}
