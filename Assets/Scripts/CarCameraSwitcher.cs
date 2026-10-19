using UnityEngine;

public class CarCameraSwitcher : MonoBehaviour
{
    [Header("Camere Auto (4)")]
    [Tooltip("0 = Main Camera, 1 = Specchietto Destro, 2 = Specchietto Sinistro, 3 = Specchietto Centrale")]
    public Camera[] carCameras = new Camera[4];

    [Header("Camera Overview")]
    public Camera overviewCamera;

    [Header("Input")]
    public KeyCode switchKey = KeyCode.C;

    [Header("Stato")]
    [SerializeField] private bool overviewActive = false;

    private void Start()
    {
        // Controlli di sicurezza
        if (carCameras == null || carCameras.Length != 4)
        {
            Debug.LogError("Devi assegnare esattamente 4 camere auto!");
            return;
        }

        if (overviewCamera == null)
        {
            Debug.LogError("Devi assegnare la camera di overview!");
            return;
        }

        // Stato iniziale: camere auto ON, overview OFF
        SetCarCameras(true);
        SetOverview(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(switchKey))
        {
            ToggleView();
        }
    }

    /// <summary>
    /// Alterna tra vista auto e vista overview
    /// </summary>
    public void ToggleView()
    {
        overviewActive = !overviewActive;

        if (overviewActive)
        {
            // Accendi overview, spegni auto
            SetCarCameras(false);
            SetOverview(true);
        }
        else
        {
            // Spegni overview, accendi auto
            SetOverview(false);
            SetCarCameras(true);
        }
    }

    /// <summary>
    /// Attiva/disattiva le 4 camere auto (Camera.enabled + AudioListener coerente)
    /// </summary>
    private void SetCarCameras(bool state)
    {
        foreach (Camera cam in carCameras)
        {
            if (cam == null) continue;

            cam.enabled = state;

            // Gestione AudioListener: attivo solo se la camera è attiva
            AudioListener listener = cam.GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = state;
        }
    }

    /// <summary>
    /// Attiva/disattiva la camera overview
    /// </summary>
    private void SetOverview(bool state)
    {
        if (overviewCamera == null) return;

        overviewCamera.enabled = state;

        AudioListener listener = overviewCamera.GetComponent<AudioListener>();
        if (listener != null)
            listener.enabled = state;
    }

    /// <summary>
    /// Espongo un metodo pubblico per eventuali pulsanti UI o eventi UnityEvent
    /// </summary>
    public void ForceCarView()
    {
        overviewActive = false;
        SetOverview(false);
        SetCarCameras(true);
    }

    public void ForceOverviewView()
    {
        overviewActive = true;
        SetCarCameras(false);
        SetOverview(true);
    }
}