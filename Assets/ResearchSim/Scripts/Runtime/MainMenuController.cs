using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ResearchSim
{
    public sealed class MainMenuController : MonoBehaviour
    {
        [Header("UI")]
        public InputField participantInput;
        public Button startButton;
        public Text statusLabel;

        [Header("Scene")]
        public string drivingSceneName = "HighwayStraight";

        private void Awake()
        {
            if (participantInput != null)
                participantInput.text = ExperimentSession.GetParticipantID();

            if (startButton != null)
                startButton.onClick.AddListener(StartExperiment);
        }

        private void OnDestroy()
        {
            if (startButton != null)
                startButton.onClick.RemoveListener(StartExperiment);
        }

        public void StartExperiment()
        {
            string participantID = participantInput != null ? participantInput.text : string.Empty;
            ExperimentSession.SetParticipantID(participantID);

            if (statusLabel != null)
                statusLabel.text = "Loading highway...";

            SceneManager.LoadScene(drivingSceneName);
        }
    }
}
