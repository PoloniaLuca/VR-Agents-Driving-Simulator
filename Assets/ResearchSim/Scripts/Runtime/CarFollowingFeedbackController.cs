using UnityEngine;
using Unity.Profiling;

namespace ResearchSim
{
    public sealed class CarFollowingFeedbackController : MonoBehaviour
    {
        public enum FeedbackState
        {
            Off,
            Green,
            YellowTooFar,
            RedTooClose,
            RedClosingTooFast
        }

        [Header("References")]
        public Transform participantVehicle;
        public Rigidbody participantRigidbody;
        public LeadVehicleController leadVehicle;

        public bool IsFeedbackEnabled { get; private set; }
        public FeedbackState CurrentState { get; private set; } = FeedbackState.Off;
        public float TargetDistanceMeters { get; private set; } = float.NaN;
        public float DistanceErrorMeters { get; private set; } = float.NaN;
        public float ClosingSpeedMps { get; private set; } = float.NaN;
        public bool TooClose { get; private set; }
        public bool TooFar { get; private set; }
        public bool ClosingTooFast { get; private set; }

        private bool protocolFeedbackEnabled;
        private bool phaseAllowsFeedback;
        private CarFollowingFeedbackSettings settings;
        private bool warnedMissingSettings;
        private bool warnedMissingReferences;
        private bool warnedInvalidDrawRect;
        private GUIStyle labelStyle;
        private bool firstFeedbackDrawPending = true;
        private static readonly ProfilerMarker FirstFeedbackDrawMarker = new ProfilerMarker("ResearchSim.Startup.FirstFeedbackDraw");

        private const float DefaultTargetFollowingDistanceMeters = 35f;
        private const float DefaultAcceptableMinDistanceMeters = 25f;
        private const float DefaultAcceptableMaxDistanceMeters = 45f;
        private const float DefaultDangerDistanceMeters = 18f;
        private const float DefaultDangerClosingSpeedMps = 3f;
        private static readonly Vector2 DefaultNormalizedScreenPosition = new Vector2(0.5f, 0.14f);
        private const float DefaultIndicatorSize = 96f;
        private const float MinimumIndicatorWidth = 64f;
        private const float MinimumIndicatorHeight = 40f;
        private const string DefaultGreenLabel = "OK";
        private const string DefaultYellowTooFarLabel = "FAR";
        private const string DefaultRedTooCloseLabel = "CLOSE";
        private const string DefaultRedClosingTooFastLabel = "SLOW";

        public void SetProtocolState(
            bool feedbackEnabled,
            CarFollowingFeedbackSettings feedbackSettings,
            ExperimentSessionController.SessionPhase phase,
            bool suppressForProtocolState,
            bool profileAllowsPhase)
        {
            protocolFeedbackEnabled = feedbackEnabled;
            settings = feedbackSettings;
            phaseAllowsFeedback = feedbackEnabled && !suppressForProtocolState && profileAllowsPhase && IsPhaseAllowed(phase);

            if (protocolFeedbackEnabled && settings == null && !warnedMissingSettings)
            {
                warnedMissingSettings = true;
                Debug.LogWarning("[CarFollowingFeedback] Feedback enabled without settings asset; using built-in pilot defaults.");
            }
        }

        private void Update()
        {
            UpdateFeedbackState();
        }

        private void OnGUI()
        {
            // GUI.skin is only valid during OnGUI. Build the style while feedback
            // is still hidden so the first visible frame does not allocate it.
            EnsureGuiStyle();

            if (!IsFeedbackEnabled || CurrentState == FeedbackState.Off)
                return;

            if (firstFeedbackDrawPending)
            {
                using (FirstFeedbackDrawMarker.Auto())
                    DrawFeedbackIndicator();
                firstFeedbackDrawPending = false;
                return;
            }

            DrawFeedbackIndicator();
        }

        private void DrawFeedbackIndicator()
        {
            Color oldColor = GUI.color;

            Rect rect = GetDrawRect();
            float borderPixels = GetBorderThicknessPixels();
            Rect innerRect = new Rect(
                rect.x + borderPixels,
                rect.y + borderPixels,
                Mathf.Max(0f, rect.width - borderPixels * 2f),
                Mathf.Max(0f, rect.height - borderPixels * 2f));

            if (ShouldDrawBorder(borderPixels))
            {
                GUI.color = GetBorderColor();
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill);
            }

            GUI.color = GetStateColor(CurrentState);
            GUI.DrawTexture(innerRect, Texture2D.whiteTexture, ScaleMode.StretchToFill);

