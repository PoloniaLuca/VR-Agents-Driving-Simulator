using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class DistanceRecorder : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private Transform target;
    [SerializeField] private InputAction recordAction = new InputAction("Record", InputActionType.Button);

    private void OnEnable(){
        recordAction.Enable();
    }

    private void OnDisable(){
        recordAction.Disable();
    }

    private void Update(){
        if (recordAction.triggered){
            RecordDistance();
        }
    }

    private void RecordDistance()
    {
        if (target == null){
            Debug.LogWarning("DistanceRecorder: No target");
            return;
        }

        float distance = Vector3.Distance(transform.position, target.position);
        
        Debug.Log($"Distanza dal PMV: {distance} ");
    }
}
