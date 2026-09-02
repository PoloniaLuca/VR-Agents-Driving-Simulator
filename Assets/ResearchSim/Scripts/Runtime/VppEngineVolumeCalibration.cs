using System.Globalization;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VehiclePhysics;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace ResearchSim
{
    /// <summary>
    /// Researcher-only runtime calibration for VPP engine volume.
    /// It does not alter the experimental music or its AudioSources.
    /// </summary>
    public sealed class VppEngineVolumeCalibration : MonoBehaviour
    {
        public const string VolumeAtRestPlayerPrefsKey = "ResearchSim.VppEngineVolumeAtRest";
        public const string VolumeAtFullLoadPlayerPrefsKey = "ResearchSim.VppEngineVolumeAtFullLoad";

        private const float FallbackDefaultVolumeAtRest = 0.40f;
        private const float FallbackDefaultVolumeAtFullLoad = 0.80f;
        private const float MinimumVolumeAtRest = 0.00f;
        private const float MaximumVolumeAtRest = 0.80f;
        private const float MinimumVolumeAtFullLoad = 0.00f;
        private const float MaximumVolumeAtFullLoad = 1.00f;

        public KeyCode toggleKey = KeyCode.F6;

        private GameObject participantVehicle;
        private ExperimentSessionController sessionController;
        private VPAudio vppAudio;
        private float defaultVolumeAtRest;
        private float defaultVolumeAtFullLoad;
        private float savedVolumeAtRest;
        private float savedVolumeAtFullLoad;
        private bool hasValidSavedValues;
        private bool initialized;

        private GameObject canvasObject;
        private GameObject panelObject;
        private InputField volumeAtRestInput;
        private InputField volumeAtFullLoadInput;
        private Button applyButton;
        private Button saveButton;
        private Button resetButton;
        private Text statusText;
        private Font uiFont;
        private bool lastLockedState;
        private bool hasLockedState;
        private CursorLockMode previousCursorLockMode;
        private bool previousCursorVisible;

        public void Configure(GameObject vehicle, ExperimentSessionController controller)
        {
            participantVehicle = vehicle;
            sessionController = controller;
            TryInitialize();
        }

        private void Start()
        {
            TryInitialize();
        }

        private void Update()
        {
            if (!initialized)
                TryInitialize();

            if (Input.GetKeyDown(toggleKey))
                TogglePanel();

            if (panelObject != null && panelObject.activeSelf)
                RefreshLockState();
        }

        private void OnDestroy()
        {
            if (panelObject != null && panelObject.activeSelf)
                RestoreCursorState();
        }

        private void OnGUI()
        {
            if (!initialized || vppAudio == null || vppAudio.engine == null ||
                !ResearchSimDebugInfoToggle.DebugInfoVisible)
                return;

            int previousGuiDepth = GUI.depth;
            GUI.depth = -100;
            float panelX = Mathf.Max(16f, Screen.width - 400f);
            GUILayout.BeginArea(new Rect(panelX, 304f, 384f, 104f), GUI.skin.box);
            GUILayout.Label("VPP Engine volumeAtRest = " + FormatValue(vppAudio.engine.volumeAtRest));
            GUILayout.Label("VPP Engine volumeAtFullLoad = " + FormatValue(vppAudio.engine.volumeAtFullLoad));
            GUILayout.Label("Music volume = 0.75 fixed");
            GUILayout.EndArea();
            GUI.depth = previousGuiDepth;
        }

        private void TryInitialize()
        {
            if (initialized)
                return;

            if (sessionController == null)
                sessionController = FindAnyObjectByType<ExperimentSessionController>();

            if (participantVehicle != null)
                vppAudio = participantVehicle.GetComponentInChildren<VPAudio>(true);

            if (vppAudio == null)
                vppAudio = FindAnyObjectByType<VPAudio>();

            if (vppAudio == null || vppAudio.engine == null)
                return;

            defaultVolumeAtRest = IsFinite(vppAudio.engine.volumeAtRest)
                ? vppAudio.engine.volumeAtRest
                : FallbackDefaultVolumeAtRest;
            defaultVolumeAtFullLoad = IsFinite(vppAudio.engine.volumeAtFullLoad)
                ? vppAudio.engine.volumeAtFullLoad
                : FallbackDefaultVolumeAtFullLoad;

            if (!IsValidPair(defaultVolumeAtRest, defaultVolumeAtFullLoad, out _))
            {
                defaultVolumeAtRest = FallbackDefaultVolumeAtRest;
                defaultVolumeAtFullLoad = FallbackDefaultVolumeAtFullLoad;
                ApplyValues(defaultVolumeAtRest, defaultVolumeAtFullLoad);
            }

            initialized = true;
            ReadSavedValues();
            if (hasValidSavedValues)
                StartCoroutine(ApplySavedValuesAfterVppInitialization());
        }

        private void ReadSavedValues()
        {
            if (!PlayerPrefs.HasKey(VolumeAtRestPlayerPrefsKey) ||
                !PlayerPrefs.HasKey(VolumeAtFullLoadPlayerPrefsKey))
                return;

            savedVolumeAtRest = PlayerPrefs.GetFloat(VolumeAtRestPlayerPrefsKey);
            savedVolumeAtFullLoad = PlayerPrefs.GetFloat(VolumeAtFullLoadPlayerPrefsKey);
            if (IsValidPair(savedVolumeAtRest, savedVolumeAtFullLoad, out _))
            {
                hasValidSavedValues = true;
                return;
            }

            Debug.LogWarning("[VppEngineVolumeCalibration] Ignored invalid saved engine volume values and kept scene defaults.");
        }

        private IEnumerator ApplySavedValuesAfterVppInitialization()
        {
            // Bootstrap runs before normal Start methods. Wait until VPP has
            // initialized so its startup logic cannot overwrite the saved pair.
            yield return null;
            yield return new WaitForEndOfFrame();

            if (!hasValidSavedValues || vppAudio == null || vppAudio.engine == null)
                yield break;

            ApplyValues(savedVolumeAtRest, savedVolumeAtFullLoad);
            if (panelObject != null && panelObject.activeSelf)
                RefreshFieldsFromVpp();
        }

        private void TogglePanel()
        {
            if (!initialized)
            {
                SetStatus("VPP engine audio is not available.");
                Debug.LogWarning("[VppEngineVolumeCalibration] Cannot open calibration panel because VPAudio was not found.");
                return;
            }

            if (panelObject == null)
                CreatePanel();

            bool show = !panelObject.activeSelf;
            panelObject.SetActive(show);
            if (show)
            {
                previousCursorLockMode = Cursor.lockState;
                previousCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                RefreshFieldsFromVpp();
                hasLockedState = false;
                RefreshLockState();
            }
            else
            {
                ClearSelectedInput();
                RestoreCursorState();
            }
        }

        private void ApplyEnteredValues()
        {
            if (IsEditingLocked())
            {
                SetStatus("Locked while the driving session is running.");
                return;
            }

            if (!TryReadEnteredValues(out float rest, out float fullLoad, out string error))
            {
                SetStatus(error);
                return;
            }

            ApplyValues(rest, fullLoad);
            RefreshFieldsFromVpp();
            SetStatus("Applied for this launch. Press Save to persist.");
        }

        private void SaveAppliedValues()
        {
            if (IsEditingLocked())
            {
                SetStatus("Locked while the driving session is running.");
                return;
            }

            if (vppAudio == null || vppAudio.engine == null)
            {
                SetStatus("Current VPP values cannot be saved because engine audio is unavailable.");
                return;
            }

            if (!IsValidPair(vppAudio.engine.volumeAtRest, vppAudio.engine.volumeAtFullLoad, out string error))
            {
                SetStatus("Current VPP values cannot be saved. " + error);
                return;
            }

            PlayerPrefs.SetFloat(VolumeAtRestPlayerPrefsKey, vppAudio.engine.volumeAtRest);
            PlayerPrefs.SetFloat(VolumeAtFullLoadPlayerPrefsKey, vppAudio.engine.volumeAtFullLoad);
            PlayerPrefs.Save();
            savedVolumeAtRest = vppAudio.engine.volumeAtRest;
            savedVolumeAtFullLoad = vppAudio.engine.volumeAtFullLoad;
            hasValidSavedValues = true;
            SetStatus("Saved. Current values are active now and will be restored next launch.");
        }

        private void ResetToDefaults()
        {
            if (IsEditingLocked())
            {
                SetStatus("Locked while the driving session is running.");
                return;
            }

            ApplyValues(defaultVolumeAtRest, defaultVolumeAtFullLoad);
            RefreshFieldsFromVpp();
            SetStatus("Scene defaults restored. Press Save to persist them.");
        }

        private void ClosePanel()
        {
            if (panelObject == null)
                return;

            panelObject.SetActive(false);
            ClearSelectedInput();
            RestoreCursorState();
        }

        private bool TryReadEnteredValues(out float rest, out float fullLoad, out string error)
        {
            rest = 0f;
            fullLoad = 0f;
            if (!TryParseValue(volumeAtRestInput != null ? volumeAtRestInput.text : "", out rest) ||
                !TryParseValue(volumeAtFullLoadInput != null ? volumeAtFullLoadInput.text : "", out fullLoad))
            {
                error = "Enter valid numbers using a decimal dot or comma.";
                return false;
            }

            return IsValidPair(rest, fullLoad, out error);
        }

        private static bool TryParseValue(string text, out float value)
        {
            string normalized = string.IsNullOrWhiteSpace(text)
                ? ""
                : text.Trim().Replace(',', '.');
            return float.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static bool IsValidPair(float rest, float fullLoad, out string error)
        {
            if (!IsFinite(rest) || !IsFinite(fullLoad))
            {
                error = "Values must be finite numbers.";
                return false;
            }

            if (rest < MinimumVolumeAtRest || rest > MaximumVolumeAtRest)
            {
                error = "volumeAtRest must be between 0.00 and 0.80.";
                return false;
            }

            if (fullLoad < MinimumVolumeAtFullLoad || fullLoad > MaximumVolumeAtFullLoad)
            {
                error = "volumeAtFullLoad must be between 0.00 and 1.00.";
                return false;
            }

            if (fullLoad < rest)
            {
                error = "volumeAtFullLoad must be greater than or equal to volumeAtRest.";
                return false;
            }

            error = "";
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void ApplyValues(float rest, float fullLoad)
        {
            if (vppAudio == null || vppAudio.engine == null)
                return;

            vppAudio.engine.volumeAtRest = rest;
            vppAudio.engine.volumeAtFullLoad = fullLoad;
        }

        private bool IsEditingLocked()
        {
            return sessionController != null && sessionController.SessionRunning;
        }

        private void RefreshLockState()
        {
            bool locked = IsEditingLocked();
            if (hasLockedState && locked == lastLockedState)
                return;

            hasLockedState = true;
            lastLockedState = locked;
            if (volumeAtRestInput != null)
                volumeAtRestInput.interactable = !locked;
            if (volumeAtFullLoadInput != null)
                volumeAtFullLoadInput.interactable = !locked;
            if (applyButton != null)
                applyButton.interactable = !locked;
            if (saveButton != null)
                saveButton.interactable = !locked;
            if (resetButton != null)
                resetButton.interactable = !locked;

            if (locked)
            {
                ClearSelectedInput();
                RefreshFieldsFromVpp();
                SetStatus("Locked while the driving session is running.");
            }
            else
            {
                SetStatus("Researcher calibration. Apply is temporary; Save persists.");
            }
        }

        private void RefreshFieldsFromVpp()
        {
            if (vppAudio == null || vppAudio.engine == null)
                return;

            if (volumeAtRestInput != null)
                volumeAtRestInput.text = FormatValue(vppAudio.engine.volumeAtRest);
            if (volumeAtFullLoadInput != null)
                volumeAtFullLoadInput.text = FormatValue(vppAudio.engine.volumeAtFullLoad);
        }

        private static string FormatValue(float value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        private void CreatePanel()
        {
            EnsureEventSystem();
            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            canvasObject = new GameObject(
                "VPP Engine Volume Calibration Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            panelObject = new GameObject("VPP Engine Volume Calibration Panel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(canvasObject.transform, false);
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(620f, 360f);
            panelRect.anchoredPosition = Vector2.zero;
            panelObject.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.11f, 0.96f);

            CreateText(panelObject.transform, "Title", "VPP Engine Volume Calibration", 24, TextAnchor.MiddleCenter,
                new Vector2(20f, -18f), new Vector2(580f, 42f));
            CreateText(panelObject.transform, "Rest Label", "Engine volumeAtRest", 18, TextAnchor.MiddleLeft,
                new Vector2(42f, -82f), new Vector2(300f, 38f));
            volumeAtRestInput = CreateInputField(panelObject.transform, "volumeAtRest Input",
                new Vector2(370f, -82f), new Vector2(200f, 38f));

            CreateText(panelObject.transform, "Full Load Label", "Engine volumeAtFullLoad", 18, TextAnchor.MiddleLeft,
                new Vector2(42f, -132f), new Vector2(300f, 38f));
            volumeAtFullLoadInput = CreateInputField(panelObject.transform, "volumeAtFullLoad Input",
                new Vector2(370f, -132f), new Vector2(200f, 38f));

            statusText = CreateText(panelObject.transform, "Status", "", 16, TextAnchor.MiddleLeft,
                new Vector2(42f, -185f), new Vector2(528f, 55f));
            statusText.color = new Color(0.92f, 0.92f, 0.72f, 1f);

            applyButton = CreateButton(panelObject.transform, "Apply Button", "Apply",
                new Vector2(42f, -258f), new Vector2(120f, 42f), ApplyEnteredValues);
            saveButton = CreateButton(panelObject.transform, "Save Button", "Save",
                new Vector2(174f, -258f), new Vector2(120f, 42f), SaveAppliedValues);
            resetButton = CreateButton(panelObject.transform, "Reset Defaults Button", "Reset Defaults",
                new Vector2(306f, -258f), new Vector2(150f, 42f), ResetToDefaults);
            CreateButton(panelObject.transform, "Close Button", "Close",
                new Vector2(468f, -258f), new Vector2(110f, 42f), ClosePanel);

            CreateText(panelObject.transform, "Hint", "F6 toggles this researcher-only panel.", 14, TextAnchor.MiddleCenter,
                new Vector2(42f, -315f), new Vector2(536f, 28f));

            panelObject.SetActive(false);
        }

        private InputField CreateInputField(Transform parent, string objectName, Vector2 position, Vector2 size)
        {
            GameObject inputObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(InputField));
            inputObject.transform.SetParent(parent, false);
            SetTopLeftRect(inputObject.GetComponent<RectTransform>(), position, size);
            inputObject.GetComponent<Image>().color = new Color(0.94f, 0.94f, 0.94f, 1f);

            Text text = CreateText(inputObject.transform, "Text", "", 18, TextAnchor.MiddleLeft,
                new Vector2(10f, 0f), new Vector2(size.x - 20f, size.y));
            text.color = Color.black;

            Text placeholder = CreateText(inputObject.transform, "Placeholder", "0.00", 18, TextAnchor.MiddleLeft,
                new Vector2(10f, 0f), new Vector2(size.x - 20f, size.y));
            placeholder.color = new Color(0.35f, 0.35f, 0.35f, 0.65f);

            InputField input = inputObject.GetComponent<InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.contentType = InputField.ContentType.Standard;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 12;
            return input;
        }

        private Button CreateButton(
            Transform parent,
            string objectName,
            string label,
            Vector2 position,
            Vector2 size,
            UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            SetTopLeftRect(buttonObject.GetComponent<RectTransform>(), position, size);
            buttonObject.GetComponent<Image>().color = new Color(0.22f, 0.35f, 0.48f, 1f);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.AddListener(action);

            CreateText(buttonObject.transform, "Text", label, 16, TextAnchor.MiddleCenter, Vector2.zero, size);
            return button;
        }

        private Text CreateText(
            Transform parent,
            string objectName,
            string content,
            int fontSize,
            TextAnchor alignment,
            Vector2 position,
            Vector2 size)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            SetTopLeftRect(textObject.GetComponent<RectTransform>(), position, size);

            Text text = textObject.GetComponent<Text>();
            text.font = uiFont;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.text = content;
            text.raycastTarget = false;
            return text;
        }

        private static void SetTopLeftRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

            GameObject eventSystemObject = new GameObject("ResearchSim Runtime EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
#else
            eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
        }

        private static void ClearSelectedInput()
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }

        private void RestoreCursorState()
        {
            Cursor.lockState = previousCursorLockMode;
            Cursor.visible = previousCursorVisible;
        }
    }
}
