using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DrivingSim;
using UnityEngine;
using UnityEngine.Splines;
using VehiclePhysics;

public class DataLogger : MonoBehaviour
{
    [Header("Riferimenti Veicolo VPP")]
    [Tooltip("VPVehicleToolkit del veicolo del partecipante.")]
    public VPVehicleToolkit vehicleToolkit;
    public Transform carTransform;

    [Header("Participant / CSV")]
    [SerializeField] private string participantId = "";
    [SerializeField] private int participantGroup = 1;

    [Header("Lane-keeping (DV4)")]
    [Tooltip("|lane_offset| oltre questa soglia [m] = il veicolo sta cambiando corsia (campione escluso dalla SDLP).")]
    public float laneChangeOffsetThreshold = 1.5f;
    [Tooltip("|velocita' laterale| oltre questa soglia [m/s] = cambio corsia in corso (campione escluso dalla SDLP).")]
    public float laneChangeLateralSpeedThreshold = 0.8f;
    [Tooltip("Mezza finestra [m] usata per decidere se il tratto e' rettilineo.")]
    public float straightHalfWindow = 25f;
    [Tooltip("Variazione massima di direzione [gradi] in quella finestra per considerare il tratto rettilineo.")]
    public float straightMaxHeadingChangeDeg = 1f;

    private int currentTrialIndex = 0;
    private string csvFilePath;     // riepilogo: 1 riga per trial
    private string rawCsvFilePath;  // canali grezzi a 60 Hz

    // Info stimolo del trial (passate da ExperimentManager)
    private int stimFrameId = 0;
    private string stimWordType = "";
    private string stimLinePosition = "";
    private string stimExitName = "";

    [Header("Stato Finestre (Sola Lettura)")]
    public bool isLoggingEnabled = false;
    public bool speedBaselineCompleted = false;
    public bool sdlpBaselineActive = false;
    public bool latencyTimerActive = false;   // true = VMB_READABLE raggiunto
    public bool postGantryActive = false;
    [Tooltip("Distanza firmata dal gantry [m]; negativa prima del gantry.")]
    public float distanceToGantry = float.NaN;

    private float totalTrialOdometer = 0f;    // Metri percorsi da quando parte il trial
    private bool trialValidityError = false;

    // --- Geometria strada (impostata da ExperimentManager.ConfigureRoad) ---
    private HighwayRoadMap roadMap;
    private SplineContainer roadContainer;
    private int roadSplineIndex = -1;
    private float laneCentreOffset = 0f;       // offset del centro corsia dalla center-line della spline
    private float gantryDistance = float.NaN;  // distanza del gantry dall'inizio della spline
    private float exitLaneStartDistance = float.PositiveInfinity;
    private bool roadWarningShown = false;

    // --- Finestre di analisi (metri rispetto al gantry; negativo = prima del gantry) ---
    private const float SPEED_BASELINE_START_M = -1000f;
    private const float SPEED_BASELINE_END_M = -500f;
    private const float SDLP_BASELINE_START_M = -800f;
    private const float SDLP_BASELINE_END_M = -500f;
    private const float PRIMARY_WINDOW_AFTER_GANTRY_M = 500f; // DV2, DV5 (limitata alla corsia di uscita)
    private const float SDLP_RESPONSE_LENGTH_M = 300f;        // DV4: VMB_READABLE -> +300 m
    private const float DV3_WINDOW_LENGTH_M = 1000f;          // DV3: VMB_READABLE -> +1000 m
    private const float BRAKE_PRESSED_THRESHOLD = 0.1f;       // basta sfiorarlo (10%)

    // Timer
    private float speedAbove75Timer = 0f;
    private Vector3 lastPosition;
    private float lastSpeedMs = 0f;
    private float lastLaneOffset = float.NaN;
    private double trialStartSimTime = 0.0;

    // Buffer dei dati (campionati a 60 Hz)
    private List<float> speedBaselineData = new List<float>();   // solo gate di validita' (>=75 km/h per 5 s)
    private List<float> baselineSpeedWindow = new List<float>(); // -1000..-500 m (DV2 / DV3)
    private List<float> baselineGasWindow = new List<float>();   // -1000..-500 m (DV1 release)
    private List<float> sdlpBaselineData = new List<float>();    // lane_offset -800..-500 m
    private List<float> sdlpResponseData = new List<float>();    // lane_offset READABLE..+300 m

