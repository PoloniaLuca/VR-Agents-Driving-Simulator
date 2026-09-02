using System.Collections.Generic;
using System.IO;
using ResearchSim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ResearchSim.EditorTools
{
    /// <summary>
    /// Legacy/editor scene generator for the ResearchSim project. Most current
    /// experiment tuning should happen in the generated scene and on
    /// DrivingExperimentManager; use this builder only when recreating scenes
    /// from assets.
    /// </summary>
    [InitializeOnLoad]
    public static class ExperimentSceneBuilder
    {
        private const string RootFolder = "Assets/ResearchSim";
        private const string ScenesFolder = RootFolder + "/Scenes";
        private const string MaterialsFolder = RootFolder + "/Materials";
        private const string MeshesFolder = RootFolder + "/Meshes";
        private const string MenuScenePath = ScenesFolder + "/Menu.unity";
        private const string HighwayScenePath = ScenesFolder + "/HighwayStraight.unity";
        private const string DrivingActionsPath = RootFolder + "/Input/CarSim_Driving.inputactions";
        private const string KajamanRoadTexturePath = "Assets/KajamansRoads/Textures/Road_2lane_dark02.png";
        private const string KajamanGrassTexturePath = "Assets/KajamansRoads/Textures/GrassGreenTexture512x512.png";
        private const string KajamanSixLaneRoadTexturePath = "Assets/KajamansRoads/Textures/Road_6lane_dark02.png";
        private const string KajamanSixLaneRoadNormalPath = "Assets/KajamansRoads/Textures/Road_6lane_dark02_n.png";
        private const string KajamanSixLaneMaterialPath = "Assets/KajamansRoads/Materials/6RoadMat.mat";
        private const string KajamanCornTexturePath = "Assets/KajamansRoads/Textures/Corn01b.png";
        private const string KajamanGuardRailTexturePath = "Assets/KajamansRoads/Textures/Guardrails01.png";
        private const string KajamanTreeLineTexturePath = "Assets/KajamansRoads/Textures/WideTreeLine.png";
        private const string KajamanGuardRailMaterialPath = "Assets/KajamansRoads/Materials/GuardRailMat.mat";
        private const string KajamanCornMaterialPath = "Assets/KajamansRoads/Materials/CornMat.mat";
        private const string KajamanTreesMaterialPath = "Assets/KajamansRoads/Materials/TreesMat.mat";
        private const string RccPrototypeModelPath = "Assets/Realistic Car Controller Pro/Models/Prototype Vehicle/Skyline (Prototype)/Model_Skyline by BUMSTRUM(3DMaesen) (Prototype).fbx";
        private const string RccEngineMediumClipPath = "Assets/Realistic Car Controller Pro/Audio/Engine/Engine_Generic_Med.wav";
        private const string VppJPickupPrefabPath = "Assets/Vehicle Physics Pro/Vehicles/JPickup/VPP JPickup.prefab";
        private const string VppCockpitVisualPath = "Assets/Vehicle Physics Pro/Vehicles/JPickup/Meshes/JPickup mesh prefab.prefab";
        private const string VppCarAudioPrefabPath = "Assets/Vehicle Physics Pro/Sample Assets/Prefabs/Car Audio.prefab";
        private const string VppCityScenePath = "Assets/Vehicle Physics Pro/Sample Assets/Locations/VP City.unity";
        private const string VppHighwayRoadMaterialPath = "Assets/Vehicle Physics Pro/Sample Assets/Art/Models/Pixelactive City/Materials/Highway Road Plain.mat";
        private const string VppHighwayLineMaterialPath = "Assets/Vehicle Physics Pro/Sample Assets/Art/Models/Pixelactive City/Materials/Highway Road Solid.mat";
        private const string VppGrassMaterialPath = "Assets/Vehicle Physics Pro/Sample Assets/Art/Models/Pixelactive City/Materials/Ter Grass 02.mat";
        private const string VppConcreteMaterialPath = "Assets/Vehicle Physics Pro/Sample Assets/Art/Models/Pixelactive City/Materials/Concrete.mat";
        private const string VppEngineIdleClipPath = "Assets/Vehicle Physics Pro/Sample Assets/Art/Audio/engine loop.wav";
        private const string VppEngineRunClipPath = "Assets/Vehicle Physics Pro/Sample Assets/Art/Audio/Car Engine Run 01.wav";
        private static readonly Vector3 VppDriverEyePosition = new Vector3(-0.4f, 1.29f, 0.43f);

        static ExperimentSceneBuilder()
        {
            EditorApplication.delayCall += AutoBuildIfProjectIsFresh;
            EditorApplication.delayCall += AutoOpenGeneratedSceneIfDefaultSceneIsLoaded;
            EditorApplication.delayCall += AutoRepairGeneratedAssetsForCurrentPipeline;
        }

        [MenuItem("ResearchSim/Build Experiment Scenes")]
        public static void BuildExperimentScenes()
        {
            // Rebuilds menu/highway scenes from project assets. This is useful
            // for recovery, but it can overwrite scene-level layout choices.
            EnsureFolders();
            Material roadMaterial = GetOrCreateKajamanRoadSurfaceMaterial();
            Material roadLineMaterial = GetOrCreateMaterial("RoadLineWhite.mat", new Color(0.92f, 0.92f, 0.82f));
            Material referenceLineMaterial = GetOrCreateMaterial("ReferenceLineCyan.mat", new Color(0.04f, 0.72f, 0.86f));
            Material groundMaterial = GetOrCreateKajamanGroundMaterial();
            Material vehicleMaterial = GetOrCreateMaterial("ResearchVehicleWhite.mat", new Color(0.9f, 0.92f, 0.9f));
            Material cabinMaterial = GetOrCreateMaterial("CabinDark.mat", new Color(0.05f, 0.06f, 0.065f));

            CreateMenuScene();
            CreateHighwayScene(roadMaterial, roadLineMaterial, referenceLineMaterial, groundMaterial, vehicleMaterial, cabinMaterial);
            ConfigureBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("ResearchSim experiment setup complete. Scenes generated in " + ScenesFolder);
        }

        public static void BuildExperimentScenesFromCommandLine()
        {
            BuildExperimentScenes();
        }

        private static void AutoBuildIfProjectIsFresh()
        {
            if (SessionState.GetBool("ResearchSim.AutoBuildChecked", false))
                return;

            SessionState.SetBool("ResearchSim.AutoBuildChecked", true);

            if (File.Exists(MenuScenePath) && File.Exists(HighwayScenePath))
                return;

            Scene activeScene = EditorSceneManager.GetActiveScene();
            if (!Application.isBatchMode && activeScene.IsValid() && activeScene.isDirty)
            {
                Debug.Log("ResearchSim auto setup skipped because the active scene has unsaved changes. Use ResearchSim > Build Experiment Scenes when ready.");
                return;
            }

            BuildExperimentScenes();
        }

        private static void AutoOpenGeneratedSceneIfDefaultSceneIsLoaded()
        {
            if (Application.isBatchMode || SessionState.GetBool("ResearchSim.AutoOpenChecked", false))
                return;

            SessionState.SetBool("ResearchSim.AutoOpenChecked", true);

            if (!File.Exists(HighwayScenePath))
                return;

            Scene activeScene = EditorSceneManager.GetActiveScene();
            if (!activeScene.IsValid() || activeScene.isDirty)
                return;

            bool isDefaultScene = string.IsNullOrEmpty(activeScene.path) ||
                                  activeScene.path == "Assets/Scenes/SampleScene.unity";

            if (!isDefaultScene)
                return;

            EditorSceneManager.OpenScene(HighwayScenePath, OpenSceneMode.Single);
            Debug.Log("ResearchSim opened generated highway scene: " + HighwayScenePath);
        }

        [MenuItem("ResearchSim/Fix Generated Scene For URP")]
        public static void RepairGeneratedAssetsForCurrentPipeline()
        {
            if (Application.isBatchMode)
                return;

            Scene activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.path == HighwayScenePath)
            {
                RepairOpenHighwayScene();
                EditorSceneManager.SaveScene(activeScene);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void AutoRepairGeneratedAssetsForCurrentPipeline()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                SessionState.GetBool("ResearchSim.AutoRepairChecked", false))
                return;

            SessionState.SetBool("ResearchSim.AutoRepairChecked", true);
            RepairGeneratedAssetsForCurrentPipeline();
        }

        private static void CreateMenuScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject sessionObject = new GameObject("Experiment Session");
            sessionObject.AddComponent<ExperimentSession>();

            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<InputSystemUIInputModule>();

            Canvas canvas = CreateCanvas();
            Font font = GetDefaultFont();

            Text title = CreateText(canvas.transform, "Title", "Driving Experiment", font, 38, TextAnchor.MiddleCenter);
            SetRect(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 135f), new Vector2(640f, 60f));

            Text label = CreateText(canvas.transform, "Participant Label", "Participant ID", font, 20, TextAnchor.MiddleLeft);
            SetRect(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-160f, 52f), new Vector2(320f, 36f));

            InputField participantInput = CreateInputField(canvas.transform, "Participant Input", font, "Soggetto_01");
            SetRect((RectTransform)participantInput.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(320f, 44f));

            Button startButton = CreateButton(canvas.transform, "Start Experiment Button", font, "Start Experiment");
            SetRect((RectTransform)startButton.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -58f), new Vector2(220f, 46f));

            Text status = CreateText(canvas.transform, "Status Label", string.Empty, font, 16, TextAnchor.MiddleCenter);
            SetRect(status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -112f), new Vector2(420f, 30f));

            MainMenuController controller = new GameObject("Menu Controller").AddComponent<MainMenuController>();
            controller.participantInput = participantInput;
            controller.startButton = startButton;
            controller.statusLabel = status;
            controller.drivingSceneName = "HighwayStraight";

            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), MenuScenePath);
        }

        private static void CreateHighwayScene(Material roadMaterial, Material roadLineMaterial, Material referenceLineMaterial, Material groundMaterial, Material vehicleMaterial, Material cabinMaterial)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateLighting();

            List<Vector3> centerlinePoints = BuildLongHighwayPoints();
            CenterlinePath centerline = CreateCenterline(centerlinePoints, false);
            CreateLongStraightHighway(roadMaterial, roadLineMaterial, groundMaterial);

            GameObject settings = new GameObject("Experiment Runtime Settings");
            settings.AddComponent<FixedTimestepSetter>();

            CreateVppPhysicalVehicle(centerlinePoints);

            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), HighwayScenePath);
        }

        private static Canvas CreateCanvas()
        {
            GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        private static Text CreateText(Transform parent, string name, string text, Font font, int size, TextAnchor alignment)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(parent, false);

            Text label = textObject.GetComponent<Text>();
            label.text = text;
            label.font = font;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = new Color(0.08f, 0.08f, 0.08f);
            return label;
        }

        private static InputField CreateInputField(Transform parent, string name, Font font, string placeholder)
        {
            GameObject fieldObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
            fieldObject.transform.SetParent(parent, false);

            Image background = fieldObject.GetComponent<Image>();
            background.color = Color.white;

            Text text = CreateText(fieldObject.transform, "Text", string.Empty, font, 20, TextAnchor.MiddleLeft);
            text.color = Color.black;
            SetStretchRect(text.rectTransform, new Vector2(14f, 6f), new Vector2(-14f, -6f));

            Text placeholderText = CreateText(fieldObject.transform, "Placeholder", placeholder, font, 20, TextAnchor.MiddleLeft);
            placeholderText.color = new Color(0.48f, 0.48f, 0.48f);
            SetStretchRect(placeholderText.rectTransform, new Vector2(14f, 6f), new Vector2(-14f, -6f));

            InputField field = fieldObject.GetComponent<InputField>();
            field.textComponent = text;
            field.placeholder = placeholderText;
            field.targetGraphic = background;
            field.text = placeholder;
            return field;
        }

        private static Button CreateButton(Transform parent, string name, Font font, string labelText)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.12f, 0.32f, 0.58f);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            Text label = CreateText(buttonObject.transform, "Text", labelText, font, 20, TextAnchor.MiddleCenter);
            label.color = Color.white;
            SetStretchRect(label.rectTransform, Vector2.zero, Vector2.zero);
            return button;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static void SetStretchRect(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void CreateLighting()
        {
            RenderSettings.ambientLight = new Color(0.72f, 0.74f, 0.76f);
            RenderSettings.fog = false;

            // Procedural skybox
            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                Material skyMat = new Material(skyShader);
                skyMat.name = "Research Procedural Skybox";
                if (skyMat.HasProperty("_SunDisk"))
                    skyMat.SetFloat("_SunDisk", 2f);
                if (skyMat.HasProperty("_SkyTint"))
                    skyMat.SetColor("_SkyTint", new Color(0.53f, 0.71f, 0.88f));
                if (skyMat.HasProperty("_GroundColor"))
                    skyMat.SetColor("_GroundColor", new Color(0.42f, 0.51f, 0.35f));
                if (skyMat.HasProperty("_AtmosphereThickness"))
                    skyMat.SetFloat("_AtmosphereThickness", 1.1f);
                if (skyMat.HasProperty("_Exposure"))
                    skyMat.SetFloat("_Exposure", 1.25f);
                RenderSettings.skybox = skyMat;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            }

            GameObject lightObject = new GameObject("Clear Day Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
        }

        private static void CreateGround(Material fallbackMaterial)
        {
            Material groundMaterial = GetOrCreateKajamanGroundMaterial();
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Sterile Matte Ground";
            ground.transform.position = new Vector3(0f, -0.08f, 0f);
            ground.transform.localScale = new Vector3(800f, 0.1f, 400f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
        }

        private static List<Vector3> BuildLoopFallbackPoints(float straightHalfLength, float radius, int pointsPerHalfTurn)
        {
            List<Vector3> points = new List<Vector3>();
            points.Add(new Vector3(-straightHalfLength, 0f, radius));
            points.Add(new Vector3(straightHalfLength, 0f, radius));

            for (int i = 1; i <= pointsPerHalfTurn; i++)
            {
                float t = i / (float)pointsPerHalfTurn;
                float angle = Mathf.Lerp(90f, -90f, t) * Mathf.Deg2Rad;
                points.Add(new Vector3(straightHalfLength + Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            points.Add(new Vector3(-straightHalfLength, 0f, -radius));

            for (int i = 1; i <= pointsPerHalfTurn; i++)
            {
                float t = i / (float)pointsPerHalfTurn;
                float angle = Mathf.Lerp(-90f, -270f, t) * Mathf.Deg2Rad;
                points.Add(new Vector3(-straightHalfLength + Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            return points;
        }

        private static List<Vector3> BuildLongHighwayPoints()
        {
            return new List<Vector3>
            {
                new Vector3(0f, 0f, -2900f),
                new Vector3(0f, 0f, -1450f),
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 1450f),
                new Vector3(0f, 0f, 2900f)
            };
        }

        private static List<Vector3> BuildVppCityCruisePoints()
        {
            return new List<Vector3>
            {
                new Vector3(20f, 0f, -64f),
                new Vector3(60f, 0f, -64f),
                new Vector3(110f, 0f, -64f),
                new Vector3(148f, 0f, -83f),
                new Vector3(254f, 0f, -63f),
                new Vector3(148f, 0f, -83f),
                new Vector3(110f, 0f, -64f),
                new Vector3(60f, 0f, -64f),
                new Vector3(20f, 0f, -64f),
                new Vector3(-38f, 0f, -90f),
                new Vector3(-124f, 0f, -50f),
                new Vector3(-38f, 0f, -90f)
            };
        }

        private static void RemoveVppDemoObjects()
        {
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null)
                    Object.DestroyImmediate(cameras[i].gameObject);
            }

            string[] vehicleNameFragments =
            {
                "VPP", "JPickup", "Sport Coupe", "Vehicle", "Dashboard", "Telemetry", "Canvas"
            };

            GameObject[] objects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject candidate = objects[i];
                if (candidate == null || candidate.transform.parent != null)
                    continue;

                for (int j = 0; j < vehicleNameFragments.Length; j++)
                {
                    if (candidate.name.Contains(vehicleNameFragments[j]))
                    {
                        Object.DestroyImmediate(candidate);
                        break;
                    }
                }
            }
        }

        private static void CreateSubtleRouteMarkers(List<Vector3> points, Material material)
        {
            GameObject routeRoot = new GameObject("Research Route Markers");
            List<PathSample> samples = SampleClosedPath(points, 18f);
            for (int i = 0; i < samples.Count; i++)
            {
                PathSample sample = samples[i];
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "Route Marker " + i.ToString("000");
                marker.transform.SetParent(routeRoot.transform, false);
                marker.transform.position = sample.Position + Vector3.up * 0.025f;
                marker.transform.rotation = Quaternion.LookRotation(sample.Tangent, Vector3.up);
                marker.transform.localScale = new Vector3(0.22f, 0.02f, 2.2f);
                marker.GetComponent<Renderer>().sharedMaterial = material;
                RemoveIfPresent<Collider>(marker);
            }
        }

        private static CenterlinePath CreateCenterline(List<Vector3> points, bool closedLoop = true)
        {
            GameObject pathObject = new GameObject("CenterLine_RightLane_Waypoints");
            CenterlinePath path = pathObject.AddComponent<CenterlinePath>();
            path.closedLoop = closedLoop;
            path.drawGizmos = false;

            Transform[] waypoints = new Transform[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                GameObject waypointObject = new GameObject("WP_" + i.ToString("000"));
                waypointObject.transform.SetParent(pathObject.transform);
                waypointObject.transform.position = points[i] + Vector3.up * 0.05f;
                waypoints[i] = waypointObject.transform;
            }

            path.waypoints = waypoints;
            return path;
        }

        private static void CreateLongStraightHighway(Material fallbackRoadMaterial, Material fallbackLineMaterial, Material fallbackGroundMaterial)
        {
            DestroyNamedRoot("Visible Three Lane Highway");
            DestroyNamedRoot("Visible Loop Fallback Road");
            DestroyNamedRoot("Runtime Visible Loop Fallback Road");
            DestroyNamedRoot("Sterile Matte Ground");

            GameObject root = new GameObject("Visible Three Lane Highway");
            Material roadMaterial = GetOrCreateKajamanSixLaneRoadMaterial();
            Material lineMaterial = LoadMaterialOrFallback(VppHighwayLineMaterialPath, fallbackLineMaterial);
            Material groundMaterial = LoadMaterialOrFallback(VppGrassMaterialPath, fallbackGroundMaterial);
            Material guardRailMaterial = GetOrCreateTextureMaterial("KajamanGuardRailURP.mat", KajamanGuardRailTexturePath, new Color(0.72f, 0.72f, 0.68f), Vector2.one);
            Material cornMaterial = GetOrCreateTextureMaterial("KajamanCornURP.mat", KajamanCornTexturePath, Color.white, new Vector2(6f, 1f));
            Material signBlueMaterial = GetOrCreateMaterial("HighwaySignBlue.mat", new Color(0.04f, 0.17f, 0.32f));
            Material signGreenMaterial = GetOrCreateMaterial("HighwaySignGreen.mat", new Color(0.04f, 0.28f, 0.15f));
            Material markerMaterial = GetOrCreateMaterial("HighwayMarkerRed.mat", new Color(0.82f, 0.08f, 0.04f));
            Material treeMaterial = GetOrCreateTextureMaterial("KajamanTreeLineURP.mat", KajamanTreeLineTexturePath, Color.white, Vector2.one);

            const float length = 6000f;
            const float halfLength = length * 0.5f;
            const float pavementWidth = 14f;

            CreateHighwayPlane(root.transform, "Kajaman Highway Ground", 190f, length, -0.1f, groundMaterial, "LongHighway_Ground.asset", true, 24f, length / 45f, 0f, 24f);
            CreateHighwayPlane(root.transform, "Kajaman Three Lane Highway", pavementWidth, length, 0f, roadMaterial, "LongHighway_Asphalt.asset", true, 1f, length / 115f, 0.5f, 1f);

            CreateGuardRail(root.transform, "Left Guard Rail", -8.6f, -halfLength, halfLength, guardRailMaterial, true);
            CreateGuardRail(root.transform, "Right Guard Rail", 8.6f, -halfLength, halfLength, guardRailMaterial, true);
            CreateSideMarkers(root.transform, -halfLength, halfLength, lineMaterial, markerMaterial);
            CreateHighwayLandscapeSigns(root.transform, -halfLength, halfLength, signBlueMaterial, signGreenMaterial, guardRailMaterial);
            CreateCornFields(root.transform, -halfLength, halfLength, cornMaterial);
            CreateRoadsideTreeRows(root.transform, -halfLength, halfLength, treeMaterial);
        }

        private static void CreateHighwayPlane(Transform parent, string name, float width, float length, float y, Material material, string meshAssetName, bool addCollider, float uvWidth, float uvLength, float uvStartX = 0f, float uvEndX = 1f)
        {
            float halfWidth = width * 0.5f;
            float halfLength = length * 0.5f;
            int zSegments = Mathf.Max(1, Mathf.CeilToInt(length / 50f));
            Vector3[] vertices = new Vector3[(zSegments + 1) * 2];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[zSegments * 6];

            for (int i = 0; i <= zSegments; i++)
            {
                float t = i / (float)zSegments;
                float z = Mathf.Lerp(-halfLength, halfLength, t);
                int vertexIndex = i * 2;
                vertices[vertexIndex] = new Vector3(-halfWidth, y, z);
                vertices[vertexIndex + 1] = new Vector3(halfWidth, y, z);
                uvs[vertexIndex] = new Vector2(uvStartX * uvWidth, t * uvLength);
                uvs[vertexIndex + 1] = new Vector2(uvEndX * uvWidth, t * uvLength);
            }

            for (int i = 0; i < zSegments; i++)
            {
                int vertexIndex = i * 2;
                int triangleIndex = i * 6;
                triangles[triangleIndex] = vertexIndex;
                triangles[triangleIndex + 1] = vertexIndex + 2;
                triangles[triangleIndex + 2] = vertexIndex + 1;
                triangles[triangleIndex + 3] = vertexIndex + 1;
                triangles[triangleIndex + 4] = vertexIndex + 2;
                triangles[triangleIndex + 5] = vertexIndex + 3;
            }

            Mesh mesh = new Mesh();
            mesh.name = name + " Mesh";
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh = SaveGeneratedMesh(mesh, meshAssetName);

            GameObject plane = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            plane.transform.SetParent(parent, false);
            plane.GetComponent<MeshFilter>().sharedMesh = mesh;
            plane.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (addCollider)
                plane.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        private static void CreateSolidLongLine(Transform parent, string name, float x, float halfLength, float width, Material material)
        {
            GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = name;
            line.transform.SetParent(parent, false);
            line.transform.position = new Vector3(x, 0.025f, 0f);
            line.transform.localScale = new Vector3(width, 0.025f, halfLength * 2f);
            line.GetComponent<Renderer>().sharedMaterial = material;
            RemoveIfPresent<Collider>(line);
        }

        private static void CreateDashedLongLine(Transform parent, string name, float x, float startZ, float endZ, Material material)
        {
            const float dashLength = 8f;
            const float gapLength = 12f;
            int index = 0;
            for (float z = startZ + 12f; z < endZ - dashLength; z += dashLength + gapLength)
            {
                GameObject dash = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dash.name = name + " " + index.ToString("000");
                dash.transform.SetParent(parent, false);
                dash.transform.position = new Vector3(x, 0.035f, z + dashLength * 0.5f);
                dash.transform.localScale = new Vector3(0.18f, 0.025f, dashLength);
                dash.GetComponent<Renderer>().sharedMaterial = material;
                RemoveIfPresent<Collider>(dash);
                index++;
            }
        }

        private static void CreateGuardRail(Transform parent, string name, float x, float startZ, float endZ, Material material, bool keepCollider)
        {
            const float segmentLength = 70f;
            int index = 0;
            for (float z = startZ; z < endZ; z += segmentLength)
            {
                float currentLength = Mathf.Min(segmentLength - 4f, endZ - z);
                if (currentLength <= 0f)
                    continue;

                GameObject rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rail.name = name + " " + index.ToString("000");
                rail.transform.SetParent(parent, false);
                rail.transform.position = new Vector3(x, 0.65f, z + currentLength * 0.5f);
                rail.transform.localScale = new Vector3(0.22f, 0.28f, currentLength);
                rail.GetComponent<Renderer>().sharedMaterial = material;
                if (!keepCollider)
                    RemoveIfPresent<Collider>(rail);
                index++;
            }
        }

        private static void CreateSideMarkers(Transform parent, float startZ, float endZ, Material postMaterial, Material markerMaterial)
        {
            int index = 0;
            for (float z = startZ + 35f; z < endZ; z += 70f)
            {
                CreateMarkerPost(parent, "Left Highway Marker " + index.ToString("000"), -10.5f, z, postMaterial, markerMaterial);
                CreateMarkerPost(parent, "Right Highway Marker " + index.ToString("000"), 10.5f, z + 25f, postMaterial, markerMaterial);
                index++;
            }
        }

        private static void CreateMarkerPost(Transform parent, string name, float x, float z, Material postMaterial, Material markerMaterial)
        {
            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = name;
            post.transform.SetParent(parent, false);
            post.transform.position = new Vector3(x, 0.5f, z);
            post.transform.localScale = new Vector3(0.16f, 1f, 0.16f);
            post.GetComponent<Renderer>().sharedMaterial = postMaterial;
            RemoveIfPresent<Collider>(post);

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = name + " Reflector";
            marker.transform.SetParent(parent, false);
            marker.transform.position = new Vector3(x, 0.95f, z - 0.08f);
            marker.transform.localScale = new Vector3(0.24f, 0.16f, 0.04f);
            marker.GetComponent<Renderer>().sharedMaterial = markerMaterial;
            RemoveIfPresent<Collider>(marker);
        }

        private static void CreateHighwayLandscapeSigns(Transform parent, float startZ, float endZ, Material signBlueMaterial, Material signGreenMaterial, Material concreteMaterial)
        {
            int index = 0;
            for (float z = startZ + 450f; z < endZ - 250f; z += 900f)
            {
                bool left = index % 2 == 0;
                float x = left ? -18f : 18f;
                Material signMaterial = left ? signBlueMaterial : signGreenMaterial;
                CreateRoadsideSign(parent, "Highway Direction Sign " + index.ToString("000"), x, z, signMaterial, concreteMaterial);
                index++;
            }
        }

        private static void CreateCornFields(Transform parent, float startZ, float endZ, Material material)
        {
            int index = 0;
            const float stripLength = 95f;
            for (float z = startZ + 20f; z < endZ; z += stripLength)
            {
                CreateFieldStrip(parent, "Left Corn Field " + index.ToString("000"), -48f, z + stripLength * 0.5f, 28f, stripLength - 5f, material);
                CreateFieldStrip(parent, "Right Corn Field " + index.ToString("000"), 48f, z + stripLength * 0.5f, 28f, stripLength - 5f, material);
                index++;
            }
        }

        private static void CreateFieldStrip(Transform parent, string name, float x, float z, float width, float length, Material material)
        {
            GameObject field = GameObject.CreatePrimitive(PrimitiveType.Cube);
            field.name = name;
            field.transform.SetParent(parent, false);
            field.transform.position = new Vector3(x, 0.18f, z);
            field.transform.localScale = new Vector3(width, 0.32f, length);
            field.GetComponent<Renderer>().sharedMaterial = material;
            RemoveIfPresent<Collider>(field);
        }

        private static void CreateRoadsideSign(Transform parent, string name, float x, float z, Material signMaterial, Material poleMaterial)
        {
            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pole.name = name + " Pole";
            pole.transform.SetParent(parent, false);
            pole.transform.position = new Vector3(x, 2f, z);
            pole.transform.localScale = new Vector3(0.22f, 4f, 0.22f);
            pole.GetComponent<Renderer>().sharedMaterial = poleMaterial;
            RemoveIfPresent<Collider>(pole);

            GameObject sign = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sign.name = name;
            sign.transform.SetParent(parent, false);
            sign.transform.position = new Vector3(x, 4.1f, z);
            sign.transform.rotation = Quaternion.Euler(0f, x < 0f ? 12f : -12f, 0f);
            sign.transform.localScale = new Vector3(4.2f, 1.55f, 0.14f);
            sign.GetComponent<Renderer>().sharedMaterial = signMaterial;
            RemoveIfPresent<Collider>(sign);
        }

        private static void CreateOverheadGantry(Transform parent, string name, float z, Material signMaterial, Material structureMaterial)
        {
            GameObject leftPost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftPost.name = name + " Left Post";
            leftPost.transform.SetParent(parent, false);
            leftPost.transform.position = new Vector3(-8.7f, 3f, z);
            leftPost.transform.localScale = new Vector3(0.24f, 6f, 0.24f);
            leftPost.GetComponent<Renderer>().sharedMaterial = structureMaterial;
            RemoveIfPresent<Collider>(leftPost);

            GameObject rightPost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightPost.name = name + " Right Post";
            rightPost.transform.SetParent(parent, false);
            rightPost.transform.position = new Vector3(8.7f, 3f, z);
            rightPost.transform.localScale = new Vector3(0.24f, 6f, 0.24f);
            rightPost.GetComponent<Renderer>().sharedMaterial = structureMaterial;
            RemoveIfPresent<Collider>(rightPost);

            GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = name + " Beam";
            beam.transform.SetParent(parent, false);
            beam.transform.position = new Vector3(0f, 6.2f, z);
            beam.transform.localScale = new Vector3(17.8f, 0.22f, 0.22f);
            beam.GetComponent<Renderer>().sharedMaterial = structureMaterial;
            RemoveIfPresent<Collider>(beam);

            GameObject sign = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sign.name = name + " Sign";
            sign.transform.SetParent(parent, false);
            sign.transform.position = new Vector3(0f, 5.2f, z - 0.15f);
            sign.transform.localScale = new Vector3(6f, 1.25f, 0.14f);
            sign.GetComponent<Renderer>().sharedMaterial = signMaterial;
            RemoveIfPresent<Collider>(sign);
        }

        private static void CreateRoadsideTreeRows(Transform parent, float startZ, float endZ, Material material)
        {
            Material trunkMaterial = GetOrCreateMaterial("HighwayTreeTrunk.mat", new Color(0.28f, 0.18f, 0.1f));
            Material leafMaterial = GetOrCreateMaterial("HighwayTreeLeaves.mat", new Color(0.12f, 0.34f, 0.12f));
            int index = 0;
            for (float z = startZ + 90f; z < endZ; z += 95f)
            {
                float leftX = -23f - (index % 4) * 5f;
                float rightX = 23f + ((index + 2) % 4) * 5f;
                CreateSimpleTree(parent, "Highway Left Tree " + index.ToString("000"), leftX, z, 5.5f + (index % 4) * 0.8f, trunkMaterial, leafMaterial);
                CreateSimpleTree(parent, "Highway Right Tree " + index.ToString("000"), rightX, z + 45f, 5.8f + ((index + 3) % 4) * 0.75f, trunkMaterial, leafMaterial);
                index++;
            }
        }

        private static void CreateSimpleTree(Transform parent, string name, float x, float z, float height, Material trunkMaterial, Material leafMaterial)
        {
            GameObject treeRoot = new GameObject(name);
            treeRoot.transform.SetParent(parent, false);
            treeRoot.transform.position = new Vector3(x, 0f, z);

            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(treeRoot.transform, false);
            trunk.transform.localPosition = new Vector3(0f, height * 0.24f, 0f);
            trunk.transform.localScale = new Vector3(0.45f, height * 0.24f, 0.45f);
            trunk.GetComponent<Renderer>().sharedMaterial = trunkMaterial;
            RemoveIfPresent<Collider>(trunk);

            GameObject crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown.name = "Crown";
            crown.transform.SetParent(treeRoot.transform, false);
            crown.transform.localPosition = new Vector3(0f, height * 0.68f, 0f);
            float crownScale = height * 0.34f;
            crown.transform.localScale = new Vector3(crownScale, crownScale * 1.25f, crownScale);
            crown.GetComponent<Renderer>().sharedMaterial = leafMaterial;
            RemoveIfPresent<Collider>(crown);
        }

        private static void CreateTreeBillboard(Transform parent, string name, float x, float z, float scale, Material material)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.position = new Vector3(x, scale * 0.45f, z);
            Vector3 lookDirection = new Vector3(-x, 0f, 0f).normalized;
            quad.transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            quad.transform.localScale = new Vector3(scale, scale, 1f);
            quad.GetComponent<Renderer>().sharedMaterial = material;
            RemoveIfPresent<Collider>(quad);
        }

        private static void CreateFallbackLoopRoadSurface(List<Vector3> points, Material roadMaterial, Material roadLineMaterial, Material referenceLineMaterial, float roadWidth)
        {
            GameObject roadRoot = new GameObject("Visible Loop Fallback Road");
            CreateRibbonObject(roadRoot.transform, "Road Surface", points, roadWidth, 0.06f, 0f, roadMaterial, "VisibleLoopFallbackRoad_Surface.asset", true);
            CreateRoadsideGuidePosts(roadRoot.transform, points, roadWidth, roadLineMaterial);
            Material treeMaterial = GetOrCreateKajamanTreeMaterial();
            CreateTreeLines(roadRoot.transform, points, treeMaterial);
        }

        private static void CreateRibbonObject(Transform parent, string name, List<Vector3> points, float width, float y, float lateralOffset, Material material, string meshAssetName, bool addCollider)
        {
            Mesh mesh = BuildRibbonMesh(points, width, y, lateralOffset, name + " Mesh");
            mesh = SaveGeneratedMesh(mesh, meshAssetName);

            GameObject ribbonObject = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            ribbonObject.transform.SetParent(parent, false);
            ribbonObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            ribbonObject.GetComponent<MeshRenderer>().sharedMaterial = material;

            if (addCollider)
                ribbonObject.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        private static Mesh BuildRibbonMesh(List<Vector3> points, float width, float y, float lateralOffset, string meshName)
        {
            int count = points.Count;
            Vector3[] vertices = new Vector3[count * 2];
            Vector2[] uvs = new Vector2[count * 2];
            int[] triangles = new int[count * 6];

            float[] distances = BuildCumulativeDistances(points);

            for (int i = 0; i < count; i++)
            {
                Vector3 previous = points[(i - 1 + count) % count];
                Vector3 current = points[i];
                Vector3 next = points[(i + 1) % count];
                Vector3 tangent = (next - previous).normalized;
                Vector3 normal = Vector3.Cross(Vector3.up, tangent).normalized;
                Vector3 center = new Vector3(current.x, y, current.z) + normal * lateralOffset;

                vertices[i * 2] = center - normal * (width * 0.5f);
                vertices[i * 2 + 1] = center + normal * (width * 0.5f);
                float tiledDistance = distances[i] / 6f;
                uvs[i * 2] = new Vector2(0f, tiledDistance);
                uvs[i * 2 + 1] = new Vector2(1f, tiledDistance);
            }

            for (int i = 0; i < count; i++)
            {
                int nextIndex = (i + 1) % count;
                int triangleIndex = i * 6;
                int left = i * 2;
                int right = left + 1;
                int nextLeft = nextIndex * 2;
                int nextRight = nextLeft + 1;

                triangles[triangleIndex] = left;
                triangles[triangleIndex + 1] = nextLeft;
                triangles[triangleIndex + 2] = right;
                triangles[triangleIndex + 3] = right;
                triangles[triangleIndex + 4] = nextLeft;
                triangles[triangleIndex + 5] = nextRight;
            }

            Mesh mesh = new Mesh();
            mesh.name = meshName;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float[] BuildCumulativeDistances(List<Vector3> points)
        {
            float[] distances = new float[points.Count];
            for (int i = 1; i < points.Count; i++)
                distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);

            return distances;
        }

        private static void CreateCenterLaneDashes(Transform parent, List<Vector3> points, Material material)
        {
            List<PathSample> samples = SampleClosedPath(points, 12f);
            for (int i = 0; i < samples.Count; i++)
            {
                PathSample sample = samples[i];
                Quaternion rotation = Quaternion.LookRotation(sample.Tangent, Vector3.up);

                GameObject dash = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dash.name = "Center Lane Dash " + i.ToString("000");
                dash.transform.SetParent(parent, false);
                dash.transform.position = sample.Position + Vector3.up * 0.145f;
                dash.transform.rotation = rotation;
                dash.transform.localScale = new Vector3(0.32f, 0.025f, 4.2f);
                dash.GetComponent<Renderer>().sharedMaterial = material;
                RemoveIfPresent<Collider>(dash);
            }
        }

        private static void CreateRoadsideGuidePosts(Transform parent, List<Vector3> points, float roadWidth, Material material)
        {
            List<PathSample> samples = SampleClosedPath(points, 30f);
            for (int i = 0; i < samples.Count; i++)
            {
                PathSample sample = samples[i];
                CreateGuidePost(parent, "Left Distance Post " + i.ToString("000"), sample, roadWidth * 0.5f + 1.35f, material);
                CreateGuidePost(parent, "Right Distance Post " + i.ToString("000"), sample, -roadWidth * 0.5f - 1.35f, material);
            }
        }

        private static void CreateGuidePost(Transform parent, string name, PathSample sample, float lateralOffset, Material material)
        {
            Vector3 normal = Vector3.Cross(Vector3.up, sample.Tangent).normalized;
            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = name;
            post.transform.SetParent(parent, false);
            post.transform.position = sample.Position + normal * lateralOffset + Vector3.up * 0.55f;
            post.transform.rotation = Quaternion.LookRotation(sample.Tangent, Vector3.up);
            post.transform.localScale = new Vector3(0.22f, 1.1f, 0.22f);
            post.GetComponent<Renderer>().sharedMaterial = material;
            RemoveIfPresent<Collider>(post);
        }

        private static void CreateTreeLines(Transform parent, List<Vector3> points, Material material)
        {
            List<PathSample> samples = SampleClosedPath(points, 20f);
            for (int i = 0; i < samples.Count; i++)
            {
                PathSample sample = samples[i];
                float lateralJitterLeft = Random.Range(18f, 26f);
                float lateralJitterRight = Random.Range(-26f, -18f);
                
                CreateTreeBillboard(parent, "Left Tree " + i.ToString("000"), sample, lateralJitterLeft, material);
                CreateTreeBillboard(parent, "Right Tree " + i.ToString("000"), sample, lateralJitterRight, material);
            }
        }

        private static void CreateTreeBillboard(Transform parent, string name, PathSample sample, float lateralOffset, Material material)
        {
            Vector3 normal = Vector3.Cross(Vector3.up, sample.Tangent).normalized;
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            
            float scale = Random.Range(14f, 22f);
            quad.transform.position = sample.Position + normal * lateralOffset + Vector3.up * (scale * 0.45f);
            
            // Randomly rotate to face slightly differently, but generally towards road
            Vector3 lookDir = (sample.Position - quad.transform.position).normalized;
            lookDir.y = 0;
            quad.transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            quad.transform.localScale = new Vector3(scale, scale, 1f);
            quad.GetComponent<Renderer>().sharedMaterial = material;
            RemoveIfPresent<Collider>(quad);
        }

        private static List<PathSample> SampleClosedPath(List<Vector3> points, float spacing)
        {
            List<PathSample> samples = new List<PathSample>();
            if (points.Count < 2 || spacing <= 0f)
                return samples;

            float totalLength = 0f;
            for (int i = 0; i < points.Count; i++)
                totalLength += Vector3.Distance(points[i], points[(i + 1) % points.Count]);

            int sampleCount = Mathf.Max(1, Mathf.FloorToInt(totalLength / spacing));
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                float targetDistance = sampleIndex * spacing;
                float walked = 0f;

                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 start = points[i];
                    Vector3 end = points[(i + 1) % points.Count];
                    Vector3 segment = end - start;
                    float length = segment.magnitude;
                    if (length <= Mathf.Epsilon)
                        continue;

                    if (walked + length >= targetDistance)
                    {
                        float t = Mathf.Clamp01((targetDistance - walked) / length);
                        samples.Add(new PathSample(Vector3.Lerp(start, end, t), segment.normalized));
                        break;
                    }

                    walked += length;
                }
            }

            return samples;
        }

        private readonly struct PathSample
        {
            public PathSample(Vector3 position, Vector3 tangent)
            {
                Position = position;
                Tangent = tangent;
            }

            public Vector3 Position { get; }
            public Vector3 Tangent { get; }
        }

        private static Mesh SaveGeneratedMesh(Mesh mesh, string assetName)
        {
            EnsureFolders();
            string path = MeshesFolder + "/" + assetName;
            Mesh previous = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (previous != null)
                AssetDatabase.DeleteAsset(path);

            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static void CreateVppPhysicalVehicle(List<Vector3> points)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VppJPickupPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("VPP JPickup prefab not found. Falling back to the simple research vehicle.");
                CreatePlaceholderVehicle(points, null, GetOrCreateMaterial("ResearchVehicleWhite.mat", new Color(0.9f, 0.92f, 0.9f)), GetOrCreateMaterial("CabinDark.mat", new Color(0.05f, 0.06f, 0.065f)));
                return;
            }

            Vector3 startPosition = points[0] + Vector3.up * 0.05f;
            Quaternion startRotation = Quaternion.LookRotation((points[1] - points[0]).normalized, Vector3.up);
            GameObject vehicle = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            vehicle.name = "Research VPP Vehicle";
            vehicle.transform.position = startPosition;
            vehicle.transform.rotation = startRotation;

            ConfigureVppVehicleForManualDriving(vehicle);
            ConfigureVppCamera(vehicle.transform);
            ConfigureVppInputBridge(vehicle);
            OverrideVppEngineClipWithRccAudio(vehicle.transform);
        }

        private static void ConfigureVppVehicleForManualDriving(GameObject vehicle)
        {
            MonoBehaviour controller = FindMonoBehaviourByTypeName(vehicle, "VehiclePhysics.VPVehicleController");
            if (controller != null)
            {
                SerializedObject serializedController = new SerializedObject(controller);
                SetSerializedBool(serializedController, "speedControl.speedLimiter", false);
                SetSerializedEnum(serializedController, "gearbox.type", 0);
                SetSerializedEnum(serializedController, "clutch.type", 1);
                SetSerializedFloat(serializedController, "clutch.maxTorqueTransfer", 180f);
                SetSerializedBool(serializedController, "gearbox.autoShift", false);
                SetSerializedFloat(serializedController, "gearbox.manualShiftTime", 0.18f);
                SetSerializedFloat(serializedController, "engine.peakRpmTorque", 185f);
                SetSerializedFloat(serializedController, "engine.rpmLimiterMax", 6500f);
                serializedController.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(controller);
            }

            MonoBehaviour input = FindMonoBehaviourByTypeName(vehicle, "VehiclePhysics.VPStandardInput");
            if (input != null)
            {
                SerializedObject serializedInput = new SerializedObject(input);
                SetSerializedBool(serializedInput, "keyboardNumbersSelectGears", true);
                SetSerializedBool(serializedInput, "disableSteerInput", false);
                SetSerializedBool(serializedInput, "disableThrottleInput", false);
                SetSerializedBool(serializedInput, "disableBrakeInput", false);
                SetSerializedBool(serializedInput, "disableClutchInput", false);
                SetSerializedBool(serializedInput, "disableGearShiftInputs", false);
                SetSerializedEnum(serializedInput, "externalIgnition", 2);
                serializedInput.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(input);
            }
        }

        private static void ConfigureVppCamera(Transform vehicle)
        {
            Camera existing = Camera.main;
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 62f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 4500f;
            cameraObject.AddComponent<AudioListener>();

            Transform driverHead = FindDescendant(vehicle, "DriverHead");
            cameraObject.transform.SetParent(driverHead != null ? driverHead : vehicle, false);
            cameraObject.transform.localPosition = Vector3.zero;
            cameraObject.transform.localRotation = Quaternion.identity;
        }

        private static void ConfigureVppInputBridge(GameObject vehicle)
        {
            VppExternalInputBridge bridge = vehicle.GetComponent<VppExternalInputBridge>();
            if (bridge == null)
                bridge = vehicle.AddComponent<VppExternalInputBridge>();

            bridge.standardInput = FindMonoBehaviourByTypeName(vehicle, "VehiclePhysics.VPStandardInput");
            bridge.vehicleController = FindMonoBehaviourByTypeName(vehicle, "VehiclePhysics.VPVehicleController");
            EditorUtility.SetDirty(bridge);
        }

        private static void OverrideVppEngineClipWithRccAudio(Transform vehicle)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(RccEngineMediumClipPath);
            if (clip == null)
                return;

            Transform engineTransform = FindDescendant(vehicle, "Engine");
            AudioSource source = engineTransform != null ? engineTransform.GetComponent<AudioSource>() : null;
            if (source == null)
                return;

            source.clip = clip;
            source.loop = true;
            source.playOnAwake = true;
            EditorUtility.SetDirty(source);
        }

        private static MonoBehaviour FindMonoBehaviourByTypeName(GameObject root, string fullName)
        {
            MonoBehaviour[] components = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < components.Length; i++)
            {
                MonoBehaviour component = components[i];
                if (component != null && component.GetType().FullName == fullName)
                    return component;
            }

            return null;
        }

        private static void SetSerializedBool(SerializedObject serializedObject, string propertyPath, bool value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            if (property != null)
                property.boolValue = value;
        }

        private static void SetSerializedFloat(SerializedObject serializedObject, string propertyPath, float value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            if (property != null)
                property.floatValue = value;
        }

        private static void SetSerializedEnum(SerializedObject serializedObject, string propertyPath, int value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            if (property != null)
                property.enumValueIndex = value;
        }

        private static void CreatePlaceholderVehicle(List<Vector3> points, CenterlinePath centerline, Material vehicleMaterial, Material cabinMaterial)
        {
            Vector3 startPosition = points[0] + Vector3.up * 0.55f;
            Quaternion startRotation = Quaternion.LookRotation((points[1] - points[0]).normalized, Vector3.up);

            GameObject vehicle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vehicle.name = "Research Vehicle Placeholder - Replace With RCC Prefab";
            vehicle.transform.position = startPosition;
            vehicle.transform.rotation = startRotation;
            vehicle.transform.localScale = Vector3.one;
            RemoveIfPresent<MeshRenderer>(vehicle);
            RemoveIfPresent<MeshFilter>(vehicle);

            BoxCollider collider = vehicle.GetComponent<BoxCollider>();
            if (collider == null)
                collider = vehicle.AddComponent<BoxCollider>();

            collider.center = new Vector3(0f, 0.35f, 0f);
            collider.size = new Vector3(1.8f, 0.75f, 4.2f);

            Rigidbody rb = vehicle.AddComponent<Rigidbody>();
            rb.mass = 1200f;
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.detectCollisions = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            HybridVehicleInput input = vehicle.AddComponent<HybridVehicleInput>();
            input.actionsAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(DrivingActionsPath);

            SimpleResearchVehicleController simpleController = vehicle.AddComponent<SimpleResearchVehicleController>();
            simpleController.acceleration = 9f;
            simpleController.brakeDeceleration = 18f;
            simpleController.maxSpeedKmh = 130f;
            simpleController.maxSteeringAngle = 22f;
            simpleController.highSpeedSteeringAngle = 4.5f;
            simpleController.highSpeedSteeringKmh = 30f;
            simpleController.wheelbase = 2.7f;
            simpleController.steeringResponse = 2.2f;
            simpleController.steeringReturnResponse = 3.8f;
            simpleController.yawRateResponse = 90f;
            simpleController.maxYawRateDegreesPerSecond = 36f;
            simpleController.rearAxleToCenter = 1.25f;
            simpleController.snapToDriveSurface = true;
            simpleController.rideHeight = 0.45f;
            simpleController.groundProbeHeight = 6f;
            simpleController.groundProbeDistance = 80f;
            simpleController.visualBodyRollDegrees = 1.4f;
            simpleController.wheelVisualRadius = 0.34f;
            simpleController.showDebugHud = true;

            Transform cockpitVisual = CreateVppCockpitVisual(vehicle.transform);
            simpleController.visualBody = cockpitVisual;
            simpleController.steeringWheelVisual = cockpitVisual != null ? FindDescendant(cockpitVisual, "SteeringWheel", "Steering_wheel") : null;
            simpleController.steeringWheelMaxRotationDegrees = 70f;
            simpleController.steeringWheelRotationAxis = Vector3.forward;
            simpleController.speedGaugeRoot = cockpitVisual != null ? FindDescendant(cockpitVisual, "Speed") : null;
            simpleController.rpmGaugeRoot = cockpitVisual != null ? FindDescendant(cockpitVisual, "Rpm", "RPM") : null;
            simpleController.speedGaugeMaxKmh = 220f;
            simpleController.analogGaugeNeedleLength = 0.036f;
            simpleController.analogGaugeNeedleWidth = 0.0035f;

            CreateCockpitFillLight(vehicle.transform);
            CreateVppCarAudio(vehicle.transform);
            CreateEngineAudio(vehicle, simpleController, input);

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(vehicle.transform, false);
            cameraObject.transform.localPosition = VppDriverEyePosition;
            cameraObject.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 62f;
            camera.nearClipPlane = 0.03f;
            cameraObject.AddComponent<AudioListener>();

            FirstPersonCameraBinder cameraBinder = vehicle.AddComponent<FirstPersonCameraBinder>();
            cameraBinder.targetCamera = camera;
            cameraBinder.localEyePosition = VppDriverEyePosition;
            cameraBinder.localEyeEulerAngles = new Vector3(0f, 0f, 0f);
        }

        private static Transform CreateVppCockpitVisual(Transform vehicleRoot)
        {
            GameObject cockpitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VppCockpitVisualPath);
            if (cockpitPrefab == null)
                return null;

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(cockpitPrefab);
            visual.name = "VPP Cockpit Visual";
            visual.transform.SetParent(vehicleRoot, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            RemoveCollidersInChildren(visual);
            DisableBehavioursInChildren(visual);
            return visual.transform;
        }

        private static Transform CreateVppCarAudio(Transform vehicleRoot)
        {
            Transform existing = vehicleRoot.Find("VPP Car Audio");
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            GameObject audioPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VppCarAudioPrefabPath);
            if (audioPrefab == null)
                return null;

            GameObject audioRoot = (GameObject)PrefabUtility.InstantiatePrefab(audioPrefab);
            audioRoot.name = "VPP Car Audio";
            audioRoot.transform.SetParent(vehicleRoot, false);
            audioRoot.transform.localPosition = Vector3.zero;
            audioRoot.transform.localRotation = Quaternion.identity;
            audioRoot.transform.localScale = Vector3.one;
            DisableBehavioursInChildren(audioRoot);
            return audioRoot.transform;
        }

        private static Light CreateCockpitFillLight(Transform vehicleRoot)
        {
            Transform existing = vehicleRoot.Find("Cockpit Fill Light");
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            GameObject lightObject = new GameObject("Cockpit Fill Light");
            lightObject.transform.SetParent(vehicleRoot, false);
            lightObject.transform.localPosition = new Vector3(-0.2f, 1.45f, 0.7f);
            lightObject.transform.localRotation = Quaternion.identity;

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.94f, 0.82f);
            light.intensity = 2.4f;
            light.range = 3.2f;
            light.shadows = LightShadows.None;
            return light;
        }

        private static SimpleEngineAudio CreateEngineAudio(GameObject vehicle, SimpleResearchVehicleController controller, HybridVehicleInput input)
        {
            SimpleEngineAudio audio = vehicle.GetComponent<SimpleEngineAudio>();
            if (audio == null)
                audio = vehicle.AddComponent<SimpleEngineAudio>();

            audio.vehicleController = controller;
            audio.inputSource = input;
            audio.idleClip = AssetDatabase.LoadAssetAtPath<AudioClip>(VppEngineIdleClipPath);
            audio.runClip = AssetDatabase.LoadAssetAtPath<AudioClip>(VppEngineRunClipPath);
            audio.engineSource = FindAudioSource(vehicle.transform, "Engine");
            audio.transmissionSource = FindAudioSource(vehicle.transform, "Transmission");
            audio.windSource = FindAudioSource(vehicle.transform, "Wind");
            audio.engineVolume = 0.38f;
            audio.transmissionVolume = 0.12f;
            audio.windVolume = 0.11f;
            audio.minPitch = 0.82f;
            audio.maxPitch = 1.35f;
            return audio;
        }

        private static Transform CreateVehicleVisual(Transform vehicleRoot, Material fallbackVehicleMaterial, Material fallbackCabinMaterial)
        {
            GameObject rccModel = AssetDatabase.LoadAssetAtPath<GameObject>(RccPrototypeModelPath);
            if (rccModel != null)
            {
                GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(rccModel);
                visual.name = "RCC Prototype Visual";
                visual.transform.SetParent(vehicleRoot, false);
                visual.transform.localPosition = new Vector3(0f, -0.08f, 0f);
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;
                RemoveCollidersInChildren(visual);
                DisableBehavioursInChildren(visual);
                RepairSkylineMaterials(visual);
                return visual.transform;
            }

            GameObject bodyObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bodyObject.name = "Vehicle Body";
            bodyObject.transform.SetParent(vehicleRoot, false);
            bodyObject.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            bodyObject.transform.localScale = new Vector3(1.8f, 0.6f, 4.2f);
            bodyObject.GetComponent<Renderer>().sharedMaterial = fallbackVehicleMaterial;
            RemoveIfPresent<Collider>(bodyObject);

            GameObject cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cabin.name = "Cabin Reference";
            cabin.transform.SetParent(bodyObject.transform, false);
            cabin.transform.localPosition = new Vector3(0f, 0.45f, 0.25f);
            cabin.transform.localScale = new Vector3(0.8f, 0.55f, 0.6f);
            cabin.GetComponent<Renderer>().sharedMaterial = fallbackCabinMaterial;
            RemoveIfPresent<Collider>(cabin);
            return bodyObject.transform;
        }

        /// <summary>
        /// Fixes magenta materials on the Skyline FBX model by re-creating materials
        /// with the correct shader and re-assigning the original textures.
        /// </summary>
        private static void RepairSkylineMaterials(GameObject visual)
        {
            const string SkylineTexFolder = "Assets/Realistic Car Controller Pro/Models/Prototype Vehicle/Skyline (Prototype)/Tex/";
            Texture2D colorTex = AssetDatabase.LoadAssetAtPath<Texture2D>(SkylineTexFolder + "skylineColor.png");
            Texture2D normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(SkylineTexFolder + "skylineColor_N.png");
            Texture2D specTex = AssetDatabase.LoadAssetAtPath<Texture2D>(SkylineTexFolder + "skylineSpecular.png");

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            Material bodyMat = new Material(shader);
            bodyMat.name = "Skyline Body Fixed";
            bodyMat.color = Color.white;
            if (bodyMat.HasProperty("_BaseColor")) bodyMat.SetColor("_BaseColor", Color.white);
            if (colorTex != null)
            {
                if (bodyMat.HasProperty("_BaseMap")) bodyMat.SetTexture("_BaseMap", colorTex);
                if (bodyMat.HasProperty("_MainTex")) bodyMat.SetTexture("_MainTex", colorTex);
            }
            if (normalTex != null && bodyMat.HasProperty("_BumpMap"))
                bodyMat.SetTexture("_BumpMap", normalTex);
            if (specTex != null && bodyMat.HasProperty("_MetallicGlossMap"))
                bodyMat.SetTexture("_MetallicGlossMap", specTex);
            if (bodyMat.HasProperty("_Smoothness")) bodyMat.SetFloat("_Smoothness", 0.65f);
            if (bodyMat.HasProperty("_Metallic")) bodyMat.SetFloat("_Metallic", 0.3f);

            Material wheelMat = new Material(shader);
            wheelMat.name = "Skyline Wheel Fixed";
            Color darkGray = new Color(0.12f, 0.12f, 0.12f);
            wheelMat.color = darkGray;
            if (wheelMat.HasProperty("_BaseColor")) wheelMat.SetColor("_BaseColor", darkGray);
            if (wheelMat.HasProperty("_Smoothness")) wheelMat.SetFloat("_Smoothness", 0.5f);

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer rend = renderers[i];
                if (rend == null) continue;
                string objName = rend.gameObject.name.ToLowerInvariant();
                Material mat = (objName.Contains("wheel") || objName.Contains("tire")) ? wheelMat : bodyMat;
                Material[] mats = rend.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                    mats[m] = mat;
                rend.sharedMaterials = mats;
            }
        }

        private static Material GetOrCreateKajamanTreeMaterial()
        {
            EnsureFolders();
            string path = MaterialsFolder + "/KajamanTree.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader);
            mat.name = "KajamanTree";
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/KajamansRoads/Textures/MyPineTree04.png");
            if (tex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }
            
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.4f);
            mat.EnableKeyword("_ALPHATEST_ON");
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 1f);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f); 

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void RepairOpenHighwayScene()
        {
            CenterlinePath centerline = Object.FindAnyObjectByType<CenterlinePath>();

            GameObject vehicle = GameObject.Find("Research Vehicle Placeholder - Replace With RCC Prefab");
            if (vehicle == null)
                return;

            vehicle.transform.localScale = Vector3.one;
            List<Vector3> routePoints = centerline != null ? GetCenterlinePoints(centerline) : BuildVppCityCruisePoints();
            if (routePoints.Count >= 2)
            {
                vehicle.transform.position = routePoints[0] + Vector3.up * 0.45f;
                vehicle.transform.rotation = Quaternion.LookRotation((routePoints[1] - routePoints[0]).normalized, Vector3.up);
            }

            RemoveIfPresent<MeshRenderer>(vehicle);
            RemoveIfPresent<MeshFilter>(vehicle);

            BoxCollider rootCollider = vehicle.GetComponent<BoxCollider>();
            if (rootCollider == null)
                rootCollider = vehicle.AddComponent<BoxCollider>();

            rootCollider.center = new Vector3(0f, 0.4f, 0f);
            rootCollider.size = new Vector3(1.8f, 0.8f, 4.2f);

            Rigidbody rb = vehicle.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.mass = 1200f;
                rb.useGravity = false;
                rb.isKinematic = true;
                rb.detectCollisions = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            }

            DestroyChildIfPresent(vehicle.transform, "RCC Prototype Visual");
            DestroyChildIfPresent(vehicle.transform, "Vehicle Body");
            DestroyChildIfPresent(vehicle.transform, "Cabin Reference");

            Transform cockpitVisual = vehicle.transform.Find("VPP Cockpit Visual");
            if (cockpitVisual == null)
                cockpitVisual = CreateVppCockpitVisual(vehicle.transform);

            CreateCockpitFillLight(vehicle.transform);
            if (vehicle.transform.Find("VPP Car Audio") == null)
                CreateVppCarAudio(vehicle.transform);

            SteeringWheelVisual steeringWheel = vehicle.GetComponent<SteeringWheelVisual>();
            if (steeringWheel != null)
                Object.DestroyImmediate(steeringWheel);

            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            camera.transform.SetParent(vehicle.transform, false);
            camera.transform.localPosition = VppDriverEyePosition;
            camera.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            camera.fieldOfView = 62f;
            camera.nearClipPlane = 0.03f;

            FirstPersonCameraBinder cameraBinder = vehicle.GetComponent<FirstPersonCameraBinder>();
            if (cameraBinder != null)
            {
                cameraBinder.targetCamera = camera;
                cameraBinder.localEyePosition = VppDriverEyePosition;
                cameraBinder.localEyeEulerAngles = new Vector3(0f, 0f, 0f);
            }

            HybridVehicleInput input = vehicle.GetComponent<HybridVehicleInput>();
            if (input == null)
                input = vehicle.AddComponent<HybridVehicleInput>();

            input.actionsAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(DrivingActionsPath);

            SimpleResearchVehicleController controller = vehicle.GetComponent<SimpleResearchVehicleController>();
            if (controller == null)
                controller = vehicle.AddComponent<SimpleResearchVehicleController>();

            controller.acceleration = 9f;
            controller.brakeDeceleration = 18f;
            controller.maxSpeedKmh = 130f;
            controller.maxSteeringAngle = 22f;
            controller.highSpeedSteeringAngle = 4.5f;
            controller.highSpeedSteeringKmh = 30f;
            controller.wheelbase = 2.7f;
            controller.steeringResponse = 2.2f;
            controller.steeringReturnResponse = 3.8f;
            controller.yawRateResponse = 90f;
            controller.maxYawRateDegreesPerSecond = 36f;
            controller.rearAxleToCenter = 1.25f;
            controller.visualBodyRollDegrees = 1.4f;
            controller.wheelVisualRadius = 0.34f;
            controller.snapToDriveSurface = true;
            controller.rideHeight = 0.45f;
            controller.groundProbeHeight = 6f;
            controller.groundProbeDistance = 80f;
            controller.visualBody = cockpitVisual;
            controller.steeringWheelVisual = cockpitVisual != null ? FindDescendant(cockpitVisual, "SteeringWheel", "Steering_wheel") : null;
            controller.steeringWheelMaxRotationDegrees = 70f;
            controller.steeringWheelRotationAxis = Vector3.forward;
            controller.speedGaugeRoot = cockpitVisual != null ? FindDescendant(cockpitVisual, "Speed") : null;
            controller.rpmGaugeRoot = cockpitVisual != null ? FindDescendant(cockpitVisual, "Rpm", "RPM") : null;
            controller.speedGaugeMaxKmh = 220f;
            controller.analogGaugeNeedleLength = 0.036f;
            controller.analogGaugeNeedleWidth = 0.0035f;
            controller.showDebugHud = true;

            CreateEngineAudio(vehicle, controller, input);

        }

        private static void RepairVisibleRoad(CenterlinePath centerline, Material roadMaterial, Material roadLineMaterial, Material referenceLineMaterial)
        {
            List<Vector3> points = GetCenterlinePoints(centerline);
            if (points.Count < 3)
                return;

            DestroyNamedRoot("Visible Loop Fallback Road");
            DestroyNamedRoot("Runtime Visible Loop Fallback Road");
            CreateFallbackLoopRoadSurface(points, roadMaterial, roadLineMaterial, referenceLineMaterial, 14f);

            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null && renderer.name.StartsWith("Road Segment"))
                    renderer.enabled = false;
            }
        }

        private static List<Vector3> GetCenterlinePoints(CenterlinePath centerline)
        {
            List<Vector3> points = new List<Vector3>();
            if (centerline.waypoints == null)
                return points;

            for (int i = 0; i < centerline.waypoints.Length; i++)
            {
                Transform waypoint = centerline.waypoints[i];
                if (waypoint != null)
                    points.Add(new Vector3(waypoint.position.x, 0f, waypoint.position.z));
            }

            return points;
        }

        private static void DestroyNamedRoot(string objectName)
        {
            GameObject existing = GameObject.Find(objectName);
            if (existing != null)
                Object.DestroyImmediate(existing);
        }

        private static void DestroyChildIfPresent(Transform parent, string childName)
        {
            if (parent == null)
                return;

            Transform child = parent.Find(childName);
            if (child != null)
                Object.DestroyImmediate(child.gameObject);
        }

        private static Transform FindDescendant(Transform root, params string[] names)
        {
            if (root == null)
                return null;

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == null)
                    continue;

                for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
                {
                    if (candidate.name == names[nameIndex])
                        return candidate;
                }
            }

            return null;
        }

        private static AudioSource FindAudioSource(Transform root, string objectName)
        {
            Transform found = FindDescendant(root, objectName);
            return found != null ? found.GetComponent<AudioSource>() : null;
        }

        private static void ApplyMaterialToNamedRenderers(string namePrefix, Material material)
        {
            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null && renderer.name.StartsWith(namePrefix))
                    renderer.sharedMaterial = material;
            }
        }

        private static void RemoveIfPresent<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            if (component != null)
                Object.DestroyImmediate(component);
        }

        private static void RemoveCollidersInChildren(GameObject root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    Object.DestroyImmediate(colliders[i]);
            }
        }

        private static void DisableBehavioursInChildren(GameObject root)
        {
            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null)
                    behaviours[i].enabled = false;
            }
        }

        private static void ConfigureBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(HighwayScenePath, true)
            };
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "ResearchSim");
            EnsureFolder(RootFolder, "Scenes");
            EnsureFolder(RootFolder, "Materials");
            EnsureFolder(RootFolder, "Meshes");
            EnsureFolder(RootFolder, "Input");
            EnsureFolder(RootFolder, "Docs");
            EnsureFolder(RootFolder, "Prefabs");
            EnsureFolder(RootFolder, "Scripts");
            EnsureFolder(RootFolder + "/Scripts", "Runtime");
            EnsureFolder(RootFolder + "/Scripts", "Editor");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static Material GetOrCreateMaterial(string fileName, Color color)
        {
            EnsureFolders();
            string path = MaterialsFolder + "/" + fileName;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            if (shader != null && material.shader != shader)
                material.shader = shader;

            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadMaterialOrFallback(string assetPath, Material fallback)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            return material != null ? material : fallback;
        }

        private static Material GetOrCreateKajamanSixLaneRoadMaterial()
        {
            Material material = GetOrCreateMaterial("KajamanSixLaneHighway.mat", Color.white);
            Texture2D roadTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(KajamanSixLaneRoadTexturePath);
            Texture2D normalTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(KajamanSixLaneRoadNormalPath);
            if (roadTexture != null)
            {
                roadTexture.wrapMode = TextureWrapMode.Repeat;
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", roadTexture);
                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", roadTexture);
            }

            if (normalTexture != null)
            {
                normalTexture.wrapMode = TextureWrapMode.Repeat;
                if (material.HasProperty("_BumpMap"))
                {
                    material.SetTexture("_BumpMap", normalTexture);
                    material.EnableKeyword("_NORMALMAP");
                }
            }

            material.color = Color.white;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.white);
            material.mainTextureScale = Vector2.one;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateTextureMaterial(string fileName, string texturePath, Color fallbackColor, Vector2 tiling)
        {
            Material material = GetOrCreateMaterial(fileName, fallbackColor);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture != null)
            {
                texture.wrapMode = TextureWrapMode.Repeat;
                material.color = Color.white;
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", Color.white);
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", Color.white);
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", texture);
                material.mainTextureScale = tiling;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateKajamanRoadSurfaceMaterial()
        {
            Material material = GetOrCreateMaterial("KajamanRoadSurface.mat", Color.white);
            Texture2D roadTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(KajamanRoadTexturePath);

            if (roadTexture == null)
            {
                material.color = new Color(0.18f, 0.18f, 0.18f);
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", material.color);
                return material;
            }

            material.color = Color.white;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.white);
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", roadTexture);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", roadTexture);

            material.mainTextureScale = new Vector2(1f, 1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateKajamanGroundMaterial()
        {
            Material material = GetOrCreateMaterial("KajamanGroundGrass.mat", new Color(0.46f, 0.58f, 0.42f));
            Texture2D grassTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(KajamanGrassTexturePath);
            if (grassTexture == null)
                return material;

            material.color = Color.white;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.white);
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", grassTexture);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", grassTexture);

            material.mainTextureScale = new Vector2(18f, 8f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Font GetDefaultFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            return font;
        }
    }
}
