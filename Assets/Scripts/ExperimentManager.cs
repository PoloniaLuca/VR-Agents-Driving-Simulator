using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using VehiclePhysics;
using UnityEngine.UI;
using UnityEngine.Splines;
using Unity.Mathematics;

// --- STRUTTURE DATI ---
public enum ExperimentState { NotStarted, PracticeInstruction, PracticeDriving, InstructionScreen, Driving, MidSessionBreak, Finished }
public enum ConfigurazioneVMB { ABC, BCA } // ABC = Parola su Riga 1, BCA = Parola su Riga 3
public enum TipoParola { ATTENZIONE, RALLENTARE, AL_KM_30 }

[System.Serializable]
public class StimoloVMB
{
    public int id;
    public TipoParola tipo;
    public ConfigurazioneVMB config;
    public string contestoRiga2;
    public string contestoRiga3;
    public string nomeUscita;
}

public class ExperimentManager : MonoBehaviour
{
    [Header("Impostazioni Partecipante")]
    [Tooltip("ID univoco del partecipante; viene usato nel nome del CSV.")]
    public string participantId = "";

    [Tooltip("Gruppo del partecipante.")]
    [Range(1, 6)]
    public int gruppoPartecipante = 1;

    [Header("Stato Corrente (Sola Lettura)")]
    public ExperimentState currentState = ExperimentState.NotStarted;
    public int currentTrialIndex = 0;
    public List<StimoloVMB> trialSequence = new List<StimoloVMB>();

    [Header("VPP Veicolo Giocatore")]
    [Tooltip("Root GameObject del veicolo VPP del partecipante.")]
    public GameObject veicoloGiocatore;

    [Header("Punti di Partenza su Spline (500 m dall'inizio)")]
    [Tooltip("SplineContainer della pista di PRATICA.")]
    public SplineContainer splinePratica;
    [Tooltip("Indice della spline dentro il container di pratica.")]
    public int splineIndexPratica = 0;
    [Tooltip("Offset laterale [m] dalla center-line per la pratica.\nPositivo = sinistra del senso di marcia, negativo = destra.")]
    public float laneOffsetPratica = 12f;

    [Tooltip("SplineContainer della pista SPERIMENTALE.")]
    public SplineContainer splineSperimentale;
    [Tooltip("Indice della spline dentro il container sperimentale.")]
    public int splineIndexSperimentale = 0;
    [Tooltip("Offset laterale [m] dalla center-line per lo sperimentale.\nPositivo = sinistra del senso di marcia, negativo = destra.")]
    public float laneOffsetSperimentale = 12f;

    [Tooltip("Distanza [m] dall'inizio della spline in cui posizionare il veicolo.")]
    public float distanzaDiPartenza = 500f;

    [Header("Layout Autostrada (distanze dall'inizio della spline)")]
    [Tooltip("Posizione del gantry (VMB) lungo la spline [m]. Il DataLogger misura distance_to_gantry da qui.\nUsa Tools > Highway > Generate Triggers > 'Read gantry from PMV model' per ricavarla dal modello in scena.")]
    public float distanzaGantry = 2500f;
    [Tooltip("Il VMB compare a questa distanza PRIMA del gantry [m] (VMB_APPEAR).")]
    public float vmbAppareMetriPrimaDelGantry = 500f;
    [Tooltip("Il testo diventa leggibile a questa distanza PRIMA del gantry [m] (VMB_READABLE). Imposta il valore reale di leggibilita'.")]
    public float vmbLeggibileMetriPrimaDelGantry = 250f;
    [Tooltip("Inizio della finestra SDLP baseline, PRIMA del gantry [m] (finisce a VMB_APPEAR).")]
    public float sdlpBaselineMetriPrimaDelGantry = 800f;
    [Tooltip("Dove inizia la corsia di uscita lungo la spline [m]. Le finestre di risposta (DV2/DV5) vengono troncate qui.")]
    public float inizioCorsiaUscita = 4500f;

    [Header("Pausa Lunga")]
    [Tooltip("La pausa lunga (scendere dal simulatore) parte dopo questo numero di trial completati.")]
    public int pausaLungaDopoNTrial = 6;

    private VPVehicleController vppVehicle;

    [Header("Riferimenti DataLogger")]
    [Tooltip("Trascina qui l'oggetto che contiene lo script DataLogger")]
    public DataLogger dataLogger;

    [Header("Riferimenti UI 2D (Schermo Pausa)")]
    public GameObject pauseScreenCanvas;
    public TextMeshProUGUI pauseInstructionsText;
    public TextMeshProUGUI pauseNextExitText;
    public TextMeshProUGUI pauseTimerText;

    [Header("Schermata Input Partecipante")]
    public GameObject participantInputPanel;
    public TMP_InputField participantIdInput;
    public TMP_Dropdown participantGroupDropdown;
    public Button participantStartButton;
    public TextMeshProUGUI participantFeedbackText;
    [Tooltip("Testo informativo/consenso mostrato nella schermata iniziale.")]
    public TextMeshProUGUI participantInfoText;
    [Tooltip("Toggle che il partecipante deve spuntare per accettare le condizioni prima di poter avviare la simulazione.")]
    public Toggle participantAgreeToggle;
    [Tooltip("Messaggio mostrato se si prova ad avviare senza aver spuntato il toggle.")]
    public string messaggioToggleMancante = "Devi accettare le condizioni per continuare.";

