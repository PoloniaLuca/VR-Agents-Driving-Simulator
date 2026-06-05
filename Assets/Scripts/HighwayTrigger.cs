using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class HighwayTrigger : MonoBehaviour
{
    public enum TriggerType { ShowPMV, EndTrial }
    public TriggerType type;
    
    private ExperimentManager manager;

    void Start()
    {
        manager = FindFirstObjectByType<ExperimentManager>();
        GetComponent<BoxCollider>().isTrigger = true; 
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            if (type == TriggerType.ShowPMV) manager.Trigger_Km_1_5();
            else if (type == TriggerType.EndTrial) manager.FineTrial();
        }
    }
}