            GUI.color = Color.white;
            labelStyle.normal.textColor = GetStateTextColor(CurrentState);
            GUI.Label(rect, GetStateLabelForDisplay(CurrentState), labelStyle);
            GUI.color = oldColor;
        }

        private void UpdateFeedbackState()
        {
            if (!protocolFeedbackEnabled || !phaseAllowsFeedback)
            {
                SetOff();
                return;
            }

            if (participantVehicle == null || participantRigidbody == null || leadVehicle == null)
            {
                if (!warnedMissingReferences)
                {
                    warnedMissingReferences = true;
                    Debug.LogWarning("[CarFollowingFeedback] Missing participant or leader reference; feedback remains off.");
                }

                SetOff();
                return;
            }

            Vector3 participantPosition = participantVehicle.position;
            Vector3 leaderPosition = leadVehicle.transform.position;
            float distance = Vector3.Distance(participantPosition, leaderPosition);
            float participantSpeed = GetParticipantVelocity().magnitude;
            float closingSpeed = participantSpeed - leadVehicle.CurrentSpeedMps;

            float targetDistance = GetTargetFollowingDistanceMeters();
            float acceptableMin = GetAcceptableMinDistanceMeters();
            float acceptableMax = Mathf.Max(acceptableMin, GetAcceptableMaxDistanceMeters());
            float dangerDistance = GetDangerDistanceMeters();
            float dangerClosingSpeed = GetDangerClosingSpeedMps();

            TargetDistanceMeters = targetDistance;
            DistanceErrorMeters = distance - targetDistance;
            ClosingSpeedMps = closingSpeed;
            TooClose = distance < acceptableMin;
            TooFar = distance > acceptableMax;
            ClosingTooFast = GetUseClosingSpeedWarning() && !TooFar && closingSpeed > dangerClosingSpeed;

            IsFeedbackEnabled = true;
            if (TooFar)
                CurrentState = FeedbackState.YellowTooFar;
            else if (distance < dangerDistance)
                CurrentState = FeedbackState.RedTooClose;
            else if (ClosingTooFast)
                CurrentState = FeedbackState.RedClosingTooFast;
            else if (TooClose)
                CurrentState = FeedbackState.RedTooClose;
            else
                CurrentState = FeedbackState.Green;
        }

        private void SetOff()
        {
            IsFeedbackEnabled = false;
            CurrentState = FeedbackState.Off;
            TargetDistanceMeters = protocolFeedbackEnabled ? GetTargetFollowingDistanceMeters() : float.NaN;
            DistanceErrorMeters = float.NaN;
            ClosingSpeedMps = float.NaN;
            TooClose = false;
            TooFar = false;
            ClosingTooFast = false;
        }

        private bool IsPhaseAllowed(ExperimentSessionController.SessionPhase phase)
        {
            switch (phase)
            {
                case ExperimentSessionController.SessionPhase.Familiarization:
                    return GetShowDuringFamiliarization();
                case ExperimentSessionController.SessionPhase.Baseline:
                    return GetShowDuringBaseline();
                case ExperimentSessionController.SessionPhase.ExperimentalBlock:
                    return GetShowDuringExperimentalBlocks();
                default:
                    return false;
            }
        }

        private Vector3 GetParticipantVelocity()
        {
#if UNITY_6000_0_OR_NEWER
            return participantRigidbody != null ? participantRigidbody.linearVelocity : Vector3.zero;
#else
            return participantRigidbody != null ? participantRigidbody.velocity : Vector3.zero;
#endif
        }

        private float GetTargetFollowingDistanceMeters()
        {
            return settings != null ? Mathf.Max(0f, settings.targetFollowingDistanceMeters) : DefaultTargetFollowingDistanceMeters;
        }

        private float GetAcceptableMinDistanceMeters()
        {
            return settings != null ? Mathf.Max(0f, settings.acceptableMinDistanceMeters) : DefaultAcceptableMinDistanceMeters;
        }

        private float GetAcceptableMaxDistanceMeters()
        {
            return settings != null ? Mathf.Max(0f, settings.acceptableMaxDistanceMeters) : DefaultAcceptableMaxDistanceMeters;
        }

        private float GetDangerDistanceMeters()
        {
            return settings != null ? Mathf.Max(0f, settings.dangerDistanceMeters) : DefaultDangerDistanceMeters;
        }

        private float GetDangerClosingSpeedMps()
        {
            return settings != null ? Mathf.Max(0f, settings.dangerClosingSpeedMps) : DefaultDangerClosingSpeedMps;
        }

        private bool GetUseClosingSpeedWarning()
        {
            return settings != null && settings.useClosingSpeedWarning;
        }

        private bool GetShowDuringFamiliarization()
        {
            return settings == null || settings.showDuringFamiliarization;
        }

