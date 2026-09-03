using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UXF;

namespace ResearchSim
{
    /// <summary>
    /// Coordinates the psychology experiment: UXF block creation, randomized
    /// conditions, music scheduling, block timing, trial transitions and the
    /// small experiment HUD. Edit the public fields on this component to change
    /// the block duration and the audio clips used by the study.
    /// </summary>
    public sealed class DrivingExperimentManager : MonoBehaviour
    {
        private const string ConditionBaseline = "Baseline";
        private const string ConditionTempoIncrease = "Tempo_Increase";
        private const string ConditionTempoDecrease = "Tempo_Decrease";
        private const string ConditionPractice = "Practice";
        private const float DefaultExperimentalTrialDurationSeconds = 120f;
        private const float DefaultSafetyBufferSeconds = 30f;

        private static readonly string[] Conditions =
        {
            ConditionBaseline,
            ConditionTempoIncrease,
            ConditionTempoDecrease
        };

        [Header("Experiment Configuration - edit here")]
        [Tooltip("Duration of every experimental block. The music transition is automatically scheduled at half of this value.")]
        [Min(5f)] public float trialDurationSeconds = DefaultExperimentalTrialDurationSeconds;
        [Tooltip("Practice duration. Practice data are not saved.")]
        [Min(30f)] public float practiceDurationSeconds = DefaultExperimentalTrialDurationSeconds;
        [Tooltip("Safety timeout used only if UXF does not end the trial normally. Keep this above Trial Duration.")]
        [Min(1f)] public float maximumTrialDurationSeconds = DefaultExperimentalTrialDurationSeconds + DefaultSafetyBufferSeconds;
        [Tooltip("Slow one-minute music segment used for slow-to-fast and fast-to-slow conditions.")]
        public AudioClip slowTempoClip;
        [Tooltip("Fast one-minute music segment used for slow-to-fast and fast-to-slow conditions.")]
        public AudioClip fastTempoClip;
        [Tooltip("Optional precomposed slow-to-fast clip. Leave empty to use Slow Tempo Clip followed by Fast Tempo Clip.")]
        public AudioClip tempoIncreaseClip;
        [Tooltip("Optional precomposed fast-to-slow clip. Leave empty to use Fast Tempo Clip followed by Slow Tempo Clip.")]
        public AudioClip tempoDecreaseClip;

        [Header("UXF")]
        public Session session;
        public bool beginFirstTrialAutomatically = true;
        [Min(5f)] public float interTrialBreakSeconds = 5f;
        public bool includePracticeTrial = true;
        public bool showExperimentHud = true;
        public bool allowQuickStartForTesting = true;
        public KeyCode quickStartKey = KeyCode.F8;
        public KeyCode skipTrialForTestingKey = KeyCode.F9;
        public KeyCode resetExperimentKey = KeyCode.R;
        public KeyCode quitKey = KeyCode.Escape;
        public string quickStartParticipantId = "TEST";
        [Min(0f)] public float blockStartNoticeSeconds = 5f;

        [Header("Timing")]
        // Derived from Trial Duration and hidden to keep duration editing in one place.
        [HideInInspector] public float tempoChangeTimeSeconds = DefaultExperimentalTrialDurationSeconds * 0.5f;
        public bool useBaselineMidpointAsReferenceEvent = true;
        public int randomizationSeedOverride;

        [Header("Audio")]
        public AudioSource musicSource;
        public AudioSource transitionMusicSource;
        [Range(0f, 1f)] public float musicVolume = 0.75f;
        public bool abortTrialIfMusicClipMissing = true;

        [Header("Vehicle")]
        public GameObject vehicleRoot;
        public Rigidbody vehicleRigidbody;
        public MonoBehaviour vppStandardInput;

        [Header("Route")]
        public CenterlinePath centerline;
        public float endpointToleranceMeters = 40f;
        public bool endTrialAtRouteEndpoint = false;

        private Coroutine tempoEventCoroutine;
        private Coroutine trialEndCoroutine;
        private bool advancingTrial;
        private bool segmentedAudioActive;
        private double scheduledTempoChangeDspTime;
        private string currentPhaseLabel = "Idle";
        private string currentConditionCode = string.Empty;
        private string currentConditionLabel = string.Empty;
        private string currentAudioLabel = string.Empty;
        private float currentTrialEndTime;
        private float currentTempoChangeTime;
        private string blockStartNoticeLabel = string.Empty;
        private float blockStartNoticeEndTime;
        private string breakCountdownLabel = string.Empty;
        private float breakCountdownEndTime;
        private bool experimentCompleted;
        private GUIStyle experimentHudBoxStyle;
        private GUIStyle experimentHudLabelStyle;

