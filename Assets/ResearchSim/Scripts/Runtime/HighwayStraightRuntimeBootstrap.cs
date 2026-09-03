using UnityEngine;
using UnityEngine.Rendering;
using System.Reflection;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ResearchSim
{
    /// <summary>
    /// Scene-load repair script for the straight highway scene. It removes old
    /// prototype visuals, places the VPP vehicle at the route start, binds the
    /// cockpit camera and disables VPP debug overlays. It should not change
    /// driving input or vehicle physics.
    /// </summary>
    public static class HighwayStraightRuntimeBootstrap
    {
        private static readonly Vector3 VppDriverEyePosition = new Vector3(-0.4f, 1.29f, 0.43f);
        private const string LeadVehiclePrefabPath = "Assets/ResearchSim/Prefabs/LeadVehicleVisuals_SportCoupe.prefab";
        private const string LeadVehicleResourcesPath = "ResearchSim/LeadVehicleVisuals_SportCoupe";
        private const string SlowMusicClipPath = "Assets/ResearchSim/Audio/Music_slow.mp3";
        private const string FastMusicClipPath = "Assets/ResearchSim/Audio/Music_fast.mp3";
        private const string SlowFastBlockClipPath = "Assets/Resources/ResearchSim/Audio/spring_tempo_increase_96-120_sudden.mp3";
        private const string FastSlowBlockClipPath = "Assets/Resources/ResearchSim/Audio/spring_tempo_decrease_120-96_sudden.mp3";
        private const string ControlStableBlockClipPath = "Assets/Resources/ResearchSim/Audio/control_stable.mp3";
        private const string SlowFastBlockResourcesPath = "ResearchSim/Audio/spring_tempo_increase_96-120_sudden";
        private const string FastSlowBlockResourcesPath = "ResearchSim/Audio/spring_tempo_decrease_120-96_sudden";
        private const string ControlStableBlockResourcesPath = "ResearchSim/Audio/control_stable";
        private const string V2SlowFastBlockClipPath = "Assets/Resources/ResearchSim/Audio/spring_vivaldi_tempo_increase_60_120_140_12min.mp3";
        private const string V2FastSlowBlockClipPath = "Assets/Resources/ResearchSim/Audio/spring_vivaldi_tempo_decrease_140_120_60_12min.mp3";
        private const string V2ControlStableBlockClipPath = "Assets/Resources/ResearchSim/Audio/pink_noise.mp3";
        private const string V2SlowFastBlockResourcesPath = "ResearchSim/Audio/spring_vivaldi_tempo_increase_60_120_140_12min";
        private const string V2FastSlowBlockResourcesPath = "ResearchSim/Audio/spring_vivaldi_tempo_decrease_140_120_60_12min";
        private const string V2ControlStableBlockResourcesPath = "ResearchSim/Audio/pink_noise";
        private const float ParticipantRightLaneOffsetMeters = 4.4f;
        private const float ParticipantSpawnHeightOffsetMeters = 0.08f;
        private const float RoadSurfaceRaycastHeightMeters = 30f;
        private const float RoadSurfaceRaycastDistanceMeters = 120f;
        private const float DefaultExpectedMaxDrivingSpeedKmh = 150f;
        private const float DefaultMinimumExperimentalBlockSeconds = 600f;
        private const float DefaultTrackExtensionSafetyMarginMeters = 5000f;
        private const float DefaultMinimumTotalStraightLengthMeters = 30000f;
        private const float StraightExtensionChunkMeters = 500f;
        private const float RuntimeRoadWidthMeters = 22f;
        private const float RuntimeRoadThicknessMeters = 0.08f;
        private const float RuntimeRoadSeamOverlapMeters = 2f;
        private const float RuntimeRoadSurfaceFallbackY = 0f;
        private const float RuntimeRoadSurfaceToleranceMeters = 0.005f;
        private const string RuntimeRoadMaterialPath = "Assets/ResearchSim/Materials/KajamanRoadSurface.mat";
        private const string RuntimeFieldMaterialPath = "Assets/ResearchSim/Materials/KajamanGroundGrass.mat";
        private const string RuntimeLineMaterialPath = "Assets/ResearchSim/Materials/RoadLineWhite.mat";
        private const string RuntimeGuardrailMaterialPath = "Assets/ResearchSim/Materials/KajamanGuardRailURP.mat";
        private static readonly string[] CompleteVisualRootNames =
        {
            "Kajaman Three Lane Highway",
            "Kajaman Highway Ground",
            "Clean Green Roadside Covers",
            "Left Clean Grass Cover",
            "Right Clean Grass Cover",
            "Flat MyListTree02b Replaced Roadside Trees",
            "Guardrail Support Posts"
        };

        private static readonly string[] CompleteVisualNamePrefixes =
        {
            "Left Guard Rail ",
            "Right Guard Rail ",
            "Highway Left Tree ",
            "Highway Right Tree ",
            "Highway Direction Sign ",
            "Flat Facing Foliage"
        };

        private sealed class RuntimeVisualTemplateSelection
        {
            public readonly List<GameObject> roots = new List<GameObject>();
            public Bounds rendererBounds;
            public float startDistance;
            public float endDistance;
            public float length;
            public int rendererCount;
            public string strategy = "improved_procedural_fallback";
            public string rootNames = "";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapHighwayStraight()
        {
            // Only run in scenes that contain the straight-road CenterlinePath.
            CenterlinePath centerline = Object.FindAnyObjectByType<CenterlinePath>();
            if (centerline == null || centerline.Count < 3)
                return;

            SetupEnvironment();
            EnsureStandaloneQuitShortcut();
            EnsureStraightHighwayLength(centerline);
            GameObject vppVehicle = GameObject.Find("Research VPP Vehicle");
            if (vppVehicle != null)
            {
                SetupVppPhysicalVehicle(vppVehicle, centerline);
                DisableVppDebugOverlays(vppVehicle);
                SetupCarFollowingExperiment(vppVehicle, centerline);
                return;
            }

            RepairInvisibleResearchVehicle(centerline);
        }

        private static void EnsureStandaloneQuitShortcut()
        {
            if (Object.FindAnyObjectByType<StandaloneQuitShortcut>() != null)
                return;

            GameObject shortcut = new GameObject("Standalone Quit Shortcut");
            shortcut.AddComponent<StandaloneQuitShortcut>();
        }

        private static void SetupCarFollowingExperiment(GameObject participantVehicle, CenterlinePath centerline)
        {
            // The new paradigm owns only scenario timing. It does not alter the
            // VPP vehicle, its physics, or any keyboard/controller/HID mapping.
            if (participantVehicle == null || centerline == null)
                return;

            DrivingExperimentManager legacyManager = Object.FindAnyObjectByType<DrivingExperimentManager>();
            if (legacyManager != null)
            {
                legacyManager.enabled = false;
                Debug.Log("[CarFollowingBootstrap] Disabled legacy DrivingExperimentManager for the car-following paradigm.");
            }

            GameObject root = GameObject.Find("Car Following Experiment");
            if (root == null)
                root = new GameObject("Car Following Experiment");

            TrialScheduler scheduler = root.GetComponent<TrialScheduler>();
            if (scheduler == null)
                scheduler = root.AddComponent<TrialScheduler>();

            MusicEventController music = root.GetComponent<MusicEventController>();
            if (music == null)
                music = root.AddComponent<MusicEventController>();
            HighwayStraightRuntimeExtensionSettings settings = Object.FindAnyObjectByType<HighwayStraightRuntimeExtensionSettings>();
            ExperimentProtocolProfile protocolProfile = settings != null ? settings.ProtocolProfile : null;
            ConfigureMusicController(music, protocolProfile);

            DrivingDataLogger logger = root.GetComponent<DrivingDataLogger>();
            if (logger == null)
                logger = root.AddComponent<DrivingDataLogger>();

            LeadVehicleController leader = EnsureLeadVehicle(centerline, participantVehicle);

            ExperimentSessionController controller = root.GetComponent<ExperimentSessionController>();
            if (controller == null)
                controller = root.AddComponent<ExperimentSessionController>();

            CarFollowingFeedbackController feedback = root.GetComponent<CarFollowingFeedbackController>();
            if (feedback == null)
                feedback = root.AddComponent<CarFollowingFeedbackController>();

            VppEngineVolumeCalibration engineVolumeCalibration = root.GetComponent<VppEngineVolumeCalibration>();
            if (engineVolumeCalibration == null)
                engineVolumeCalibration = root.AddComponent<VppEngineVolumeCalibration>();
            engineVolumeCalibration.Configure(participantVehicle, controller);

            controller.protocolProfile = protocolProfile;

            Rigidbody participantRigidbody = participantVehicle.GetComponent<Rigidbody>();
            controller.scheduler = scheduler;
            controller.music = music;
            controller.logger = logger;
            controller.feedbackController = feedback;
            controller.leader = leader;
            controller.centerline = centerline;
            controller.participantVehicle = participantVehicle.transform;
            controller.participantRigidbody = participantRigidbody;
            controller.autoArmOnSceneStart = true;
            controller.participantRightLaneOffsetMeters = ParticipantRightLaneOffsetMeters;
            controller.participantSpawnHeightOffsetMeters = ParticipantSpawnHeightOffsetMeters;

            logger.centerline = centerline;
            logger.leadVehicle = leader;
            logger.feedbackController = feedback;
            logger.participantVehicle = participantVehicle.transform;
            logger.participantRigidbody = participantRigidbody;

            feedback.participantVehicle = participantVehicle.transform;
            feedback.participantRigidbody = participantRigidbody;
            feedback.leadVehicle = leader;

            if (leader != null)
                leader.participantRigidbody = participantRigidbody;

            if (controller.protocolProfile != null)
                Debug.Log("[CarFollowingBootstrap] Using protocol profile from bootstrap settings: " + controller.protocolProfile.ProfileIdOrName + ".");
            else
                Debug.Log("[CarFollowingBootstrap] No protocol profile assigned on bootstrap settings; using ExperimentSessionController fallback defaults.");

            Debug.Log("[CarFollowingBootstrap] Car-following components ready. Leader waits for participant movement.");
        }

        private static LeadVehicleController EnsureLeadVehicle(CenterlinePath centerline, GameObject participantVehicle)
        {
            GameObject leaderObject = GameObject.Find("Lead Vehicle");
            if (leaderObject == null)
            {
                GameObject prefab = LoadLeadVehiclePrefab();
                if (prefab != null)
                {
                    leaderObject = Object.Instantiate(prefab);
                    leaderObject.name = "Lead Vehicle";
                    Debug.Log("[CarFollowingBootstrap] Instantiated leader prefab: " + LeadVehiclePrefabPath + ".");
                }
                else
                {
                    Debug.LogWarning("[CarFollowingBootstrap] Lead vehicle prefab not available at '" + LeadVehiclePrefabPath + "'. The car-following task will run without a leader until one is assigned.");
                    return null;
                }
            }

            LeadVehicleController leader = leaderObject.GetComponent<LeadVehicleController>();
            if (leader == null)
                leader = leaderObject.AddComponent<LeadVehicleController>();

            LeadVehicleVisualSync visualSync = leaderObject.GetComponent<LeadVehicleVisualSync>();
            if (visualSync == null)
                leaderObject.AddComponent<LeadVehicleVisualSync>();

            SanitizeLeaderVisual(leaderObject);

            leader.centerline = centerline;
            leader.participantRigidbody = participantVehicle != null ? participantVehicle.GetComponent<Rigidbody>() : null;
            leader.participantStartSpeedKmh = 5f;
            leader.participantStartGraceSeconds = 1f;
            leader.participantStartDistanceMeters = 0.75f;
            leader.participantStartSustainSeconds = 0.2f;
            leader.initialDistanceAheadMeters = 45f;
            leader.cruiseSpeedKmh = 70f;
            leader.startSpeedKmh = 0f;
            leader.cruiseAccelerationMps2 = 1.8f;
            leader.decelerationTargetSpeedKmh = 55f;
            leader.decelerationDurationSeconds = 4f;
            leader.holdDurationSeconds = 6f;
            leader.returnToCruiseDurationSeconds = 6f;
            return leader;
        }

        private static void SanitizeLeaderVisual(GameObject leaderObject)
        {
            if (leaderObject == null)
                return;

            // The leader uses the imported vehicle only as a visual shell.
            // Disable imported driving/physics scripts so VPP remains active
            // only on the participant vehicle.
            MonoBehaviour[] behaviours = leaderObject.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                if (behaviour is LeadVehicleController || behaviour is LeadVehicleVisualSync)
                    continue;

                behaviour.enabled = false;
            }

            Collider[] colliders = leaderObject.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = false;
            }

            Rigidbody[] bodies = leaderObject.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody rb = bodies[i];
                if (rb == null)
                    continue;

                rb.isKinematic = true;
                rb.useGravity = false;
                rb.detectCollisions = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            Renderer[] renderers = leaderObject.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                renderer.enabled = true;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        private static GameObject LoadLeadVehiclePrefab()
        {
            GameObject resourcesPrefab = Resources.Load<GameObject>(LeadVehicleResourcesPath);
            if (resourcesPrefab != null)
                return resourcesPrefab;

#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<GameObject>(LeadVehiclePrefabPath);
#else
            return null;
#endif
        }

        private static void ConfigureMusicController(MusicEventController music, ExperimentProtocolProfile protocolProfile)
        {
            if (music == null)
                return;

            AudioClip slowFast;
            AudioClip fastSlow;
            AudioClip controlStable;
            bool useV2 = protocolProfile != null && protocolProfile.useV2Protocol;

#if UNITY_EDITOR
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AudioClip slow = AssetDatabase.LoadAssetAtPath<AudioClip>(SlowMusicClipPath);
            AudioClip fast = AssetDatabase.LoadAssetAtPath<AudioClip>(FastMusicClipPath);
            if (slow != null)
                music.slowTempoClips = new[] { slow };
            if (fast != null)
                music.fastTempoClips = new[] { fast };

            slowFast = LoadPreparedBlockAudioClip(useV2 ? V2SlowFastBlockClipPath : SlowFastBlockClipPath);
            fastSlow = LoadPreparedBlockAudioClip(useV2 ? V2FastSlowBlockClipPath : FastSlowBlockClipPath);
            controlStable = LoadPreparedBlockAudioClip(useV2 ? V2ControlStableBlockClipPath : ControlStableBlockClipPath);
#else
            slowFast = Resources.Load<AudioClip>(useV2 ? V2SlowFastBlockResourcesPath : SlowFastBlockResourcesPath);
            fastSlow = Resources.Load<AudioClip>(useV2 ? V2FastSlowBlockResourcesPath : FastSlowBlockResourcesPath);
            controlStable = Resources.Load<AudioClip>(useV2 ? V2ControlStableBlockResourcesPath : ControlStableBlockResourcesPath);
#endif
            PreloadBlockAudioClip(slowFast, "SlowFast");
            PreloadBlockAudioClip(fastSlow, "FastSlow");
            PreloadBlockAudioClip(controlStable, "ControlStable");

            if (useV2)
            {
                music.blockMusicClips = new[]
                {
                    new MusicEventController.BlockMusicClip
                    {
                        condition = MusicEventController.MusicBlockCondition.SlowFast,
                        stimulusId = V2ProtocolDefinition.GetStimulusId(MusicEventController.MusicBlockCondition.SlowFast),
                        clip = slowFast
                    },
                    new MusicEventController.BlockMusicClip
                    {
                        condition = MusicEventController.MusicBlockCondition.FastSlow,
                        stimulusId = V2ProtocolDefinition.GetStimulusId(MusicEventController.MusicBlockCondition.FastSlow),
                        clip = fastSlow
                    },
                    new MusicEventController.BlockMusicClip
                    {
                        condition = MusicEventController.MusicBlockCondition.ControlStable,
                        stimulusId = V2ProtocolDefinition.GetStimulusId(MusicEventController.MusicBlockCondition.ControlStable),
                        clip = controlStable
                    }
                };
                return;
            }

            music.blockMusicClips = new[]
            {
                new MusicEventController.BlockMusicClip
                {
                    condition = MusicEventController.MusicBlockCondition.SlowFast,
                    stimulusId = "spring_tempo_increase_96-120_sudden",
                    clip = slowFast,
                    hasTempoChange = true,
                    tempoChangeTimeSeconds = 240f,
                    preBpm = 96f,
                    postBpm = 120f,
                    transitionType = "sudden"
                },
                new MusicEventController.BlockMusicClip
                {
                    condition = MusicEventController.MusicBlockCondition.FastSlow,
                    stimulusId = "spring_tempo_decrease_120-96_sudden",
                    clip = fastSlow,
                    hasTempoChange = true,
                    tempoChangeTimeSeconds = 240f,
                    preBpm = 120f,
                    postBpm = 96f,
                    transitionType = "sudden"
                },
                new MusicEventController.BlockMusicClip
                {
                    condition = MusicEventController.MusicBlockCondition.ControlStable,
                    stimulusId = "ControlStable",
                    clip = controlStable
                }
            };
        }

        private static void PreloadBlockAudioClip(AudioClip clip, string label)
        {
            if (clip == null)
                return;

            if (clip.loadState == AudioDataLoadState.Loaded)
                return;

            bool requested = clip.LoadAudioData();
            if (requested)
            {
                Debug.Log("[CarFollowingBootstrap] Requested preload for block AudioClip: " + label + " / " + clip.name + ".");
                return;
            }

            Debug.LogWarning("[CarFollowingBootstrap] AudioClip preload request failed for " + label + " / " + clip.name + ". Playback will continue with normal on-demand loading.");
        }

#if UNITY_EDITOR
        private static AudioClip LoadPreparedBlockAudioClip(string assetPath)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            if (clip != null)
                return clip;

            Object rawAsset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (rawAsset != null)
            {
                Debug.LogWarning(
                    "[CarFollowingBootstrap] '" + assetPath + "' exists but Unity did not import it as an AudioClip. " +
                    "Check the audio importer settings for the audio file.");
            }
            else
            {
                Debug.LogWarning("[CarFollowingBootstrap] Prepared block audio file not found: '" + assetPath + "'.");
            }

            return null;
        }
#endif

        private static void SetupEnvironment()
        {
            // Remove legacy generated scenery that conflicts with the Kajaman
            // highway scene. The visible road/trees/fields are scene objects.
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Skybox;

            GameObject oldGround = GameObject.Find("Sterile Matte Ground");
            if (oldGround != null)
                Object.Destroy(oldGround);

            DestroyObjectsStartingWith("Center Lane Dash");
            DestroyObjectsStartingWith("Left Edge Line");
            DestroyObjectsStartingWith("Right Edge Line");
            DestroyObjectsStartingWith("Reference Lane Line");
            DestroyObjectsStartingWith("Left Tree");
            DestroyObjectsStartingWith("Right Tree");
        }

        private static void EnsureStraightHighwayLength(CenterlinePath centerline)
        {
            if (centerline == null || centerline.waypoints == null || centerline.waypoints.Length < 2 || centerline.closedLoop)
                return;

            HighwayStraightRuntimeExtensionSettings settings = Object.FindAnyObjectByType<HighwayStraightRuntimeExtensionSettings>();
            bool extensionEnabled = settings == null || settings.runtimeHighwayExtensionEnabled;
            if (!extensionEnabled)
            {
                Debug.Log("[CarFollowingBootstrap] Runtime highway extension disabled by scene settings.");
                return;
            }

            float currentLength = GetCenterlineLength(centerline);
            float expectedMaxSpeedKmh = settings != null ? Mathf.Max(1f, settings.expectedMaxDrivingSpeedKmh) : DefaultExpectedMaxDrivingSpeedKmh;
            float minimumBlockSeconds = settings != null ? Mathf.Max(1f, settings.minimumExperimentalBlockSeconds) : DefaultMinimumExperimentalBlockSeconds;
            float safetyMarginMeters = settings != null ? Mathf.Max(0f, settings.trackExtensionSafetyMarginMeters) : DefaultTrackExtensionSafetyMarginMeters;
            float minimumTotalLengthMeters = settings != null ? Mathf.Max(1000f, settings.minimumTotalStraightLengthMeters) : DefaultMinimumTotalStraightLengthMeters;
            float durationBasisSeconds = GetRuntimeExtensionDurationBasisSeconds(minimumBlockSeconds, out string durationBasisLabel);
            float expectedMaxSpeedMps = expectedMaxSpeedKmh / 3.6f;
            float computedRequiredLength = expectedMaxSpeedMps * durationBasisSeconds + safetyMarginMeters;
            float targetLength = Mathf.Max(minimumTotalLengthMeters, computedRequiredLength);

            Debug.Log(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "[CarFollowingBootstrap] Highway extension planning: prebuiltLength={0:F0}m, expectedMaxSpeed={1:F1}km/h, durationBasis={2:F1}s ({3}), safetyMargin={4:F0}m, computedRequiredLength={5:F0}m, targetLength={6:F0}m.",
                currentLength,
                expectedMaxSpeedKmh,
                durationBasisSeconds,
                durationBasisLabel,
                safetyMarginMeters,
                computedRequiredLength,
                targetLength));

            if (currentLength >= targetLength)
            {
                Debug.Log(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "[CarFollowingBootstrap] Runtime highway extension skipped: prebuilt route length {0:F0}m already covers target {1:F0}m.",
                    currentLength,
                    targetLength));
                return;
            }

            Transform first = centerline.waypoints[0];
            Transform last = centerline.waypoints[centerline.waypoints.Length - 1];
            if (first == null || last == null)
                return;

            Vector3 direction = last.position - first.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
                return;

            direction.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
            Vector3 origin = first.position;

            GameObject previousExtension = GameObject.Find("Runtime Straight Highway Extension");
            if (previousExtension != null)
                Object.Destroy(previousExtension);

            GameObject extensionRoot = new GameObject("Runtime Straight Highway Extension");
            bool clonedVisuals = TryCreateClonedVisualExtension(
                extensionRoot.transform,
                origin,
                direction,
                currentLength,
                targetLength,
                out int visualCloneCount,
                out int clonedRendererCount,
                out string visualStrategy,
                out string selectedVisualRoots,
                out float visualTemplateLength,
                out float visualTemplateStartDistance,
                out float visualTemplateEndDistance,
                out float visualRouteStartDistance,
                out float visualRouteEndDistance,
                out float visualCloneSpacing,
                out float visualEndDistance,
                out Bounds visualTemplateBounds);

            int roadColliderChunks = CreateRuntimeRoadColliderExtension(extensionRoot.transform, origin, right, direction, currentLength, targetLength);
            int proceduralObjectCount = roadColliderChunks;
            string materialStrategy = clonedVisuals
                ? "cloned source renderer materials; invisible procedural road colliders"
                : "URP-safe procedural fallback materials";

            if (!clonedVisuals)
            {
                proceduralObjectCount += CreateProceduralVisibleHighwayExtension(extensionRoot.transform, origin, right, direction, currentLength, targetLength);
            }

            ExtendCenterlineWaypoints(centerline, origin, direction, currentLength, targetLength);
            bool visualCoversTarget = visualEndDistance >= targetLength - 0.5f;
            Debug.Log(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "[CarFollowingBootstrap] Runtime highway extension created: routeLength={0:F0}m -> {1:F0}m, extensionDistance={2:F0}m, endPosition={3}, visualStrategy={4}, selectedRoots=[{5}], templateBoundsCenter={6}, templateBoundsSize={7}, templateStart={8:F0}m, templateEnd={9:F0}m, templateLength={10:F0}m, routeStartAlongRoad={11:F0}m, routeEndAlongRoad={12:F0}m, cloneSpacing={13:F0}m, visualClones={14}, clonedRenderers={15}, proceduralObjects={16}, targetEndAlongRoad={17:F0}m, finalVisualEnd={18:F0}m, visualCoversTarget={19}, placementBasis=route_length_spacing, colliderStrategy=clean_runtime_road_colliders, materialStrategy={20}.",
                currentLength,
                targetLength,
                targetLength - currentLength,
                FormatVector(origin + direction * targetLength),
                visualStrategy,
                selectedVisualRoots,
                FormatVector(visualTemplateBounds.center),
                FormatVector(visualTemplateBounds.size),
                visualTemplateStartDistance,
                visualTemplateEndDistance,
                visualTemplateLength,
                visualRouteStartDistance,
                visualRouteEndDistance,
                visualCloneSpacing,
                visualCloneCount,
                clonedRendererCount,
                proceduralObjectCount,
                targetLength,
                visualEndDistance,
                visualCoversTarget,
                materialStrategy));
        }

        private static bool TryCreateClonedVisualExtension(
            Transform extensionRoot,
            Vector3 origin,
            Vector3 direction,
            float routeLength,
            float targetLength,
            out int cloneCount,
            out int clonedRendererCount,
            out string strategy,
            out string selectedRootNames,
            out float templateLength,
            out float templateStartDistance,
            out float templateEndDistance,
            out float routeStartDistance,
            out float routeEndDistance,
            out float cloneSpacing,
            out float finalVisualEndDistance,
            out Bounds templateBounds)
        {
            cloneCount = 0;
            clonedRendererCount = 0;
            strategy = "improved_procedural_fallback";
            selectedRootNames = "";
            templateLength = 0f;
            templateStartDistance = 0f;
            templateEndDistance = 0f;
            routeStartDistance = 0f;
            routeEndDistance = 0f;
            cloneSpacing = 0f;
            finalVisualEndDistance = 0f;
            templateBounds = new Bounds();

            if (!TrySelectCompleteVisualTemplate(origin, direction, out RuntimeVisualTemplateSelection selection, out string rejectionSummary))
            {
                Debug.LogWarning("[CarFollowingBootstrap] No complete visual highway template could be selected. Inspected roots/prefixes: " + rejectionSummary + ". Runtime highway extension will use improved procedural fallback visuals.");
                return false;
            }

            if (selection.length <= 100f)
            {
                Debug.LogWarning(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "[CarFollowingBootstrap] Selected visual template is too short ({0:F1}m). Runtime highway extension will use improved procedural fallback visuals. Roots=[{1}].",
                    selection.length,
                    selection.rootNames));
                return false;
            }

            if (routeLength <= 100f)
            {
                Debug.LogWarning(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "[CarFollowingBootstrap] Route length is too short for visual clone placement ({0:F1}m). Runtime highway extension will use improved procedural fallback visuals.",
                    routeLength));
                return false;
            }

            Transform clonesParent = new GameObject("Complete Visual Module Clones").transform;
            clonesParent.SetParent(extensionRoot, false);

            routeStartDistance = 0f;
            routeEndDistance = routeLength;
            cloneSpacing = routeLength;
            float targetEndAlongRoad = targetLength;
            float neededVisualDistance = Mathf.Max(0f, targetEndAlongRoad - routeEndDistance);
            int plannedCloneCount = Mathf.CeilToInt(neededVisualDistance / cloneSpacing);
            for (int cloneIndex = 0; cloneIndex < plannedCloneCount; cloneIndex++)
            {
                float offset = cloneSpacing * (cloneIndex + 1);
                GameObject moduleRoot = new GameObject("Complete Visual Module Clone " + cloneCount.ToString("00"));
                moduleRoot.transform.SetParent(clonesParent, false);

                for (int i = 0; i < selection.roots.Count; i++)
                {
                    GameObject sourceRoot = selection.roots[i];
                    if (sourceRoot == null)
                        continue;

                    GameObject clone = Object.Instantiate(sourceRoot, moduleRoot.transform, true);
                    clone.name = sourceRoot.name + " Runtime Clone";
                    clone.transform.position += direction * offset;
                    SanitizeRuntimeVisualClone(clone);
                }

                cloneCount++;
            }

            clonedRendererCount = selection.rendererCount * cloneCount;
            strategy = selection.strategy;
            selectedRootNames = selection.rootNames;
            templateLength = selection.length;
            templateStartDistance = selection.startDistance;
            templateEndDistance = selection.endDistance;
            finalVisualEndDistance = routeEndDistance + cloneSpacing * cloneCount;
            templateBounds = selection.rendererBounds;
            bool visualCoversTarget = finalVisualEndDistance >= targetEndAlongRoad - 0.5f;
            Debug.Log(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "[CarFollowingBootstrap] Highway visual cloning selected: strategy={0}, roots=[{1}], rendererBoundsCenter={2}, rendererBoundsSize={3}, templateStart={4:F0}m, templateEnd={5:F0}m, templateLength={6:F0}m, routeStartAlongRoad={7:F0}m, routeEndAlongRoad={8:F0}m, cloneSpacing={9:F0}m, targetEndAlongRoad={10:F0}m, neededVisualDistance={11:F0}m, cloneCount={12}, finalVisualEnd={13:F0}m, visualCoversTarget={14}, placementBasis=route_length_spacing, colliders=clean_runtime_road_colliders.",
                strategy,
                selectedRootNames,
                FormatVector(templateBounds.center),
                FormatVector(templateBounds.size),
                selection.startDistance,
                selection.endDistance,
                templateLength,
                routeStartDistance,
                routeEndDistance,
                cloneSpacing,
                targetEndAlongRoad,
                neededVisualDistance,
                cloneCount,
                finalVisualEndDistance,
                visualCoversTarget));
            if (!visualCoversTarget)
            {
                Debug.LogWarning(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "[CarFollowingBootstrap] Visual highway clones do not cover the target end: finalVisualEnd={0:F0}m, targetEndAlongRoad={1:F0}m, templateEnd={2:F0}m, cloneSpacing={3:F0}m, cloneCount={4}.",
                    finalVisualEndDistance,
                    targetEndAlongRoad,
                    selection.endDistance,
                    cloneSpacing,
                    cloneCount));
            }

            return cloneCount > 0 || visualCoversTarget;
        }

        private static bool TryFindOriginalRoadSurfaceY(Vector3 seamSamplePosition, Transform ignoreRoot, out float surfaceY, out string source)
        {
            surfaceY = 0f;
            source = "none";
            Physics.SyncTransforms();

            Vector3 rayOrigin = new Vector3(
                seamSamplePosition.x,
                seamSamplePosition.y + RoadSurfaceRaycastHeightMeters,
                seamSamplePosition.z);
            RaycastHit[] hits = Physics.RaycastAll(
                rayOrigin,
                Vector3.down,
                RoadSurfaceRaycastDistanceMeters,
                ~0,
                QueryTriggerInteraction.Ignore);

            if (hits == null || hits.Length == 0)
                return false;

            RaycastHit bestHit = new RaycastHit();
            float bestDistance = float.PositiveInfinity;
            bool foundPreferred = false;
            bool foundAny = false;

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null)
                    continue;

                Transform hitTransform = hit.collider.transform;
                if (ignoreRoot != null && (hitTransform == ignoreRoot || hitTransform.IsChildOf(ignoreRoot)))
                    continue;

                string hitName = hit.collider.gameObject.name;
                if (IsUnsafeRoadSurfaceSampleName(hitName))
                    continue;

                bool preferred = IsPreferredOriginalRoadSurfaceName(hitName);
                if (foundPreferred && !preferred)
                    continue;

                if (preferred && !foundPreferred)
                {
                    foundPreferred = true;
                    bestDistance = float.PositiveInfinity;
                }

                if (hit.distance >= bestDistance)
                    continue;

                bestDistance = hit.distance;
                bestHit = hit;
                foundAny = true;
            }

            if (!foundAny)
                return false;

            surfaceY = bestHit.point.y;
            source = (foundPreferred ? "raycast_preferred:" : "raycast:") + bestHit.collider.gameObject.name;
            return true;
        }

        private static bool IsPreferredOriginalRoadSurfaceName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            return value.Contains("Kajaman Three Lane Highway") ||
                   value.Contains("Kajaman Highway Ground") ||
                   value.Contains("Highway") ||
                   value.Contains("Road") ||
                   value.Contains("Ground");
        }

        private static bool IsUnsafeRoadSurfaceSampleName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            return value.Contains("Runtime Straight Highway Extension") ||
                   value.Contains("Extended Road") ||
                   value.Contains("Vehicle") ||
                   value.Contains("Lead") ||
                   value.Contains("Guard Rail") ||
                   value.Contains("Tree") ||
                   value.Contains("Sign") ||
                   value.Contains("Camera") ||
                   value.Contains("Manager") ||
                   value.Contains("Controller") ||
                   value.Contains("UXF") ||
                   value.Contains("Input");
        }

        private static bool TrySelectCompleteVisualTemplate(Vector3 origin, Vector3 direction, out RuntimeVisualTemplateSelection selection, out string rejectionSummary)
        {
            selection = new RuntimeVisualTemplateSelection();
            List<string> inspected = new List<string>();

            for (int i = 0; i < CompleteVisualRootNames.Length; i++)
            {
                string rootName = CompleteVisualRootNames[i];
                GameObject candidate = GameObject.Find(rootName);
                inspected.Add(rootName + (candidate != null ? ":found" : ":missing"));
                TryAddVisualTemplateCandidate(selection.roots, candidate);
            }

            Transform[] transforms = Object.FindObjectsOfType<Transform>(true);
            for (int prefixIndex = 0; prefixIndex < CompleteVisualNamePrefixes.Length; prefixIndex++)
            {
                string prefix = CompleteVisualNamePrefixes[prefixIndex];
                int matched = 0;
                for (int i = 0; i < transforms.Length; i++)
                {
                    Transform transform = transforms[i];
                    if (transform == null || !transform.name.StartsWith(prefix, System.StringComparison.Ordinal))
                        continue;

                    matched++;
                    TryAddVisualTemplateCandidate(selection.roots, transform.gameObject);
                }

                inspected.Add(prefix + "*:" + matched.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            RemoveChildDuplicateVisualRoots(selection.roots);

            for (int i = selection.roots.Count - 1; i >= 0; i--)
            {
                GameObject root = selection.roots[i];
                if (root == null || !IsSafeRuntimeVisualTemplate(root))
                    selection.roots.RemoveAt(i);
            }

            rejectionSummary = string.Join(", ", inspected.ToArray());
            if (selection.roots.Count == 0)
                return false;

            if (!TryCalculateRendererBounds(selection.roots, origin, direction, out selection.rendererBounds, out selection.startDistance, out selection.endDistance, out selection.rendererCount))
                return false;

            selection.length = selection.endDistance - selection.startDistance;
            selection.strategy = selection.roots.Count == 1 ? "complete_root_clone" : "multi_root_clone";
            selection.rootNames = BuildRootNameList(selection.roots);
            return selection.rendererCount > 0 && selection.length > 0f;
        }

        private static void TryAddVisualTemplateCandidate(List<GameObject> roots, GameObject candidate)
        {
            if (roots == null || candidate == null)
                return;

            if (IsUnsafeCloneName(candidate.name))
                return;

            Renderer[] renderers = candidate.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return;

            for (int i = 0; i < roots.Count; i++)
            {
                if (roots[i] == candidate)
                    return;
            }

            roots.Add(candidate);
        }

        private static void RemoveChildDuplicateVisualRoots(List<GameObject> roots)
        {
            if (roots == null)
                return;

            for (int i = roots.Count - 1; i >= 0; i--)
            {
                GameObject candidate = roots[i];
                if (candidate == null)
                {
                    roots.RemoveAt(i);
                    continue;
                }

                Transform candidateTransform = candidate.transform;
                for (int j = 0; j < roots.Count; j++)
                {
                    if (i == j || roots[j] == null)
                        continue;

                    Transform possibleAncestor = roots[j].transform;
                    if (candidateTransform != possibleAncestor && candidateTransform.IsChildOf(possibleAncestor))
                    {
                        roots.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private static bool TryCalculateRendererBounds(
            List<GameObject> roots,
            Vector3 origin,
            Vector3 direction,
            out Bounds rendererBounds,
            out float minDistance,
            out float maxDistance,
            out int rendererCount)
        {
            rendererBounds = new Bounds();
            minDistance = float.PositiveInfinity;
            maxDistance = float.NegativeInfinity;
            rendererCount = 0;
            bool hasBounds = false;

            for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
            {
                GameObject root = roots[rootIndex];
                if (root == null)
                    continue;

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    Renderer renderer = renderers[rendererIndex];
                    if (renderer == null)
                        continue;

                    Bounds bounds = renderer.bounds;
                    if (!hasBounds)
                    {
                        rendererBounds = bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        rendererBounds.Encapsulate(bounds);
                    }

                    AccumulateProjectedBounds(bounds, origin, direction, ref minDistance, ref maxDistance);
                    rendererCount++;
                }
            }

            if (!hasBounds)
            {
                minDistance = 0f;
                maxDistance = 0f;
                return false;
            }

            return true;
        }

        private static void AccumulateProjectedBounds(Bounds bounds, Vector3 origin, Vector3 direction, ref float minDistance, ref float maxDistance)
        {
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = center + new Vector3(extents.x * x, extents.y * y, extents.z * z);
                        float distance = Vector3.Dot(corner - origin, direction);
                        minDistance = Mathf.Min(minDistance, distance);
                        maxDistance = Mathf.Max(maxDistance, distance);
                    }
                }
            }
        }

        private static string BuildRootNameList(List<GameObject> roots)
        {
            if (roots == null || roots.Count == 0)
                return "";

            List<string> names = new List<string>();
            for (int i = 0; i < roots.Count; i++)
            {
                if (roots[i] != null)
                    names.Add(roots[i].name);
            }

            return string.Join(", ", names.ToArray());
        }

        private static bool IsSafeRuntimeVisualTemplate(GameObject sourceRoot)
        {
            if (sourceRoot == null)
                return false;

            string rootName = sourceRoot.name;
            if (IsUnsafeCloneName(rootName))
                return false;

            Camera[] cameras = sourceRoot.GetComponentsInChildren<Camera>(true);
            AudioListener[] listeners = sourceRoot.GetComponentsInChildren<AudioListener>(true);
            AudioSource[] audioSources = sourceRoot.GetComponentsInChildren<AudioSource>(true);
            if ((cameras != null && cameras.Length > 0) ||
                (listeners != null && listeners.Length > 0) ||
                (audioSources != null && audioSources.Length > 0))
                return false;

            MonoBehaviour[] behaviours = sourceRoot.GetComponentsInChildren<MonoBehaviour>(true);
            if (behaviours == null)
                return true;

            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                string typeName = behaviour.GetType().Name;
                if (IsUnsafeCloneName(typeName))
                    return false;
            }

            return true;
        }

        private static bool IsUnsafeCloneName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            return value.Contains("Vehicle") ||
                   value.Contains("Camera") ||
                   value.Contains("Manager") ||
                   value.Contains("Controller") ||
                   value.Contains("Scheduler") ||
                   value.Contains("UXF") ||
                   value.Contains("Input") ||
                   value.Contains("Audio") ||
                   value.Contains("EventSystem") ||
                   value.Contains("CenterLine") ||
                   value.Contains("Centerline") ||
                   value.Contains("Lead");
        }

        private static void SanitizeRuntimeVisualClone(GameObject clone)
        {
            if (clone == null)
                return;

            MonoBehaviour[] behaviours = clone.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                behaviour.enabled = false;
                Object.Destroy(behaviour);
            }

            Rigidbody[] bodies = clone.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] != null)
                    Object.Destroy(bodies[i]);
            }

            Collider[] colliders = clone.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = false;
            }

            Camera[] cameras = clone.GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null)
                    Object.Destroy(cameras[i]);
            }

            AudioSource[] audioSources = clone.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < audioSources.Length; i++)
            {
                if (audioSources[i] != null)
                    Object.Destroy(audioSources[i]);
            }

            AudioListener[] listeners = clone.GetComponentsInChildren<AudioListener>(true);
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != null)
                    Object.Destroy(listeners[i]);
            }
        }

        private static int CreateRuntimeRoadColliderExtension(Transform parent, Vector3 origin, Vector3 right, Vector3 direction, float extensionStart, float extensionEnd)
        {
            Vector3 seamPosition = origin + direction * extensionStart;
            Vector3 seamSamplePosition = seamPosition - direction * Mathf.Min(RuntimeRoadSeamOverlapMeters * 0.5f, 1f);
            bool surfaceFromRaycast = TryFindOriginalRoadSurfaceY(seamSamplePosition, parent, out float originalRoadSurfaceY, out string surfaceSource);
            if (!surfaceFromRaycast)
            {
                originalRoadSurfaceY = RuntimeRoadSurfaceFallbackY;
                surfaceSource = "fallback";
                Debug.LogWarning(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "[CarFollowingBootstrap] Could not raycast original road surface at seam {0}. Falling back to originalRoadSurfaceY={1:F3}m for runtime road colliders.",
                    FormatVector(seamPosition),
                    originalRoadSurfaceY));
            }

            float runtimeColliderCenterY = originalRoadSurfaceY - RuntimeRoadThicknessMeters * 0.5f;
            float runtimeColliderTopY = runtimeColliderCenterY + RuntimeRoadThicknessMeters * 0.5f;
            float topHeightError = runtimeColliderTopY - originalRoadSurfaceY;
            bool topWithinTolerance = Mathf.Abs(topHeightError) <= RuntimeRoadSurfaceToleranceMeters;
            Debug.Log(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "[CarFollowingBootstrap] Runtime road collider seam alignment: seamPosition={0}, seamSamplePosition={1}, originalRoadSurfaceY={2:F3}m, surfaceSource={3}, runtimeColliderHeight={4:F3}m, runtimeColliderCenterY={5:F3}m, runtimeColliderTopY={6:F3}m, seamOverlapMeters={7:F1}, topHeightError={8:F4}m, topWithinTolerance={9}.",
                FormatVector(seamPosition),
                FormatVector(seamSamplePosition),
                originalRoadSurfaceY,
                surfaceSource,
                RuntimeRoadThicknessMeters,
                runtimeColliderCenterY,
                runtimeColliderTopY,
                RuntimeRoadSeamOverlapMeters,
                topHeightError,
                topWithinTolerance));

            int chunks = 0;
            float colliderStart = Mathf.Max(0f, extensionStart - RuntimeRoadSeamOverlapMeters);
            for (float d = colliderStart; d < extensionEnd; d += StraightExtensionChunkMeters)
            {
                float chunkLength = Mathf.Min(StraightExtensionChunkMeters, extensionEnd - d);
                Vector3 chunkCenter = origin + direction * (d + chunkLength * 0.5f);
                chunkCenter.y = runtimeColliderCenterY;
                CreateRuntimeBox(
                    "Extended Road Collider",
                    chunkCenter,
                    right,
                    direction,
                    RuntimeRoadWidthMeters,
                    RuntimeRoadThicknessMeters,
                    chunkLength,
                    null,
                    parent,
                    true,
                    false);
                chunks++;
            }

            return chunks;
        }

        private static int CreateProceduralVisibleHighwayExtension(Transform parent, Vector3 origin, Vector3 right, Vector3 direction, float extensionStart, float extensionEnd)
        {
            Material roadMaterial = CreateRuntimeMaterial("Runtime Highway Asphalt", RuntimeRoadMaterialPath, new Color(0.34f, 0.36f, 0.37f));
            Material shoulderMaterial = CreateRuntimeMaterial("Runtime Highway Shoulder", RuntimeFieldMaterialPath, new Color(0.26f, 0.30f, 0.22f));
            Material lineMaterial = CreateRuntimeMaterial("Runtime Highway Lines", RuntimeLineMaterialPath, new Color(0.88f, 0.88f, 0.84f));
            Material railMaterial = CreateRuntimeMaterial("Runtime Highway Guardrails", RuntimeGuardrailMaterialPath, new Color(0.48f, 0.54f, 0.56f));

            int objectCount = 0;
            for (float d = extensionStart; d < extensionEnd; d += StraightExtensionChunkMeters)
            {
                float chunkLength = Mathf.Min(StraightExtensionChunkMeters, extensionEnd - d);
                Vector3 chunkCenter = origin + direction * (d + chunkLength * 0.5f);

                CreateRuntimeBox("Extended Road", chunkCenter + Vector3.up * (RuntimeRoadThicknessMeters * 0.5f - 0.02f), right, direction, RuntimeRoadWidthMeters, RuntimeRoadThicknessMeters, chunkLength, roadMaterial, parent, false, true);
                objectCount++;

                CreateRuntimeBox("Extended Left Field", chunkCenter - right * 22f - Vector3.up * 0.04f, right, direction, 22f, 0.04f, chunkLength, shoulderMaterial, parent, false, true);
                objectCount++;

                CreateRuntimeBox("Extended Right Field", chunkCenter + right * 22f - Vector3.up * 0.04f, right, direction, 22f, 0.04f, chunkLength, shoulderMaterial, parent, false, true);
                objectCount++;

                CreateRuntimeBox("Extended Left Guardrail", chunkCenter - right * 11.8f + Vector3.up * 0.5f, right, direction, 0.18f, 0.18f, chunkLength, railMaterial, parent, false, true);
                objectCount++;

                CreateRuntimeBox("Extended Right Guardrail", chunkCenter + right * 11.8f + Vector3.up * 0.5f, right, direction, 0.18f, 0.18f, chunkLength, railMaterial, parent, false, true);
                objectCount++;

                float[] laneLineOffsets = { -3.6f, 0f, 3.6f };
                for (int i = 0; i < laneLineOffsets.Length; i++)
                    objectCount += CreateDashedLaneLine(parent, origin, right, direction, d, chunkLength, laneLineOffsets[i], lineMaterial);
            }

            return objectCount;
        }

        private static void ExtendCenterlineWaypoints(CenterlinePath centerline, Vector3 origin, Vector3 direction, float currentLength, float targetLength)
        {
            List<Transform> waypoints = new List<Transform>(centerline.waypoints);
            float nextDistance = Mathf.Ceil(currentLength / 1500f) * 1500f;
            if (nextDistance <= currentLength + 1f)
                nextDistance += 1500f;

            for (float d = nextDistance; d < targetLength; d += 1500f)
                waypoints.Add(CreateRuntimeWaypoint(centerline.transform, "WP_Ext_" + Mathf.RoundToInt(d).ToString("00000"), origin + direction * d));

            waypoints.Add(CreateRuntimeWaypoint(centerline.transform, "WP_Ext_End", origin + direction * targetLength));
            centerline.waypoints = waypoints.ToArray();
            centerline.closedLoop = false;
        }

        private static Transform CreateRuntimeWaypoint(Transform parent, string name, Vector3 position)
        {
            GameObject waypoint = new GameObject(name);
            waypoint.transform.SetParent(parent, true);
            waypoint.transform.position = position;
            return waypoint.transform;
        }

        private static int CreateDashedLaneLine(Transform parent, Vector3 origin, Vector3 right, Vector3 direction, float startDistance, float chunkLength, float lateralOffset, Material material)
        {
            const float DashLength = 9f;
            const float DashGap = 15f;
            int dashCount = 0;
            for (float local = 0f; local < chunkLength; local += DashLength + DashGap)
            {
                float dashLength = Mathf.Min(DashLength, chunkLength - local);
                Vector3 center = origin + direction * (startDistance + local + dashLength * 0.5f) + right * lateralOffset + Vector3.up * 0.055f;
                CreateRuntimeBox("Extended Lane Dash", center, right, direction, 0.18f, 0.02f, dashLength, material, parent, false);
                dashCount++;
            }

            return dashCount;
        }

        private static void CreateRuntimeBox(
            string name,
            Vector3 position,
            Vector3 right,
            Vector3 forward,
            float width,
            float height,
            float length,
            Material material,
            Transform parent,
            bool colliderEnabled,
            bool rendererEnabled = true)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, true);
            box.transform.position = position;
            box.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            box.transform.localScale = new Vector3(width, height, length);

            Renderer renderer = box.GetComponent<Renderer>();
            if (renderer != null && rendererEnabled)
            {
                renderer.sharedMaterial = material;
            }
            else if (renderer != null)
            {
                renderer.enabled = false;
            }

            Collider collider = box.GetComponent<Collider>();
            if (collider != null)
                collider.enabled = colliderEnabled;
        }

        private static Material CreateRuntimeMaterial(string name, string sourceAssetPath, Color fallbackColor)
        {
            Material sourceMaterial = LoadRuntimeSourceMaterial(sourceAssetPath);
            Material material;
            string sourceDescription;

            if (sourceMaterial != null)
            {
                material = new Material(sourceMaterial);
                sourceDescription = sourceAssetPath;
            }
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                sourceDescription = "fallback shader Universal Render Pipeline/Lit";
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                    sourceDescription = "last-resort fallback shader Standard";
                }

                if (shader == null)
                {
                    Debug.LogWarning("[CarFollowingBootstrap] Could not find URP/Lit or Standard shader for runtime material '" + name + "'. Leaving Unity default material in place where possible.");
                    return null;
                }

                Debug.LogWarning("[CarFollowingBootstrap] Runtime highway material asset missing or unavailable: '" + sourceAssetPath + "'. Using " + sourceDescription + " for '" + name + "'.");
                material = new Material(shader);
                ApplyMaterialColor(material, fallbackColor);
            }

            material.name = name;
            Debug.Log("[CarFollowingBootstrap] Runtime highway material '" + name + "' uses " + sourceDescription + ".");
            return material;
        }

        private static Material LoadRuntimeSourceMaterial(string assetPath)
        {
#if UNITY_EDITOR
            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
                Debug.LogWarning("[CarFollowingBootstrap] Runtime highway material asset not found: '" + assetPath + "'.");
            return material;
#else
            return null;
#endif
        }

        private static void ApplyMaterialColor(Material material, Color color)
        {
            if (material == null)
                return;

            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }

        private static float GetRuntimeExtensionDurationBasisSeconds(float minimumBlockSeconds, out string basisLabel)
        {
            float longestClipSeconds = GetLongestConfiguredBlockClipLengthSeconds();
            if (longestClipSeconds > 0f)
            {
                if (longestClipSeconds >= minimumBlockSeconds)
                    basisLabel = "longest configured block AudioClip";
                else
                    basisLabel = "minimumExperimentalBlockSeconds; longest configured block AudioClip is shorter";
                return Mathf.Max(minimumBlockSeconds, longestClipSeconds);
            }

            basisLabel = "minimumExperimentalBlockSeconds fallback";
            return minimumBlockSeconds;
        }

        private static float GetLongestConfiguredBlockClipLengthSeconds()
        {
            float longest = 0f;
#if UNITY_EDITOR
            longest = Mathf.Max(longest, GetEditorAudioClipLengthSeconds(SlowFastBlockClipPath));
            longest = Mathf.Max(longest, GetEditorAudioClipLengthSeconds(FastSlowBlockClipPath));
            longest = Mathf.Max(longest, GetEditorAudioClipLengthSeconds(ControlStableBlockClipPath));
#else
            longest = Mathf.Max(longest, GetResourceAudioClipLengthSeconds(SlowFastBlockResourcesPath));
            longest = Mathf.Max(longest, GetResourceAudioClipLengthSeconds(FastSlowBlockResourcesPath));
            longest = Mathf.Max(longest, GetResourceAudioClipLengthSeconds(ControlStableBlockResourcesPath));
#endif
            return longest;
        }

