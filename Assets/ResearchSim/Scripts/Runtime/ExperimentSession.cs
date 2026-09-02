using System.IO;
using UnityEngine;

namespace ResearchSim
{
    public sealed class ExperimentSession : MonoBehaviour
    {
        public const string ParticipantPrefsKey = "ResearchSim.ParticipantID";
        private const string DefaultParticipantID = "Soggetto_01";

        private static ExperimentSession instance;

        [SerializeField] private string fallbackParticipantID = DefaultParticipantID;

        public static string ParticipantID { get; private set; } = DefaultParticipantID;

        public static ExperimentSession Instance
        {
            get
            {
                EnsureInstance();
                return instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureInstance();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            ParticipantID = PlayerPrefs.GetString(ParticipantPrefsKey, fallbackParticipantID);
        }

        public static void SetParticipantID(string participantID)
        {
            EnsureInstance();

            ParticipantID = string.IsNullOrWhiteSpace(participantID)
                ? DefaultParticipantID
                : participantID.Trim();

            PlayerPrefs.SetString(ParticipantPrefsKey, ParticipantID);
            PlayerPrefs.Save();
        }

        public static string GetParticipantID()
        {
            EnsureInstance();
            return string.IsNullOrWhiteSpace(ParticipantID) ? DefaultParticipantID : ParticipantID;
        }

        public static string GetFileSafeParticipantID()
        {
            string safeID = GetParticipantID();
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
                safeID = safeID.Replace(invalidChar, '_');

            return string.IsNullOrWhiteSpace(safeID) ? DefaultParticipantID : safeID;
        }

        private static void EnsureInstance()
        {
            if (instance != null)
                return;

            instance = FindAnyObjectByType<ExperimentSession>();
            if (instance != null)
                return;

            GameObject sessionObject = new GameObject("Experiment Session");
            instance = sessionObject.AddComponent<ExperimentSession>();
        }
    }
}