    [Header("Riferimenti Cartelli 3D (Nella Scena)")]
    public GameObject modelloFisicoPMV;
    public TextMeshProUGUI testoPMV_Riga1;
    public TextMeshProUGUI testoPMV_Riga2;
    public TextMeshProUGUI testoPMV_Riga3;
    public TextMeshProUGUI testoCartelloVerde_3km;
    public TextMeshProUGUI testoCartelloBianco_4_0km;

    [Header("Ottimizzazione Performance")]
    public GameObject ambientePratica;      // Trascina qui il contenitore Pratica
    public GameObject ambienteSperimentale; // Trascina qui il contenitore Sperimentale

    [Header("Gestione Traffico AI")]
    [Tooltip("Trascina qui ENTRAMBI gli AISpawnManager (Nord e Sud)")]
    public List<DrivingSim.AISpawnManager> aiSpawners = new List<DrivingSim.AISpawnManager>();

    public SpeedWarningSystem speedWarning;

    [Header("Pausa Di Emergenza (Ricercatore)")]
    [Tooltip("Tasto che mette in pausa/riprende l'intera simulazione (fisica, AI, timer e logging).")]
    public KeyCode emergencyPauseKey = KeyCode.P;
    [Tooltip("Pannello opzionale mostrato mentre la simulazione è in pausa di emergenza.")]
    public GameObject emergencyPausePanel;
    [Tooltip("Testo opzionale mostrato nel pannello di pausa di emergenza.")]
    public TextMeshProUGUI emergencyPauseText;

    private bool isEmergencyPaused = false;
    private float timeScalePrimaDellaPausa = 1f;

    public bool IsEmergencyPaused => isEmergencyPaused;

    public VPVehicleController PlayerVehicle => vppVehicle;

    private void Awake()
    {
        CachePlayerVehicle();
    }

    private void Update()
    {
        if (Input.GetKeyDown(emergencyPauseKey))
        {
            ToggleEmergencyPause();
        }
    }

    private void OnDestroy()
    {
        // Safety net: never leave the editor/game stuck at timeScale 0
        // if this object is destroyed while paused.
        if (isEmergencyPaused)
            Time.timeScale = timeScalePrimaDellaPausa > 0f ? timeScalePrimaDellaPausa : 1f;
    }

    // --- PAUSA DI EMERGENZA (attivabile in qualsiasi momento dal ricercatore) ---

    public void ToggleEmergencyPause()
    {
        if (currentState != ExperimentState.Driving &&
            currentState != ExperimentState.PracticeDriving)
            return;
        if (isEmergencyPaused)
            RiprendiSimulazione();
        else
            MettiInPausaSimulazione();
    }

    public void MettiInPausaSimulazione()
    {
        if (isEmergencyPaused)
            return;

        isEmergencyPaused = true;
        timeScalePrimaDellaPausa = Time.timeScale;

        // Freezing timeScale stops physics (vehicle, AI traffic), any
        // coroutine timers driven by Time.deltaTime, and any Update/FixedUpdate
        // logic in the DataLogger that relies on scaled time.
        Time.timeScale = 0f;

        // Belt-and-braces: explicitly pause the player vehicle too, in case
        // it also reacts to real/unscaled input elsewhere.
        ImpostaVeicoloInPausa(true);

        if (emergencyPausePanel != null)
            emergencyPausePanel.SetActive(true);

        if (emergencyPauseText != null)
            emergencyPauseText.text = $"SIMULATION PAUSED\nPress '{emergencyPauseKey}' to resume";

        Debug.Log($"[ExperimentManager] Simulazione messa in PAUSA manualmente (tasto {emergencyPauseKey}).");

        // Confirmed: DataLogger drives all its per-frame tracking off
        // Time.fixedDeltaTime/Time.deltaTime (FixedUpdate + Update), so
        // Time.timeScale = 0 already freezes it completely — no explicit
        // Pause/Resume call needed on dataLogger.
    }

    public void RiprendiSimulazione()
    {
        if (!isEmergencyPaused)
            return;

        isEmergencyPaused = false;
        Time.timeScale = timeScalePrimaDellaPausa > 0f ? timeScalePrimaDellaPausa : 1f;

        // Only let the vehicle drive again if we were actually in a driving
        // phase; otherwise leave it paused as the normal flow expects.
        bool stavaGuidando =
            currentState == ExperimentState.Driving ||
            currentState == ExperimentState.PracticeDriving;

        ImpostaVeicoloInPausa(!stavaGuidando);

        if (emergencyPausePanel != null)
            emergencyPausePanel.SetActive(false);

        Debug.Log("[ExperimentManager] Simulazione RIPRESA.");
    }

