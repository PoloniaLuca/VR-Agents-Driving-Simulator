using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

// --- STRUTTURE DATI ---
public enum ExperimentState { NotStarted, InstructionScreen, Driving, MidSessionBreak, Finished }
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

    [Header("Riferimenti Veicolo")]
    public GameObject veicoloGiocatore; 
    public Transform puntoDiPartenza; 
    [Tooltip("Trascina qui lo script che gestisce volante e pedali")]
    public Behaviour scriptControlloAuto;

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

    void Start()
    {
        // All'avvio, nascondiamo i testi del portale e carichiamo la matrice del gruppo scelto
        NascondiTestoPMV();
        CaricaSequenzaGruppo(gruppoPartecipante);
        AvviaProssimoTrial();
    }

    // --- GESTIONE DELLA MATRICE (IL PROTOCOLLO) ---
    private void CaricaSequenzaGruppo(int gruppo)
    {
        trialSequence.Clear();
        
        if (gruppo == 1)
        {
            // T1: Set A (ATTENZIONE / Line 1)
            trialSequence.Add(NuovoStimolo(80, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "TRANSITO", "DIFFICILE", "COMASINA"));
            // T2: CONTROL (TANG. NORD / Line 1) - Fisso al trial 2
            trialSequence.Add(NuovoStimolo(95, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "TRAFFICO", "IRREGOLARE", "BICOCCA"));
            // T3: Set C (RALLENTARE / Line 1) 
            trialSequence.Add(NuovoStimolo(53, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "VIABILITA", "DIFFICOLTOSA", "CORMANO"));
            // T4: Set B (ATTENZIONE / Line 3)
            trialSequence.Add(NuovoStimolo(86, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "CODE LUNGHE", "IN AUMENTO", "SESTO"));
            // T5: Set C (RALLENTARE / Line 1)
            trialSequence.Add(NuovoStimolo(101, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "PRESENZA", "DI DETRITI", "SEGRATE"));
            // T6: CONTROL (TANG. NORD / Line 1) - Fisso al trial 6
            trialSequence.Add(NuovoStimolo(22, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "SEGNALETICA", "NON VALIDA", "GOBBA"));
            // T7: Set A (ATTENZIONE / Line 1)
            trialSequence.Add(NuovoStimolo(27, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "RAFFICHE", "DI VENTO", "COMASINA"));
            // T8: Set C (RALLENTARE / Line 1)
            trialSequence.Add(NuovoStimolo(63, TipoParola.RALLENTARE, ConfigurazioneVMB.ABC, "GUARDRAIL", "DANNEGGIATO", "BICOCCA"));
            // T9: Set B (ATTENZIONE / Line 3)
            trialSequence.Add(NuovoStimolo(32, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "GHIACCIO", "A TRATTI", "CORMANO"));
            // T10: Set C (RALLENTARE / Line 3)
            trialSequence.Add(NuovoStimolo(33, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "RIDUZIONE", "DELLE CORSIE", "SESTO"));
            // T11: CONTROL (TANG. NORD / Line 1) - Fisso al trial 11
            trialSequence.Add(NuovoStimolo(4, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "TRASPORTO", "ECCEZIONALE", "SEGRATE"));
            // T12: Set A (ATTENZIONE / Line 1)
            trialSequence.Add(NuovoStimolo(94, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "MATERIALI", "DISPERSI", "GOBBA"));
            // T13: Set B (ATTENZIONE / Line 3)
            trialSequence.Add(NuovoStimolo(74, TipoParola.ATTENZIONE, ConfigurazioneVMB.BCA, "OSTACOLO", "IN STRADA", "COMASINA"));
            // T14: Set C (RALLENTARE / Line 3)
            trialSequence.Add(NuovoStimolo(33, TipoParola.RALLENTARE, ConfigurazioneVMB.BCA, "CANTIERE", "STRADALE", "BICOCCA"));
            // T15: CONTROL (TANG. NORD / Line 1) - Fisso al trial 15
            trialSequence.Add(NuovoStimolo(30, TipoParola.CONTROLLO, ConfigurazioneVMB.ABC, "CODE INTENSE", "IN USCITA", "CORMANO"));
            // T16: Set A (ATTENZIONE / Line 1)
            trialSequence.Add(NuovoStimolo(14, TipoParola.ATTENZIONE, ConfigurazioneVMB.ABC, "AUTOMEZZO", "IN AVARIA", "SESTO"));
        }
        // else if (gruppo == 2) { Da inserire anche gli altri gruppi }
        
        Debug.Log($"Caricata sequenza di {trialSequence.Count} trial per il Gruppo {gruppo}");
    }

    private StimoloVMB NuovoStimolo(int id, TipoParola tipo, ConfigurazioneVMB config, string riga2, string riga3, string uscita)
    {
        return new StimoloVMB { id = id, tipo = tipo, config = config, contestoRiga2 = riga2, contestoRiga3 = riga3, nomeUscita = uscita };
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
        pauseInstructionsText.text = "Drive in the right lane. Maintain ~80 km/h.\n       Take the exit when it appears.";
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
        IniziaGuida();
    }

    private void IniziaGuida()
    {
        currentState = ExperimentState.Driving;
        Debug.Log($"Iniziato Trial {currentTrialIndex + 1}");
        NascondiTestoPMV();
        if (veicoloGiocatore != null && puntoDiPartenza != null)
        {
            // 1. Teletrasporto al punto di partenza
            veicoloGiocatore.transform.position = puntoDiPartenza.position;
            veicoloGiocatore.transform.rotation = puntoDiPartenza.rotation;

            // 2. Azzeramento delle forze fisiche (standstill a 0 km/h)
            Rigidbody rb = veicoloGiocatore.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                
                // NOTA SUL ROLLING START: Nel caso vogliamo che l'auto parta a 40 km/h.
                // rb.velocity = veicoloGiocatore.transform.forward * 11.11f; // 11.11 m/s corrispondono a circa 40 km/h
            }
        }

        // 3. Sblocco comandi del simulatore
        if (scriptControlloAuto != null)
        {
            scriptControlloAuto.enabled = true;
        }
    }

    public void FineTrial()
    {
        if (currentState != ExperimentState.Driving) return;
        Debug.Log($"Terminato Trial {currentTrialIndex + 1}");

        // Blocca i comandi (volante e pedali) per evitare input accidentali durante lo schermo grigio
        if (scriptControlloAuto != null)
        {
            scriptControlloAuto.enabled = false;
        }
        currentTrialIndex++;
        AvviaProssimoTrial();
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

    private void NascondiTestoPMV()
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