        private void Awake()
        {
            RefreshDerivedTiming();
            ResolveReferences();
            ConfigurePortableDataSaving();
        }

        private void OnValidate()
        {
            RefreshDerivedTiming();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ConfigurePortableDataSaving();
            if (session == null)
                return;

            session.onSessionBegin.AddListener(BuildSession);
            session.onTrialBegin.AddListener(OnTrialBegin);
            session.onTrialEnd.AddListener(OnTrialEnd);
        }

        private void OnDisable()
        {
            if (session == null)
                return;

            session.onSessionBegin.RemoveListener(BuildSession);
            session.onTrialBegin.RemoveListener(OnTrialBegin);
            session.onTrialEnd.RemoveListener(OnTrialEnd);
        }

        private void Update()
        {
            if (Input.GetKeyDown(quitKey))
            {
                QuitApplication();
                return;
            }

            if (experimentCompleted)
            {
                if (Input.GetKeyDown(resetExperimentKey))
                    ResetExperimentForNextParticipant();

                return;
            }

            if (!allowQuickStartForTesting || session == null)
                return;

            if (!session.hasInitialised && Input.GetKeyDown(quickStartKey))
                QuickStartSessionForTesting();

            if (Input.GetKeyDown(skipTrialForTestingKey) && TryGetActiveTrial(out Trial currentTrial))
                EndTrial(currentTrial, "manual_test_skip");
        }

        public void BuildSession(Session activeSession)
        {
            RefreshDerivedTiming();
            session = activeSession;
            session.blocks.Clear();
            session.endAfterLastTrial = false;

            EnsureUxFHeaders(session);

            // UXF blocks are rebuilt at session start so every participant gets
            // a clean randomized order using the current Inspector settings.
            int seed = randomizationSeedOverride != 0
                ? randomizationSeedOverride
                : GenerateSessionRandomSeed();

            string[] randomizedConditions = GetRandomizedConditions(seed);
            float experimentalTempoChangeTime = GetMidpointTime(trialDurationSeconds);
            float practiceTempoChangeTime = GetMidpointTime(practiceDurationSeconds);
            session.settings.SetValue("randomization_seed", seed);
            session.settings.SetValue("condition_order", string.Join("|", randomizedConditions));
            session.settings.SetValue("trial_duration_seconds", trialDurationSeconds);
            session.settings.SetValue("tempo_change_time_seconds", experimentalTempoChangeTime);
            session.settings.SetValue("practice_duration_seconds", practiceDurationSeconds);

            if (includePracticeTrial)
            {
                Block practiceBlock = session.CreateBlock(1);
                Trial practiceTrial = practiceBlock.firstTrial;
                practiceBlock.saveData = false;
                practiceTrial.saveData = false;
                practiceBlock.settings.SetValue("audio_condition", ConditionPractice);
                practiceBlock.settings.SetValue("condition_order_index", 0);
                practiceTrial.settings.SetValue("audio_condition", ConditionPractice);
                practiceTrial.settings.SetValue("condition_order_index", 0);
                practiceTrial.settings.SetValue("trial_duration_seconds", practiceDurationSeconds);
                practiceTrial.settings.SetValue("tempo_change_time_seconds", practiceTempoChangeTime);
                practiceTrial.settings.SetValue("music_present", false);
                practiceTrial.settings.SetValue("practice_trial", true);
            }

            for (int i = 0; i < randomizedConditions.Length; i++)
            {
                string condition = randomizedConditions[i];
                Block block = session.CreateBlock(1);
                Trial trial = block.firstTrial;

                block.settings.SetValue("audio_condition", condition);
                block.settings.SetValue("condition_order_index", i + 1);

                trial.settings.SetValue("audio_condition", condition);
                trial.settings.SetValue("condition_order_index", i + 1);
                trial.settings.SetValue("trial_duration_seconds", trialDurationSeconds);
                trial.settings.SetValue("tempo_change_time_seconds", experimentalTempoChangeTime);
                trial.settings.SetValue("music_present", condition != ConditionBaseline);
                trial.settings.SetValue("practice_trial", false);
            }

            Debug.Log("Driving experiment session built. Practice: " + includePracticeTrial + ". Experimental order: " + string.Join(", ", randomizedConditions));

            if (beginFirstTrialAutomatically)
                session.FirstTrial.Begin();
        }