#if UNITY_EDITOR
        private static float GetEditorAudioClipLengthSeconds(string assetPath)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            return clip != null ? clip.length : 0f;
        }
#endif

        private static float GetResourceAudioClipLengthSeconds(string resourcesPath)
        {
            AudioClip clip = Resources.Load<AudioClip>(resourcesPath);
            return clip != null ? clip.length : 0f;
        }

        private static string FormatVector(Vector3 value)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "({0:F1}, {1:F1}, {2:F1})",
                value.x,
                value.y,
                value.z);
        }

        private static float GetCenterlineLength(CenterlinePath centerline)
        {
            if (centerline == null || centerline.waypoints == null || centerline.waypoints.Length < 2)
                return 0f;

            float length = 0f;
            for (int i = 0; i < centerline.waypoints.Length - 1; i++)
            {
                Transform a = centerline.waypoints[i];
                Transform b = centerline.waypoints[i + 1];
                if (a != null && b != null)
                    length += Vector3.Distance(a.position, b.position);
            }

            return length;
        }

        private static void DisableVppDebugOverlays(GameObject vehicle)
        {
            // Telemetry overlays are useful while tuning, but they should not
            // appear in the participant-facing experiment.
            if (vehicle == null)
                return;

            MonoBehaviour[] behaviours = vehicle.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour.GetType().FullName != "VehiclePhysics.VPTelemetry")
                    continue;

                SetBoolField(behaviour, "showTelemetry", false);
                SetBoolField(behaviour, "enableHotKey", false);
                SetBoolField(behaviour, "showCenterOfMass", false);
                SetBoolField(behaviour, "showWheelGizmos", false);
                SetBoolField(behaviour, "showLocalFrame", false);
                SetBoolField(behaviour, "showContactPoints", false);
                SetBoolField(behaviour, "showTireSlip", false);
                SetBoolField(behaviour, "showTireForces", false);
            }
        }

        private static void SetupVppPhysicalVehicle(GameObject vehicle, CenterlinePath centerline)
        {
            // Keep VPP in control: this method only resets pose/rigidbody state
            // and camera placement at the start of the scene.
            Vector3[] points = GetPathPoints(centerline);
            if (points.Length >= 2)
            {
                Vector3 tangent = (points[1] - points[0]).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
                Vector3 position = points[0] + right * ParticipantRightLaneOffsetMeters + Vector3.up * ParticipantSpawnHeightOffsetMeters;
                if (TryProjectVehicleToRoadSurface(position, ParticipantSpawnHeightOffsetMeters, vehicle.transform, out Vector3 groundedPosition))
                    position = groundedPosition;

                Quaternion rotation = Quaternion.LookRotation(tangent, Vector3.up);
                vehicle.transform.SetPositionAndRotation(position, rotation);

                Rigidbody poseBody = vehicle.GetComponent<Rigidbody>();
                if (poseBody != null)
                {
                    poseBody.position = position;
                    poseBody.rotation = rotation;
                }

                Physics.SyncTransforms();
            }

            Rigidbody rb = vehicle.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.detectCollisions = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            VppExternalInputBridge bridge = vehicle.GetComponent<VppExternalInputBridge>();
            if (bridge == null)
                bridge = vehicle.AddComponent<VppExternalInputBridge>();

            FanatecSteeringWheelVisualSync fanatecWheelVisual = vehicle.GetComponent<FanatecSteeringWheelVisualSync>();
            if (fanatecWheelVisual == null)
                fanatecWheelVisual = vehicle.AddComponent<FanatecSteeringWheelVisualSync>();
            fanatecWheelVisual.inputBridge = bridge;
            fanatecWheelVisual.fanatecProvider = vehicle.GetComponent<FanatecHidInputProvider>();

            VppBuiltInVehicleControls builtInControls = vehicle.GetComponent<VppBuiltInVehicleControls>();
            if (builtInControls == null)
                vehicle.AddComponent<VppBuiltInVehicleControls>();

            ResearchSimDebugInfoToggle debugInfoToggle = vehicle.GetComponent<ResearchSimDebugInfoToggle>();
            if (debugInfoToggle == null)
                vehicle.AddComponent<ResearchSimDebugInfoToggle>();

            NightLightingToggle nightLightingToggle = vehicle.GetComponent<NightLightingToggle>();
            if (nightLightingToggle == null)
                vehicle.AddComponent<NightLightingToggle>();



            AudioSource[] vehicleAudioSources = vehicle.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < vehicleAudioSources.Length; i++)
            {
                AudioSource source = vehicleAudioSources[i];
                if (source == null)
                    continue;

                source.playOnAwake = false;
                if (source.isPlaying)
                    source.Stop();
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            Transform driverHead = FindDescendant(vehicle.transform, "DriverHead");
            camera.transform.SetParent(driverHead != null ? driverHead : vehicle.transform, false);
            camera.transform.localPosition = Vector3.zero;
            camera.transform.localRotation = Quaternion.identity;
            camera.fieldOfView = 62f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 4500f;
        }

        private static void RepairInvisibleResearchVehicle(CenterlinePath centerline)
        {
            // Fallback for older scene states that still contain the simple
            // placeholder vehicle instead of the VPP prefab.
            GameObject vehicle = GameObject.Find("Research Vehicle Placeholder - Replace With RCC Prefab");
            if (vehicle == null)
                return;

            DestroyChild(vehicle.transform, "RCC Prototype Visual");
            DestroyChild(vehicle.transform, "Vehicle Body");
            DestroyChild(vehicle.transform, "Cabin Reference");

            SteeringWheelVisual steeringWheel = vehicle.GetComponent<SteeringWheelVisual>();
            if (steeringWheel != null)
                Object.Destroy(steeringWheel);

            Vector3[] points = GetPathPoints(centerline);
            if (points.Length >= 2)
            {
                Vector3 tangent = (points[1] - points[0]).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
                Vector3 position = points[0] + right * ParticipantRightLaneOffsetMeters + Vector3.up * 0.45f;
                if (TryProjectVehicleToRoadSurface(position, 0.45f, vehicle.transform, out Vector3 groundedPosition))
                    position = groundedPosition;

                Quaternion rotation = Quaternion.LookRotation(tangent, Vector3.up);
                vehicle.transform.SetPositionAndRotation(position, rotation);

                Rigidbody resetBody = vehicle.GetComponent<Rigidbody>();
                if (resetBody != null)
                {
                    resetBody.position = position;
                    resetBody.rotation = rotation;
                }

                Physics.SyncTransforms();
            }
            vehicle.transform.localScale = Vector3.one;

            Rigidbody rb = vehicle.GetComponent<Rigidbody>();
            if (rb == null)
                rb = vehicle.AddComponent<Rigidbody>();

            rb.useGravity = false;
            rb.isKinematic = true;
            rb.detectCollisions = false;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            HybridVehicleInput input = vehicle.GetComponent<HybridVehicleInput>();
            if (input == null)
                input = vehicle.AddComponent<HybridVehicleInput>();

            SimpleResearchVehicleController controller = vehicle.GetComponent<SimpleResearchVehicleController>();
            if (controller == null)
                controller = vehicle.AddComponent<SimpleResearchVehicleController>();

            controller.enabled = true;
            controller.visualBody = vehicle.transform.Find("VPP Cockpit Visual");
            EnsureCockpitFillLight(vehicle.transform);
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
            controller.snapToDriveSurface = true;
            controller.rideHeight = 0.45f;
            controller.groundProbeHeight = 6f;
            controller.groundProbeDistance = 80f;
            controller.steeringWheelVisual = FindDescendant(controller.visualBody, "SteeringWheel", "Steering_wheel");
            controller.steeringWheelMaxRotationDegrees = 70f;
            controller.steeringWheelRotationAxis = Vector3.forward;
            controller.speedGaugeRoot = FindDescendant(controller.visualBody, "Speed");
            controller.rpmGaugeRoot = FindDescendant(controller.visualBody, "Rpm", "RPM");
            controller.speedGaugeMaxKmh = 220f;
            controller.analogGaugeNeedleLength = 0.036f;
            controller.analogGaugeNeedleWidth = 0.0035f;
            controller.showDebugHud = true;

            ResearchSimDebugInfoToggle debugInfoToggle = vehicle.GetComponent<ResearchSimDebugInfoToggle>();
            if (debugInfoToggle == null)
                vehicle.AddComponent<ResearchSimDebugInfoToggle>();

            NightLightingToggle nightLightingToggle = vehicle.GetComponent<NightLightingToggle>();
            if (nightLightingToggle == null)
                vehicle.AddComponent<NightLightingToggle>();

            SimpleEngineAudio engineAudio = vehicle.GetComponent<SimpleEngineAudio>();
            if (engineAudio != null)
            {
                engineAudio.vehicleController = controller;
                engineAudio.inputSource = input;
                engineAudio.engineSource = FindAudioSource(vehicle.transform, "Engine");
                engineAudio.transmissionSource = FindAudioSource(vehicle.transform, "Transmission");
                engineAudio.windSource = FindAudioSource(vehicle.transform, "Wind");
            }

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
            camera.transform.localRotation = Quaternion.identity;
            camera.fieldOfView = 62f;
            camera.nearClipPlane = 0.03f;

            FirstPersonCameraBinder binder = vehicle.GetComponent<FirstPersonCameraBinder>();
            if (binder != null)
            {
                binder.targetCamera = camera;
                binder.localEyePosition = VppDriverEyePosition;
                binder.localEyeEulerAngles = Vector3.zero;
            }
        }

        private static Vector3[] GetPathPoints(CenterlinePath centerline)
        {
            if (centerline.waypoints == null)
                return new Vector3[0];

            Vector3[] points = new Vector3[centerline.waypoints.Length];
            for (int i = 0; i < centerline.waypoints.Length; i++)
            {
                Transform waypoint = centerline.waypoints[i];
                points[i] = waypoint != null ? new Vector3(waypoint.position.x, 0f, waypoint.position.z) : Vector3.zero;
            }

            return points;
        }

        private static bool TryProjectVehicleToRoadSurface(Vector3 position, float verticalOffset, Transform ignoreRoot, out Vector3 projected)
        {
            projected = position;
            Physics.SyncTransforms();

            Vector3 origin = new Vector3(position.x, position.y + RoadSurfaceRaycastHeightMeters, position.z);
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                RoadSurfaceRaycastDistanceMeters,
                ~0,
                QueryTriggerInteraction.Ignore);

            if (hits == null || hits.Length == 0)
                return false;

            float bestDistance = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null)
                    continue;

                Transform hitTransform = hit.collider.transform;
                if (ignoreRoot != null && (hitTransform == ignoreRoot || hitTransform.IsChildOf(ignoreRoot)))
                    continue;

                if (hit.distance >= bestDistance)
                    continue;

                bestDistance = hit.distance;
                projected = hit.point + Vector3.up * Mathf.Max(0.01f, verticalOffset);
                found = true;
            }

            return found;
        }

        private static void DestroyChild(Transform parent, string childName)
        {
            if (parent == null)
                return;

            Transform child = parent.Find(childName);
            if (child != null)
                Object.Destroy(child.gameObject);
        }

        private static void EnsureCockpitFillLight(Transform vehicleRoot)
        {
            if (vehicleRoot == null || vehicleRoot.Find("Cockpit Fill Light") != null)
                return;

            GameObject lightObject = new GameObject("Cockpit Fill Light");
            lightObject.transform.SetParent(vehicleRoot, false);
            lightObject.transform.localPosition = new Vector3(-0.2f, 1.45f, 0.7f);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.94f, 0.82f);
            light.intensity = 2.4f;
            light.range = 3.2f;
            light.shadows = LightShadows.None;
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

        private static void SetBoolField(object target, string fieldName, bool value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(bool))
                field.SetValue(target, value);
        }

        private static void DestroyObjectsStartingWith(string namePrefix)
        {
            GameObject[] objects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject candidate = objects[i];
                if (candidate != null && candidate.name.StartsWith(namePrefix))
                    Object.Destroy(candidate);
            }
        }
    }
}
