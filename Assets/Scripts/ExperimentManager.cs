using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using VehiclePhysics;
using UnityEngine.UI;

// --- STRUTTURE DATI ---
public enum ExperimentState { NotStarted, PracticeInstruction, PracticeDriving, InstructionScreen, Driving, MidSessionBreak, Finished }
public enum ConfigurazioneVMB { ABC, BCA } // ABC = Parola su Riga 1, BCA = Parola su Riga 3
public enum TipoParola { ATTENZIONE, RALLENTARE, CONTROLLO, TANG_NORD }

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
    [Tooltip("Punto in cui appare l'auto per il trial di FAMILIARIZZAZIONE")]
    public Transform puntoDiPartenzaPratica;
    [Tooltip("Punto in cui appare l'auto per i TRIAL SPERIMENTALI (1-16)")]
    public Transform puntoDiPartenzaSperimentale;

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
        if(currentState != ExperimentState.Driving ||
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
        NascondiPMV();

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

        float timer = 30f; 
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
        if(ambientePratica != null) ambientePratica.SetActive(true);
        if(ambienteSperimentale != null) ambienteSperimentale.SetActive(false); // Spegne AI e autostrada lunga

        
        // Spegniamo e puliamo tutti gli spawner per sicurezza
        foreach(var spawner in aiSpawners) {
            if(spawner != null) spawner.StopAndClearTraffic();
        }
        
        if (speedWarning != null) speedWarning.ResetWarningSystem();
        Debug.Log("Iniziato Trial di Familiarizzazione");

        PosizionaEVeicolo(puntoDiPartenzaPratica);
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

        // Pausa lunga di 3 minuti dopo il trial 8 (indice 8 è il 9° trial, quindi controlliamo se siamo appena usciti dal trial 8)
        if (currentTrialIndex == 8 && currentState != ExperimentState.MidSessionBreak)
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
        float timer = 30f;
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
        if(ambientePratica != null) ambientePratica.SetActive(false); // Spegne la strada di prova
        if(ambienteSperimentale != null) ambienteSperimentale.SetActive(true); // Accende tutto il sistema complesso

        
        foreach(var spawner in aiSpawners) {
            if(spawner != null) spawner.StartTraffic();
        }

        
        // if(aiSpawnManager != null) aiSpawnManager.StartTraffic(); // Fa nascere le macchine AI
        
        Debug.Log($"Iniziato Trial {currentTrialIndex + 1}");
        NascondiPMV();
        
        PosizionaEVeicolo(puntoDiPartenzaSperimentale);
        ImpostaVeicoloInPausa(false);

        // Attiva fisicamente il sistema di allarme velocità
        if (speedWarning != null) 
        {
            speedWarning.ResetWarningSystem();
            speedWarning.isSystemEnabled = true; // ACCENDI QUI
        }

        // Avviamo la registrazione dei dati SOLO nei trial sperimentali
        if (dataLogger != null)
            dataLogger.StartLogging(currentTrialIndex + 1);
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
    private void PosizionaEVeicolo(Transform punto)
    {
        if (veicoloGiocatore == null || punto == null)
            return;

        ImpostaVeicoloInPausa(true);

        veicoloGiocatore.transform.SetPositionAndRotation(
            punto.position,
            punto.rotation);

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
        pauseInstructionsText.text = "Please step out of the simulator.\nThe researcher will give you further instructions.";
        pauseNextExitText.text = "";
        pauseTimerText.text = "Waiting for researcher...";
        
        // Attende che chi gestisce il test prema Spazio per riprendere
        yield return new WaitUntil(() => Input.GetKeyDown(KeyCode.Space));
        StartCoroutine(FaseSchermataIstruzioni());
    }

    private void CompletaEsperimento()
    {
        ImpostaVeicoloInPausa(true);
        currentState = ExperimentState.Finished;
        pauseScreenCanvas.SetActive(true);
        pauseInstructionsText.text = "Experiment complete.\nThank you for your participation.";
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
                trialSequence.Add(NuovoStimolo(68, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "GHIACCIO", "PERICOLO", "COMASINA"));
                trialSequence.Add(NuovoStimolo(53, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "VIABILITA", "DIFFICOLTOSA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(27, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "RAFFICHE", "DI VENTO", "CORMANO"));
                trialSequence.Add(NuovoStimolo(41, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "CONTROLLO", "VELOCITÀ", "SESTO"));
                trialSequence.Add(NuovoStimolo(44, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "VENTO FORTE", "IN CORSO ORA", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(80, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "TRANSITO", "DIFFICILE", "GOBBA"));
                trialSequence.Add(NuovoStimolo(16, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "POSSIBILE", "GHIACCIO", "COMASINA"));
                trialSequence.Add(NuovoStimolo(9, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "RAFFICHE", "IMPROVVISE", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(95, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "TRAFFICO", "IRREGOLARE", "CORMANO"));
                trialSequence.Add(NuovoStimolo(42, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "PERICOLO", "SLITTAMENTO", "SESTO"));
                trialSequence.Add(NuovoStimolo(79, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "POSSIBILE", "OSTACOLO", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(12, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "POSSIBILI", "INCIDENTI", "GOBBA"));
                trialSequence.Add(NuovoStimolo(70, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "MANTENERE", "DISTANZA", "COMASINA"));
                trialSequence.Add(NuovoStimolo(58, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "CONTROLLI", "IN CORSO ORA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(8, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "SEGNALATI", "TAMPONAMENTI", "CORMANO"));
                trialSequence.Add(NuovoStimolo(25, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "CARREGGIATA", "SCIVOLOSA", "SESTO"));
                trialSequence.Add(NuovoStimolo(32, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "GHIACCIO", "A TRATTI", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(73, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "POSSIBILI", "ALLAGAMENTI", "GOBBA"));
                break;

            case 2:
                // GRUPPO 2
                trialSequence.Add(NuovoStimolo(68, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "GHIACCIO", "PERICOLO", "COMASINA"));
                trialSequence.Add(NuovoStimolo(53, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "VIABILITA", "DIFFICOLTOSA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(27, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "RAFFICHE", "DI VENTO", "CORMANO"));
                trialSequence.Add(NuovoStimolo(41, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "CONTROLLO", "VELOCITÀ", "SESTO"));
                trialSequence.Add(NuovoStimolo(44, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "VENTO FORTE", "IN CORSO ORA", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(80, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "TRANSITO", "DIFFICILE", "GOBBA"));
                trialSequence.Add(NuovoStimolo(16, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "POSSIBILE", "GHIACCIO", "COMASINA"));
                trialSequence.Add(NuovoStimolo(9, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "RAFFICHE", "IMPROVVISE", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(95, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "TRAFFICO", "IRREGOLARE", "CORMANO"));
                trialSequence.Add(NuovoStimolo(42, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "PERICOLO", "SLITTAMENTO", "SESTO"));
                trialSequence.Add(NuovoStimolo(79, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "POSSIBILE", "OSTACOLO", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(12, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "POSSIBILI", "INCIDENTI", "GOBBA"));
                trialSequence.Add(NuovoStimolo(70, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "MANTENERE", "DISTANZA", "COMASINA"));
                trialSequence.Add(NuovoStimolo(58, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "CONTROLLI", "IN CORSO ORA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(8, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "SEGNALATI", "TAMPONAMENTI", "CORMANO"));
                trialSequence.Add(NuovoStimolo(25, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "CARREGGIATA", "SCIVOLOSA", "SESTO"));
                trialSequence.Add(NuovoStimolo(32, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "GHIACCIO", "A TRATTI", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(73, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "POSSIBILI", "ALLAGAMENTI", "GOBBA"));
                break;

            case 3:
                // GRUPPO 3
                trialSequence.Add(NuovoStimolo(68, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "GHIACCIO", "PERICOLO", "COMASINA"));
                trialSequence.Add(NuovoStimolo(53, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "VIABILITA", "DIFFICOLTOSA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(27, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "RAFFICHE", "DI VENTO", "CORMANO"));
                trialSequence.Add(NuovoStimolo(41, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "CONTROLLO", "VELOCITÀ", "SESTO"));
                trialSequence.Add(NuovoStimolo(44, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "VENTO FORTE", "IN CORSO ORA", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(80, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "TRANSITO", "DIFFICILE", "GOBBA"));
                trialSequence.Add(NuovoStimolo(16, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "POSSIBILE", "GHIACCIO", "COMASINA"));
                trialSequence.Add(NuovoStimolo(9, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "RAFFICHE", "IMPROVVISE", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(95, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "TRAFFICO", "IRREGOLARE", "CORMANO"));
                trialSequence.Add(NuovoStimolo(42, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "PERICOLO", "SLITTAMENTO", "SESTO"));
                trialSequence.Add(NuovoStimolo(79, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "POSSIBILE", "OSTACOLO", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(12, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "POSSIBILI", "INCIDENTI", "GOBBA"));
                trialSequence.Add(NuovoStimolo(70, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "MANTENERE", "DISTANZA", "COMASINA"));
                trialSequence.Add(NuovoStimolo(58, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "CONTROLLI", "IN CORSO ORA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(8, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "SEGNALATI", "TAMPONAMENTI", "CORMANO"));
                trialSequence.Add(NuovoStimolo(25, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "CARREGGIATA", "SCIVOLOSA", "SESTO"));
                trialSequence.Add(NuovoStimolo(32, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "GHIACCIO", "A TRATTI", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(73, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "POSSIBILI", "ALLAGAMENTI", "GOBBA"));
                break;

            case 4:
                // GRUPPO 4
                trialSequence.Add(NuovoStimolo(68, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "GHIACCIO", "PERICOLO", "COMASINA"));
                trialSequence.Add(NuovoStimolo(53, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "VIABILITA", "DIFFICOLTOSA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(27, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "RAFFICHE", "DI VENTO", "CORMANO"));
                trialSequence.Add(NuovoStimolo(41, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "CONTROLLO", "VELOCITÀ", "SESTO"));
                trialSequence.Add(NuovoStimolo(44, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "VENTO FORTE", "IN CORSO ORA", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(80, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "TRANSITO", "DIFFICILE", "GOBBA"));
                trialSequence.Add(NuovoStimolo(16, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "POSSIBILE", "GHIACCIO", "COMASINA"));
                trialSequence.Add(NuovoStimolo(9, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "RAFFICHE", "IMPROVVISE", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(95, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "TRAFFICO", "IRREGOLARE", "CORMANO"));
                trialSequence.Add(NuovoStimolo(42, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "PERICOLO", "SLITTAMENTO", "SESTO"));
                trialSequence.Add(NuovoStimolo(79, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "POSSIBILE", "OSTACOLO", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(12, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "POSSIBILI", "INCIDENTI", "GOBBA"));
                trialSequence.Add(NuovoStimolo(70, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "MANTENERE", "DISTANZA", "COMASINA"));
                trialSequence.Add(NuovoStimolo(58, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "CONTROLLI", "IN CORSO ORA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(8, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "SEGNALATI", "TAMPONAMENTI", "CORMANO"));
                trialSequence.Add(NuovoStimolo(25, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "CARREGGIATA", "SCIVOLOSA", "SESTO"));
                trialSequence.Add(NuovoStimolo(32, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "GHIACCIO", "A TRATTI", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(73, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "POSSIBILI", "ALLAGAMENTI", "GOBBA"));
                break;

            case 5:
                // GRUPPO 5
                trialSequence.Add(NuovoStimolo(68, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "GHIACCIO", "PERICOLO", "COMASINA"));
                trialSequence.Add(NuovoStimolo(53, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "VIABILITA", "DIFFICOLTOSA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(27, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "RAFFICHE", "DI VENTO", "CORMANO"));
                trialSequence.Add(NuovoStimolo(41, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "CONTROLLO", "VELOCITÀ", "SESTO"));
                trialSequence.Add(NuovoStimolo(44, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "VENTO FORTE", "IN CORSO ORA", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(80, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "TRANSITO", "DIFFICILE", "GOBBA"));
                trialSequence.Add(NuovoStimolo(16, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "POSSIBILE", "GHIACCIO", "COMASINA"));
                trialSequence.Add(NuovoStimolo(9, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "RAFFICHE", "IMPROVVISE", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(95, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "TRAFFICO", "IRREGOLARE", "CORMANO"));
                trialSequence.Add(NuovoStimolo(42, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "PERICOLO", "SLITTAMENTO", "SESTO"));
                trialSequence.Add(NuovoStimolo(79, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "POSSIBILE", "OSTACOLO", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(12, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "POSSIBILI", "INCIDENTI", "GOBBA"));
                trialSequence.Add(NuovoStimolo(70, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "MANTENERE", "DISTANZA", "COMASINA"));
                trialSequence.Add(NuovoStimolo(58, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "CONTROLLI", "IN CORSO ORA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(8, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "SEGNALATI", "TAMPONAMENTI", "CORMANO"));
                trialSequence.Add(NuovoStimolo(25, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "CARREGGIATA", "SCIVOLOSA", "SESTO"));
                trialSequence.Add(NuovoStimolo(32, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "GHIACCIO", "A TRATTI", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(73, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "POSSIBILI", "ALLAGAMENTI", "GOBBA"));
                break;

            case 6:
                // GRUPPO 6
                trialSequence.Add(NuovoStimolo(68, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "GHIACCIO", "PERICOLO", "COMASINA"));
                trialSequence.Add(NuovoStimolo(53, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "VIABILITA", "DIFFICOLTOSA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(27, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "RAFFICHE", "DI VENTO", "CORMANO"));
                trialSequence.Add(NuovoStimolo(41, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "CONTROLLO", "VELOCITÀ", "SESTO"));
                trialSequence.Add(NuovoStimolo(44, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "VENTO FORTE", "IN CORSO ORA", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(80, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "TRANSITO", "DIFFICILE", "GOBBA"));
                trialSequence.Add(NuovoStimolo(16, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "POSSIBILE", "GHIACCIO", "COMASINA"));
                trialSequence.Add(NuovoStimolo(9, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "RAFFICHE", "IMPROVVISE", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(95, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "TRAFFICO", "IRREGOLARE", "CORMANO"));
                trialSequence.Add(NuovoStimolo(42, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "PERICOLO", "SLITTAMENTO", "SESTO"));
                trialSequence.Add(NuovoStimolo(79, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "POSSIBILE", "OSTACOLO", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(12, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "POSSIBILI", "INCIDENTI", "GOBBA"));
                trialSequence.Add(NuovoStimolo(70, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "MANTENERE", "DISTANZA", "COMASINA"));
                trialSequence.Add(NuovoStimolo(58, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "CONTROLLI", "IN CORSO ORA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(8, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "SEGNALATI", "TAMPONAMENTI", "CORMANO"));
                trialSequence.Add(NuovoStimolo(25, TipoParola.TANG_NORD, ConfigurazioneVMB.ABC, "CARREGGIATA", "SCIVOLOSA", "SESTO"));
                trialSequence.Add(NuovoStimolo(32, TipoParola.TANG_NORD, ConfigurazioneVMB.BCA, "GHIACCIO", "A TRATTI", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(73, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "POSSIBILI", "ALLAGAMENTI", "GOBBA"));
                break;
        }
        Debug.Log($"Caricata sequenza di {trialSequence.Count} trial per il Gruppo {gruppo}");
    }

    private StimoloVMB NuovoStimolo(int id, TipoParola tipo, ConfigurazioneVMB config, string riga2, string riga3, string uscita)
    {
        return new StimoloVMB { id = id, tipo = tipo, config = config, contestoRiga2 = riga2, contestoRiga3 = riga3, nomeUscita = uscita };
    }

    // --- GESTIONE FISICA DEI CARTELLI (TESTI) ---
    private void ApplicaStimoloAiCartelli(StimoloVMB stimolo)
    {
        string criticalWord = "";
        if (stimolo.tipo == TipoParola.ATTENZIONE) criticalWord = "ATTENZIONE";
        else if (stimolo.tipo == TipoParola.RALLENTARE) criticalWord = "RALLENTARE";
        else if (stimolo.tipo == TipoParola.CONTROLLO) criticalWord = "TANG. NORD";

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