        private void QuickStartSessionForTesting()
        {
            if (session == null || session.hasInitialised)
                return;

            ConfigurePortableDataSaving();

            // Used only for local checks: bypass the UXF startup form without
            // changing the real participant workflow.
            Canvas[] canvases = session.GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
                canvases[i].enabled = false;

            session.Begin("DrivingTempoStability_TEST", quickStartParticipantId, 1);
        }

        public void OnTrialBegin(Trial trial)
        {
            StopActiveCoroutines();
            ResolveReferences();
            ResetVehicleToStart();
            experimentCompleted = false;
            breakCountdownLabel = string.Empty;

            // The transition is intentionally derived from the trial duration:
            // changing Trial Duration is the single timing edit needed.
            string condition = trial.settings.GetString("audio_condition", ConditionBaseline);
            float duration = trial.settings.GetFloat("trial_duration_seconds", trialDurationSeconds);
            float tempoTime = GetMidpointTime(duration);
            bool isPractice = trial.settings.GetBool("practice_trial", false);

            trial.result["audio_condition"] = condition;
            trial.result["condition_order_index"] = trial.settings.GetInt("condition_order_index", trial.block.number);
            trial.result["tempo_change_planned_time"] = tempoTime;
            trial.result["tempo_change_occurred"] = false;
            trial.result["trial_duration_planned"] = duration;
            trial.result["trial_end_reason"] = string.Empty;
            trial.result["audio_clip_name"] = string.Empty;
            trial.result["audio_clip_missing"] = false;
            trial.result["audio_mode"] = string.Empty;
            trial.result["audio_first_clip_name"] = string.Empty;
            trial.result["audio_second_clip_name"] = string.Empty;
            trial.result["practice_trial"] = isPractice;

            currentPhaseLabel = isPractice ? "Practice - data not saved" : "Experimental block " + trial.result["condition_order_index"];
            currentConditionCode = condition;
            currentConditionLabel = GetDisplayConditionLabel(condition);
            currentAudioLabel = condition == ConditionBaseline || condition == ConditionPractice ? "No music" : "Music scheduled";
            currentTrialEndTime = Time.time + duration;
            currentTempoChangeTime = Time.time + tempoTime;
            ShowBlockStartNotice(trial, condition, duration, tempoTime, isPractice);

            bool audioReady = ConfigureAudio(condition, trial, tempoTime, duration);
            if (!audioReady && abortTrialIfMusicClipMissing)
            {
                trial.result["audio_clip_missing"] = true;
                Debug.LogError("Experimental audio clip is missing for condition: " + condition);
                StartCoroutine(EndInvalidTrialNextFrame(trial, "missing_audio_clip"));
                return;
            }

            bool shouldRecordReferenceEvent = condition != ConditionBaseline || useBaselineMidpointAsReferenceEvent;
            if (shouldRecordReferenceEvent && !isPractice)
                tempoEventCoroutine = StartCoroutine(RecordTempoEventAtAudioTime(trial, condition, tempoTime));

            trialEndCoroutine = StartCoroutine(EndTrialWhenComplete(trial, duration));
            Debug.Log("Driving trial begin: " + currentPhaseLabel + ", condition=" + condition + ", duration=" + duration + "s.");
        }

        public void OnTrialEnd(Trial trial)
        {
            StopActiveCoroutines();
            StopMusic();
            StopVehicleMotion();
            currentAudioLabel = "Stopped";

            if (session == null || session.isEnding || advancingTrial)
                return;

            advancingTrial = true;
            StartCoroutine(AdvanceAfterBreak(trial));
        }

        private IEnumerator RecordTempoEventAtAudioTime(Trial trial, string condition, float plannedAudioTime)
        {
            if (segmentedAudioActive)
            {
                while (IsTrialStillCurrentAndRunning(trial) && AudioSettings.dspTime < scheduledTempoChangeDspTime)
                    yield return null;
            }
            else if (condition == ConditionBaseline || musicSource == null || musicSource.clip == null)
            {
                yield return new WaitForSeconds(Mathf.Max(0f, plannedAudioTime));
            }
            else
            {
                while (IsTrialStillCurrentAndRunning(trial) && musicSource.time < plannedAudioTime)
                    yield return null;
            }

            if (trial.status != TrialStatus.InProgress)
                yield break;

            trial.result["tempo_change_timestamp"] = Time.time;
            trial.result["tempo_change_dsp_timestamp"] = AudioSettings.dspTime;
            trial.result["tempo_change_audio_time"] = segmentedAudioActive ? plannedAudioTime : (musicSource != null && musicSource.clip != null ? musicSource.time : plannedAudioTime);
            trial.result["tempo_change_occurred"] = condition != ConditionBaseline;
            trial.result["tempo_event_label"] = condition == ConditionBaseline ? "Baseline_Midpoint_Reference" : condition;
        }

