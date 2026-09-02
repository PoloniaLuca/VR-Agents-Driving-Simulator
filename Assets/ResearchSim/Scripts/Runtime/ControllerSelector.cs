using UnityEngine;

namespace ResearchSim
{
    /// <summary>
    /// Optional keyboard selector for testing. Attach to the same GameObject
    /// as VppExternalInputBridge, or assign bridge manually.
    /// F1 = Auto, F2 = Fanatec, F3 = G29.
    /// </summary>
    public sealed class ControllerSelector : MonoBehaviour
    {
        public VppExternalInputBridge bridge;
        public bool showHud = true;

        private void Awake()
        {
            if (bridge == null)
                bridge = GetComponent<VppExternalInputBridge>();

            if (bridge == null)
                bridge = GetComponentInChildren<VppExternalInputBridge>(true);
        }

        private void Update()
        {
            if (bridge == null)
                return;

            if (Input.GetKeyDown(KeyCode.F1))
                bridge.SetExternalController(VppExternalInputBridge.ExternalController.Auto);
            else if (Input.GetKeyDown(KeyCode.F2))
                bridge.SetExternalController(VppExternalInputBridge.ExternalController.Fanatec);
            else if (Input.GetKeyDown(KeyCode.F3))
                bridge.SetExternalController(VppExternalInputBridge.ExternalController.G29);
        }

        private void OnGUI()
        {
            if (!showHud || bridge == null)
                return;

            GUI.Label(
                new Rect(20f, 20f, 500f, 25f),
                "Controller: " + bridge.externalController +
                "   F1 Auto | F2 Fanatec | F3 G29");
        }
    }
}
