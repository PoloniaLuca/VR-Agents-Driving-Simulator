using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DrivingSim
{
    /// <summary>
    /// Editor utility that generates a geometrically correct parallel-offset spline.
    ///
    /// WHY SAMPLING instead of knot-offset
    /// ------------------------------------
    /// Offsetting Bezier knot positions directly compresses inner curves and
    /// expands outer curves, causing the spline to touch guard rails.
    /// The correct approach is to sample the source curve at many evenly-spaced
    /// points, offset each sample perpendicularly to the local tangent, then
    /// fit a new smooth spline through those offset points.
    /// This produces the true parallel (offset) curve at any distance.
    ///
    /// Usage
    /// -----
    ///   1. Add this component to any GameObject in the scene.
    ///   2. Assign Source Spline.
    ///   3. Set Lateral Offset (+8.5 left side, -8.5 right side).
    ///   4. Right-click the component header → "Generate Offset Spline".
    ///   5. Repeat with opposite offset + Reverse Direction = true for other carriageway.
    ///   6. Remove this component when done.
    /// </summary>
    public class SplineOffsetTool : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("The centre-line SplineContainer to offset from.")]
        public SplineContainer sourceSpline;

        [Tooltip("Index of the spline inside the container (usually 0).")]
        public int splineIndex = 0;

        [Header("Offset")]
        [Tooltip("Lateral distance [m] from the source spline.\n" +
                 "Positive = left of direction of travel.\n" +
                 "Negative = right of direction of travel.\n\n" +
                 "Example (34 m road, two 17 m carriageways):\n" +
                 "  Left carriageway  →  +8.5\n" +
                 "  Right carriageway →  -8.5")]
        public float lateralOffset = 8.5f;

        [Tooltip("Number of sample points used to build the offset curve.\n" +
                 "More samples = smoother result on tight curves.\n" +
                 "50-100 is usually ideal. 200 for very tight bends.")]
        [Range(10, 300)]
        public int sampleCount = 80;

        [Tooltip("Reverse the knot order so the spline runs in the opposite direction.\n" +
                 "Enable for the counter-traffic carriageway.")]
        public bool reverseDirection = true;

        [Header("Output")]
        [Tooltip("Name of the generated GameObject.")]
        public string outputName = "Spline_Offset";

        // ─────────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
        [ContextMenu("Generate Offset Spline")]
        public void GenerateOffsetSpline()
        {
            if (sourceSpline == null)
            {
                Debug.LogError("[SplineOffsetTool] sourceSpline is not assigned.", this);
                return;
            }

            var srcSpline = sourceSpline.Splines[splineIndex];

            if (srcSpline.Count < 2)
            {
                Debug.LogError("[SplineOffsetTool] Source spline needs at least 2 knots.", this);
                return;
            }

            // ── Create output GameObject ──────────────────────────────────────
            GameObject newGO = new GameObject(outputName);
            Undo.RegisterCreatedObjectUndo(newGO, $"Generate Offset Spline '{outputName}'");

            // Match the source transform so local-space coordinates stay consistent
            newGO.transform.SetPositionAndRotation(
                sourceSpline.transform.position,
                sourceSpline.transform.rotation);
            newGO.transform.localScale = sourceSpline.transform.localScale;

            SplineContainer newContainer = newGO.AddComponent<SplineContainer>();
            var newSpline = newContainer.Spline;
            newSpline.Clear();

            // ── Sample the source spline and offset each point ────────────────
            //
            // Sampling (not knot-offsetting) gives the geometrically correct
            // parallel curve because at every sample we compute the true
            // perpendicular to the curve, not an approximation.
            //
            int    total    = Mathf.Max(sampleCount, 4);
            float3 worldUp  = new float3(0f, 1f, 0f);

            var positions = new float3[total];

            for (int i = 0; i < total; i++)
            {
                float t = (float)i / (total - 1);

                // Evaluate position and tangent in local spline space
                srcSpline.Evaluate(t,
                    out float3 localPos,
                    out float3 localTangent,
                    out float3 _);

                // Perpendicular in the horizontal plane:
                // cross(tangent, up) → right of direction of travel
                float3 tanNorm = math.normalize(localTangent);
                float3 right   = math.normalize(math.cross(tanNorm, worldUp));

                // Positive lateralOffset = left (negate right)
                float3 offset = -right * lateralOffset;
                offset.y = 0f; // never shift vertically

                positions[i] = localPos + offset;
            }

            // ── Optionally reverse the sample array ───────────────────────────
            if (reverseDirection)
                System.Array.Reverse(positions);

            // ── Build the new spline with AutoSmooth tangents ─────────────────
            // AutoSmooth lets Unity compute natural C1-continuous tangents from
            // the sample positions, giving a smooth curve through all points.
            for (int i = 0; i < total; i++)
            {
                newSpline.Add(
                    new BezierKnot(positions[i]),
                    TangentMode.AutoSmooth);
            }

            // ── Report ────────────────────────────────────────────────────────
            float srcLen = srcSpline.GetLength();
            float newLen = newSpline.GetLength();

            Debug.Log(
                $"[SplineOffsetTool] Generated '{outputName}'\n" +
                $"  Samples        : {total}\n" +
                $"  Offset         : {lateralOffset:+0.00;-0.00} m\n" +
                $"  Reversed       : {reverseDirection}\n" +
                $"  Source length  : {srcLen:F1} m\n" +
                $"  Output length  : {newLen:F1} m  " +
                $"(Δ {newLen - srcLen:+0.0;-0.0} m, expected for {lateralOffset:+0.00;-0.00} m offset)");

            Selection.activeGameObject = newGO;
            EditorUtility.SetDirty(newGO);
        }
#else
        [ContextMenu("Generate Offset Spline")]
        public void GenerateOffsetSpline()
            => Debug.LogWarning("[SplineOffsetTool] Editor-only tool.");
#endif
    }
}