        private IEnumerator EndTrialWhenComplete(Trial trial, float plannedDuration)
        {
            float start = Time.time;
            float hardLimit = Mathf.Max(maximumTrialDurationSeconds, plannedDuration);

            while (trial.status == TrialStatus.InProgress)
            {
                float elapsed = Time.time - start;
                if (elapsed >= plannedDuration)
                {
                    EndTrial(trial, "planned_duration");
                    yield break;
                }

                if (elapsed >= hardLimit)
                {
                    EndTrial(trial, "safety_time_limit");
                    yield break;
                }

                if (endTrialAtRouteEndpoint && HasReachedRouteEndpoint())
                {
                    EndTrial(trial, "route_endpoint");
                    yield break;
                }

                yield return null;
            }
        }

        private IEnumerator AdvanceAfterBreak(Trial completedTrial)
        {
            float breakDuration = Mathf.Max(5f, interTrialBreakSeconds);
            bool finalTrial = session != null && completedTrial == session.LastTrial;
            if (!finalTrial)
            {
                breakCountdownEndTime = Time.time + breakDuration;
                breakCountdownLabel = "PAUSA";
                while (Time.time < breakCountdownEndTime)
                    yield return null;
            }

            try
            {
                if (session != null && completedTrial != session.LastTrial)
                    session.BeginNextTrial();
                else if (session != null)
                {
                    experimentCompleted = true;
                    currentPhaseLabel = "Experiment complete";
                    currentConditionCode = string.Empty;
                    currentConditionLabel = "Finished";
                    currentAudioLabel = "Stopped";
                    blockStartNoticeLabel = "FINE ESPERIMENTO";
                    blockStartNoticeEndTime = Time.time + 3600f;
                    session.End();
                }
            }
            finally
            {
                breakCountdownLabel = string.Empty;
                advancingTrial = false;
            }
        }

        private void EndTrial(Trial trial, string reason)
        {
            if (trial.status != TrialStatus.InProgress)
                return;

            trial.result["trial_end_reason"] = reason;
            trial.result["final_speed_kmh"] = vehicleRigidbody != null ? GetVelocity(vehicleRigidbody).magnitude * 3.6f : 0f;
            trial.result["final_position_x"] = vehicleRoot != null ? vehicleRoot.transform.position.x : transform.position.x;
            trial.result["final_position_z"] = vehicleRoot != null ? vehicleRoot.transform.position.z : transform.position.z;
            trial.End();
        }

        private void ResetExperimentForNextParticipant()
        {
            StopActiveCoroutines();
            StopMusic();
            StopVehicleMotion();

            if (session != null && session.hasInitialised)
                session.End();

            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.name);
        }

        private void QuitApplication()
        {
            StopActiveCoroutines();
            StopMusic();
            StopVehicleMotion();

            if (session != null && session.hasInitialised)
                session.End();

            Application.Quit();
        }

        private IEnumerator EndInvalidTrialNextFrame(Trial trial, string reason)
        {
            yield return null;
            EndTrial(trial, reason);
        }

        private bool ConfigureAudio(string condition, Trial trial, float tempoTime, float duration)
        {
            if (musicSource == null)
                return condition == ConditionBaseline || condition == ConditionPractice;

            StopMusic();
            segmentedAudioActive = false;

            AudioClip clip = null;
            if (condition == ConditionTempoIncrease)
                clip = tempoIncreaseClip;
            else if (condition == ConditionTempoDecrease)
                clip = tempoDecreaseClip;

            if (condition == ConditionBaseline || condition == ConditionPractice)
            {
                trial.result["audio_mode"] = "silent_control";
                currentAudioLabel = "No music";
                return true;
            }

            if (clip == null)
                return ConfigureSegmentedAudio(condition, trial, tempoTime, duration);

            musicSource.clip = clip;
            musicSource.volume = musicVolume;
            musicSource.loop = false;
            musicSource.spatialBlend = 0f;
            musicSource.time = 0f;
            musicSource.Play();

            trial.result["audio_mode"] = "single_precomposed_clip";
            trial.result["audio_clip_name"] = clip.name;
            currentAudioLabel = "Playing " + clip.name;
            if (clip.length < tempoTime)
                Debug.LogWarning("Experimental audio clip is shorter than the planned tempo-change time: " + clip.name);

            return true;
        }

