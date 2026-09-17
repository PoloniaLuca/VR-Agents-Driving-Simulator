using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using DrivingSim;
using VehiclePhysics;
using System.IO;
using System.Text;

public class DataLogger : MonoBehaviour
{
    [Header("Riferimenti Veicolo VPP")]
    [Tooltip("VPVehicleToolkit del veicolo del partecipante.")]
    public VPVehicleToolkit vehicleToolkit;
    public Transform carTransform;

    [Header("Participant / CSV")]
    [SerializeField] private string participantId = "";
    [SerializeField] private int participantGroup = 1;

    private int currentTrialIndex = 0;
    private string csvFilePath;

    [Header("Stato Finestre (Sola Lettura)")]
    public bool isLoggingEnabled = false;
    public bool speedBaselineCompleted = false;
    public bool sdlpBaselineActive = false;
    public bool latencyTimerActive = false;
    public bool postGantryActive = false;

    private float totalTrialOdometer = 0f;    // Conta i metri da quando parte il trial (0 a 4000)
    private bool trialValidityError = false; 

    // Timer e Odometri
    private float speedAbove75Timer = 0f;
    private float postGantryOdometer = 0f;
    private Vector3 lastPosition;
    private float lastSpeedMs = 0f;

    // Buffer dei dati (Campionati a 60Hz)
    private List<float> speedBaselineData = new List<float>();
    private List<float> sdlpBaselineData = new List<float>();
    private List<float> sdlpPostGantryData = new List<float>();

    // Buffer circolare per acceleratore (5 secondi a 60Hz = 300 frame)
    private Queue<float> throttlePreReadableBuffer = new Queue<float>();
    private List<float> throttlePostReadableData = new List<float>();

    // Variabili per il tracciamento in tempo reale delle DV
    private float brakingLatencyTimer = 0f;
    private int framesSustainedDeceleration = 0;
    
    // --- RISULTATI FINALI (Le 8 DVs) ---
    [Header("Risultati Trial Corrente (Le 8 DVs)")]
    public float DV1_BrakingLatency = -1f;
    public float DV2_MinSpeed = float.MaxValue;
    public float DV3_DurationBelowBaseline = 0f;
    public float DV4_SDLP_Change = 0f;
    public int DV5_BrakeEngagement = 0;
    public float DV6_SpeedRecoveryTime = -1f;
    public float DV7_ThrottleReduction = 0f;
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

