using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Splines;
using TMPro;
using DrivingSim;

/// <summary>
/// Dashboard display for the player car.
///
/// Shows:
///   • Current speed in display km/h (real-world equivalent via HighwaySpeedScale)
///   • Optional: distance travelled / total highway length
///   • Optional: progress bar fill
///
/// Attach this component to the same GameObject as the TextMeshProUGUI speed label,
/// or to any parent – it searches children automatically.
/// </summary>
public class DashboardSpeed : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Inspector
    // ─────────────────────────────────────────────────────────────────────────
    [Header("Car")]
    public CarController carController;

    [Header("Speed display")]
    [Tooltip("If true, format shows 3 digits with leading zeros (e.g. 080).\n" +
             "If false, shows plain integer (e.g. 80).")]
    public bool paddedFormat = true;

    [Tooltip("How quickly the displayed number catches up to actual speed.\n" +
             "Higher = more responsive. 8 = natural slight lag.")]
    [Range(1f, 20f)] public float smoothSpeed = 8f;

    [Header("Progress (optional)")]
    [Tooltip("The SplineContainer used by HighwayBuilder.\n" +
             "Assign to enable the km-travelled display and progress bar.")]
    public SplineContainer roadSpline;

    [Tooltip("Spline index inside the container (usually 0).")]
    public int splineIndex = 0;

    [Tooltip("TextMeshProUGUI that shows 'X.X / 5.0 km'.\n" +
             "Leave unassigned to hide progress text.")]
    public TextMeshProUGUI progressText;

    [Tooltip("UI Image (Image Type = Filled) driven by progress 0–1.\n" +
             "Leave unassigned to hide progress bar.")]
    public Image progressBarFill;

    // ─────────────────────────────────────────────────────────────────────────
    //  Private
    // ─────────────────────────────────────────────────────────────────────────
    private TextMeshProUGUI speedText;
    private float           smoothedSpeedKmh;
    private float           splineLength;

    // ─────────────────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────────
    void Start()
    {
        speedText = GetComponent<TextMeshProUGUI>();
        if (speedText == null)
            speedText = GetComponentInChildren<TextMeshProUGUI>();

        if (speedText == null)
            Debug.LogError("[DashboardSpeed] TextMeshProUGUI missing on " + gameObject.name);

        if (roadSpline != null)
            splineLength = roadSpline.Splines[splineIndex].GetLength();
    }

    void Update()
    {
        if (carController == null) return;

        UpdateSpeed();
        UpdateProgress();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Speed
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateSpeed()
    {
        if (speedText == null) return;

        // GetDisplaySpeedKmh() uses HighwaySpeedScale if present,
        // otherwise falls back to raw physics km/h.
        float targetKmh = carController.GetDisplaySpeedKmh();
        smoothedSpeedKmh = Mathf.Lerp(smoothedSpeedKmh, targetKmh,
                                       Time.deltaTime * smoothSpeed);

        int displayValue = Mathf.FloorToInt(smoothedSpeedKmh);
        speedText.text   = paddedFormat
            ? displayValue.ToString("000")
            : displayValue.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Progress
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateProgress()
    {
        if (roadSpline == null) return;
        if (progressText == null && progressBarFill == null) return;

        float normT        = EstimateSplineT();
        float realLengthKm = HighwaySpeedScale.Instance != null
            ? HighwaySpeedScale.Instance.RealHighwayLengthKm
            : splineLength / 1000f;

        float displayDistKm = normT * realLengthKm;
        float progress      = Mathf.Clamp01(normT);

        if (progressText != null)
            progressText.text = $"{displayDistKm:F1} / {realLengthKm:F1} km";

        if (progressBarFill != null)
            progressBarFill.fillAmount = progress;
    }

    /// <summary>
    /// Finds the normalised spline parameter (0–1) closest to the car's position
    /// using a 32-sample scan. Cheap enough to run every frame.
    /// </summary>
    private float EstimateSplineT()
    {
        if (carController == null || roadSpline == null || splineLength <= 0f)
            return 0f;

        const int Samples  = 32;
        float     bestT    = 0f;
        float     bestDist = float.MaxValue;
        // Access the car position via its transform rather than Rigidbody
        Vector3   carPos   = carController.transform.position;

        for (int i = 0; i <= Samples; i++)
        {
            float   t = (float)i / Samples;
            Vector3 p = roadSpline.transform.TransformPoint(
                roadSpline.Splines[splineIndex].EvaluatePosition(t));
            float   d = (p - carPos).sqrMagnitude;
            if (d < bestDist) { bestDist = d; bestT = t; }
        }

        return bestT;
    }
}
