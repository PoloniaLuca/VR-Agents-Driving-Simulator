using UnityEngine;

namespace ResearchSim
{
    public sealed class FirstPersonCameraBinder : MonoBehaviour
    {
        public Camera targetCamera;
        public Transform cameraMount;
        public Vector3 localEyePosition = new Vector3(-0.25f, 1.10f, 0.15f);
        public Vector3 localEyeEulerAngles = new Vector3(5f, 0f, 0f);
        public bool disableOtherCameras = true;

        private void Start()
        {
            Bind();
        }

        [ContextMenu("Bind First Person Camera")]
        public void Bind()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            if (targetCamera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                targetCamera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            Transform parent = cameraMount != null ? cameraMount : transform;
            targetCamera.transform.SetParent(parent, false);

            targetCamera.transform.localPosition = localEyePosition;
            targetCamera.transform.localRotation = Quaternion.Euler(localEyeEulerAngles);
            targetCamera.tag = "MainCamera";

            if (!disableOtherCameras)
                return;

            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include);
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera != null && camera != targetCamera)
                    camera.enabled = false;
            }
        }
    }
}
