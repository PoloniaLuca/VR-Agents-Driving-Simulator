using UnityEngine;

namespace ResearchSim
{
    [DisallowMultipleComponent]
    public sealed class HighwayStraightRuntimeExtensionSettings : MonoBehaviour
    {
        [Header("Experiment Protocol")]
        [SerializeField]
        [Tooltip("Optional protocol profile. If empty, the session controller uses fallback defaults.")]
        private ExperimentProtocolProfile protocolProfile;

        [Header("Runtime Highway Extension")]
        public bool runtimeHighwayExtensionEnabled = true;
        [Min(1f)] public float expectedMaxDrivingSpeedKmh = 150f;
        [Min(1f)] public float minimumExperimentalBlockSeconds = 600f;
        [Min(0f)] public float trackExtensionSafetyMarginMeters = 5000f;
        [Min(1000f)] public float minimumTotalStraightLengthMeters = 30000f;

        public ExperimentProtocolProfile ProtocolProfile
        {
            get { return protocolProfile; }
        }
    }
}