    private void CachePlayerVehicle()
    {
        if (veicoloGiocatore == null)
        {
            Debug.LogError("[ExperimentManager] veicoloGiocatore non assegnato.", this);
            return;
        }

        vppVehicle = veicoloGiocatore.GetComponentInChildren<VPVehicleController>();
        if (vppVehicle == null)
            Debug.LogError("[ExperimentManager] Nessun VPVehicleController trovato nel veicolo giocatore.", this);
    }

    void Start()
    {
        // Keep the vehicle stopped until participant information is confirmed.
        ImpostaVeicoloInPausa(true);

        if (participantIdInput != null)
            participantIdInput.text = participantId;

        if (participantGroupDropdown != null &&
            participantGroupDropdown.options.Count > 0)
        {
            participantGroupDropdown.value = Mathf.Clamp(
                gruppoPartecipante - 1,
                0,
                participantGroupDropdown.options.Count - 1
            );

            participantGroupDropdown.RefreshShownValue();
        }

        if (participantStartButton != null)
        {
            participantStartButton.onClick.RemoveListener(
                ConfermaPartecipanteEAvvia
            );

            participantStartButton.onClick.AddListener(
                ConfermaPartecipanteEAvvia
            );
        }

        if (participantAgreeToggle != null)
        {
            participantAgreeToggle.onValueChanged.RemoveListener(
                OnAgreeToggleChanged
            );

            participantAgreeToggle.onValueChanged.AddListener(
                OnAgreeToggleChanged
            );
        }

        if (pauseScreenCanvas != null)
            pauseScreenCanvas.SetActive(true);

        MostraSchermataInputPartecipante();
    }

    // --- FASE DI INPUT ---

    private void MostraSchermataInputPartecipante()
    {
        if (participantInputPanel != null)
            participantInputPanel.SetActive(true);

        // At startup, only the participant input panel is visible.
        if (pauseInstructionsText != null)
            pauseInstructionsText.gameObject.SetActive(false);

        if (pauseNextExitText != null)
            pauseNextExitText.gameObject.SetActive(false);

        if (pauseTimerText != null)
            pauseTimerText.gameObject.SetActive(false);

        if (participantFeedbackText != null)
            participantFeedbackText.text = "";

        // Reset consent toggle every time the panel is (re)shown, and keep
        // the Start button locked until the participant checks it again.
        if (participantAgreeToggle != null)
        {
            participantAgreeToggle.isOn = false;
        }

        AggiornaStatoBottoneStart();
    }

    /// <summary>
    /// Chiamato quando il partecipante spunta/de-spunta il toggle di consenso.
    /// Abilita il bottone Start solo se il toggle è flaggato.
    /// </summary>
    private void OnAgreeToggleChanged(bool isOn)
    {
        AggiornaStatoBottoneStart();

        // Clear any previous "please check the box" warning as soon as the
        // participant ticks it.
        if (isOn && participantFeedbackText != null)
            participantFeedbackText.text = "";
    }

    private void AggiornaStatoBottoneStart()
    {
        if (participantStartButton == null)
            return;

        // If no toggle is assigned, don't block starting (keeps backward
        // compatibility with scenes that don't use the consent toggle).
        bool toggleOk = participantAgreeToggle == null || participantAgreeToggle.isOn;

        participantStartButton.interactable = toggleOk;
    }

    private void MostraTestiPausa()
    {
        if (pauseInstructionsText != null)
            pauseInstructionsText.gameObject.SetActive(true);

        if (pauseNextExitText != null)
            pauseNextExitText.gameObject.SetActive(true);

        if (pauseTimerText != null)
            pauseTimerText.gameObject.SetActive(true);
    }

    public void ConfermaPartecipanteEAvvia()
    {
        // Safety net: never start if the consent toggle exists and is not checked,
        // even if this method gets called through some other path than the button.
        if (participantAgreeToggle != null && !participantAgreeToggle.isOn)
        {
            MostraErroreInput(messaggioToggleMancante);
            return;
        }

        string id = participantIdInput != null
            ? participantIdInput.text.Trim()
            : participantId.Trim();

        int gruppo = gruppoPartecipante;

        if (participantGroupDropdown != null &&
            participantGroupDropdown.options.Count > 0)
        {
            // Dropdown is zero-based; experiment groups are 1-based.
            gruppo = participantGroupDropdown.value + 1;
        }

        // Validate ID.
        if (string.IsNullOrWhiteSpace(id))
        {
            MostraErroreInput("Inserisci un Participant ID.");
            return;
        }

        // Validate group.
        if (gruppo < 1 || gruppo > 6)
        {
            MostraErroreInput("Seleziona un gruppo da 1 a 6.");
            return;
        }

        // Store participant information.
        participantId = id;
        gruppoPartecipante = gruppo;

        Debug.Log($"participantId: {participantId}\ngruppoPartecipante: {gruppoPartecipante}");

        // Preserve existing trial-sequence logic.
        CaricaSequenzaGruppo(gruppoPartecipante);

        // Give the information to the logger.
        if (dataLogger != null)
        {
            dataLogger.SetParticipantInfo(
                participantId,
                gruppoPartecipante
            );
        }

        // Pre-build the road map used by the logger (distance_to_gantry / lane_offset).
        ConfiguraLoggerStrada();

        // Hide participant screen.
        if (participantInputPanel != null)
            participantInputPanel.SetActive(false);

        // Restore the normal pause-screen texts.
        MostraTestiPausa();

        // Continue with the existing experiment flow.
        StartCoroutine(FasePraticaIstruzioni());
    }