        private bool ConfigureSegmentedAudio(string condition, Trial trial, float tempoTime, float duration)
        {
            if (condition == ConditionBaseline || condition == ConditionPractice)
                return true;

            if (transitionMusicSource == null || slowTempoClip == null || fastTempoClip == null)
                return false;

            AudioClip firstClip = condition == ConditionTempoIncrease ? slowTempoClip : fastTempoClip;
            AudioClip secondClip = condition == ConditionTempoIncrease ? fastTempoClip : slowTempoClip;
            if (firstClip == null || secondClip == null)
                return false;

            // Schedule both sources on the DSP clock to avoid audible delay at
            // the tempo transition.
            double startDspTime = AudioSettings.dspTime + 0.2d;
            scheduledTempoChangeDspTime = startDspTime + tempoTime;
            segmentedAudioActive = true;

            musicSource.clip = firstClip;
            musicSource.volume = musicVolume;
            musicSource.loop = true;
            musicSource.spatialBlend = 0f;
            musicSource.time = 0f;

            transitionMusicSource.clip = secondClip;
            transitionMusicSource.volume = musicVolume;
            transitionMusicSource.loop = true;
            transitionMusicSource.spatialBlend = 0f;
            transitionMusicSource.time = 0f;

            double scheduledTrialEndDspTime = startDspTime + duration;
            musicSource.PlayScheduled(startDspTime);
            musicSource.SetScheduledEndTime(scheduledTempoChangeDspTime);
            transitionMusicSource.PlayScheduled(scheduledTempoChangeDspTime);
            transitionMusicSource.SetScheduledEndTime(scheduledTrialEndDspTime);

            trial.result["audio_mode"] = "segmented_scheduled_clips";
            trial.result["audio_clip_name"] = firstClip.name + " -> " + secondClip.name;
            trial.result["audio_first_clip_name"] = firstClip.name;
            trial.result["audio_second_clip_name"] = secondClip.name;
            currentAudioLabel = FormatDisplayClipSequence(firstClip, secondClip);

            if (firstClip.length < tempoTime)
                Debug.LogWarning("First experimental audio clip is shorter than the planned tempo-change time and will loop until the scheduled transition: " + firstClip.name);

            float secondSegmentDuration = Mathf.Max(0f, duration - tempoTime);
            if (secondClip.length < secondSegmentDuration)
                Debug.LogWarning("Second experimental audio clip is shorter than the remaining block duration and will loop until the scheduled block end: " + secondClip.name);

            return true;
        }

        private void StopMusic()
        {
            if (musicSource == null)
                return;

            musicSource.Stop();
            musicSource.clip = null;

            if (transitionMusicSource != null)
            {
                transitionMusicSource.Stop();
                transitionMusicSource.clip = null;
                transitionMusicSource.loop = false;
            }

            musicSource.loop = false;
            segmentedAudioActive = false;
        }

        private void ResetVehicleToStart()
        {
            if (vehicleRoot == null)
                return;

            // Reset only at trial boundaries. During driving, VPP remains in
            // charge of physics and input.
            Transform root = vehicleRoot.transform;
            Vector3 start = root.position;
            Quaternion rotation = root.rotation;

            if (centerline != null && centerline.waypoints != null && centerline.waypoints.Length >= 2)
            {
                Transform first = centerline.waypoints[0];
                Transform second = centerline.waypoints[1];
                if (first != null && second != null)
                {
                    Vector3 direction = second.position - first.position;
                    direction.y = 0f;
                    start = first.position + Vector3.up * 0.05f;
                    if (direction.sqrMagnitude > 0.01f)
                        rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                }
            }

            root.SetPositionAndRotation(start, rotation);
            StopVehicleMotion();
        }

        private void StopVehicleMotion()
        {
            if (vehicleRigidbody != null)
            {
#if UNITY_6000_0_OR_NEWER
                vehicleRigidbody.linearVelocity = Vector3.zero;
#else
                vehicleRigidbody.velocity = Vector3.zero;
#endif
                vehicleRigidbody.angularVelocity = Vector3.zero;
            }

            SetFloatMember(vppStandardInput, 0f, "externalSteer", "externalThrottle", "externalBrake", "externalClutch");
        }

