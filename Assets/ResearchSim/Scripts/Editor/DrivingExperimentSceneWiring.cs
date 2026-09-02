using System;
using System.Collections.Generic;
using System.IO;
using ResearchSim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UXF;
using UXF.UI;

namespace ResearchSim.EditorTools
{
    /// <summary>
    /// Editor-only helper that keeps the highway scene connected to UXF,
    /// telemetry, audio sources and the research vehicle. It should only fill
    /// missing references/defaults; experiment parameters are edited on the
    /// DrivingExperimentManager component.
    /// </summary>
    [InitializeOnLoad]
    public static class DrivingExperimentSceneWiring
    {
        private const string HighwayScenePath = "Assets/ResearchSim/Scenes/HighwayStraight.unity";
        private const string UxfRigPrefabPath = "Assets/UXF/Prefabs/[UXF_Rig].prefab";
        private const string SlowTempoClipPath = "Assets/ResearchSim/Audio/Music_slow.mp3";
        private const string FastTempoClipPath = "Assets/ResearchSim/Audio/Music_fast.mp3";
        private const string ManagerObjectName = "Driving Experiment Manager";
        private const string VehicleObjectName = "Research VPP Vehicle";

        static DrivingExperimentSceneWiring()
        {
            EditorApplication.delayCall += WireIfNeeded;
        }

        [MenuItem("ResearchSim/Wire UXF Driving Experiment")]
        public static void WireMenu()
        {
            WireScene(forceSave: true);
        }

        private static void WireIfNeeded()
        {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (!File.Exists(HighwayScenePath) || AssetDatabase.LoadAssetAtPath<GameObject>(UxfRigPrefabPath) == null)
                return;

            WireScene(forceSave: false);
        }

        private static void WireScene(bool forceSave)
        {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            Scene previousScene = EditorSceneManager.GetActiveScene();
            string previousScenePath = previousScene.path;
            bool restorePreviousScene = !string.IsNullOrEmpty(previousScenePath) && previousScenePath != HighwayScenePath;

            Scene scene = EditorSceneManager.OpenScene(HighwayScenePath, OpenSceneMode.Single);
            bool changed = false;

            GameObject rig = GameObject.Find("[UXF_Rig]");
            if (rig == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UxfRigPrefabPath);
                if (prefab != null)
                {
                    rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    rig.name = "[UXF_Rig]";
                    changed = true;
                }
            }

            if (rig == null)
            {
                rig = new GameObject("[UXF_Rig]");
                changed = true;
            }

            Session session = rig.GetComponentInChildren<Session>(true);
            if (session == null)
            {
                session = rig.AddComponent<Session>();
                changed = true;
            }

            changed |= ConfigureSession(session);
            UIController uiController = rig.GetComponentInChildren<UIController>(true);
            if (uiController != null)
                changed |= ConfigureExperimenterUi(uiController);

            GameObject managerObject = GameObject.Find(ManagerObjectName);
            if (managerObject == null)
            {
                managerObject = new GameObject(ManagerObjectName);
                changed = true;
            }
            changed |= SetTransformPosition(managerObject.transform, new Vector3(0f, -1000f, -1000f));

            DrivingExperimentManager manager = managerObject.GetComponent<DrivingExperimentManager>();
            if (manager == null)
            {
                manager = managerObject.AddComponent<DrivingExperimentManager>();
                changed = true;
            }

            GameObject vehicle = GameObject.Find(VehicleObjectName);
            CenterlinePath centerline = UnityEngine.Object.FindAnyObjectByType<CenterlinePath>();
            Rigidbody rb = vehicle != null ? vehicle.GetComponent<Rigidbody>() : null;

            AudioSource musicSource = managerObject.GetComponent<AudioSource>();
            if (musicSource == null)
            {
                musicSource = managerObject.AddComponent<AudioSource>();
                changed = true;
            }

            AudioSource transitionMusicSource = null;
            AudioSource[] musicSources = managerObject.GetComponents<AudioSource>();
            for (int i = 0; i < musicSources.Length; i++)
            {
                if (musicSources[i] != musicSource)
                {
                    transitionMusicSource = musicSources[i];
                    break;
                }
            }

            if (transitionMusicSource == null)
            {
                transitionMusicSource = managerObject.AddComponent<AudioSource>();
                changed = true;
            }
            changed |= ConfigureExperimentAudioSource(musicSource);
            changed |= ConfigureExperimentAudioSource(transitionMusicSource);

            AudioClip slowTempoClip = AssetDatabase.LoadAssetAtPath<AudioClip>(SlowTempoClipPath);
            AudioClip fastTempoClip = AssetDatabase.LoadAssetAtPath<AudioClip>(FastTempoClipPath);

