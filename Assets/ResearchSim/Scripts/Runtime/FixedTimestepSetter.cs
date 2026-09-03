using UnityEngine;

namespace ResearchSim
{
    public sealed class FixedTimestepSetter : MonoBehaviour
    {
        [Tooltip("0.02 seconds equals 50 Hz.")]
        public float fixedDeltaTime = 0.02f;

        public float maximumDeltaTime = 0.1f;

        private void Awake()
        {
            Time.fixedDeltaTime = fixedDeltaTime;
            Time.maximumDeltaTime = maximumDeltaTime;
        }
    }
}
