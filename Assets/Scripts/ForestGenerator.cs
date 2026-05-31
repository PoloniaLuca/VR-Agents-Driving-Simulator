using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using Random = UnityEngine.Random;

[RequireComponent(typeof(SplineContainer))]
public class ForestGenerator : MonoBehaviour
{
    [Header("Impostazioni Spline")]
    [Tooltip("Distanza in metri tra un albero e l'altro lungo la spline")]
    public float spacing = 20f;
    [Tooltip("Distanza laterale dal centro della spline (es. 22 per l'autostrada)")]
    public float lateralOffset = 22f;

    [Header("Impostazioni Foresta (Jitter)")]
    [Tooltip("Variazione casuale in avanti/indietro (metri)")]
    public float positionJitterZ = 5f;
    [Tooltip("Variazione casuale laterale (metri)")]
    public float positionJitterX = 3f;
    [Tooltip("Ruota casualmente l'albero su se stesso per variare la silhouette")]
    public bool randomRotation = true;
    [Tooltip("Applica una variazione casuale alla scala dell'albero")]
    public bool randomScale = true;
    public float minScale = 0.8f;
    public float maxScale = 1.2f;

    [Header("Prefab degli Alberi")]
    public GameObject[] treePrefabs;

    [Header("Generazione")]
    [Tooltip("Genera gli alberi sul lato destro della spline")]
    public bool generateRight = true;
    [Tooltip("Genera gli alberi sul lato sinistro della spline")]
    public bool generateLeft = true;
    [Tooltip("Il GameObject genitore in cui verranno raggruppati tutti gli alberi generati")]
    public Transform forestParent;

    private SplineContainer splineContainer;

    void Start()
    {
        // Questo script non genera gli alberi in Start per evitare blocchi.
        // Usa il pulsante "Generate Forest" nell'Inspector (tramite script Editor personalizzato) 
        // o chiama GenerateForest() da un altro script manager.
    }

    [ContextMenu("Generate Forest")]
    public void GenerateForest()
    {
        splineContainer = GetComponent<SplineContainer>();
        if (splineContainer == null || treePrefabs.Length == 0)
        {
            Debug.LogError("Manca la Spline o non hai assegnato nessun Prefab di albero!");
            return;
        }

        // Se non è stato assegnato un genitore, ne crea uno per tenere la hierarchy pulita
        if (forestParent == null)
        {
            GameObject parentObj = new GameObject("Forest_" + gameObject.name);
            forestParent = parentObj.transform;
            forestParent.SetParent(this.transform);
        }

        // Calcola la lunghezza totale della spline
        float length = splineContainer.CalculateLength();
        int numTrees = Mathf.FloorToInt(length / spacing);

        for (int i = 0; i < numTrees; i++)
        {
            // T (valore da 0 a 1) che rappresenta la percentuale di completamento lungo la spline
            float t = (float)i / (numTrees - 1);

            // Valutazione della posizione e della tangente (direzione) sulla spline in quel punto T
            float3 position;
            float3 tangent;
            float3 upVector;
            splineContainer.Evaluate(0, t, out position, out tangent, out upVector);

            // Normalizza la tangente per avere una direzione pulita
            tangent = math.normalize(tangent);

            // Calcola il vettore "Right" (perpendicolare alla direzione della strada)
            float3 rightVector = math.cross(upVector, tangent);

            // Generazione lato DESTRO
            if (generateRight)
            {
                SpawnTree(position, rightVector, 1);
            }

            // Generazione lato SINISTRO
            if (generateLeft)
            {
                SpawnTree(position, rightVector, -1);
            }
        }
        
        Debug.Log("Foresta generata con successo!");
    }

    private void SpawnTree(float3 splinePosition, float3 rightVector, int sideMultiplier)
    {
        // 1. Scegli un albero casuale dall'array
        GameObject prefabToSpawn = treePrefabs[Random.Range(0, treePrefabs.Length)];

        // 2. Calcola l'offset di base (es. 22 metri a destra o a sinistra)
        float3 basePosition = splinePosition + (rightVector * lateralOffset * sideMultiplier);

        // 3. Applica il Jitter (Caos controllato)
        float currentJitterX = Random.Range(-positionJitterX, positionJitterX);
        float currentJitterZ = Random.Range(-positionJitterZ, positionJitterZ);
        
        // Variazione laterale aggiuntiva
        basePosition += rightVector * currentJitterX; 
        // Variazione in avanti/indietro rispetto alla spline
        // (Per semplicità usiamo un offset globale su Z, ma in curve strette potrebbe sbavare. 
        // Per precisione assoluta andrebbe applicata lungo la tangente, ma per gli alberi va benissimo così).
        basePosition.z += currentJitterZ; 

        // 4. Istanzia l'albero
        GameObject newTree = Instantiate(prefabToSpawn, basePosition, Quaternion.identity, forestParent);

        // 5. Rotazione casuale
        if (randomRotation)
        {
            newTree.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
        }

        // 6. Scala casuale
        if (randomScale)
        {
            float scale = Random.Range(minScale, maxScale);
            newTree.transform.localScale = new Vector3(scale, scale, scale);
        }
        
        // 7. Raycast verso il basso per incollare l'albero al terreno (opzionale ma consigliato)
        // Questo evita che gli alberi fluttuino se il Terrain non è perfettamente piatto.
        // Assicurati che il Terrain abbia un collider.
        RaycastHit hit;
        // Facciamo partire il raggio da molto in alto (es. Y = 100)
        Vector3 rayStart = new Vector3(newTree.transform.position.x, 100f, newTree.transform.position.z);
        if (Physics.Raycast(rayStart, Vector3.down, out hit, 200f))
        {
            newTree.transform.position = hit.point;
        }
    }

    [ContextMenu("Clear Forest")]
    public void ClearForest()
    {
        if (forestParent != null)
        {
            // Distrugge tutti i figli (gli alberi)
            for (int i = forestParent.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(forestParent.GetChild(i).gameObject);
            }
        }
    }
}