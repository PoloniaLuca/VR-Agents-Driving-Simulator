using UnityEngine;
using UnityEngine.Serialization;

namespace ResearchSim
{
    public enum BlockOrderMode
    {
        Fixed,
        ShuffleByParticipantId,
        RandomEachSession,
        CounterbalancedByParticipantId,
        ManualProfileOrder,
        CounterbalancedByParticipantNumberModulo
    }

    [CreateAssetMenu(fileName = "ExperimentProtocolProfile", menuName = "Research Sim/Experiment Protocol Profile")]
    public sealed class ExperimentProtocolProfile : ScriptableObject
    {
        public string profileId = "ProtocolProfile";

        [Header("Protocol Version")]
        public string protocolVersion = "V1";
        public bool useV2Protocol;
        [Min(30f)] public float fallbackExperimentalBlockSeconds = 480f;

        [Header("Familiarization")]
        public bool includeFamiliarization = true;
        [Min(10f)] public float familiarizationSeconds = 240f;

        [Header("Baseline")]
        public bool includeBaseline = true;
        [Min(10f)] public float baselineSeconds = 240f;

        [Header("Experimental Blocks")]
        public MusicEventController.MusicBlockCondition[] experimentalBlockConditions =
        {
            MusicEventController.MusicBlockCondition.ControlStable,
            MusicEventController.MusicBlockCondition.SlowFast,
            MusicEventController.MusicBlockCondition.FastSlow
        };

        public BlockOrderMode blockOrderMode = BlockOrderMode.ShuffleByParticipantId;

        [Header("Car-Following Feedback")]
        [FormerlySerializedAs("enableCarFollowingFeedback")]
        [InspectorName("Enable")]
        [Tooltip("Enable participant-facing car-following feedback for this protocol.")]
        public bool enableFeedback = false;

        [InspectorName("Settings")]
        [Tooltip("Labels, thresholds, colors, and screen placement for the feedback indicator.")]
        public CarFollowingFeedbackSettings feedbackSettings;

        [FormerlySerializedAs("showFeedbackDuringFamiliarization")]
        [InspectorName("Familiarization")]
        [Tooltip("Show feedback during familiarization when feedback is enabled.")]
        public bool showInFamiliarization = true;

        [FormerlySerializedAs("showFeedbackDuringBaseline")]
        [InspectorName("Baseline")]
        [Tooltip("Show feedback during baseline when feedback is enabled.")]
        public bool showInBaseline = true;

        [FormerlySerializedAs("showFeedbackDuringExperimentalBlocks")]
        [InspectorName("Experimental Blocks")]
        [Tooltip("Show feedback during experimental blocks when feedback is enabled.")]
        public bool showInExperimentalBlocks = true;

        public string ProfileIdOrName
        {
            get { return string.IsNullOrWhiteSpace(profileId) ? name : profileId.Trim(); }
        }
    }
}