    private void MostraErroreInput(string messaggio)
    {
        if (participantFeedbackText != null)
            participantFeedbackText.text = messaggio;

        Debug.LogWarning("[ExperimentManager] " + messaggio);
    }

    // --- FASE DI FAMILIARIZZAZIONE (TRIAL 0) ---
    private IEnumerator FasePraticaIstruzioni()
    {
        currentState = ExperimentState.PracticeInstruction;

        pauseScreenCanvas.SetActive(true);
        MostraTestiPausa();

        pauseInstructionsText.text =
            "PRACTICE TRIAL\nDrive in the right lane. Maintain ~80 km/h.\nTake the exit when it appears.";

        // Puoi mettere il nome dell'uscita che hai scritto fisicamente nei cartelli della pratica
        pauseNextExitText.text = "Next exit: <b>MONZA</b>";

        float timer = 1f;
        while (timer > 0)
        {
            pauseTimerText.text = $"Starting practice in: {Mathf.Ceil(timer)}s";
            timer -= Time.deltaTime;
            yield return null;
        }

        pauseScreenCanvas.SetActive(false);
        IniziaGuidaPratica();
    }

    private void IniziaGuidaPratica()
    {
        currentState = ExperimentState.PracticeDriving;

        // --- OTTIMIZZAZIONE ---
        if (ambientePratica != null) ambientePratica.SetActive(true);
        if (ambienteSperimentale != null) ambienteSperimentale.SetActive(false); // Spegne AI e autostrada lunga

        // Spegniamo e puliamo tutti gli spawner per sicurezza
        foreach (var spawner in aiSpawners)
        {
            if (spawner != null) spawner.StopAndClearTraffic();
        }

        if (speedWarning != null) speedWarning.ResetWarningSystem();
        Debug.Log("Iniziato Trial di Familiarizzazione");

        // Posiziona il veicolo 500m lungo la spline di pratica, offsettato lateralmente
        GetPoseOnSpline(
            splinePratica,
            splineIndexPratica,
            distanzaDiPartenza,
            laneOffsetPratica,
            out Vector3 pos,
            out Quaternion rot);

        PosizionaEVeicolo(pos, rot);
        ImpostaVeicoloInPausa(false);
    }

    // --- MACCHINA A STATI E FLUSSO ---
    public void AvviaProssimoTrial()
    {
        if (currentTrialIndex >= trialSequence.Count)
        {
            CompletaEsperimento();
            return;
        }

        // Pausa lunga dopo N trial completati (con 12 trial: dopo il 6°, cioe' a meta' sessione)
        if (currentTrialIndex == pausaLungaDopoNTrial && currentState != ExperimentState.MidSessionBreak)
        {
            StartCoroutine(PausaLungaGSS());
            return;
        }

        StartCoroutine(FaseSchermataIstruzioni());
    }

    private IEnumerator FaseSchermataIstruzioni()
    {
        currentState = ExperimentState.InstructionScreen;
        StimoloVMB trialAttuale = trialSequence[currentTrialIndex];

        // Setup Schermo Grigio 2D
        pauseScreenCanvas.SetActive(true);
        MostraTestiPausa();

        pauseInstructionsText.text =
            "Drive in the right lane. Maintain ~80 km/h.\nTake the exit when it appears.\n<size=80%>(If your speed drops well below normal highway speed, you will hear a short beep as a reminder.).</size>";
        pauseNextExitText.text = $"Next exit: <b>{trialAttuale.nomeUscita}</b>";

        // Compila fisicamente i cartelli 3D in background
        ApplicaStimoloAiCartelli(trialAttuale);

        // Timer di 30 secondi
        float timer = 1f;
        while (timer > 0)
        {
            pauseTimerText.text = $"Starting in: {Mathf.Ceil(timer)}s";
            timer -= Time.deltaTime;
            yield return null;
        }

        pauseScreenCanvas.SetActive(false);
        IniziaGuidaSperimentale();
    }