            changed |= SetObject(ref manager.session, session);
            changed |= SetObject(ref manager.vehicleRoot, vehicle);
            changed |= SetObject(ref manager.vehicleRigidbody, rb);
            changed |= SetObject(ref manager.centerline, centerline);
            changed |= SetObject(ref manager.musicSource, musicSource);
            changed |= SetObject(ref manager.transitionMusicSource, transitionMusicSource);
            changed |= SetObjectIfNull(ref manager.slowTempoClip, slowTempoClip);
            changed |= SetObjectIfNull(ref manager.fastTempoClip, fastTempoClip);
            changed |= SetFloatIfInvalid(ref manager.trialDurationSeconds, 120f);
            changed |= SetFloatIfInvalid(ref manager.practiceDurationSeconds, 120f);
            changed |= SetFloatIfInvalid(ref manager.maximumTrialDurationSeconds, manager.trialDurationSeconds + 30f);
            changed |= SetFloatIfInvalid(ref manager.interTrialBreakSeconds, 5f);
            changed |= SetFloatIfInvalid(ref manager.blockStartNoticeSeconds, 5f);

            if (vehicle != null)
                changed |= ConfigureTelemetryTracker(vehicle, rb, centerline, session);

            if (changed || forceSave)
            {
                EditorUtility.SetDirty(rig);
                EditorUtility.SetDirty(managerObject);
                if (vehicle != null)
                    EditorUtility.SetDirty(vehicle);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("ResearchSim UXF driving experiment wiring complete.");
            }

            if (restorePreviousScene)
                EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
        }

        private static bool ConfigureSession(Session session)
        {
            // UXF settings and headers define the CSV schema used for analysis.
            bool changed = false;
            changed |= SetString(ref session.experimentName, "DrivingTempoStability");
            changed |= SetBool(ref session.endAfterLastTrial, false);

            changed |= AddUnique(session.settingsToLog, "audio_condition");
            changed |= AddUnique(session.settingsToLog, "condition_order_index");
            changed |= AddUnique(session.settingsToLog, "music_present");
            changed |= AddUnique(session.settingsToLog, "trial_duration_seconds");
            changed |= AddUnique(session.settingsToLog, "tempo_change_time_seconds");
            changed |= AddUnique(session.settingsToLog, "practice_trial");

            changed |= AddUnique(session.customHeaders, "tempo_change_planned_time");
            changed |= AddUnique(session.customHeaders, "tempo_change_timestamp");
            changed |= AddUnique(session.customHeaders, "tempo_change_dsp_timestamp");
            changed |= AddUnique(session.customHeaders, "tempo_change_audio_time");
            changed |= AddUnique(session.customHeaders, "tempo_change_occurred");
            changed |= AddUnique(session.customHeaders, "tempo_event_label");
            changed |= AddUnique(session.customHeaders, "audio_clip_name");
            changed |= AddUnique(session.customHeaders, "audio_clip_missing");
            changed |= AddUnique(session.customHeaders, "audio_mode");
            changed |= AddUnique(session.customHeaders, "audio_first_clip_name");
            changed |= AddUnique(session.customHeaders, "audio_second_clip_name");
            changed |= AddUnique(session.customHeaders, "practice_trial");
            changed |= AddUnique(session.customHeaders, "trial_duration_planned");
            changed |= AddUnique(session.customHeaders, "trial_end_reason");
            changed |= AddUnique(session.customHeaders, "final_speed_kmh");
            changed |= AddUnique(session.customHeaders, "final_position_x");
            changed |= AddUnique(session.customHeaders, "final_position_z");

            FileSaver fileSaver = session.GetComponent<FileSaver>();
            if (fileSaver == null)
            {
                fileSaver = session.gameObject.AddComponent<FileSaver>();
                changed = true;
            }

            Directory.CreateDirectory(ResearchDataPaths.UxfDataRoot);
            changed |= SetEnum(ref fileSaver.dataSaveLocation, DataSaveLocation.Fixed);
            changed |= SetString(ref fileSaver.storagePath, ResearchDataPaths.RelativeUxfDataRoot);
            fileSaver.active = true;

            if (session.dataHandlers == null || Array.IndexOf(session.dataHandlers, fileSaver) < 0)
            {
                session.dataHandlers = new DataHandler[] { fileSaver };
                changed = true;
            }

            return changed;
        }

        private static bool ConfigureExperimenterUi(UIController uiController)
        {
            // Participant fields are intentionally minimal and match the
            // project documentation.
            bool changed = false;
            changed |= SetString(ref uiController.experimentName, "DrivingTempoStability");
            changed |= SetEnum(ref uiController.startupMode, StartupMode.BuiltInUI);
            changed |= SetEnum(ref uiController.settingsMode, SettingsMode.Empty);
            changed |= SetEnum(ref uiController.ppidMode, PPIDMode.AcquireFromUI);
            changed |= SetEnum(ref uiController.sessionNumMode, SessionNumMode.AlwaysSession1);
            changed |= SetString(
                ref uiController.termsAndConditions,
                "I confirm that informed consent has been collected and that this participant meets the inclusion criteria.");

            changed |= EnsureParticipantField(uiController.participantDataPoints, "Age", "age", FormDataType.Int);
            changed |= EnsureParticipantField(uiController.participantDataPoints, "Gender", "gender", FormDataType.DropDown, "Female", "Male", "Non-binary", "Prefer not to say");
            changed |= EnsureParticipantField(uiController.participantDataPoints, "Driving experience years", "driving_experience_years", FormDataType.Float);
            changed |= EnsureParticipantField(uiController.participantDataPoints, "In-car music listening frequency", "in_car_music_frequency", FormDataType.DropDown, "Never", "Rarely", "Sometimes", "Often", "Always");

            return changed;
        }