        private bool HasReachedRouteEndpoint()
        {
            if (vehicleRoot == null || centerline == null || centerline.waypoints == null || centerline.waypoints.Length == 0)
                return false;

            Transform endpoint = centerline.waypoints[centerline.waypoints.Length - 1];
            if (endpoint == null)
                return false;

            Vector3 delta = vehicleRoot.transform.position - endpoint.position;
            delta.y = 0f;
            return delta.magnitude <= endpointToleranceMeters;
        }

        private bool TryGetActiveTrial(out Trial trial)
        {
            trial = null;
            if (session == null || !session.hasInitialised || session.isEnding)
                return false;

            try
            {
                if (!session.InTrial)
                    return false;

                trial = session.CurrentTrial;
                return trial != null && trial.status == TrialStatus.InProgress;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool IsTrialStillCurrentAndRunning(Trial trial)
        {
            if (trial == null)
                return false;

            return TryGetActiveTrial(out Trial currentTrial) && currentTrial == trial;
        }

        private void StopActiveCoroutines()
        {
            if (tempoEventCoroutine != null)
            {
                StopCoroutine(tempoEventCoroutine);
                tempoEventCoroutine = null;
            }

            if (trialEndCoroutine != null)
            {
                StopCoroutine(trialEndCoroutine);
                trialEndCoroutine = null;
            }
        }

        private void ResolveReferences()
        {
            // References are auto-filled when possible, but explicit Inspector
            // assignments remain the preferred configuration point.
            if (session == null)
                session = FindAnyObjectByType<Session>();

            if (vehicleRoot == null)
                vehicleRoot = GameObject.Find("Research VPP Vehicle");

            if (vehicleRigidbody == null && vehicleRoot != null)
                vehicleRigidbody = vehicleRoot.GetComponent<Rigidbody>();

            if (centerline == null)
                centerline = FindAnyObjectByType<CenterlinePath>();

            if (musicSource == null)
                musicSource = GetComponent<AudioSource>();

            if (musicSource == null)
                musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0f;
            musicSource.maxDistance = 1f;
            musicSource.dopplerLevel = 0f;

            if (transitionMusicSource == null)
            {
                AudioSource[] sources = GetComponents<AudioSource>();
                for (int i = 0; i < sources.Length; i++)
                {
                    if (sources[i] != musicSource)
                    {
                        transitionMusicSource = sources[i];
                        break;
                    }
                }
            }

            if (transitionMusicSource == null)
                transitionMusicSource = gameObject.AddComponent<AudioSource>();
            transitionMusicSource.playOnAwake = false;
            transitionMusicSource.spatialBlend = 0f;
            transitionMusicSource.maxDistance = 1f;
            transitionMusicSource.dopplerLevel = 0f;

            if (vppStandardInput == null && vehicleRoot != null)
                vppStandardInput = FindComponentByFullName(vehicleRoot, "VehiclePhysics.VPStandardInput");
        }

        private void ConfigurePortableDataSaving()
        {
            if (session == null)
                session = FindAnyObjectByType<Session>();

            if (session == null)
                return;

            FileSaver fileSaver = session.GetComponent<FileSaver>();
            if (fileSaver == null)
                fileSaver = session.gameObject.AddComponent<FileSaver>();

            fileSaver.dataSaveLocation = DataSaveLocation.Fixed;
            fileSaver.storagePath = ResearchDataPaths.EnsureUxfDataRoot();
            fileSaver.active = true;

            if (session.dataHandlers == null || Array.IndexOf(session.dataHandlers, fileSaver) < 0)
                session.dataHandlers = new DataHandler[] { fileSaver };
        }

        private MonoBehaviour FindComponentByFullName(GameObject root, string fullName)
        {
            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour != null && behaviour.GetType().FullName == fullName)
                    return behaviour;
            }

            return null;
        }

