using UnityEngine;

namespace ResearchSim
{
    /// <summary>
    /// Creates a procedural steering wheel in front of the cockpit camera
    /// and rotates it based on the current steering input.
    /// </summary>
    public sealed class SteeringWheelVisual : MonoBehaviour
    {
        [Header("References")]
        public HybridVehicleInput inputSource;
        public Camera cockpitCamera;

        [Header("Wheel Transform")]
        public Vector3 localOffset = new Vector3(0f, -0.22f, 0.38f);
        public float columnTiltDegrees = 22f;
        public float wheelScale = 0.15f;

        [Header("Rotation")]
        public float maxRotationDegrees = 90f;
        public float rotationSmoothing = 12f;

        private Transform wheelPivot;
        private float currentAngle;

        private void Start()
        {
            if (cockpitCamera == null)
                cockpitCamera = Camera.main;
            if (cockpitCamera == null)
                return;

            BuildWheelGeometry();
        }

        private void LateUpdate()
        {
            if (wheelPivot == null || cockpitCamera == null)
                return;

            // Position: follow camera
            wheelPivot.position = cockpitCamera.transform.TransformPoint(localOffset);

            // Base orientation: camera forward + column tilt (tilted toward driver)
            // Aggiungiamo 90 gradi perché il volante è costruito piatto sul piano XZ
            Quaternion baseRot = cockpitCamera.transform.rotation * Quaternion.Euler(90f - columnTiltDegrees, 0f, 0f);

            // Steering rotation around the wheel's local forward axis (Z in Unity)
            float targetAngle = 0f;
            if (inputSource != null)
                targetAngle = -inputSource.Steering * maxRotationDegrees;

            currentAngle = Mathf.Lerp(currentAngle, targetAngle, Time.deltaTime * rotationSmoothing);

            // Apply: base orientation first, then spin around the Y axis (the wheel's local up/normal vector)
            wheelPivot.rotation = baseRot * Quaternion.Euler(0f, currentAngle, 0f);
        }

        private void BuildWheelGeometry()
        {
            wheelPivot = new GameObject("Steering Wheel Pivot").transform;
            wheelPivot.SetParent(transform, false);

            Material wheelMat = CreateWheelMaterial();

            // Outer ring
            CreateRing(wheelPivot, 1f, 0.065f, 28, wheelMat);
            // Three spokes at 0°, 120°, 240°
            for (int i = 0; i < 3; i++)
                CreateSpoke(wheelPivot, i * 120f, wheelMat);
            // Center hub
            CreateHub(wheelPivot, wheelMat);

            wheelPivot.localScale = Vector3.one * wheelScale;
        }

        private void CreateRing(Transform parent, float radius, float tubeRadius, int segments, Material mat)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = (float)i / segments * Mathf.PI * 2f;
                float a1 = (float)(i + 1) / segments * Mathf.PI * 2f;
                float aMid = (a0 + a1) * 0.5f;

                float x = Mathf.Cos(aMid) * radius;
                float z = Mathf.Sin(aMid) * radius;

                float segLen = Mathf.Sqrt(
                    Mathf.Pow(Mathf.Cos(a1) - Mathf.Cos(a0), 2) +
                    Mathf.Pow(Mathf.Sin(a1) - Mathf.Sin(a0), 2)) * radius;

                GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                seg.name = "Ring_" + i;
                seg.transform.SetParent(parent, false);
                seg.transform.localPosition = new Vector3(x, 0f, z);

                float tangent = Mathf.Atan2(Mathf.Sin(a1) - Mathf.Sin(a0),
                                             Mathf.Cos(a1) - Mathf.Cos(a0)) * Mathf.Rad2Deg;
                seg.transform.localRotation = Quaternion.Euler(0f, -tangent, 90f);
                seg.transform.localScale = new Vector3(tubeRadius * 2f, segLen * 0.52f, tubeRadius * 2f);
                seg.GetComponent<Renderer>().sharedMaterial = mat;
                DestroyCollider(seg);
            }
        }

        private void CreateSpoke(Transform parent, float angleDeg, Material mat)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            float halfR = 0.5f;

            GameObject spoke = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            spoke.name = "Spoke_" + angleDeg;
            spoke.transform.SetParent(parent, false);
            spoke.transform.localPosition = new Vector3(
                Mathf.Cos(rad) * halfR, 0f, Mathf.Sin(rad) * halfR);
            spoke.transform.localRotation = Quaternion.Euler(0f, -angleDeg, 90f);
            spoke.transform.localScale = new Vector3(0.045f, halfR, 0.045f);
            spoke.GetComponent<Renderer>().sharedMaterial = mat;
            DestroyCollider(spoke);
        }

        private void CreateHub(Transform parent, Material mat)
        {
            GameObject hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hub.name = "Hub";
            hub.transform.SetParent(parent, false);
            hub.transform.localPosition = Vector3.zero;
            hub.transform.localRotation = Quaternion.identity;
            hub.transform.localScale = new Vector3(0.26f, 0.02f, 0.26f);
            hub.GetComponent<Renderer>().sharedMaterial = mat;
            DestroyCollider(hub);
        }

        private static Material CreateWheelMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material mat = new Material(shader);
            mat.name = "Steering Wheel Mat";
            Color dark = new Color(0.06f, 0.06f, 0.06f);
            mat.color = dark;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", dark);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.4f);
            return mat;
        }

        private static void DestroyCollider(GameObject go)
        {
            Collider c = go.GetComponent<Collider>();
            if (c != null) Object.Destroy(c);
        }
    }
}
