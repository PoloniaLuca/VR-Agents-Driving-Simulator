using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

// --- STRUTTURE DATI ---
public enum ExperimentState { NotStarted, PracticeInstruction, PracticeDriving, InstructionScreen, Driving, MidSessionBreak, Finished }
public enum ConfigurazioneVMB { ABC, BCA } // ABC = Parola su Riga 1, BCA = Parola su Riga 3
public enum TipoParola { ATTENZIONE, RALLENTARE, CONTROLLO }

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
    [Tooltip("Inserire 1, 2, 3 o 4 in base al gruppo del Quadrato Latino")]
    [Range(1, 4)]
    public int gruppoPartecipante = 1;
    
    [Header("Stato Corrente (Sola Lettura)")]
    public ExperimentState currentState = ExperimentState.NotStarted;
    public int currentTrialIndex = 0;
    public List<StimoloVMB> trialSequence = new List<StimoloVMB>();

    [Header("Riferimenti Veicolo e Teletrasporto")]
    public GameObject veicoloGiocatore; 
    [Tooltip("Punto in cui appare l'auto per il trial di FAMILIARIZZAZIONE")]
    public Transform puntoDiPartenzaPratica; 
    [Tooltip("Punto in cui appare l'auto per i TRIAL SPERIMENTALI (1-16)")]
    public Transform puntoDiPartenzaSperimentale; 
    [Tooltip("Trascina qui lo script che gestisce volante e pedali")]
    public Behaviour scriptControlloAuto;

    [Header("Riferimenti DataLogger")]
    [Tooltip("Trascina qui l'oggetto che contiene lo script DataLogger")]
    public DataLogger dataLogger;

    [Header("Riferimenti UI 2D (Schermo Pausa)")]
    public GameObject pauseScreenCanvas; 
    public TextMeshProUGUI pauseInstructionsText;
    public TextMeshProUGUI pauseNextExitText;
    public TextMeshProUGUI pauseTimerText;

    [Header("Riferimenti Cartelli 3D (Nella Scena)")]
    public GameObject modelloFisicoPMV;
    public TextMeshProUGUI testoPMV_Riga1;
    public TextMeshProUGUI testoPMV_Riga2;
    public TextMeshProUGUI testoPMV_Riga3;
    public TextMeshProUGUI testoCartelloVerde_3km;
    public TextMeshProUGUI testoCartelloBianco_4_0km;


    public SpeedWarningSystem speedWarning; // Trascina qui l'oggetto dell'auto
    void Start()
    {
        // All'avvio, nascondiamo i testi del portale e carichiamo la matrice del gruppo scelto
        NascondiPMV();
        CaricaSequenzaGruppo(gruppoPartecipante);
        
        StartCoroutine(FasePraticaIstruzioni());
    }
    
    // --- FASE DI FAMILIARIZZAZIONE (TRIAL 0) ---
    private IEnumerator FasePraticaIstruzioni()
    {
        currentState = ExperimentState.PracticeInstruction;
        
        pauseScreenCanvas.SetActive(true);
        pauseInstructionsText.text = "PRACTICE TRIAL\nDrive in the right lane. Maintain ~80 km/h.\nTake the exit when it appears.";
        
        // Puoi mettere il nome dell'uscita che hai scritto fisicamente nei cartelli della pratica
        pauseNextExitText.text = "Next exit: <b>MONZA</b>"; 

        float timer = 2f; 
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
        
        if (speedWarning != null) speedWarning.ResetWarningSystem();
        Debug.Log("Iniziato Trial di Familiarizzazione");

        if (veicoloGiocatore != null && puntoDiPartenzaPratica != null)
        {
            veicoloGiocatore.transform.position = puntoDiPartenzaPratica.position;
            veicoloGiocatore.transform.rotation = puntoDiPartenzaPratica.rotation;
            AzzeraFisicaAuto();
        }

        if (scriptControlloAuto != null) scriptControlloAuto.enabled = true;
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
        pauseInstructionsText.text = "Drive in the right lane. Maintain ~80 km/h.\nTake the exit when it appears.\n<size=80%>(If your speed drops well below normal highway speed, you will hear a short beep as a reminder.).</size>";
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
        Debug.Log($"Iniziato Trial {currentTrialIndex + 1}");
        NascondiPMV();
        
        if (veicoloGiocatore != null && puntoDiPartenzaSperimentale != null)
        {
            veicoloGiocatore.transform.position = puntoDiPartenzaSperimentale.position;
            veicoloGiocatore.transform.rotation = puntoDiPartenzaSperimentale.rotation;
            AzzeraFisicaAuto();
        }

        if (scriptControlloAuto != null) scriptControlloAuto.enabled = true;

        // Attiva fisicamente il sistema di allarme velocità
        if (speedWarning != null) 
        {
            speedWarning.ResetWarningSystem();
            speedWarning.isSystemEnabled = true; // ACCENDI QUI
        }

        // Avviamo la registrazione dei dati SOLO nei trial sperimentali
        if (dataLogger != null) dataLogger.StartLogging();
    }

    // --- FUNZIONE DI FINE TRIAL UNIFICATA ---
    public void FineTrial()
    {
        if (scriptControlloAuto != null) scriptControlloAuto.enabled = false;

        
        if (speedWarning != null) 
        {
            speedWarning.isSystemEnabled = false; // SPEGNI QUI
            speedWarning.ResetWarningSystem();
        }

        if (currentState == ExperimentState.PracticeDriving)
        {
            Debug.Log("Terminato Trial di Familiarizzazione. Inizio sessione sperimentale.");
            StartCoroutine(SchermataFinePratica()); //  Avvia la schermata di attesa
        }
        else if (currentState == ExperimentState.Driving)
        {
            Debug.Log($"Terminato Trial {currentTrialIndex + 1}");

            // Assicuriamoci di fermare il logger alla fine del trial
            if (dataLogger != null) dataLogger.StopLogging();

            currentTrialIndex++;
            AvviaProssimoTrial();
        }
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

    // --- METODI DI SUPPORTO ---
    private void AzzeraFisicaAuto()
    {
        Rigidbody rb = veicoloGiocatore.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
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
                // Condizioni: Set A(ATT/1), Set B(ATT/3), Set C(RAL/1), Set D(RAL/3)
                trialSequence.Add(NuovoStimolo(80, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "TRANSITO", "DIFFICILE", "COMASINA"));
                trialSequence.Add(NuovoStimolo(95, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "TRAFFICO", "IRREGOLARE", "BICOCCA")); // Controllo fisso
                trialSequence.Add(NuovoStimolo(53, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "VIABILITA", "DIFFICOLTOSA", "CORMANO"));
                trialSequence.Add(NuovoStimolo(86, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "CODE LUNGHE", "IN AUMENTO", "SESTO"));
                trialSequence.Add(NuovoStimolo(14, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "AUTOMEZZO", "IN AVARIA", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(22, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "SEGNALETICA", "NON VALIDA", "GOBBA")); // Controllo fisso
                trialSequence.Add(NuovoStimolo(27, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "RAFFICHE", "DI VENTO", "COMASINA"));
                trialSequence.Add(NuovoStimolo(101, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "PRESENZA", "DI DETRITI", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(32, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "GHIACCIO", "A TRATTI", "CORMANO"));
                trialSequence.Add(NuovoStimolo(20, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "CANTIERE", "STRADALE", "SESTO"));
                trialSequence.Add(NuovoStimolo(4, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "TRASPORTO", "ECCEZIONALE", "SEGRATE")); // Controllo fisso
                trialSequence.Add(NuovoStimolo(94, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "MATERIALI", "DISPERSI", "GOBBA"));
                trialSequence.Add(NuovoStimolo(63, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "GUARDRAIL", "DANNEGGIATO", "COMASINA"));
                trialSequence.Add(NuovoStimolo(74, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "OSTACOLO", "IN STRADA", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(30, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "CODE INTENSE", "IN USCITA", "CORMANO")); // Controllo fisso
                trialSequence.Add(NuovoStimolo(33, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "RIDUZIONE", "DELLE CORSIE", "SESTO"));
                break;

            case 2:
                // GRUPPO 2 (Shift delle condizioni)
                trialSequence.Add(NuovoStimolo(86, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "CODE LUNGHE", "IN AUMENTO", "COMASINA"));
                trialSequence.Add(NuovoStimolo(95, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "TRAFFICO", "IRREGOLARE", "BICOCCA")); // Controllo fisso
                trialSequence.Add(NuovoStimolo(80, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "TRANSITO", "DIFFICILE", "CORMANO"));
                trialSequence.Add(NuovoStimolo(53, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "VIABILITA", "DIFFICOLTOSA", "SESTO"));
                trialSequence.Add(NuovoStimolo(14, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "AUTOMEZZO", "IN AVARIA", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(22, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "SEGNALETICA", "NON VALIDA", "GOBBA")); // Controllo fisso
                trialSequence.Add(NuovoStimolo(32, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "GHIACCIO", "A TRATTI", "COMASINA"));
                trialSequence.Add(NuovoStimolo(27, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "RAFFICHE", "DI VENTO", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(101, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "PRESENZA", "DI DETRITI", "CORMANO"));
                trialSequence.Add(NuovoStimolo(20, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "CANTIERE", "STRADALE", "SESTO"));
                trialSequence.Add(NuovoStimolo(4, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "TRASPORTO", "ECCEZIONALE", "SEGRATE")); // Controllo fisso
                trialSequence.Add(NuovoStimolo(74, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "OSTACOLO", "IN STRADA", "GOBBA"));
                trialSequence.Add(NuovoStimolo(94, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "MATERIALI", "DISPERSI", "COMASINA"));
                trialSequence.Add(NuovoStimolo(63, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "GUARDRAIL", "DANNEGGIATO", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(30, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "CODE INTENSE", "IN USCITA", "CORMANO")); // Controllo fisso
                trialSequence.Add(NuovoStimolo(33, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "RIDUZIONE", "DELLE CORSIE", "SESTO"));
                break;

            case 3:
                // GRUPPO 3 (Shift delle condizioni)
                trialSequence.Add(NuovoStimolo(53, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "VIABILITA", "DIFFICOLTOSA", "COMASINA"));
                trialSequence.Add(NuovoStimolo(95, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "TRAFFICO", "IRREGOLARE", "BICOCCA")); // Controllo fisso
                trialSequence.Add(NuovoStimolo(80, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "TRANSITO", "DIFFICILE", "CORMANO"));
                trialSequence.Add(NuovoStimolo(14, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "AUTOMEZZO", "IN AVARIA", "SESTO"));
                trialSequence.Add(NuovoStimolo(86, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "CODE LUNGHE", "IN AUMENTO", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(22, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "SEGNALETICA", "NON VALIDA", "GOBBA")); // Controllo fisso
                trialSequence.Add(NuovoStimolo(101, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "PRESENZA", "DI DETRITI", "COMASINA"));
                trialSequence.Add(NuovoStimolo(27, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "RAFFICHE", "DI VENTO", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(20, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "CANTIERE", "STRADALE", "CORMANO"));
                trialSequence.Add(NuovoStimolo(32, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "GHIACCIO", "A TRATTI", "SESTO"));
                trialSequence.Add(NuovoStimolo(4, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "TRASPORTO", "ECCEZIONALE", "SEGRATE")); // Controllo fisso
                trialSequence.Add(NuovoStimolo(63, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "GUARDRAIL", "DANNEGGIATO", "GOBBA"));
                trialSequence.Add(NuovoStimolo(94, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "MATERIALI", "DISPERSI", "COMASINA"));
                trialSequence.Add(NuovoStimolo(33, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "RIDUZIONE", "DELLE CORSIE", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(30, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "CODE INTENSE", "IN USCITA", "CORMANO")); // Controllo fisso
                trialSequence.Add(NuovoStimolo(74, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "OSTACOLO", "IN STRADA", "SESTO"));
                break;

            case 4:
                // GRUPPO 4 (Shift delle condizioni)
                trialSequence.Add(NuovoStimolo(14, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "AUTOMEZZO", "IN AVARIA", "COMASINA"));
                trialSequence.Add(NuovoStimolo(95, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "TRAFFICO", "IRREGOLARE", "BICOCCA")); // Controllo fisso
                trialSequence.Add(NuovoStimolo(86, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "CODE LUNGHE", "IN AUMENTO", "CORMANO"));
                trialSequence.Add(NuovoStimolo(80, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "TRANSITO", "DIFFICILE", "SESTO"));
                trialSequence.Add(NuovoStimolo(53, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "VIABILITA", "DIFFICOLTOSA", "SEGRATE"));
                trialSequence.Add(NuovoStimolo(22, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "SEGNALETICA", "NON VALIDA", "GOBBA")); // Controllo fisso
                trialSequence.Add(NuovoStimolo(20, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "CANTIERE", "STRADALE", "COMASINA"));
                trialSequence.Add(NuovoStimolo(32, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "GHIACCIO", "A TRATTI", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(27, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "RAFFICHE", "DI VENTO", "CORMANO"));
                trialSequence.Add(NuovoStimolo(101, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "PRESENZA", "DI DETRITI", "SESTO"));
                trialSequence.Add(NuovoStimolo(4, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "TRASPORTO", "ECCEZIONALE", "SEGRATE")); // Controllo fisso
                trialSequence.Add(NuovoStimolo(33, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "RIDUZIONE", "DELLE CORSIE", "GOBBA"));
                trialSequence.Add(NuovoStimolo(74, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "OSTACOLO", "IN STRADA", "COMASINA"));
                trialSequence.Add(NuovoStimolo(94, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "MATERIALI", "DISPERSI", "BICOCCA"));
                trialSequence.Add(NuovoStimolo(30, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "CODE INTENSE", "IN USCITA", "CORMANO")); // Controllo fisso
                trialSequence.Add(NuovoStimolo(63, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "GUARDRAIL", "DANNEGGIATO", "SESTO"));
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