        private static void EnsureUxFHeaders(Session targetSession)
        {
            AddUnique(targetSession.settingsToLog, "audio_condition");
            AddUnique(targetSession.settingsToLog, "condition_order_index");
            AddUnique(targetSession.settingsToLog, "music_present");
            AddUnique(targetSession.settingsToLog, "trial_duration_seconds");
            AddUnique(targetSession.settingsToLog, "tempo_change_time_seconds");
            AddUnique(targetSession.settingsToLog, "practice_trial");

            AddUnique(targetSession.customHeaders, "tempo_change_planned_time");
            AddUnique(targetSession.customHeaders, "tempo_change_timestamp");
            AddUnique(targetSession.customHeaders, "tempo_change_dsp_timestamp");
            AddUnique(targetSession.customHeaders, "tempo_change_audio_time");
            AddUnique(targetSession.customHeaders, "tempo_change_occurred");
            AddUnique(targetSession.customHeaders, "tempo_event_label");
            AddUnique(targetSession.customHeaders, "audio_clip_name");
            AddUnique(targetSession.customHeaders, "audio_clip_missing");
            AddUnique(targetSession.customHeaders, "audio_mode");
            AddUnique(targetSession.customHeaders, "audio_first_clip_name");
            AddUnique(targetSession.customHeaders, "audio_second_clip_name");
            AddUnique(targetSession.customHeaders, "practice_trial");
            AddUnique(targetSession.customHeaders, "trial_duration_planned");
            AddUnique(targetSession.customHeaders, "trial_end_reason");
            AddUnique(targetSession.customHeaders, "final_speed_kmh");
            AddUnique(targetSession.customHeaders, "final_position_x");
            AddUnique(targetSession.customHeaders, "final_position_z");
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (!values.Contains(value))
                values.Add(value);
        }

        private static string[] GetRandomizedConditions(int seed)
        {
            string[] randomized = (string[])Conditions.Clone();
            System.Random random = new System.Random(seed);

            for (int i = randomized.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                string temp = randomized[i];
                randomized[i] = randomized[j];
                randomized[j] = temp;
            }

            return randomized;
        }

