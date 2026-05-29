using UnityEngine;
using UnityEngine.Splines; // Richiede il pacchetto Unity Splines
using Unity.Mathematics;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class HighwayBuilder : MonoBehaviour
{
    [Header("Parametri Autostrada")]
    public SplineContainer splineContainer;
    
    [Tooltip("Larghezza totale in metri (es. 7.5 per due corsie A7)")]
    public float roadWidth = 10.5f; 
    
    // [Tooltip("Larghezza reale delle corsie guidabili (es. 7.5)")]
    // public float drivableWidth = 7.5f;      
    
    [Tooltip("Quanti segmenti compongono la strada. Aumenta per curve più fluide.")]
    [Range(10, 1000)] public int resolution = 200; 

    // Questa riga ti permette di generare la strada cliccando col tasto destro
    // sul componente direttamente nell'editor, senza dover avviare il gioco (Play)!
    [ContextMenu("Genera Asfalto")]
    public void GenerateRoad()
    {
        if (splineContainer == null || splineContainer.Splines.Count == 0)
        {
            Debug.LogWarning("Manca la Spline! Assegna il contenitore.");
            return;
        }

        Spline spline = splineContainer.Spline;
        
        // Liste per i dati matematici della Mesh
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        // 1. CALCOLO DEI VERTICI (Punti 3D nello spazio)
        for (int i = 0; i <= resolution; i++)
        {
            // t è la nostra percentuale lungo la curva (da 0.0 inizio a 1.0 fine)
            float t = i / (float)resolution; 

            // Estrapoliamo posizione, direzione (tangente) e l'asse Z ("su")
            spline.Evaluate(t, out float3 pos, out float3 tangent, out float3 up);

            // Calcoliamo il vettore "Destra" facendo il prodotto vettoriale (cross product)
            // tra la tangente (avanti) e l'asse Su.
            float3 right = math.cross(math.normalize(tangent), math.normalize(up));

            // Generiamo i due bordi della strada allontanandoci dal centro
            Vector3 leftPoint = (Vector3)(pos - (right * (roadWidth / 2f)));
            Vector3 rightPoint = (Vector3)(pos + (right * (roadWidth / 2f)));

            // Per evitare che l'asfalto "compentri" con la mappa sottostante
            // lo alziamo di pochissimo sull'asse Y
            leftPoint.y += 0.1f;
            rightPoint.y += 0.1f;

            vertices.Add(leftPoint);
            vertices.Add(rightPoint);

            // Calcolo coordinate Texture (UV) per non deformare l'asfalto
            // Calcolo coordinate Texture (UV) proporzionate
            // Let's assume your texture represents a 2x2 meter patch of asphalt
            float textureScale = 2f; 

            float lengthT = (t * spline.GetLength()) / textureScale;
            float widthU = roadWidth / textureScale;

            uvs.Add(new Vector2(0, lengthT));       // Bordo Sinistro
            uvs.Add(new Vector2(widthU, lengthT));  // Bordo Destro
        }

        // 2. CALCOLO DEI TRIANGOLI (Connessione dei punti)
        for (int i = 0; i < resolution; i++)
        {
            int root = i * 2; 

            // Primo triangolo (Ordine invertito per far puntare la faccia in alto)
            triangles.Add(root);
            triangles.Add(root + 1);
            triangles.Add(root + 2);

            // Secondo triangolo
            triangles.Add(root + 1);
            triangles.Add(root + 3);
            triangles.Add(root + 2);
        }

        // 3. COSTRUZIONE DELLA MESH
        Mesh roadMesh = new Mesh();
        roadMesh.name = "Asfalto A7";
        roadMesh.vertices = vertices.ToArray();
        roadMesh.triangles = triangles.ToArray();
        roadMesh.uv = uvs.ToArray();
        
        roadMesh.RecalculateNormals(); // Fondamentale per far riflettere la luce correttamente

        // Assegniamo la mesh creata al componente MeshFilter del nostro oggetto
        GetComponent<MeshFilter>().mesh = roadMesh;

        // --- RENDIAMO LA STRADA SOLIDA ---
        MeshCollider col = GetComponent<MeshCollider>();
        if (col == null) 
        {
            col = gameObject.AddComponent<MeshCollider>(); // Lo aggiunge se non c'è
        }
        col.sharedMesh = roadMesh; // Assegna la forma esatta dell'asfalto alla fisica
    }
}