    void FixedUpdate()
    {
        // Il logger si spegne completamente se non è abilitato (es. durante la Pratica)
        if (vehicleToolkit == null || carTransform == null || !isLoggingEnabled) return;

        float currentSpeedMs = vehicleToolkit.speed;
        float currentSpeedKmH = HighwaySpeedScale.Instance != null
            ? HighwaySpeedScale.Instance.PhysicsMsToDisplayKmh(currentSpeedMs)
            : vehicleToolkit.speedInKph;
        float currentLateralPos = carTransform.position.x;
        float currentAcceleration = (currentSpeedMs - lastSpeedMs) / Time.fixedDeltaTime; // m/s^2
        float currentThrottle = GetThrottlePedal();
        float currentBrake = GetBrakePedal();

        float distanceThisFrame = Vector3.Distance(carTransform.position, lastPosition);
        totalTrialOdometer += distanceThisFrame;

        // 1. SPEED BASELINE CONDIZIONALE (Solo tra 1.0km e 1.4km)
        if (!speedBaselineCompleted && !trialValidityError)
        {
            // Controlliamo se abbiamo superato il primo chilometro
            if (totalTrialOdometer >= 1000f)
            {
                if (currentSpeedKmH >= 75f)
                {
                    speedAbove75Timer += Time.fixedDeltaTime;
                    speedBaselineData.Add(currentSpeedKmH);

                    // Se mantiene 75 km/h continuativamente per 5 secondi
                    if (speedAbove75Timer >= 5f)
                    {
                        speedBaselineCompleted = true;
                        Debug.Log($"DataLogger: Speed Baseline acquisita a {totalTrialOdometer:F0} metri.");
                    }
                }
                else
                {
                    // Se scende sotto i 75 prima di finire i 5 secondi, resetta il timer locale
                    speedAbove75Timer = 0f;
                    speedBaselineData.Clear();
                }

                // CONTROLLO FALLIMENTO: Se arriviamo a 1.4km senza aver completato la baseline
                if (totalTrialOdometer >= 1400f && !speedBaselineCompleted)
                {
                    trialValidityError = true;
                    Debug.Log("BASELINE_NOT_REACHED: L'utente non ha stabilizzato la velocità tra 1.0 e 1.4 km.");
                }
            }
        }

        // 2. SDLP BASELINE PRE-PORTALE (Km 1.2 - 1.5)
        if (sdlpBaselineActive)
        {
            sdlpBaselineData.Add(currentLateralPos);
        }
        
        // 3. LOGICA BRAKING LATENCY & THROTTLE (Da VMB_Readable in poi)
        if (latencyTimerActive)
        {
            brakingLatencyTimer += Time.fixedDeltaTime;

            // Raccogli 5 secondi di acceleratore post-readable (DV7)
            if (throttlePostReadableData.Count < 300) 
                throttlePostReadableData.Add(currentThrottle);

            // Calcolo DV1: Decelerazione sostenuta <= -0.5 m/s^2 per 500 ms (30 frame)
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
        }

        // 4. ODOMETRO POST-PORTALE (Km 2.0 in poi)
        if (postGantryActive)
        {
            distanceThisFrame = Vector3.Distance(carTransform.position, lastPosition);
            postGantryOdometer += distanceThisFrame;

            // Finestra 0 a +300m (DV4 SDLP)
            if (postGantryOdometer <= 300f)
            {
                sdlpPostGantryData.Add(currentLateralPos);
            }

            // Finestra 0 a +500m (DV2, DV5)
            if (postGantryOdometer <= 500f)
            {
                if (currentSpeedKmH < DV2_MinSpeed) DV2_MinSpeed = currentSpeedKmH;
                if (currentBrake > 0.1f) DV5_BrakeEngagement = 1; // Basta sfiorarlo (10%)
            }

            // Finestra 0 a +1000m (DV3, DV6)
            if (postGantryOdometer <= 1000f)
            {
                float baselineMeanSpeed = speedBaselineData.Count > 0 ? speedBaselineData.Average() : 80f;

                // DV3: Tempo trascorso sotto la velocità di baseline
                if (currentSpeedKmH < baselineMeanSpeed) DV3_DurationBelowBaseline += Time.fixedDeltaTime;

                // DV6: Recovery time (solo dopo aver raggiunto la velocità minima, deve tornare entro 5 km/h)
                if (DV6_SpeedRecoveryTime < 0 && postGantryOdometer > 500f) // Supponendo che il minimo si trovi nei primi 500m
                {
                    if (currentSpeedKmH >= (baselineMeanSpeed - 5f))
                    {
                        // In un sistema reale, dovresti avviare un timer dedicato dal momento esatto del raggiungimento della minSpeed
                        // Qui per semplicità calcoliamo il tempo totale da gantry
                        DV6_SpeedRecoveryTime = brakingLatencyTimer; // Approximation temporanea
                    }
                }
            }
        }

        lastPosition = carTransform.position;
        lastSpeedMs = currentSpeedMs;

        WriteTrialToCsv();
    }

    // --- METODI DEI TRIGGER ---
    public void Trigger_SDLPBaselineStart() { sdlpBaselineActive = true; }
    public void Trigger_VMBAppear() { sdlpBaselineActive = false; speedBaselineCompleted = true; }
    public void Trigger_VMBReadable() { latencyTimerActive = true; }
    public void Trigger_Gantry() { postGantryActive = true; postGantryOdometer = 0f; }

