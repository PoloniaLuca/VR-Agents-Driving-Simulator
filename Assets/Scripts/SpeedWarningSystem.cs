using UnityEngine;

public class SpeedWarningSystem : MonoBehaviour
{
    [Header("Riferimenti")]
    [Tooltip("Il Rigidbody dell'auto per leggere la velocità")]
    public Rigidbody carRigidbody;
    [Tooltip("L'AudioSource che contiene il suono del Bip neutro")]
    public AudioSource beepAudioSource;

    [Header("Impostazioni")]
    [Tooltip("Velocità sotto la quale suona l'allarme (es. 60 km/h)")]
    public float underSpeedThreshold = 60f;
    [Tooltip("Velocità da raggiungere per poter 'armare' l'allarme (evita bip alla partenza)")]
    public float armingSpeed = 65f;

    private bool isArmed = false;   // Diventa true quando superi i 65 km/h
    private bool hasBeeped = false; // Evita che il suono venga riprodotto 60 volte al secondo

    void Update()
    {
        if (carRigidbody == null || beepAudioSource == null) return;

        // Calcoliamo la velocità in km/h
        float currentSpeedKmH = carRigidbody.linearVelocity.magnitude * 3.6f;

        // 1. Armiamo il sistema solo DOPO che l'utente è partito e ha raggiunto una velocità di crociera decente (es. 65 km/h)
        if (!isArmed && currentSpeedKmH >= armingSpeed)
        {
            isArmed = true;
            hasBeeped = false;
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

    // Questa funzione può essere chiamata dall'ExperimentManager all'inizio di ogni trial
    // per resettare il sistema (es. quando l'auto viene teletrasportata a 0 km/h)
    public void ResetWarningSystem()
    {
        isArmed = false;
        hasBeeped = false;
    }
}