    // Variabili per il tracciamento in tempo reale delle DV
    private float brakingLatencyTimer = 0f;
    private int framesSustainedDeceleration = 0;
    private readonly float[] decelTop = { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
    private bool baselineAvailable = false;
    private float distanceAtReadable = float.NaN;

    // Marker di evento (tempo = clock di simulazione relativo all'inizio trial)
    private double tVmbAppear = double.NaN, tVmbReadable = double.NaN, tGantry = double.NaN;
    private float dVmbAppear = float.NaN, dVmbReadable = float.NaN, dGantry = float.NaN;
    private string pendingEvent = "";

    // Buffer scrittura raw (evita di aprire il file a ogni frame)
    private readonly StringBuilder rawBuffer = new StringBuilder(64 * 1024);
    private const int RAW_FLUSH_CHARS = 32 * 1024;

    // --- RISULTATI FINALI ---
    [Header("Risultati Trial Corrente")]
    // DV1 (secondi; -1 = non rilevato). Il CSV li esporta in ms.
    public float DV1_BrakingLatency = -1f;      // decel <= -0.5 m/s^2 per >= 500 ms  (esistente)
    public float DV1_ReleaseLatency = -1f;      // rilascio acceleratore              (nuovo)
    public float baselineAccel = float.NaN;     // media gas_pedal_pct -1000..-500 m
    public float baselineAccelSd = float.NaN;
    // DV2
    public float DV2_MinSpeed = float.MaxValue; // min_speed (esistente)
    public float baselineSpeed = float.NaN;
    public float baselineSpeedSd = float.NaN;
    public float DV2b_SpeedReduction = float.NaN;
    public float DV2a_MaxDecel = float.NaN;
    public float DV2a_MaxDecel2nd = float.NaN;
    public float DV2a_MaxDecel3rd = float.NaN;
    // DV3
    public float DV3_DurationBelowBaseline = 0f;
    // DV4
    public float DV4_SDLP_Change = float.NaN;
    public float SDLP_Response = float.NaN;
    public float SDLP_Baseline = float.NaN;
    // DV5
    public int DV5_BrakeEngagement = 0;
    // DV8
    public int DV8_ExitCompliance = 0;

    void Start()
    {
        if (vehicleToolkit == null && carTransform != null)
            vehicleToolkit = carTransform.GetComponentInParent<VPVehicleToolkit>();

        if (vehicleToolkit == null)
            Debug.LogError("[DataLogger] VPVehicleToolkit non assegnato/trovato.", this);

        if (carTransform == null && vehicleToolkit != null)
            carTransform = vehicleToolkit.transform;

        if (carTransform != null)
            lastPosition = carTransform.position;

        // IMPERATIVO: Forza il motore fisico di Unity a 60 Hz per rispettare il protocollo (1/60 = 0.016666)
        Time.fixedDeltaTime = 1f / 60f;
    }

    // --- CONFIGURAZIONE (chiamata da ExperimentManager) ---

    /// <summary>
    /// Dice al logger su quale spline guida il partecipante e dove si trovano gantry e corsia di uscita.
    /// Costruisce (una volta sola) la mappa stradale usata per distance_to_gantry e lane_offset.
    /// </summary>
    public void ConfigureRoad(
        SplineContainer container,
        int splineIndex,
        float laneCentreOffsetM,
        float gantrySplineDistance,
        float exitLaneStartSplineDistance)
    {
        if (container == null)
        {
            Debug.LogError("[DataLogger] ConfigureRoad: SplineContainer nullo.", this);
            return;
        }

        if (roadMap == null || roadContainer != container || roadSplineIndex != splineIndex)
        {
            roadMap = new HighwayRoadMap(container, splineIndex);
            roadContainer = container;
            roadSplineIndex = splineIndex;
            Debug.Log($"[DataLogger] Mappa stradale costruita: lunghezza {roadMap.Length:F0} m.");
        }

        laneCentreOffset = laneCentreOffsetM;
        gantryDistance = gantrySplineDistance;
        exitLaneStartDistance = exitLaneStartSplineDistance;
        roadWarningShown = false;
    }

    /// <summary>Metadati dello stimolo del trial: finiscono nel CSV di riepilogo.</summary>
    public void SetTrialInfo(int frameId, string wordType, string linePosition, string exitName)
    {
        stimFrameId = frameId;
        stimWordType = wordType ?? "";
        stimLinePosition = linePosition ?? "";
        stimExitName = exitName ?? "";
    }

    void FixedUpdate()
    {
        // Il logger si spegne completamente se non è abilitato (es. durante la Pratica)
        if (vehicleToolkit == null || carTransform == null || !isLoggingEnabled) return;

        float dt = Time.fixedDeltaTime;
        double simTime = Time.fixedTimeAsDouble;

        float currentSpeedMs = vehicleToolkit.speed;
        float currentSpeedKmH = HighwaySpeedScale.Instance != null
            ? HighwaySpeedScale.Instance.PhysicsMsToDisplayKmh(currentSpeedMs)
            : vehicleToolkit.speedInKph;
        float currentAcceleration = (currentSpeedMs - lastSpeedMs) / dt; // m/s^2 (velocita' grezza, nessuno smoothing)
        float currentThrottle = GetThrottlePedal();                      // 0..1
        float currentBrake = GetBrakePedal();                            // 0..1
        float gasPct = currentThrottle * 100f;
        float brakePct = currentBrake * 100f;

        // --- GEOMETRIA: distance_to_gantry e lane_offset (in metri, rispetto al centro corsia) ---
        float distToGantry = float.NaN;
        float laneOffset = float.NaN;
        bool roadStraight = false;
        bool laneChanging = false;

        if (roadMap != null && !float.IsNaN(gantryDistance) &&
            roadMap.Project(carTransform.position, out float roadDist, out float roadSide))
        {
            distToGantry = roadDist - gantryDistance;
            laneOffset = roadSide - laneCentreOffset;

            float lateralSpeed = float.IsNaN(lastLaneOffset) ? 0f : (laneOffset - lastLaneOffset) / dt;
            lastLaneOffset = laneOffset;

            laneChanging = Mathf.Abs(laneOffset) > laneChangeOffsetThreshold ||
                           Mathf.Abs(lateralSpeed) > laneChangeLateralSpeedThreshold;
            roadStraight = roadMap.IsStraight(roadDist, straightHalfWindow, straightMaxHeadingChangeDeg);
        }
        else if (!roadWarningShown)
        {
            roadWarningShown = true;
            Debug.LogError("[DataLogger] Mappa stradale / gantry non configurati: distance_to_gantry e lane_offset non disponibili. " +
                           "ExperimentManager deve chiamare ConfigureRoad().", this);
        }

        distanceToGantry = distToGantry;
        bool cleanLaneSample = roadStraight && !laneChanging; // SDLP: solo rettilineo e senza cambio corsia

        float distanceThisFrame = Vector3.Distance(carTransform.position, lastPosition);
        totalTrialOdometer += distanceThisFrame;

        // 1. GATE DI VALIDITA' DELLA BASELINE (invariato: >=75 km/h per 5 s tra 1.0 e 1.4 km dall'inizio trial)
        if (!speedBaselineCompleted && !trialValidityError)
        {
            if (totalTrialOdometer >= 1000f)
            {
                if (currentSpeedKmH >= 75f)
                {
                    speedAbove75Timer += dt;
                    speedBaselineData.Add(currentSpeedKmH);

                    if (speedAbove75Timer >= 5f)
                    {
                        speedBaselineCompleted = true;
                        Debug.Log($"DataLogger: Speed Baseline acquisita a {totalTrialOdometer:F0} metri.");
                    }
                }
                else
                {
                    speedAbove75Timer = 0f;
                    speedBaselineData.Clear();
                }

                if (totalTrialOdometer >= 1400f && !speedBaselineCompleted)
                {
                    trialValidityError = true;
                    Debug.Log("BASELINE_NOT_REACHED: L'utente non ha stabilizzato la velocità tra 1.0 e 1.4 km.");
                }
            }
        }

        // 2. FINESTRE DI BASELINE (misurate in distanza dal gantry)
        if (distToGantry >= SPEED_BASELINE_START_M && distToGantry < SPEED_BASELINE_END_M)
        {
            baselineSpeedWindow.Add(currentSpeedKmH);   // DV2 / DV3
            baselineGasWindow.Add(gasPct);              // DV1 release
        }

        if (distToGantry >= SDLP_BASELINE_START_M && distToGantry < SDLP_BASELINE_END_M && cleanLaneSample)
        {
            sdlpBaselineData.Add(laneOffset);           // DV4 baseline
        }

        // 3. FINESTRE DI RISPOSTA (partono da VMB_READABLE)
        if (latencyTimerActive)
        {
            brakingLatencyTimer += dt;

            // DV1 (esistente): decelerazione sostenuta <= -0.5 m/s^2 per 500 ms (30 frame)
            if (DV1_BrakingLatency < 0)
            {
                if (currentAcceleration <= -0.5f) framesSustainedDeceleration++;
                else framesSustainedDeceleration = 0;

                if (framesSustainedDeceleration >= 30)
                {
                    // L'evento è iniziato 500ms fa, quindi sottraiamo 0.5s dal timer attuale
                    DV1_BrakingLatency = brakingLatencyTimer - 0.5f;
                }
            }

            // DV1 (nuovo): rilascio dell'acceleratore
            UpdateReleaseLatency(gasPct);

            // Finestra primaria: VMB_READABLE -> +500 m oltre il gantry (limitata alla corsia di uscita): DV2, DV5
            float primaryEnd = Mathf.Min(PRIMARY_WINDOW_AFTER_GANTRY_M, exitLaneStartDistance - gantryDistance);
            if (distToGantry <= primaryEnd)
            {
                if (currentSpeedKmH < DV2_MinSpeed) DV2_MinSpeed = currentSpeedKmH;
                RegisterDecel(currentAcceleration);
                if (currentBrake > BRAKE_PRESSED_THRESHOLD) DV5_BrakeEngagement = 1;
            }

            // Finestra estesa: VMB_READABLE -> +1000 m: DV3
            if (distToGantry <= distanceAtReadable + DV3_WINDOW_LENGTH_M)
            {
                if (baselineAvailable && currentSpeedKmH < baselineSpeed)
                    DV3_DurationBelowBaseline += dt;
            }

            // Finestra di risposta SDLP: VMB_READABLE -> +300 m: DV4
            if (distToGantry <= distanceAtReadable + SDLP_RESPONSE_LENGTH_M && cleanLaneSample)
            {
                sdlpResponseData.Add(laneOffset);
            }
        }

        // 4. CANALI GREZZI A 60 Hz (nessuna soglia, nessuna elaborazione)
        AppendRawRow(simTime, currentSpeedKmH, currentSpeedMs, gasPct, brakePct,
                     distToGantry, laneOffset, laneChanging, roadStraight);

        lastPosition = carTransform.position;
        lastSpeedMs = currentSpeedMs;
    }

    // --- DV1: LATENZA DI RILASCIO DELL'ACCELERATORE ---
    private void UpdateReleaseLatency(float gasPct)
    {
        // Soglia relativa al throttle di crociera di ciascun guidatore (punti percentuali sotto baseline_accel).
        // Valore di partenza per il pilot: da confermare sulle tracce grezze (basta ricalcolare, non rifare i trial).
        const float REL_THRESH = 10f;

        if (DV1_ReleaseLatency >= 0f) return;        // gia' rilevato
        if (float.IsNaN(baselineAccel)) return;      // baseline non disponibile

        if (gasPct < baselineAccel - REL_THRESH)
            DV1_ReleaseLatency = brakingLatencyTimer; // secondi da VMB_READABLE
    }

    private void RegisterDecel(float a)
    {
        if (a < decelTop[0]) { decelTop[2] = decelTop[1]; decelTop[1] = decelTop[0]; decelTop[0] = a; }
        else if (a < decelTop[1]) { decelTop[2] = decelTop[1]; decelTop[1] = a; }
        else if (a < decelTop[2]) { decelTop[2] = a; }
    }

    // --- METODI DEI TRIGGER ---
    // I trigger sono marker di evento: le finestre di baseline sono misurate in distanza dal gantry,
    // cosi' non dipendono da dove atterra esattamente il collider.
    public void Trigger_SDLPBaselineStart()
    {
        sdlpBaselineActive = true;
        MarkEvent("SDLP_BASELINE_START");
    }

    public void Trigger_VMBAppear()
    {
        sdlpBaselineActive = false;
        speedBaselineCompleted = true;

        if (isLoggingEnabled && double.IsNaN(tVmbAppear))
        {
            tVmbAppear = Time.fixedTimeAsDouble - trialStartSimTime;
            dVmbAppear = CurrentDistanceToGantry();
        }
        MarkEvent("VMB_APPEAR");
    }

    public void Trigger_VMBReadable()
    {
        if (latencyTimerActive) return; // evita doppie attivazioni (piu' collider del veicolo)

        latencyTimerActive = true;

        if (isLoggingEnabled)
        {
            tVmbReadable = Time.fixedTimeAsDouble - trialStartSimTime;
            dVmbReadable = CurrentDistanceToGantry();
            distanceAtReadable = dVmbReadable;
            FreezeBaselines();
        }
        MarkEvent("VMB_READABLE");
    }

    public void Trigger_Gantry()
    {
        if (postGantryActive) return;

        postGantryActive = true;

        if (isLoggingEnabled)
        {
            tGantry = Time.fixedTimeAsDouble - trialStartSimTime;
            dGantry = CurrentDistanceToGantry();
        }
        MarkEvent("GANTRY");
    }

    private void MarkEvent(string name)
    {
        if (!isLoggingEnabled) return;
        pendingEvent = pendingEvent.Length == 0 ? name : pendingEvent + "|" + name;
    }

    private float CurrentDistanceToGantry()
    {
        if (roadMap == null || carTransform == null || float.IsNaN(gantryDistance)) return float.NaN;
        return roadMap.Project(carTransform.position, out float d, out float s) ? d - gantryDistance : float.NaN;
    }

    // Fissa media e SD di baseline (speed e gas) quando compare VMB_READABLE (la finestra -1000..-500 m e' gia' chiusa).
    private void FreezeBaselines()
    {
        baselineSpeed = Mean(baselineSpeedWindow);
        baselineSpeedSd = CalculateStandardDeviation(baselineSpeedWindow);
        baselineAccel = Mean(baselineGasWindow);
        baselineAccelSd = CalculateStandardDeviation(baselineGasWindow);
        baselineAvailable = !float.IsNaN(baselineSpeed) && !float.IsNaN(baselineAccel);

        if (!baselineAvailable)
            Debug.LogWarning("[DataLogger] Nessun campione nella finestra baseline -1000..-500 m: DV2b, DV3 e DV1_release non calcolabili.");
    }

    // --- CALCOLO FINALE ED ESPORTAZIONE ---
    public void FineTrial(bool uscitaCorretta)
    {
        if (!isLoggingEnabled) return; // Sicurezza extra: non calcolare nulla se spento

        bool baselineValid = !trialValidityError;
        if (!baselineValid)
            Debug.LogWarning("FINE TRIAL: BASELINE_NOT_REACHED. Il trial viene salvato con BaselineValid=0.");

        DV8_ExitCompliance = uscitaCorretta ? 1 : 0;

        // DV2: max decel + riduzione di velocita'
        DV2a_MaxDecel = FiniteOrNaN(decelTop[0]);
        DV2a_MaxDecel2nd = FiniteOrNaN(decelTop[1]);
        DV2a_MaxDecel3rd = FiniteOrNaN(decelTop[2]);

        bool hasMin = DV2_MinSpeed != float.MaxValue;
        DV2b_SpeedReduction = (baselineAvailable && hasMin && baselineSpeed > 0f)
            ? (baselineSpeed - DV2_MinSpeed) / baselineSpeed
            : float.NaN;

        // DV4: SDLP change
        SDLP_Baseline = CalculateStandardDeviation(sdlpBaselineData);
        SDLP_Response = CalculateStandardDeviation(sdlpResponseData);
        DV4_SDLP_Change = SDLP_Response - SDLP_Baseline; // NaN se uno dei due manca

        // Stampa i risultati in Console per il test
        Debug.Log("=== RISULTATI TRIAL ===");
        Debug.Log($"DV1 decel latency: {DV1_BrakingLatency} s | release latency: {DV1_ReleaseLatency} s (baseline_accel {baselineAccel:F1} ± {baselineAccelSd:F1})");
        Debug.Log($"DV2 min_speed: {(hasMin ? DV2_MinSpeed : float.NaN):F1} km/h | baseline {baselineSpeed:F1} ± {baselineSpeedSd:F1} | reduction {DV2b_SpeedReduction:F3} | max decel {DV2a_MaxDecel:F2} m/s^2");
        Debug.Log($"DV3 Time Below Baseline: {DV3_DurationBelowBaseline} s");
        Debug.Log($"DV4 SDLP Change: {DV4_SDLP_Change} m (response {SDLP_Response}, baseline {SDLP_Baseline})");
        Debug.Log($"DV5 Brake Engaged: {DV5_BrakeEngagement}");
        Debug.Log($"DV8 Exit Compliance: {DV8_ExitCompliance}");
        Debug.Log("=======================");

        FlushRawBuffer();
        WriteSummaryRow(baselineValid);

        StopLogging();
    }

    // --- METODI PER CONTROLLARE IL LOGGER DALL'ESTERNO ---
    public void StartLogging()
    {
        StartLogging(0);
    }

    public void StartLogging(int trialIndex)
    {
        ResetLogger();

        currentTrialIndex = trialIndex;

        if (string.IsNullOrWhiteSpace(participantId))
        {
            Debug.LogError("[DataLogger] Cannot start logging: participant ID is empty.");
            return;
        }

        if (participantGroup < 1 || participantGroup > 6)
        {
            Debug.LogError("[DataLogger] Cannot start logging: invalid participant group.");
            return;
        }

        csvFilePath = BuildCsvFilePath();
        rawCsvFilePath = BuildRawCsvFilePath();

        EnsureCsvWithHeader(csvFilePath, SummaryHeader);
        EnsureCsvWithHeader(rawCsvFilePath, RawHeader);

        trialStartSimTime = Time.fixedTimeAsDouble;
        isLoggingEnabled = true;

        Debug.Log($"DataLogger: Registrazione DATI INIZIATA. Trial {currentTrialIndex}");
        Debug.Log($"DataLogger: CSV riepilogo = {csvFilePath}");
        Debug.Log($"DataLogger: CSV raw 60 Hz = {rawCsvFilePath}");
    }

    public void StopLogging()
    {
        FlushRawBuffer();
        isLoggingEnabled = false;
        ResetLogger();
        Debug.Log("DataLogger: Registrazione DATI FERMATA.");
    }

    public void ResetLogger()
    {
        // Pulisce tutto per il prossimo trial
        speedBaselineCompleted = false; sdlpBaselineActive = false; latencyTimerActive = false; postGantryActive = false;
        speedAbove75Timer = 0f; brakingLatencyTimer = 0f; framesSustainedDeceleration = 0;
        speedBaselineData.Clear(); baselineSpeedWindow.Clear(); baselineGasWindow.Clear();
        sdlpBaselineData.Clear(); sdlpResponseData.Clear();
        rawBuffer.Clear();

        DV1_BrakingLatency = -1f; DV1_ReleaseLatency = -1f;
        baselineAccel = float.NaN; baselineAccelSd = float.NaN;
        DV2_MinSpeed = float.MaxValue; baselineSpeed = float.NaN; baselineSpeedSd = float.NaN;
        DV2b_SpeedReduction = float.NaN; DV2a_MaxDecel = float.NaN; DV2a_MaxDecel2nd = float.NaN; DV2a_MaxDecel3rd = float.NaN;
        DV3_DurationBelowBaseline = 0f;
        DV4_SDLP_Change = float.NaN; SDLP_Response = float.NaN; SDLP_Baseline = float.NaN;
        DV5_BrakeEngagement = 0;
        DV8_ExitCompliance = 0;

        decelTop[0] = decelTop[1] = decelTop[2] = float.PositiveInfinity;
        baselineAvailable = false;
        distanceAtReadable = float.NaN;
        tVmbAppear = tVmbReadable = tGantry = double.NaN;
        dVmbAppear = dVmbReadable = dGantry = float.NaN;
        pendingEvent = "";
        distanceToGantry = float.NaN;

        totalTrialOdometer = 0f;     // Fondamentale resettare l'odometro
        trialValidityError = false;  // Resetta lo stato di errore
        lastSpeedMs = 0f;
        lastLaneOffset = float.NaN;
        if (roadMap != null) roadMap.ResetHint();
        if (carTransform != null) lastPosition = carTransform.position;
    }

    void OnApplicationQuit() { FlushRawBuffer(); }
    void OnDisable() { FlushRawBuffer(); }

    // --- MATEMATICA E INPUT ---
    // Deviazione standard di popolazione (come np.std). NaN se ci sono meno di 2 campioni.
    private float CalculateStandardDeviation(List<float> values)
    {
        if (values.Count < 2) return float.NaN;
        float avg = values.Average();
        float sumOfSquares = 0f;
        foreach (float v in values) sumOfSquares += Mathf.Pow(v - avg, 2);
        return Mathf.Sqrt(sumOfSquares / values.Count);
    }

    private static float Mean(List<float> values)
    {
        return values.Count > 0 ? values.Average() : float.NaN;
    }

    private static float FiniteOrNaN(float v)
    {
        return float.IsInfinity(v) ? float.NaN : v;
    }

    private float GetThrottlePedal()
    => vehicleToolkit != null && vehicleToolkit.vehicle != null
        ? Mathf.Clamp01(VPVehicleToolkit.GetThrottle(vehicleToolkit.vehicle))
        : 0f;

    private float GetBrakePedal()
        => vehicleToolkit != null && vehicleToolkit.vehicle != null
            ? Mathf.Clamp01(VPVehicleToolkit.GetBrake(vehicleToolkit.vehicle))
            : 0f;


    // ------ TESTING ------

    // Variabile per il timer di debug
    private float debugLogTimer = 0f;

    void Update()
    {
        if (!isLoggingEnabled) return; // Non stampare i log LIVE durante la pratica

        // Usiamo Time.deltaTime (e non fixedDeltaTime) perché Update gira al framerate dello schermo
        debugLogTimer += Time.deltaTime;

        if (debugLogTimer >= 5f)
        {
            StampaDatiInTempoReale();
            debugLogTimer = 0f; // Resetta il timer
        }
    }

    public void SetParticipantInfo(string id, int group)
    {
        participantId = id.Trim();
        participantGroup = group;

        csvFilePath = BuildCsvFilePath();
        rawCsvFilePath = BuildRawCsvFilePath();

        Debug.Log($"DataLogger: Participant={participantId}, Group={participantGroup}");
        Debug.Log($"DataLogger: CSV={csvFilePath}");
        Debug.Log($"DataLogger: CSV raw={rawCsvFilePath}");
    }

    private string BuildCsvFilePath()
    {
        string safeParticipantId = MakeSafeFileName(participantId);

        return Path.Combine(
            Application.persistentDataPath,
            $"car_simulator_{safeParticipantId}_{participantGroup}.csv"
        );
    }

    private string BuildRawCsvFilePath()
    {
        string safeParticipantId = MakeSafeFileName(participantId);

        return Path.Combine(
            Application.persistentDataPath,
            $"car_simulator_{safeParticipantId}_{participantGroup}_raw.csv"
        );
    }

    private string MakeSafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "UNKNOWN";

        StringBuilder result = new StringBuilder(value.Trim());

        foreach (char invalidChar in Path.GetInvalidFileNameChars())
            result.Replace(invalidChar, '_');

        return result.ToString();
    }

