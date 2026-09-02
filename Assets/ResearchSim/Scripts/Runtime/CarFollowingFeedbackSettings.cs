using UnityEngine;

namespace ResearchSim
{
    [CreateAssetMenu(fileName = "CarFollowingFeedbackSettings", menuName = "Research Sim/Car Following Feedback Settings")]
    public sealed class CarFollowingFeedbackSettings : ScriptableObject
    {
        [Header("Activation")]
        public bool feedbackEnabledDefault = false;

        [Header("Distance Thresholds")]
        [Min(0f)] public float targetFollowingDistanceMeters = 35f;
        [Min(0f)] public float acceptableMinDistanceMeters = 25f;
        [Min(0f)] public float acceptableMaxDistanceMeters = 45f;
        [Min(0f)] public float dangerDistanceMeters = 18f;
        [InspectorName("Use Closing Speed Warning")]
        [Tooltip("If enabled, feedback can show the RedClosingTooFast state when the participant approaches the leader too quickly. If disabled, feedback is based only on distance.")]
        public bool useClosingSpeedWarning = false;
        [Min(0f)] public float dangerClosingSpeedMps = 3f;

        [Header("Phase Visibility")]
        public bool showDuringFamiliarization = true;
        public bool showDuringBaseline = true;
        public bool showDuringExperimentalBlocks = true;

        [Header("Participant UI")]
        [Tooltip("Normalized screen position. X: 0 left, 0.5 center, 1 right. Y: 0 top, 0.5 middle, 1 bottom.")]
        public Vector2 normalizedScreenPosition = new Vector2(0.5f, 0.14f);
        [Tooltip("Square indicator size in pixels.")]
        [Min(24f)] public float indicatorSize = 96f;

        [Header("Participant UI Labels")]
        public string greenLabel = "OK";
        public string yellowTooFarLabel = "FAR";
        public string redTooCloseLabel = "CLOSE";
        public string redClosingTooFastLabel = "SLOW";

        [Header("Participant UI Border")]
        public bool showBorder = false;
        [Min(0f)] public float borderThicknessPixels = 0f;
        public Color borderColor = new Color(0.02f, 0.02f, 0.02f, 0.95f);
    }
}
