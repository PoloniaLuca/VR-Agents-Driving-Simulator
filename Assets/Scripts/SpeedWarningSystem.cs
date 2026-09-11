using UnityEngine;
using VehiclePhysics;
using DrivingSim;

public class SpeedWarningSystem : MonoBehaviour
{
    [Header("Riferimenti VPP")]
    [Tooltip("VPVehicleToolkit del veicolo del partecipante.")]
    public VPVehicleToolkit vehicleToolkit;
    [Tooltip("L'AudioSource che contiene il suono del Bip neutro")]
    public AudioSource beepAudioSource;

    [Header("Impostazioni")]
    [Tooltip("Velocità sotto la quale suona l'allarme (es. 60 km/h)")]
    public float underSpeedThreshold = 60f;
    [Tooltip("Velocità da raggiungere per poter 'armare' l'allarme (evita bip alla partenza)")]
    public float armingSpeed = 65f;

    private bool isArmed = false;   // Diventa true quando superi i 65 km/h
    private bool hasBeeped = false; // Evita che il suono venga riprodotto 60 volte al secondo
    
    [Header("Controllo Esperimento")]
    public bool isSystemEnabled = false; // Questo sarà il nostro interruttore


    void Update()
    {
        if (!isSystemEnabled || vehicleToolkit == null || beepAudioSource == null) return;

        // if (ExperimentManager.Instance.currentState != ExperimentState.Driving) return;

        // Velocità VPP in m/s, convertita nella velocità display/reale del simulatore.
        float currentSpeedKmH = HighwaySpeedScale.Instance != null
            ? HighwaySpeedScale.Instance.PhysicsMsToDisplayKmh(vehicleToolkit.speed)
            : vehicleToolkit.speedInKph;

         if (currentSpeedKmH < 1f) 
        {
            isArmed = false;
            hasBeeped = false;
        }

        // 1. Armiamo il sistema solo DOPO che l'utente è partito e ha raggiunto una velocità di crociera decente (es. 65 km/h)
        if (!isArmed && currentSpeedKmH >= armingSpeed)
        {
            isArmed = true;
            hasBeeped = false;
            Debug.Log("SpeedWarning: Sistema ARMATO (Superati i 65 km/h)");
        }

        // 2. Se il sistema è armato e la velocità scende sotto i 60 km/h, SUONA!
        if (isArmed && currentSpeedKmH < underSpeedThreshold && !hasBeeped)
        {
            beepAudioSource.Play(); // Suona il Bip
            hasBeeped = true;       // Registra che ha suonato per non ripeterlo
            
            // Disarmiamo il sistema. Per risuonare, il giocatore dovrà prima tornare sopra i 65 km/h
            isArmed = false; 
            Debug.Log("SpeedWarning: Velocità sotto i 60 km/h! BIP suonato.");
        }
    }

    private void Awake()
    {
        if (vehicleToolkit == null)
            vehicleToolkit = GetComponentInParent<VPVehicleToolkit>();

        if (vehicleToolkit == null)
            Debug.LogError("[SpeedWarningSystem] VPVehicleToolkit non assegnato/trovato.", this);
    }

    // Questa funzione può essere chiamata dall'ExperimentManager all'inizio di ogni trial
    // per resettare il sistema (es. quando l'auto viene teletrasportata a 0 km/h)
    public void ResetWarningSystem()
    {
        isArmed = false;
        hasBeeped = false;
    }
}