    // ---------------------------------------------------------------
    //  CSV
    // ---------------------------------------------------------------
    private const string SummaryHeader =
        "ParticipantID,Group,TrialIndex,FrameID,WordType,LinePosition,ExitName,BaselineValid," +
        "DV1_decel_latency_ms,DV1_release_latency_ms,baseline_accel,baseline_accel_sd," +
        "min_speed,baseline_speed,baseline_speed_sd,DV2b_speed_reduction," +
        "DV2a_max_decel,DV2a_max_decel_2nd,DV2a_max_decel_3rd," +
        "DV3_time_below_baseline,DV4_sdlp_change,SDLP_response,SDLP_baseline,n_sdlp_response,n_sdlp_baseline," +
        "DV5_brake_pressed,DV8_exit_compliance," +
        "t_VMB_APPEAR_s,t_VMB_READABLE_s,t_GANTRY_s,d_VMB_APPEAR_m,d_VMB_READABLE_m,d_GANTRY_m";

    private const string RawHeader =
        "ParticipantID,Group,TrialIndex,timestamp_s,sim_time_s,trial_time_s," +
        "speed_kmh,speed_ms,gas_pedal_pct,brake_pct,brake_pressed," +
        "distance_to_gantry_m,lane_offset_m,lane_change_flag,road_straight,event";

