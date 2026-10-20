using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Tools > Highway > Generate Triggers
///
/// Genera i trigger della simulazione lungo la spline, a distanze calcolate dalla posizione del gantry:
///   SDLP          gantry - sdlpBaselineMetriPrimaDelGantry   (marker inizio finestra SDLP baseline)
///   ShowPMV       gantry - vmbAppareMetriPrimaDelGantry      (VMB_APPEAR: mostra il PMV)
///   PMV_Readable  gantry - vmbLeggibileMetriPrimaDelGantry   (VMB_READABLE)
///   PMV           gantry                                     (GANTRY)
///
/// NON tocca i trigger di uscita (FineTrial, UscitaMancata): li lascia dove sono e ne stampa la distanza lungo la spline.
/// Tutte le distanze sono lette da ExperimentManager (stessa fonte usata dal DataLogger).
///
/// Mettere questo file in una cartella "Editor" (es. Assets/Editor/).
/// </summary>
public class HighwayTriggerGenerator : EditorWindow
{
    private const string GeneratedRootName = "HighwayTriggers_Generated";

    private ExperimentManager manager;
    private SerializedObject so;

    private float triggerWidth = 24f;      // lungo la larghezza della strada [m]
    private float triggerHeight = 8f;      // [m]
    private float triggerThickness = 1.5f; // lungo il senso di marcia [m]
    private bool includePractice = false;
    private bool removeExisting = true;

    private Vector2 scroll;
    private HighwayRoadMap previewMap;
    private SplineContainer previewMapSpline;
    private int previewMapIndex = -1;

    private class Track
    {
        public string name;
        public SplineContainer spline;
        public int index;
        public float laneOffset;
        public GameObject envRoot;
    }

    private struct Spec
    {
        public HighwayTrigger.TriggerType type;
        public string label;
        public float distance;
    }

    [MenuItem("Tools/Highway/Generate Triggers")]
    private static void Open()
    {
        GetWindow<HighwayTriggerGenerator>("Highway Triggers");
    }

    private void OnEnable()
    {
        if (manager == null)
            manager = FindFirstObjectByType<ExperimentManager>();
    }

    // ---------------------------------------------------------------
    //  UI
    // ---------------------------------------------------------------
    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        manager = (ExperimentManager)EditorGUILayout.ObjectField("ExperimentManager", manager, typeof(ExperimentManager), true);
        if (manager == null)
        {
            EditorGUILayout.HelpBox("Nessun ExperimentManager trovato nella scena aperta.", MessageType.Warning);
            EditorGUILayout.EndScrollView();
            return;
        }

        if (so == null || so.targetObject != manager)
            so = new SerializedObject(manager);
        so.Update();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Layout (salvato su ExperimentManager)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(so.FindProperty("distanzaGantry"));
        EditorGUILayout.PropertyField(so.FindProperty("sdlpBaselineMetriPrimaDelGantry"));
        EditorGUILayout.PropertyField(so.FindProperty("vmbAppareMetriPrimaDelGantry"));
        EditorGUILayout.PropertyField(so.FindProperty("vmbLeggibileMetriPrimaDelGantry"));
        EditorGUILayout.PropertyField(so.FindProperty("inizioCorsiaUscita"));
        so.ApplyModifiedProperties();

        if (GUILayout.Button("Read gantry from PMV model (modelloFisicoPMV)"))
            ReadGantryFromPmvModel();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Collider", EditorStyles.boldLabel);
        triggerWidth = EditorGUILayout.FloatField("Larghezza [m]", triggerWidth);
        triggerHeight = EditorGUILayout.FloatField("Altezza [m]", triggerHeight);
        triggerThickness = EditorGUILayout.FloatField("Spessore [m]", triggerThickness);

        EditorGUILayout.Space();
        removeExisting = EditorGUILayout.ToggleLeft("Rimuovi i trigger esistenti (SDLP / ShowPMV / PMV_Readable / PMV) prima di generare", removeExisting);
        includePractice = EditorGUILayout.ToggleLeft("Genera anche per la pista di PRATICA (stesse distanze)", includePractice);

        EditorGUILayout.Space();
        DrawPreview();

