using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ResearchSim
{
    /// <summary>
    /// Provides a minimal quit shortcut for standalone builds. This is kept
    /// separate from driving input so Esc does not affect VPP/Fanatec mappings.
    /// </summary>
    public sealed class StandaloneQuitShortcut : MonoBehaviour
    {
        public KeyCode quitKey = KeyCode.Escape;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (!Input.GetKeyDown(quitKey))
                return;

#if UNITY_EDITOR
            Debug.Log("[StandaloneQuitShortcut] Escape pressed. Stopping Play Mode in the Unity Editor.");
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