        private bool GetShowDuringBaseline()
        {
            return settings == null || settings.showDuringBaseline;
        }

        private bool GetShowDuringExperimentalBlocks()
        {
            return settings == null || settings.showDuringExperimentalBlocks;
        }

        private Vector2 GetNormalizedScreenPosition()
        {
            return settings != null ? settings.normalizedScreenPosition : DefaultNormalizedScreenPosition;
        }

        private float GetIndicatorSize()
        {
            return settings != null ? Mathf.Max(24f, settings.indicatorSize) : DefaultIndicatorSize;
        }

        private bool ShouldDrawBorder(float borderThicknessPixels)
        {
            return settings != null && settings.showBorder && borderThicknessPixels > 0f;
        }

        private float GetBorderThicknessPixels()
        {
            if (settings == null || !settings.showBorder)
                return 0f;

            return Mathf.Max(0f, settings.borderThicknessPixels);
        }

        private Color GetBorderColor()
        {
            return settings != null ? settings.borderColor : new Color(0.02f, 0.02f, 0.02f, 0.95f);
        }

        private Rect GetDrawRect()
        {
            float size = GetIndicatorSize();
            Vector2 position = GetNormalizedScreenPosition();

            bool invalidRect = float.IsNaN(size) ||
                float.IsInfinity(size) ||
                size <= 0f ||
                float.IsNaN(position.x) ||
                float.IsNaN(position.y) ||
                float.IsInfinity(position.x) ||
                float.IsInfinity(position.y);

            if (invalidRect)
            {
                if (!warnedInvalidDrawRect)
                {
                    warnedInvalidDrawRect = true;
                    Debug.LogWarning("[CarFollowingFeedback] Invalid indicator position or size; using visible fallback rect.");
                }

                return new Rect(
                    Screen.width * 0.5f - DefaultIndicatorSize * 0.5f,
                    24f,
                    DefaultIndicatorSize,
                    DefaultIndicatorSize);
            }

            float width = Mathf.Max(MinimumIndicatorWidth, size);
            float height = Mathf.Max(MinimumIndicatorHeight, size);
            float x = Mathf.Clamp01(position.x) * Screen.width - width * 0.5f;
            float y = Mathf.Clamp01(position.y) * Screen.height - height * 0.5f;

            if (Screen.width > 0)
                x = Mathf.Clamp(x, 0f, Mathf.Max(0f, Screen.width - width));
            if (Screen.height > 0)
                y = Mathf.Clamp(y, 0f, Mathf.Max(0f, Screen.height - height));

            return new Rect(x, y, width, height);
        }

        private static Color GetStateTextColor(FeedbackState state)
        {
            return state == FeedbackState.YellowTooFar ? Color.black : Color.white;
        }

        private static Color GetStateColor(FeedbackState state)
        {
            switch (state)
            {
                case FeedbackState.Green:
                    return new Color(0.05f, 0.7f, 0.18f, 1f);
                case FeedbackState.YellowTooFar:
                    return new Color(1f, 0.86f, 0.05f, 1f);
                case FeedbackState.RedTooClose:
                    return new Color(0.9f, 0.04f, 0.03f, 1f);
                case FeedbackState.RedClosingTooFast:
                    return new Color(1f, 0.22f, 0.02f, 1f);
                default:
                    return new Color(0f, 0f, 0f, 0f);
            }
        }

        private string GetStateLabelForDisplay(FeedbackState state)
        {
            switch (state)
            {
                case FeedbackState.Green:
                    return GetConfiguredLabel(settings != null ? settings.greenLabel : null, DefaultGreenLabel);
                case FeedbackState.YellowTooFar:
                    return GetConfiguredLabel(settings != null ? settings.yellowTooFarLabel : null, DefaultYellowTooFarLabel);
                case FeedbackState.RedTooClose:
                    return GetConfiguredLabel(settings != null ? settings.redTooCloseLabel : null, DefaultRedTooCloseLabel);
                case FeedbackState.RedClosingTooFast:
                    return GetConfiguredLabel(settings != null ? settings.redClosingTooFastLabel : null, DefaultRedClosingTooFastLabel);
                default:
                    return "";
            }
        }

        private static string GetConfiguredLabel(string configuredLabel, string fallbackLabel)
        {
            return string.IsNullOrWhiteSpace(configuredLabel) ? fallbackLabel : configuredLabel.Trim();
        }

        private void EnsureGuiStyle()
        {
            if (labelStyle != null)
                return;

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.fontSize = 18;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.wordWrap = true;
            labelStyle.normal.textColor = Color.white;
        }
    }
}
