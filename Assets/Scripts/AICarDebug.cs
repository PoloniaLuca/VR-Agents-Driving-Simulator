using UnityEngine;

namespace DrivingSim
{
    /// <summary>
    /// Attach this to the AI vehicle root to diagnose why it is not moving.
    /// Remove it once the issue is resolved.
    /// </summary>
    public class AICarDebug : MonoBehaviour
    {
        private AICarInput   aiInput;
        private CarController carCtrl;
        private Rigidbody    rb;

        private void Awake()
        {
            aiInput = GetComponent<AICarInput>();
            carCtrl = GetComponent<CarController>();
            rb      = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            Debug.Log(
                $"[AICarDebug] {name}\n" +
                $"  AICarInput present  : {aiInput  != null}\n" +
                $"  AICarInput enabled  : {(aiInput  != null ? aiInput.enabled.ToString()  : "N/A")}\n" +
                $"  CarController present: {carCtrl != null}\n" +
                $"  CarController enabled: {(carCtrl != null ? carCtrl.enabled.ToString() : "N/A")}\n" +
                $"  Rigidbody isKinematic: {(rb != null ? rb.isKinematic.ToString() : "N/A")}\n" +
                $"  Rigidbody mass      : {(rb != null ? rb.mass.ToString("F1") : "N/A")}\n" +
                $"  Speed (m/s)         : {(rb != null ? rb.linearVelocity.magnitude.ToString("F2") : "N/A")}\n" +
                $"  ICarInput output →  Throttle:{aiInput?.Throttle:F3}  Brake:{aiInput?.Brake:F3}  Steering:{aiInput?.Steering:F3}\n" +
                $"  AICarInput.enabled  : {aiInput?.enabled}"
            );
        }

        private void OnGUI()
        {
            if (aiInput == null || carCtrl == null) return;

            GUILayout.BeginArea(new Rect(10, 50, 340, 220));
            GUILayout.Box(
                $"=== AI CAR DEBUG ===\n" +
                $"Throttle : {aiInput.Throttle:F3}\n" +
                $"Brake    : {aiInput.Brake:F3}\n" +
                $"Steering : {aiInput.Steering:F3}\n" +
                $"Speed    : {(rb != null ? rb.linearVelocity.magnitude : 0f):F2} m/s\n" +
                $"Kinematic: {(rb != null ? rb.isKinematic.ToString() : "?")}\n" +
                $"InputSrc assigned: {carCtrl != null}"
            );
            GUILayout.EndArea();
        }
    }
}