    private void IniziaGuidaSperimentale()
    {
        currentState = ExperimentState.Driving;

        // --- OTTIMIZZAZIONE ---
        if (ambientePratica != null) ambientePratica.SetActive(false); // Spegne la strada di prova
        if (ambienteSperimentale != null) ambienteSperimentale.SetActive(true); // Accende tutto il sistema complesso

        foreach (var spawner in aiSpawners)
        {
            if (spawner != null) spawner.StartTraffic();
        }

        Debug.Log($"Iniziato Trial {currentTrialIndex + 1}");

        // Posiziona il veicolo 500m lungo la spline sperimentale, offsettato lateralmente
        GetPoseOnSpline(
            splineSperimentale,
            splineIndexSperimentale,
            distanzaDiPartenza,
            laneOffsetSperimentale,
            out Vector3 pos,
            out Quaternion rot);

        PosizionaEVeicolo(pos, rot);
        ImpostaVeicoloInPausa(false);

        // Attiva fisicamente il sistema di allarme velocità
        if (speedWarning != null)
        {
            speedWarning.ResetWarningSystem();
            speedWarning.isSystemEnabled = true; // ACCENDI QUI
        }

        // Avviamo la registrazione dei dati SOLO nei trial sperimentali
        if (dataLogger != null)
        {
            ConfiguraLoggerStrada();

            StimoloVMB trial = trialSequence[currentTrialIndex];
            dataLogger.SetTrialInfo(
                trial.id,
                EtichettaTipoParola(trial.tipo),
                trial.config == ConfigurazioneVMB.ABC ? "Line 1" : "Line 3",
                trial.nomeUscita);

            dataLogger.StartLogging(currentTrialIndex + 1);
        }
    }

    // --- FUNZIONE DI FINE TRIAL UNIFICATA ---
    public void FineTrial()
    {
        ImpostaVeicoloInPausa(true);

        if (speedWarning != null)
        {
            speedWarning.isSystemEnabled = false;
            speedWarning.ResetWarningSystem();
        }

        if (currentState == ExperimentState.PracticeDriving)
        {
            Debug.Log("Terminato Trial di Familiarizzazione. Inizio sessione sperimentale.");
            StartCoroutine(SchermataFinePratica());
        }
        else if (currentState == ExperimentState.Driving)
        {
            Debug.Log($"Terminato Trial {currentTrialIndex + 1}");

            if (dataLogger != null) dataLogger.StopLogging();

            currentTrialIndex++;
            AvviaProssimoTrial();
        }

        // Defer traffic/environment teardown to the next frame,
        // since we're still inside a physics trigger callback here.
        StartCoroutine(FineTrialDeferredCleanup());
    }

    private IEnumerator FineTrialDeferredCleanup()
    {
        yield return null; // wait until the current physics callback has finished

        foreach (var spawner in aiSpawners)
        {
            if (spawner != null) spawner.StopAndClearTraffic();
        }

        if (ambienteSperimentale != null) ambienteSperimentale.SetActive(false);
    }

    private IEnumerator SchermataFinePratica()
    {
        // Usiamo uno stato di pausa generico
        currentState = ExperimentState.InstructionScreen;

        pauseScreenCanvas.SetActive(true);
        pauseInstructionsText.text = "Practice complete.\nPlease let the researcher know if you are ready.";
        pauseNextExitText.text = ""; // Nascondiamo l'uscita per ora
        pauseTimerText.text = "Researcher: Press SPACE to start the experiment...";

        // Il sistema si "congela" finché non premi la barra spaziatrice
        yield return new WaitUntil(() => Input.GetKeyDown(KeyCode.Space));

        // Una volta premuto, avviamo regolarmente il Trial 1 (che farà partire i suoi 30 secondi)
        AvviaProssimoTrial();
    }

    // --- METODI DI SUPPORTO VPP ---

    /// <summary>
    /// Calcola posizione e rotazione a <paramref name="distanceMetres"/> metri
    /// lungo la spline, offsettate lateralmente lungo la normale orizzontale.
    /// </summary>
    private void GetPoseOnSpline(
        SplineContainer container,
        int splineIdx,
        float distanceMetres,
        float lateralOffset,
        out Vector3 worldPos,
        out Quaternion worldRot)
    {
        worldPos = Vector3.zero;
        worldRot = Quaternion.identity;

        if (container == null)
        {
            Debug.LogError("[ExperimentManager] SplineContainer non assegnato.", this);
            return;
        }

        Spline spline = container.Splines[splineIdx];
        float splineLength = spline.GetLength();

        // Clamp per non superare la fine della spline
        float clampedDist = Mathf.Clamp(distanceMetres, 0f, splineLength);

        // In QUESTA versione di Splines:
        //   - ritorna: posizione locale (float3)
        //   - 4° out param: t normalizzato (float)
        float3 localPos = SplineUtility.GetPointAtLinearDistance(
            spline, 0f, clampedDist, out float t);

        // Tangente locale a quel t
        float3 localTangent = spline.EvaluateTangent(t);

        Vector3 worldTangent = container.transform
            .TransformDirection((Vector3)localTangent).normalized;

        // Normale orizzontale (sinistra del senso di marcia, stessa convenzione di AICarInput)
        Vector3 worldNormal = Vector3.Cross(Vector3.up, worldTangent).normalized;

        // Posizione mondo + offset laterale
        Vector3 worldCenter = container.transform.TransformPoint((Vector3)localPos);
        worldPos = worldCenter + worldNormal * lateralOffset;

        // Rotazione allineata alla tangente della spline (muso in avanti)
        if (worldTangent.sqrMagnitude > 0.001f)
            worldRot = Quaternion.LookRotation(worldTangent, Vector3.up);
    }

