using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using DrivingSim; 

public class DataLogger : MonoBehaviour
{
    [Header("Riferimenti Veicolo")]
    public Rigidbody carRigidbody;
    public Transform carTransform;
    // (Più avanti aggiungeremo il riferimento allo script dei pedali per leggere Freno e Acceleratore)
    public MonoBehaviour inputSource;
    
    private ICarInput carInput; // Interfaccia generica

    [Header("Riferimenti per DataLogger")]
    public CarController carController;

    [Header("Stato Finestre (Sola Lettura)")]
    public bool isLoggingEnabled = false;
    public bool speedBaselineCompleted = false;
    public bool sdlpBaselineActive = false;
    public bool latencyTimerActive = false;
    public bool postGantryActive = false;

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
        carInput = inputSource as ICarInput;
        if (carTransform != null) lastPosition = carTransform.position;
        
        // IMPERATIVO: Forza il motore fisico di Unity a 60 Hz per rispettare il protocollo (1/60 = 0.016666)
        Time.fixedDeltaTime = 1f / 60f; 
    }

    void FixedUpdate()
    {
        // Il logger si spegne completamente se non è abilitato (es. durante la Pratica)
        if (carRigidbody == null || !isLoggingEnabled) return;
        
        float currentSpeedMs = carRigidbody.linearVelocity.magnitude;
        float currentSpeedKmH = currentSpeedMs * 3.6f;
        float currentLateralPos = carTransform.position.x;
        float currentAcceleration = (currentSpeedMs - lastSpeedMs) / Time.fixedDeltaTime; // m/s^2
        float currentThrottle = GetThrottlePedal();
        float currentBrake = GetBrakePedal();

        // 1. SPEED BASELINE CONDIZIONALE (Senza Trigger Fisico)
        if (!speedBaselineCompleted)
        {
            if (currentSpeedKmH >= 75f)
            {
                speedAbove75Timer += Time.fixedDeltaTime;
                speedBaselineData.Add(currentSpeedKmH);

                // Se mantiene 75 km/h continuativamente per 5 secondi
                if (speedAbove75Timer >= 5f)
                {
                    speedBaselineCompleted = true;
                    Debug.Log("DataLogger: Speed Baseline di 5s acquisita con successo!");
                }
            }
            else
            {
                // Se la velocità scende sotto i 75 km/h, il protocollo richiede che sia "continuo", quindi resettiamo
                speedAbove75Timer = 0f;
                speedBaselineData.Clear();
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
            float distanceThisFrame = Vector3.Distance(carTransform.position, lastPosition);
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

        StopLogging();
    }

     // --- METODI PER CONTROLLARE IL LOGGER DALL'ESTERNO ---
    public void StartLogging()
    {
        ResetLogger();
        isLoggingEnabled = true;
        Debug.Log("DataLogger: Registrazione DATI INIZIATA.");
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
    private float GetThrottlePedal() { return carInput != null ? carInput.Throttle : 0f; }
    private float GetBrakePedal() { return carInput != null ? carInput.Brake : 0f; }


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