    // --- CALCOLO FINALE ED ESPORTAZIONE ---
    public void FineTrial(bool uscitaCorretta)
    {
        if (!isLoggingEnabled) return; // Sicurezza extra: non calcolare nulla se spento

        if (trialValidityError)
        {
            Debug.LogWarning("FINE TRIAL: Dati non salvati perché BASELINE_NOT_REACHED.");
            // Qui potresti decidere di loggare comunque il DV8 o scartare tutto
            StopLogging();
            return;
        }

        DV8_ExitCompliance = uscitaCorretta ? 1 : 0;

        // Calcolo DV4 (SDLP Change)
        float sdPre = CalculateStandardDeviation(sdlpBaselineData);
        float sdPost = CalculateStandardDeviation(sdlpPostGantryData);
        DV4_SDLP_Change = sdPost - sdPre;

        // Calcolo DV7 (Throttle Reduction)
        float avgThrottlePre = throttlePreReadableBuffer.Count > 0 ? throttlePreReadableBuffer.Average() : 0f;
        float avgThrottlePost = throttlePostReadableData.Count > 0 ? throttlePostReadableData.Average() : 0f;
        DV7_ThrottleReduction = avgThrottlePre - avgThrottlePost;

        // Stampa i risultati in Console per il test
        Debug.Log("=== RISULTATI TRIAL ===");
        Debug.Log($"DV1 Braking Latency: {DV1_BrakingLatency} s");
        Debug.Log($"DV2 Min Speed: {DV2_MinSpeed} km/h");
        Debug.Log($"DV3 Time Below Baseline: {DV3_DurationBelowBaseline} s");
        Debug.Log($"DV4 SDLP Change: {DV4_SDLP_Change} m");
        Debug.Log($"DV5 Brake Engaged: {DV5_BrakeEngagement}");
        Debug.Log($"DV6 Speed Recovery Time: {DV6_SpeedRecoveryTime} s");
        Debug.Log($"DV7 Throttle Reduction: {DV7_ThrottleReduction}%");
        Debug.Log($"DV8 Exit Compliance: {DV8_ExitCompliance}");
        Debug.Log("=======================");

        // IMPORTANT: persist this trial before resetting the logger.
        WriteTrialToCsv();

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
            Debug.LogError(
                "[DataLogger] Cannot start logging: participant ID is empty."
            );
            return;
        }

        if (participantGroup < 1 || participantGroup > 6)
        {
            Debug.LogError(
                "[DataLogger] Cannot start logging: invalid participant group."
            );
            return;
        }

        csvFilePath = BuildCsvFilePath();

        EnsureCsvFileExists();

        isLoggingEnabled = true;