    // Formattazione invariante: con il locale italiano ToString("F6") darebbe la virgola decimale e romperebbe il CSV.
    private static string F(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "NA" : v.ToString("F6", CultureInfo.InvariantCulture);
    private static string F(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "NA" : v.ToString("F6", CultureInfo.InvariantCulture);
    private static string Ms(float seconds) => seconds < 0f ? "-1" : (seconds * 1000f).ToString("F3", CultureInfo.InvariantCulture);

    // Crea il file con intestazione. Se esiste gia' con un'intestazione diversa (vecchio formato),
    // lo rinomina invece di mescolare colonne diverse nello stesso CSV.
    private void EnsureCsvWithHeader(string path, string header)
    {
        try
        {
            if (File.Exists(path))
            {
                string firstLine;
                using (StreamReader reader = new StreamReader(path))
                    firstLine = reader.ReadLine();

                if (firstLine == header)
                    return;

                string backup = Path.Combine(
                    Path.GetDirectoryName(path),
                    Path.GetFileNameWithoutExtension(path) +
                    "_oldformat_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                File.Move(path, backup);
                Debug.LogWarning($"[DataLogger] Intestazione CSV diversa: vecchio file spostato in {backup}");
            }

            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (StreamWriter writer = new StreamWriter(stream))
            {
                writer.WriteLine(header);
                writer.Flush();
                stream.Flush(true);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[DataLogger] Errore creazione CSV {path}: {ex}");
        }
    }

    private void AppendRawRow(
        double simTime, float speedKmh, float speedMs, float gasPct, float brakePct,
        float distToGantry, float laneOffset, bool laneChanging, bool roadStraight)
    {
        rawBuffer.Append(CsvEscape(participantId)).Append(',')
                 .Append(participantGroup).Append(',')
                 .Append(currentTrialIndex).Append(',')
                 .Append(F(Time.realtimeSinceStartupAsDouble)).Append(',')
                 .Append(F(simTime)).Append(',')
                 .Append(F(simTime - trialStartSimTime)).Append(',')
                 .Append(F(speedKmh)).Append(',')
                 .Append(F(speedMs)).Append(',')
                 .Append(F(gasPct)).Append(',')
                 .Append(F(brakePct)).Append(',')
                 .Append(brakePct / 100f > BRAKE_PRESSED_THRESHOLD ? 1 : 0).Append(',')
                 .Append(F(distToGantry)).Append(',')
                 .Append(F(laneOffset)).Append(',')
                 .Append(laneChanging ? 1 : 0).Append(',')
                 .Append(roadStraight ? 1 : 0).Append(',')
                 .Append(pendingEvent)
                 .Append('\n');

        pendingEvent = "";

        if (rawBuffer.Length >= RAW_FLUSH_CHARS)
            FlushRawBuffer();
    }

    private void FlushRawBuffer()
    {
        if (rawBuffer.Length == 0 || string.IsNullOrWhiteSpace(rawCsvFilePath))
            return;

        try
        {
            File.AppendAllText(rawCsvFilePath, rawBuffer.ToString(), new UTF8Encoding(false));
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[DataLogger] Errore durante il salvataggio CSV raw: {ex}");
        }

        rawBuffer.Clear();
    }

    private void WriteSummaryRow(bool baselineValid)
    {
        if (string.IsNullOrWhiteSpace(csvFilePath))
            csvFilePath = BuildCsvFilePath();

        string row = string.Join(",",
            CsvEscape(participantId),
            participantGroup.ToString(CultureInfo.InvariantCulture),
            currentTrialIndex.ToString(CultureInfo.InvariantCulture),
            stimFrameId.ToString(CultureInfo.InvariantCulture),
            CsvEscape(stimWordType),
            CsvEscape(stimLinePosition),
            CsvEscape(stimExitName),
            baselineValid ? "1" : "0",
            // DV1
            Ms(DV1_BrakingLatency),
            Ms(DV1_ReleaseLatency),
            F(baselineAccel),
            F(baselineAccelSd),
            // DV2
            DV2_MinSpeed == float.MaxValue ? "NA" : F(DV2_MinSpeed),
            F(baselineSpeed),
            F(baselineSpeedSd),
            F(DV2b_SpeedReduction),
            F(DV2a_MaxDecel),
            F(DV2a_MaxDecel2nd),
            F(DV2a_MaxDecel3rd),
            // DV3
            baselineAvailable ? F(DV3_DurationBelowBaseline) : "NA",
            // DV4
            F(DV4_SDLP_Change),
            F(SDLP_Response),
            F(SDLP_Baseline),
            sdlpResponseData.Count.ToString(CultureInfo.InvariantCulture),
            sdlpBaselineData.Count.ToString(CultureInfo.InvariantCulture),
            // DV5 / DV8
            DV5_BrakeEngagement.ToString(CultureInfo.InvariantCulture),
            DV8_ExitCompliance.ToString(CultureInfo.InvariantCulture),
            // Marker di evento (clock di simulazione, secondi dall'inizio trial) e distanza dal gantry all'evento
            F(tVmbAppear),
            F(tVmbReadable),
            F(tGantry),
            F(dVmbAppear),
            F(dVmbReadable),
            F(dGantry)
        );

        try
        {
            using (FileStream stream = new FileStream(
                csvFilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read))
            using (StreamWriter writer = new StreamWriter(stream))
            {
                writer.WriteLine(row);

                // Flush StreamWriter and force the FileStream to flush.
                writer.Flush();
                stream.Flush(true);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[DataLogger] Errore durante il salvataggio CSV: {ex}");
        }
    }

    private string CsvEscape(string value)
    {
        if (value == null)
            return "";

        if (value.Contains(",") ||
            value.Contains("\"") ||
            value.Contains("\n") ||
            value.Contains("\r"))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private void StampaDatiInTempoReale()
    {
        string stato = $"[LIVE 5s] Odometro: {totalTrialOdometer:F0}m | Dist. gantry: {(float.IsNaN(distanceToGantry) ? "-" : distanceToGantry.ToString("F0") + "m")} | ";

        stato += $"DV1 decel: {(DV1_BrakingLatency < 0 ? "In attesa" : DV1_BrakingLatency.ToString("F2") + "s")} | ";
        stato += $"DV1 release: {(DV1_ReleaseLatency < 0 ? "In attesa" : DV1_ReleaseLatency.ToString("F2") + "s")} | ";
        stato += $"DV2 (MinSpd): {(DV2_MinSpeed == float.MaxValue ? "-" : DV2_MinSpeed.ToString("F1") + "km/h")} | ";
        stato += $"DV3 (TimeBelow): {DV3_DurationBelowBaseline:F1}s | ";
        stato += $"Freno Toccato (DV5): {(DV5_BrakeEngagement == 1 ? "SI" : "NO")}";

        Debug.Log(stato);
    }
}