        EditorGUILayout.Space();
        if (GUILayout.Button("Report trigger esistenti (solo log)"))
            ReportExisting();

        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("GENERA TRIGGER", GUILayout.Height(32)))
            Generate();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndScrollView();
    }

    private void DrawPreview()
    {
        Track exp = GetTracks().FirstOrDefault();
        if (exp == null || exp.spline == null)
        {
            EditorGUILayout.HelpBox("splineSperimentale non assegnata su ExperimentManager.", MessageType.Warning);
            return;
        }

        if (previewMap == null || previewMapSpline != exp.spline || previewMapIndex != exp.index)
        {
            previewMap = new HighwayRoadMap(exp.spline, exp.index);
            previewMapSpline = exp.spline;
            previewMapIndex = exp.index;
        }
        HighwayRoadMap map = previewMap;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Lunghezza spline: {map.Length:F0} m   |   partenza veicolo: {manager.distanzaDiPartenza:F0} m");
        foreach (Spec s in BuildSpecs())
            sb.AppendLine($"{s.type,-13} {s.label,-15} @ {s.distance:F0} m");
        sb.Append($"Corsia di uscita da {manager.inizioCorsiaUscita:F0} m");

        string problem = ValidateLayout(map.Length);
        EditorGUILayout.HelpBox(sb.ToString(), MessageType.None);
        if (problem != null)
            EditorGUILayout.HelpBox(problem, problem.StartsWith("ERRORE") ? MessageType.Error : MessageType.Warning);
    }

    // ---------------------------------------------------------------
    //  LOGICA
    // ---------------------------------------------------------------
    private List<Track> GetTracks()
    {
        var tracks = new List<Track>
        {
            new Track
            {
                name = "Sperimentale",
                spline = manager.splineSperimentale,
                index = manager.splineIndexSperimentale,
                laneOffset = manager.laneOffsetSperimentale,
                envRoot = manager.ambienteSperimentale
            }
        };

        if (includePractice)
        {
            tracks.Add(new Track
            {
                name = "Pratica",
                spline = manager.splinePratica,
                index = manager.splineIndexPratica,
                laneOffset = manager.laneOffsetPratica,
                envRoot = manager.ambientePratica
            });
        }

        return tracks;
    }

    private List<Spec> BuildSpecs()
    {
        float g = manager.distanzaGantry;
        return new List<Spec>
        {
            new Spec { type = HighwayTrigger.TriggerType.SDLP,         label = "SDLP_baseline",  distance = g - manager.sdlpBaselineMetriPrimaDelGantry },
            new Spec { type = HighwayTrigger.TriggerType.ShowPMV,      label = "VMB_appear",     distance = g - manager.vmbAppareMetriPrimaDelGantry },
            new Spec { type = HighwayTrigger.TriggerType.PMV_Readable, label = "VMB_readable",   distance = g - manager.vmbLeggibileMetriPrimaDelGantry },
            new Spec { type = HighwayTrigger.TriggerType.PMV,          label = "Gantry",         distance = g },
        };
    }

    private string ValidateLayout(float splineLength)
    {
        float sdlp = manager.sdlpBaselineMetriPrimaDelGantry;
        float appear = manager.vmbAppareMetriPrimaDelGantry;
        float readable = manager.vmbLeggibileMetriPrimaDelGantry;

        if (!(sdlp > appear && appear > readable && readable > 0f))
            return "ERRORE - ordine non valido: serve  SDLP > VMB_APPEAR > VMB_READABLE > 0  (metri prima del gantry).";

        if (manager.distanzaGantry - sdlp < 0f)
            return "ERRORE - il trigger SDLP cadrebbe prima dell'inizio della spline.";

        if (manager.distanzaGantry > splineLength)
            return "ERRORE - il gantry cade oltre la fine della spline.";

        if (manager.distanzaGantry - 1000f < manager.distanzaDiPartenza)
            return "Attenzione: la finestra baseline (-1000 m dal gantry) parte prima del punto di partenza del veicolo: " +
                   "il guidatore non avrebbe spazio per stabilizzare la velocita'.";

        if (manager.inizioCorsiaUscita < manager.distanzaGantry + 500f)
            return "Attenzione: la corsia di uscita inizia meno di 500 m dopo il gantry: la finestra primaria sara' troncata.";

        return null;
    }

    private void ReadGantryFromPmvModel()
    {
        if (manager.splineSperimentale == null || manager.modelloFisicoPMV == null)
        {
            EditorUtility.DisplayDialog("Highway Triggers", "Servono splineSperimentale e modelloFisicoPMV assegnati su ExperimentManager.", "OK");
            return;
        }

        var map = new HighwayRoadMap(manager.splineSperimentale, manager.splineIndexSperimentale);
        map.Project(manager.modelloFisicoPMV.transform.position, out float d, out float side);

        Undo.RecordObject(manager, "Set gantry distance");
        manager.distanzaGantry = Mathf.Round(d * 10f) / 10f;
        EditorUtility.SetDirty(manager);
        so = null;

        string msg = $"Gantry (pivot di '{manager.modelloFisicoPMV.name}') a {d:F1} m lungo la spline, " +
                     $"{side:F1} m di lato dalla center-line. distanzaGantry aggiornata.";
        Debug.Log("[HighwayTriggers] " + msg);

        if (Mathf.Abs(side - manager.laneOffsetSperimentale) > 40f)
            Debug.LogWarning("[HighwayTriggers] Il modello PMV e' molto lontano dalla corsia del partecipante: controlla che il pivot sia sul gantry.");
    }

    private static bool IsExit(HighwayTrigger t)
    {
        return t.type == HighwayTrigger.TriggerType.FineTrial || t.type == HighwayTrigger.TriggerType.UscitaMancata;
    }

    private bool BelongsToTrack(HighwayTrigger t, Track track, HighwayRoadMap map)
    {
        if (track.envRoot != null)
            return t.transform.IsChildOf(track.envRoot.transform);

        // Nessun contenitore ambiente assegnato: usa la vicinanza alla spline.
        map.Project(t.transform.position, out float d, out float side);
        return d > 0f && d < map.Length && Mathf.Abs(side - track.laneOffset) < 60f;
    }

    private Transform ParentFor(Track track)
    {
        return track.envRoot != null ? track.envRoot.transform : null;
    }

    private GameObject GeneratedRootOf(Track track)
    {
        Transform parent = ParentFor(track);
        if (parent != null)
        {
            Transform existing = parent.Find(GeneratedRootName);
            return existing != null ? existing.gameObject : null;
        }

        return GameObject.Find(GeneratedRootName + "_" + track.name);
    }

    private void ReportExisting()
    {
        foreach (Track track in GetTracks())
        {
            if (track.spline == null) continue;
            var map = new HighwayRoadMap(track.spline, track.index);

            var sb = new StringBuilder($"[HighwayTriggers] Trigger esistenti - pista {track.name} (lunghezza {map.Length:F0} m)\n");
            foreach (HighwayTrigger t in FindObjectsByType<HighwayTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                         .Where(x => BelongsToTrack(x, track, map))
                         .OrderBy(x => { map.Project(x.transform.position, out float d, out float s); return d; }))
            {
                map.Project(t.transform.position, out float dist, out float side);
                sb.AppendLine($"  {t.type,-14} '{t.name}'  @ {dist:F1} m  (lato {side:F1} m)");
            }
            Debug.Log(sb.ToString());
        }
    }

    // ---------------------------------------------------------------
    //  GENERAZIONE
    // ---------------------------------------------------------------
    private void Generate()
    {
        List<Track> tracks = GetTracks();
        foreach (Track tr in tracks)
        {
            if (tr.spline == null)
            {
                EditorUtility.DisplayDialog("Highway Triggers", $"Spline della pista {tr.name} non assegnata su ExperimentManager.", "OK");
                return;
            }
        }

        var maps = tracks.ToDictionary(tr => tr, tr => new HighwayRoadMap(tr.spline, tr.index));

        foreach (Track tr in tracks)
        {
            string problem = ValidateLayout(maps[tr].Length);
            if (problem != null && problem.StartsWith("ERRORE"))
            {
                EditorUtility.DisplayDialog("Highway Triggers", $"Pista {tr.name}: {problem}", "OK");
                return;
            }
        }

        // Riepilogo e conferma
        var toRemove = new Dictionary<Track, List<HighwayTrigger>>();
        var summary = new StringBuilder();
        foreach (Track tr in tracks)
        {
            var existing = removeExisting
                ? FindObjectsByType<HighwayTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(t => !IsExit(t))
                    .Where(t => !IsInsideGenerated(t))
                    .Where(t => BelongsToTrack(t, tr, maps[tr]))
                    .ToList()
                : new List<HighwayTrigger>();
            toRemove[tr] = existing;

            summary.AppendLine($"Pista {tr.name}: 4 trigger nuovi, {existing.Count} esistenti da rimuovere.");
        }
        summary.AppendLine();
        summary.AppendLine("I trigger di uscita NON vengono toccati. L'operazione e' annullabile con Ctrl+Z.");

        if (!EditorUtility.DisplayDialog("Genera trigger", summary.ToString(), "Genera", "Annulla"))
            return;

        Undo.SetCurrentGroupName("Generate highway triggers");
        int undoGroup = Undo.GetCurrentGroup();

        var log = new StringBuilder("[HighwayTriggers] Generazione completata\n");

        foreach (Track tr in tracks)
        {
            HighwayRoadMap map = maps[tr];

            // 1) log + rimozione dei trigger esistenti
            foreach (HighwayTrigger t in toRemove[tr])
            {
                map.Project(t.transform.position, out float d, out float s);
                log.AppendLine($"  rimosso: {t.type} '{t.name}' (era a {d:F1} m)");
                RemoveTrigger(t);
            }

            // 2) rimuove un'eventuale generazione precedente
            GameObject oldRoot = GeneratedRootOf(tr);
            if (oldRoot != null)
                Undo.DestroyObjectImmediate(oldRoot);

            // 3) crea il nuovo gruppo
            Transform parent = ParentFor(tr);
            var root = new GameObject(parent != null ? GeneratedRootName : GeneratedRootName + "_" + tr.name);
            Undo.RegisterCreatedObjectUndo(root, "Generate highway triggers");
            if (parent != null)
                root.transform.SetParent(parent, false);

            foreach (Spec spec in BuildSpecs())
            {
                if (spec.distance < 0f || spec.distance > map.Length)
                {
                    log.AppendLine($"  SALTATO {spec.type}: distanza {spec.distance:F0} m fuori dalla spline ({map.Length:F0} m)");
                    continue;
                }

                CreateTrigger(root.transform, map, tr.laneOffset, spec);
                log.AppendLine($"  creato:  {spec.type,-13} @ {spec.distance:F0} m");
            }

            // 4) verifica uscite (non toccate)
            foreach (HighwayTrigger ex in FindObjectsByType<HighwayTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                         .Where(IsExit).Where(x => BelongsToTrack(x, tr, map)))
            {
                map.Project(ex.transform.position, out float d, out float s);
                log.AppendLine($"  uscita (invariata): {ex.type} '{ex.name}' @ {d:F1} m");
                if (ex.type == HighwayTrigger.TriggerType.FineTrial && d < manager.distanzaGantry + 500f)
                    log.AppendLine("    ATTENZIONE: questa uscita e' a meno di 500 m dal gantry.");
            }
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log(log.ToString());
    }

    private bool IsInsideGenerated(HighwayTrigger t)
    {
        for (Transform p = t.transform; p != null; p = p.parent)
            if (p.name.StartsWith(GeneratedRootName))
                return true;
        return false;
    }

    private static void RemoveTrigger(HighwayTrigger t)
    {
        GameObject go = t.gameObject;
        bool onlyTrigger = go.transform.childCount == 0 && go.GetComponents<Component>().Length <= 3; // Transform + BoxCollider + HighwayTrigger

        if (onlyTrigger)
        {
            Undo.DestroyObjectImmediate(go);
        }
        else
        {
            // Il GameObject ha anche altro: rimuove solo lo script (e il trigger collider che serviva a lui).
            Undo.DestroyObjectImmediate(t);
            Debug.LogWarning($"[HighwayTriggers] '{go.name}' contiene altri componenti: rimosso solo HighwayTrigger (il BoxCollider e' rimasto).");
        }
    }

    private void CreateTrigger(Transform parent, HighwayRoadMap map, float laneOffset, Spec spec)
    {
        map.GetPoint(spec.distance, out Vector3 point, out Vector3 forward);
        Vector3 center = point + HighwayRoadMap.SideVector(forward) * laneOffset; // centrato sulla corsia del partecipante

        var go = new GameObject($"TRG_{spec.type}_{spec.label}_{spec.distance:F0}m");
        Undo.RegisterCreatedObjectUndo(go, "Generate highway triggers");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(center, Quaternion.LookRotation(forward, Vector3.up));

        Vector3 s = go.transform.lossyScale;
        var box = Undo.AddComponent<BoxCollider>(go);
        box.isTrigger = true;
        // dimensioni in metri reali, indipendentemente dalla scala dei parent
        box.size = new Vector3(triggerWidth / s.x, triggerHeight / s.y, triggerThickness / s.z);
        box.center = new Vector3(0f, triggerHeight * 0.5f / s.y, 0f);

        var trigger = Undo.AddComponent<HighwayTrigger>(go);
        trigger.type = spec.type;
        EditorUtility.SetDirty(trigger);
    }
}