    /// <summary>
    /// Vecchia API: posiziona il veicolo a partire da un Transform.
    /// Mantenuta per retrocompatibilità con altri call-site.
    /// </summary>
    private void PosizionaEVeicolo(Transform punto)
    {
        if (punto == null)
            return;

        PosizionaEVeicolo(punto.position, punto.rotation);
    }

    /// <summary>
    /// Posiziona il veicolo a partire da posizione e rotazione mondo.
    /// </summary>
    private void PosizionaEVeicolo(Vector3 posizione, Quaternion rotazione)
    {
        if (veicoloGiocatore == null)
            return;

        ImpostaVeicoloInPausa(true);

        veicoloGiocatore.transform.SetPositionAndRotation(posizione, rotazione);

        Rigidbody rb = veicoloGiocatore.GetComponentInChildren<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.Sleep();
        }

        ImpostaVeicoloInPausa(false);
    }

    private void ImpostaVeicoloInPausa(bool pausa)
    {
        if (vppVehicle != null)
            vppVehicle.paused = pausa;
    }

    public bool IsPlayerVehicle(Collider other)
    {
        if (other == null || vppVehicle == null)
            return false;

        return other.GetComponentInParent<VPVehicleController>() == vppVehicle;
    }

    private IEnumerator PausaLungaGSS()
    {
        currentState = ExperimentState.MidSessionBreak;
        pauseScreenCanvas.SetActive(true);
        pauseInstructionsText.text = "Per favore scendi dal simulatore.\nIl ricercatore ti dara' le informazioni.";
        pauseNextExitText.text = "";
        pauseTimerText.text = "Aspetta il ricercatore...";

        // Attende che chi gestisce il test prema Spazio per riprendere
        yield return new WaitUntil(() => Input.GetKeyDown(KeyCode.Space));
        StartCoroutine(FaseSchermataIstruzioni());
    }

    private void CompletaEsperimento()
    {
        ImpostaVeicoloInPausa(true);
        currentState = ExperimentState.Finished;
        pauseScreenCanvas.SetActive(true);
        pauseInstructionsText.text = "Esperimento completato.\nGrazie per aver partecipato.";
        pauseNextExitText.text = "";
        pauseTimerText.text = "";
    }

    // --- GESTIONE DELLA MATRICE (IL PROTOCOLLO) ---
    private void CaricaSequenzaGruppo(int gruppo)
    {
        trialSequence.Clear();

        switch (gruppo)
        {
            case 1:
                // GRUPPO 1
                trialSequence.Add(NuovoStimolo(11, TipoParola.AL_KM_30, ConfigurazioneVMB.ABC, "AUTOMEZZO", "IN AVARIA", "COMASINA"));
                trialSequence.Add(NuovoStimolo(4, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "SEGNALATI", "TAMPONAMENTI", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(10, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "INCENDIO", "IN GALLERIA", "CORMANO"));
                trialSequence.Add(NuovoStimolo(7, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "CANTIERE IN", "MOVIMENTO", "SESTO"));
                trialSequence.Add(NuovoStimolo(6, TipoParola.AL_KM_30, ConfigurazioneVMB.BCA, "RAFFICHE", "DI VENTO", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(2, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "OSTACOLO", "IN STRADA", "GOBBA"));
                trialSequence.Add(NuovoStimolo(5, TipoParola.AL_KM_30, ConfigurazioneVMB.ABC, "TRAFFICO", "IRREGOLARE", "COMASINA"));
                trialSequence.Add(NuovoStimolo(3, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "VIABILITA", "DIFFICOLTOSA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(12, TipoParola.AL_KM_30, ConfigurazioneVMB.BCA, "SEGNALETICA", "PROVVISORIA", "CORMANO"));
                trialSequence.Add(NuovoStimolo(9, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "MATERIALI", "DISPERSI", "SESTO"));
                trialSequence.Add(NuovoStimolo(1, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "LAVORATORI", "IN CORSIA", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(8, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "GUARDRAIL", "DANNEGGIATO", "GOBBA"));
                break;

            case 2:
                // GRUPPO 2
                trialSequence.Add(NuovoStimolo(12, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "SEGNALETICA", "PROVVISORIA", "COMASINA"));
                trialSequence.Add(NuovoStimolo(6, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "RAFFICHE", "DI VENTO", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(5, TipoParola.AL_KM_30, ConfigurazioneVMB.BCA, "TRAFFICO", "IRREGOLARE", "CORMANO"));
                trialSequence.Add(NuovoStimolo(8, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "GUARDRAIL", "DANNEGGIATO", "SESTO"));
                trialSequence.Add(NuovoStimolo(4, TipoParola.AL_KM_30, ConfigurazioneVMB.ABC, "SEGNALATI", "TAMPONAMENTI", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(11, TipoParola.AL_KM_30, ConfigurazioneVMB.BCA, "AUTOMEZZO", "IN AVARIA", "GOBBA"));
                trialSequence.Add(NuovoStimolo(3, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "VIABILITA", "DIFFICOLTOSA", "COMASINA"));
                trialSequence.Add(NuovoStimolo(10, TipoParola.AL_KM_30, ConfigurazioneVMB.ABC, "INCENDIO", "IN GALLERIA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(7, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "CANTIERE IN", "MOVIMENTO", "CORMANO"));
                trialSequence.Add(NuovoStimolo(2, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "OSTACOLO", "IN STRADA", "SESTO"));
                trialSequence.Add(NuovoStimolo(9, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "MATERIALI", "DISPERSI", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(1, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "LAVORATORI", "IN CORSIA", "GOBBA"));
                break;

            case 3:
                // GRUPPO 3
                trialSequence.Add(NuovoStimolo(12, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "SEGNALETICA", "PROVVISORIA", "COMASINA"));
                trialSequence.Add(NuovoStimolo(3, TipoParola.AL_KM_30, ConfigurazioneVMB.ABC, "VIABILITA", "DIFFICOLTOSA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(8, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "GUARDRAIL", "DANNEGGIATO", "CORMANO"));
                trialSequence.Add(NuovoStimolo(5, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "TRAFFICO", "IRREGOLARE", "SESTO"));
                trialSequence.Add(NuovoStimolo(2, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "OSTACOLO", "IN STRADA", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(11, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "AUTOMEZZO", "IN AVARIA", "GOBBA"));
                trialSequence.Add(NuovoStimolo(7, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "CANTIERE IN", "MOVIMENTO", "COMASINA"));
                trialSequence.Add(NuovoStimolo(4, TipoParola.AL_KM_30, ConfigurazioneVMB.BCA, "SEGNALATI", "TAMPONAMENTI", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(1, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "LAVORATORI", "IN CORSIA", "CORMANO"));
                trialSequence.Add(NuovoStimolo(9, TipoParola.AL_KM_30, ConfigurazioneVMB.ABC, "MATERIALI", "DISPERSI", "SESTO"));
                trialSequence.Add(NuovoStimolo(6, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "RAFFICHE", "DI VENTO", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(10, TipoParola.AL_KM_30, ConfigurazioneVMB.BCA, "INCENDIO", "IN GALLERIA", "GOBBA"));
                break;

            case 4:
                // GRUPPO 4
                trialSequence.Add(NuovoStimolo(10, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "INCENDIO", "IN GALLERIA", "COMASINA"));
                trialSequence.Add(NuovoStimolo(3, TipoParola.AL_KM_30, ConfigurazioneVMB.BCA, "VIABILITA", "DIFFICOLTOSA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(7, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "CANTIERE IN", "MOVIMENTO", "CORMANO"));
                trialSequence.Add(NuovoStimolo(2, TipoParola.AL_KM_30, ConfigurazioneVMB.ABC, "OSTACOLO", "IN STRADA", "SESTO"));
                trialSequence.Add(NuovoStimolo(9, TipoParola.AL_KM_30, ConfigurazioneVMB.BCA, "MATERIALI", "DISPERSI", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(4, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "SEGNALATI", "TAMPONAMENTI", "GOBBA"));
                trialSequence.Add(NuovoStimolo(1, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "LAVORATORI", "IN CORSIA", "COMASINA"));
                trialSequence.Add(NuovoStimolo(12, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "SEGNALETICA", "PROVVISORIA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(8, TipoParola.AL_KM_30, ConfigurazioneVMB.ABC, "GUARDRAIL", "DANNEGGIATO", "CORMANO"));
                trialSequence.Add(NuovoStimolo(5, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "TRAFFICO", "IRREGOLARE", "SESTO"));
                trialSequence.Add(NuovoStimolo(11, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "AUTOMEZZO", "IN AVARIA", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(6, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "RAFFICHE", "DI VENTO", "GOBBA"));
                break;

            case 5:
                // GRUPPO 5
                trialSequence.Add(NuovoStimolo(9, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "MATERIALI", "DISPERSI", "COMASINA"));
                trialSequence.Add(NuovoStimolo(8, TipoParola.AL_KM_30, ConfigurazioneVMB.BCA, "GUARDRAIL", "DANNEGGIATO", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(11, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "AUTOMEZZO", "IN AVARIA", "CORMANO"));
                trialSequence.Add(NuovoStimolo(10, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "INCENDIO", "IN GALLERIA", "SESTO"));
                trialSequence.Add(NuovoStimolo(7, TipoParola.AL_KM_30, ConfigurazioneVMB.ABC, "CANTIERE IN", "MOVIMENTO", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(5, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "TRAFFICO", "IRREGOLARE", "GOBBA"));
                trialSequence.Add(NuovoStimolo(2, TipoParola.AL_KM_30, ConfigurazioneVMB.BCA, "OSTACOLO", "IN STRADA", "COMASINA"));
                trialSequence.Add(NuovoStimolo(4, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "SEGNALATI", "TAMPONAMENTI", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(1, TipoParola.AL_KM_30, ConfigurazioneVMB.ABC, "LAVORATORI", "IN CORSIA", "CORMANO"));
                trialSequence.Add(NuovoStimolo(6, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "RAFFICHE", "DI VENTO", "SESTO"));
                trialSequence.Add(NuovoStimolo(12, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "SEGNALETICA", "PROVVISORIA", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(3, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "VIABILITA", "DIFFICOLTOSA", "GOBBA"));
                break;

            case 6:
                // GRUPPO 6
                trialSequence.Add(NuovoStimolo(6, TipoParola.AL_KM_30, ConfigurazioneVMB.ABC, "RAFFICHE", "DI VENTO", "COMASINA"));
                trialSequence.Add(NuovoStimolo(8, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "GUARDRAIL", "DANNEGGIATO", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(11, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "AUTOMEZZO", "IN AVARIA", "CORMANO"));
                trialSequence.Add(NuovoStimolo(1, TipoParola.AL_KM_30, ConfigurazioneVMB.BCA, "LAVORATORI", "IN CORSIA", "SESTO"));
                trialSequence.Add(NuovoStimolo(4, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "SEGNALATI", "TAMPONAMENTI", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(9, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "MATERIALI", "DISPERSI", "GOBBA"));
                trialSequence.Add(NuovoStimolo(12, TipoParola.AL_KM_30, ConfigurazioneVMB.ABC, "SEGNALETICA", "PROVVISORIA", "COMASINA"));
                trialSequence.Add(NuovoStimolo(10, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "INCENDIO", "IN GALLERIA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(3, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "VIABILITA", "DIFFICOLTOSA", "CORMANO"));
                trialSequence.Add(NuovoStimolo(2, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "OSTACOLO", "IN STRADA", "SESTO"));
                trialSequence.Add(NuovoStimolo(7, TipoParola.AL_KM_30, ConfigurazioneVMB.BCA, "CANTIERE IN", "MOVIMENTO", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(5, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "TRAFFICO", "IRREGOLARE", "GOBBA"));
                break;
        }
        Debug.Log($"Caricata sequenza di {trialSequence.Count} trial per il Gruppo {gruppo}");
    }

    private StimoloVMB NuovoStimolo(int id, TipoParola tipo, ConfigurazioneVMB config, string riga2, string riga3, string uscita)
    {
        return new StimoloVMB { id = id, tipo = tipo, config = config, contestoRiga2 = riga2, contestoRiga3 = riga3, nomeUscita = uscita };
    }

    private void ConfiguraLoggerStrada()
    {
        if (dataLogger == null || splineSperimentale == null)
            return;

        dataLogger.ConfigureRoad(
            splineSperimentale,
            splineIndexSperimentale,
            laneOffsetSperimentale,
            distanzaGantry,
            inizioCorsiaUscita);
    }

    // Etichette usate nel CSV (stesse dell'Excel: Neutral / Action / Warning)
    private static string EtichettaTipoParola(TipoParola tipo)
    {
        switch (tipo)
        {
            case TipoParola.AL_KM_30: return "Neutral";
            case TipoParola.RALLENTARE: return "Action";
            case TipoParola.ATTENZIONE: return "Warning";
            default: return tipo.ToString();
        }
    }

    // --- GESTIONE FISICA DEI CARTELLI (TESTI) ---
    private void ApplicaStimoloAiCartelli(StimoloVMB stimolo)
    {
        string criticalWord = "";
        if (stimolo.tipo == TipoParola.ATTENZIONE) criticalWord = "ATTENZIONE";
        else if (stimolo.tipo == TipoParola.RALLENTARE) criticalWord = "RALLENTARE";
        else if (stimolo.tipo == TipoParola.AL_KM_30) criticalWord = "AL KM 30";

        if (stimolo.config == ConfigurazioneVMB.ABC)
        {
            testoPMV_Riga1.text = criticalWord;
            testoPMV_Riga2.text = stimolo.contestoRiga2;
            testoPMV_Riga3.text = stimolo.contestoRiga3;
        }
        else // BCA
        {
            testoPMV_Riga1.text = stimolo.contestoRiga2;
            testoPMV_Riga2.text = stimolo.contestoRiga3;
            testoPMV_Riga3.text = criticalWord;
        }

        testoCartelloVerde_3km.text = $"{stimolo.nomeUscita}";
        testoCartelloBianco_4_0km.text = stimolo.nomeUscita;
    }

    public void Trigger_Km_1_5()
    {
        if (modelloFisicoPMV != null)
        {
            modelloFisicoPMV.SetActive(true);
        }

        testoPMV_Riga1.gameObject.SetActive(true);
        testoPMV_Riga2.gameObject.SetActive(true);
        testoPMV_Riga3.gameObject.SetActive(true);
    }

    private void NascondiPMV()
    {
        if (modelloFisicoPMV != null)
        {
            modelloFisicoPMV.SetActive(false);
        }

        testoPMV_Riga1.gameObject.SetActive(false);
        testoPMV_Riga2.gameObject.SetActive(false);
        testoPMV_Riga3.gameObject.SetActive(false);
    }
}