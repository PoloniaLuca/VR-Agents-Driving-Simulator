using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class HighwayTrigger : MonoBehaviour
{
    public enum TriggerType { SDLP, ShowPMV, PMV_Readable, PMV, FineTrial, UscitaMancata }
    public TriggerType type;
    
    private ExperimentManager manager;
    private DataLogger dataLogger;

    void Start()
    {
        manager = FindFirstObjectByType<ExperimentManager>();
        dataLogger = FindFirstObjectByType<DataLogger>();

        GetComponent<BoxCollider>().isTrigger = true; 
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            switch (type)
            {
                case TriggerType.SDLP:
                    if (dataLogger != null) dataLogger.Trigger_SDLPBaselineStart();
                    break;

                case TriggerType.ShowPMV:
                    // Questo trigger fa due cose: mostra il testo visivamente e avvisa il logger
                    if (manager != null) manager.Trigger_Km_1_5();
                    if (dataLogger != null) dataLogger.Trigger_VMBAppear();
                    break;

                case TriggerType.PMV_Readable:
                    if (dataLogger != null) dataLogger.Trigger_VMBReadable();
                    break;

                case TriggerType.PMV: // Sotto il Gantry
                    if (dataLogger != null) dataLogger.Trigger_Gantry();
                    break;

                case TriggerType.FineTrial:
                    Debug.Log("Trial completato: Uscita presa correttamente.");
                    if (dataLogger != null) dataLogger.FineTrial(true);
                    if (manager != null) manager.FineTrial();
                    break;

                case TriggerType.UscitaMancata:
                    Debug.Log("Trial fallito: Il soggetto ha mancato l'uscita.");
                    if (dataLogger != null) dataLogger.FineTrial(false);
                    if (manager != null) manager.FineTrial();
                    break;
            }
        }
    }
}