        private static bool EnsureParticipantField(List<FormElementEntry> fields, string displayName, string internalName, FormDataType type, params string[] options)
        {
            FormElementEntry entry = fields.Find(candidate => candidate.internalName == internalName);
            if (entry == null)
            {
                entry = new FormElementEntry();
                fields.Add(entry);
            }

            bool changed = false;
            changed |= SetString(ref entry.displayName, displayName);
            changed |= SetString(ref entry.internalName, internalName);
            changed |= SetEnum(ref entry.dataType, type);

            if (options.Length > 0)
            {
                List<string> desiredOptions = new List<string>(options);
                if (entry.dropDownOptions.Count != desiredOptions.Count)
                {
                    entry.dropDownOptions = desiredOptions;
                    changed = true;
                }
                else
                {
                    for (int i = 0; i < desiredOptions.Count; i++)
                    {
                        if (entry.dropDownOptions[i] == desiredOptions[i])
                            continue;

                        entry.dropDownOptions = desiredOptions;
                        changed = true;
                        break;
                    }
                }
            }

            return changed;
        }

        private static bool ConfigureTelemetryTracker(GameObject vehicle, Rigidbody rb, CenterlinePath centerline, Session session)
        {
            // Track speed, position and lane offset from the VPP vehicle for
            // every UXF trial.
            bool changed = false;
            CarTelemetryTracker tracker = vehicle.GetComponent<CarTelemetryTracker>();
            if (tracker == null)
            {
                tracker = vehicle.AddComponent<CarTelemetryTracker>();
                changed = true;
            }

            changed |= SetObject(ref tracker.vehicleRigidbody, rb);
            changed |= SetObject(ref tracker.centerline, centerline);
            changed |= SetString(ref tracker.objectName, "research_vehicle");
            changed |= SetEnum(ref tracker.updateType, TrackerUpdateType.FixedUpdate);

            if (!session.trackedObjects.Contains(tracker))
            {
                session.trackedObjects.Add(tracker);
                changed = true;
            }

            return changed;
        }

        private static bool ConfigureExperimentAudioSource(AudioSource source)
        {
            // Experimental music is 2D and should never play on scene load. A
            // short max distance keeps the Unity AudioSource gizmo out of the
            // visible highway when Game view gizmos are enabled.
            bool changed = false;
            if (source.playOnAwake)
            {
                source.playOnAwake = false;
                changed = true;
            }
            if (source.loop)
            {
                source.loop = false;
                changed = true;
            }
            if (!Mathf.Approximately(source.spatialBlend, 0f))
            {
                source.spatialBlend = 0f;
                changed = true;
            }
            if (!Mathf.Approximately(source.maxDistance, 1f))
            {
                source.maxDistance = 1f;
                changed = true;
            }
            if (!Mathf.Approximately(source.dopplerLevel, 0f))
            {
                source.dopplerLevel = 0f;
                changed = true;
            }
            return changed;
        }

        private static bool AddUnique(List<string> list, string value)
        {
            if (list.Contains(value))
                return false;

            list.Add(value);
            return true;
        }

        private static bool SetObject<T>(ref T current, T value) where T : UnityEngine.Object
        {
            if (current == value)
                return false;

            current = value;
            return true;
        }

        private static bool SetObjectIfNull<T>(ref T current, T value) where T : UnityEngine.Object
        {
            if (current != null || value == null)
                return false;

            current = value;
            return true;
        }

        private static bool SetString(ref string current, string value)
        {
            if (current == value)
                return false;

            current = value;
            return true;
        }

        private static bool SetBool(ref bool current, bool value)
        {
            if (current == value)
                return false;

            current = value;
            return true;
        }

        private static bool SetFloatIfInvalid(ref float current, float value)
        {
            if (current > 0f)
                return false;

            current = value;
            return true;
        }

        private static bool SetTransformPosition(Transform transform, Vector3 value)
        {
            if (transform.position == value)
                return false;

            transform.position = value;
            return true;
        }

        private static bool SetEnum<T>(ref T current, T value) where T : struct, Enum
        {
            if (EqualityComparer<T>.Default.Equals(current, value))
                return false;

            current = value;
            return true;
        }
    }
}
