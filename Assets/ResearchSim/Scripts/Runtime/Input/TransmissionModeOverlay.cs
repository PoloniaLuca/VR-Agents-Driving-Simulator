using UnityEngine;

namespace ResearchSim
{
    /// <summary>
    /// Lightweight runtime overlay for transmission mode changes. It uses
    /// OnGUI to avoid adding dependencies to the experiment UI hierarchy.
    /// </summary>
    public sealed class TransmissionModeOverlay : MonoBehaviour
    {
        public Vector2 size = new Vector2(620f, 118f);
        public float topOffset = 64f;
        public int titleFontSize = 22;
        public int bodyFontSize = 16;

        private string message;
        private float hideAtTime;

        public void Show(TransmissionMode mode, float duration)
        {
            message = BuildMessage(mode);
            hideAtTime = Time.unscaledTime + Mathf.Max(0.1f, duration);
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(message) || Time.unscaledTime >= hideAtTime)
                return;

            Rect rect = new Rect((Screen.width - size.x) * 0.5f, topOffset, size.x, size.y);
            GUIStyle box = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = bodyFontSize,
                wordWrap = true
            };
            box.normal.textColor = Color.white;

            GUI.Box(rect, message, box);
        }

        private static string BuildMessage(TransmissionMode mode)
        {
            switch (mode)
            {
                case TransmissionMode.Automatic:
                    return "Transmission Mode: Automatic\nCambio automatico. Frizione non richiesta.";
                case TransmissionMode.ManualHPatternEasy:
                    return "Transmission Mode: Manual H-Pattern Easy\nUsa il cambio ad H. Frizione consigliata per partire, non obbligatoria per cambiare.";
                case TransmissionMode.ManualHPatternRealistic:
                    return "Transmission Mode: Manual H-Pattern Realistic\nUsa cambio ad H e frizione. Cambiata senza frizione: folle.";
                default:
                    return "Transmission Mode: " + mode;
            }
        }
    }
}