        Debug.Log(
            $"DataLogger: Registrazione DATI INIZIATA. Trial {currentTrialIndex}"
        );
        Debug.Log($"DataLogger: CSV path = {csvFilePath}");
    }

    public void StopLogging()
    {
        isLoggingEnabled = false;
        ResetLogger();
        Debug.Log("DataLogger: Registrazione DATI FERMATA.");
    }


    public void ResetLogger()
    {
        // Pulisce tutto per il prossimo trial
        speedBaselineCompleted = false; sdlpBaselineActive = false; latencyTimerActive = false; postGantryActive = false;
        speedAbove75Timer = 0f; postGantryOdometer = 0f; brakingLatencyTimer = 0f; framesSustainedDeceleration = 0;
        speedBaselineData.Clear(); sdlpBaselineData.Clear(); sdlpPostGantryData.Clear();
        throttlePreReadableBuffer.Clear(); throttlePostReadableData.Clear();
        DV1_BrakingLatency = -1f; DV2_MinSpeed = float.MaxValue; DV3_DurationBelowBaseline = 0f; 
        DV4_SDLP_Change = 0f; DV5_BrakeEngagement = 0; DV6_SpeedRecoveryTime = -1f; DV7_ThrottleReduction = 0f;
        totalTrialOdometer = 0f;     // Fondamentale resettare l'odometro
        trialValidityError = false;  // Resetta lo stato di errore
    }

    // --- MATEMATICA E INPUT ---
    private float CalculateStandardDeviation(List<float> values)
    {
        if (values.Count < 2) return 0f;
        float avg = values.Average();
        float sumOfSquares = 0f;
        foreach (float v in values) sumOfSquares += Mathf.Pow(v - avg, 2);
        return Mathf.Sqrt(sumOfSquares / values.Count);
    }

    // --- MATEMATICA E INPUT ---
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

        Debug.Log($"DataLogger: Participant={participantId}, Group={participantGroup}");
        Debug.Log($"DataLogger: CSV={csvFilePath}");
    }

    private string BuildCsvFilePath()
    {
        string safeParticipantId = MakeSafeFileName(participantId);

        return Path.Combine(
            Application.persistentDataPath,
            $"car_simulator_{safeParticipantId}_{participantGroup}.csv"
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

    private void EnsureCsvFileExists()
    {
        if (File.Exists(csvFilePath))
            return;

        string header =
            "ParticipantID,Group,TrialIndex," +
            "DV1_BrakingLatency,DV2_MinSpeed,DV3_DurationBelowBaseline," +
            "DV4_SDLP_Change,DV5_BrakeEngagement,DV6_SpeedRecoveryTime," +
            "DV7_ThrottleReduction,DV8_ExitCompliance";

        using (FileStream stream = new FileStream(
            csvFilePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read))
        using (StreamWriter writer = new StreamWriter(stream))
        {
            writer.WriteLine(header);
            writer.Flush();
            stream.Flush(true);
        }
    }

    private void WriteTrialToCsv()
    {
        if (string.IsNullOrWhiteSpace(csvFilePath))
            csvFilePath = BuildCsvFilePath();

        string row = string.Join(",",
            CsvEscape(participantId),
            participantGroup.ToString(),
            currentTrialIndex.ToString(),
            DV1_BrakingLatency.ToString("F6"),
            DV2_MinSpeed.ToString("F6"),
            DV3_DurationBelowBaseline.ToString("F6"),
            DV4_SDLP_Change.ToString("F6"),
            DV5_BrakeEngagement.ToString(),
            DV6_SpeedRecoveryTime.ToString("F6"),
            DV7_ThrottleReduction.ToString("F6"),
            DV8_ExitCompliance.ToString()
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

            Debug.Log(
                $"DataLogger: Trial {currentTrialIndex} salvato in CSV."
            );
        }
        catch (System.Exception ex)
        {
            Debug.LogError(
                $"[DataLogger] Errore durante il salvataggio CSV: {ex}"
            );
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
        // REINSERITO IL CALCOLO DELLA SDLP LIVE CHE AVEVAMO SCRITTO PRIMA
        float sdPreLive = CalculateStandardDeviation(sdlpBaselineData);
        float sdPostLive = CalculateStandardDeviation(sdlpPostGantryData);
        float sdlpChangeLive = sdPostLive - sdPreLive;
        
        // Creiamo una stringa compatta per non intasare troppo la console
        string stato = $"[LIVE 5s] Odometro Post-Gantry: {postGantryOdometer:F1}m | ";
        
        // Aggiungiamo le DV che si aggiornano in tempo reale
        stato += $"DV1: {(DV1_BrakingLatency < 0 ? "In attesa" : DV1_BrakingLatency.ToString("F2") + "s")} | ";
        stato += $"DV2 (MinSpd): {(DV2_MinSpeed == float.MaxValue ? "-" : DV2_MinSpeed.ToString("F1") + "km/h")} | ";
        stato += $"DV3 (TimeBelow): {DV3_DurationBelowBaseline:F1}s | ";
        stato += $"Freno Toccato (DV5): {(DV5_BrakeEngagement == 1 ? "SI" : "NO")}";

        Debug.Log(stato);
    }
}