        private static int GenerateSessionRandomSeed()
        {
            string source = string.Format(
                CultureInfo.InvariantCulture,
                "{0}:{1}:{2}",
                DateTime.UtcNow.Ticks,
                Guid.NewGuid(),
                UnityEngine.Random.Range(int.MinValue, int.MaxValue));
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < source.Length; i++)
                    hash = hash * 31 + source[i];
                return hash == 0 ? 1 : Mathf.Abs(hash);
            }
        }

        private static void SetFloatMember(object target, float value, params string[] memberNames)
        {
            if (target == null)
                return;

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            Type type = target.GetType();

            for (int i = 0; i < memberNames.Length; i++)
            {
                FieldInfo field = type.GetField(memberNames[i], Flags);
                if (field != null && field.FieldType == typeof(float))
                    field.SetValue(target, value);
            }
        }

        private static Vector3 GetVelocity(Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        private static float GetMidpointTime(float duration)
        {
            return Mathf.Max(0f, duration * 0.5f);
        }

        private void RefreshDerivedTiming()
        {
            trialDurationSeconds = Mathf.Max(5f, trialDurationSeconds);
            practiceDurationSeconds = Mathf.Max(30f, practiceDurationSeconds);
            interTrialBreakSeconds = Mathf.Max(5f, interTrialBreakSeconds);
            tempoChangeTimeSeconds = GetMidpointTime(trialDurationSeconds);
            maximumTrialDurationSeconds = Mathf.Max(maximumTrialDurationSeconds, trialDurationSeconds);
        }

        private void ShowBlockStartNotice(Trial trial, string condition, float duration, float tempoTime, bool isPractice)
        {
            int orderIndex = trial.settings.GetInt("condition_order_index", trial.block.number);
            string audioLabel = GetPlannedAudioClipLabel(condition);
            blockStartNoticeLabel = isPractice
                ? string.Format(CultureInfo.InvariantCulture, "PRATICA\n{0:0} s - senza musica", duration)
                : string.Format(CultureInfo.InvariantCulture, "NUOVO BLOCCO {0}/3\n{1}\nAudio: {2}\nDurata {3:0} s - cambio a {4:0} s", orderIndex, condition, audioLabel, duration, tempoTime);
            blockStartNoticeEndTime = Time.time + Mathf.Max(0f, blockStartNoticeSeconds);
        }

        private string GetPlannedAudioClipLabel(string condition)
        {
            if (condition == ConditionTempoIncrease)
                return FormatDisplayClipSequence(slowTempoClip, fastTempoClip);
            if (condition == ConditionTempoDecrease)
                return FormatDisplayClipSequence(fastTempoClip, slowTempoClip);
            if (condition == ConditionBaseline || condition == ConditionPractice)
                return "No music";
            return condition;
        }

        private static string FormatDisplayClipSequence(AudioClip firstClip, AudioClip secondClip)
        {
            string firstName = GetDisplayClipName(firstClip);
            string secondName = GetDisplayClipName(secondClip);
            return firstName + " -> " + secondName;
        }

        private static string GetDisplayClipName(AudioClip clip)
        {
            if (clip == null)
                return "missing";

            if (clip.name == "Music_fast")
                return "Music_slow";
            if (clip.name == "Music_slow")
                return "Music_fast";

            return clip.name;
        }

        private static string GetDisplayConditionLabel(string condition)
        {
            if (condition == ConditionTempoIncrease)
                return ConditionTempoIncrease;
            if (condition == ConditionTempoDecrease)
                return ConditionTempoDecrease;
            if (condition == ConditionBaseline)
                return ConditionBaseline;
            if (condition == ConditionPractice)
                return ConditionPractice;
            return condition;
        }

        private void OnGUI()
        {
            if (experimentCompleted)
            {
                string completedLabel = string.Format(
                    CultureInfo.InvariantCulture,
                    "FINE ESPERIMENTO\n{0}: nuovo partecipante\n{1}: esci",
                    resetExperimentKey,
                    quitKey);
                DrawCenteredBox(completedLabel, 34, 640f, 170f, 0.5f, 110f);
                return;
            }

            float remaining = Mathf.Max(0f, currentTrialEndTime - Time.time);
            if (session == null || !session.hasInitialised)
            {
                string waitingLabel = "Experiment: waiting for UXF\nCondition: not started\nAudio: not started\nRemaining: --";
                if (allowQuickStartForTesting)
                    waitingLabel += "\nF8: quick test start";

                if (showExperimentHud)
                    DrawExperimentHudBox(new Rect(16f, 52f, 520f, 178f), waitingLabel);
                return;
            }

            string label = string.Format(
                CultureInfo.InvariantCulture,
                "Experiment: {0}\nCondition: {1}\nAudio: {2}\nRemaining: {3:0}s",
                currentPhaseLabel,
                currentConditionLabel,
                currentAudioLabel,
                remaining);

            if (currentConditionCode == ConditionTempoIncrease || currentConditionCode == ConditionTempoDecrease)
                label += string.Format(CultureInfo.InvariantCulture, "\nTempo change in: {0:0}s", Mathf.Max(0f, currentTempoChangeTime - Time.time));
            else if (currentConditionCode == ConditionPractice || currentConditionCode == ConditionBaseline)
                label += "\nSilent condition";

            if (allowQuickStartForTesting)
                label += "\nF9: skip current trial";

            if (showExperimentHud)
                DrawExperimentHudBox(new Rect(16f, 52f, 520f, 198f), label);

            if (!string.IsNullOrEmpty(breakCountdownLabel) && Time.time < breakCountdownEndTime)
            {
                float countdown = Mathf.Ceil(Mathf.Max(0f, breakCountdownEndTime - Time.time));
                DrawCenteredBox(string.Format(CultureInfo.InvariantCulture, "PAUSA\nProssimo blocco tra {0:0}", countdown), 34, 560f, 150f, 0.5f, 120f);
            }

            if (Time.time < blockStartNoticeEndTime && !string.IsNullOrEmpty(blockStartNoticeLabel))
                DrawCenteredBox(blockStartNoticeLabel, 30, 760f, 170f, 0.5f, 96f);
        }

        private void DrawExperimentHudBox(Rect rect, string text)
        {
            EnsureExperimentHudStyles();
            GUI.Box(rect, GUIContent.none, experimentHudBoxStyle);
            GUI.Label(new Rect(rect.x + 16f, rect.y + 14f, rect.width - 32f, rect.height - 28f), text, experimentHudLabelStyle);
        }

        private void EnsureExperimentHudStyles()
        {
            if (experimentHudBoxStyle == null)
            {
                experimentHudBoxStyle = new GUIStyle(GUI.skin.box)
                {
                    padding = new RectOffset(12, 12, 10, 10)
                };
                experimentHudBoxStyle.normal.background = Texture2D.whiteTexture;
                experimentHudBoxStyle.normal.textColor = Color.black;
            }

            if (experimentHudLabelStyle == null)
            {
                experimentHudLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    wordWrap = true,
                    alignment = TextAnchor.UpperLeft,
                    clipping = TextClipping.Clip
                };
                experimentHudLabelStyle.normal.textColor = Color.black;
            }
        }

        private static void DrawCenteredBox(string text, int fontSize, float width, float height, float xCenter, float y)
        {
            GUIStyle noticeStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            Rect noticeRect = new Rect((Screen.width - width) * xCenter, y, width, height);
            GUI.Box(noticeRect, text, noticeStyle);
        }
    }
}
