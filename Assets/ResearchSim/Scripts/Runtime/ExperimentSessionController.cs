using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UXF;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ResearchSim
{
    /// <summary>
    /// Coordinates the car-following + music experiment. It sits outside VPP
    /// and input code: the participant vehicle remains controlled by the
    /// existing keyboard, gamepad, Fanatec and Arduino paths.
    /// </summary>
    public class ExperimentSessionController : MonoBehaviour
    {
        public enum SessionPhase
        {
            Idle,
            WaitingForParticipantStart,
            Familiarization,
            Baseline,
            ExperimentalBlock,
            Completed
        }

        [System.Serializable]
        public sealed class BlockLeaderSpeedEvent
        {
            public bool enabled = true;
            [Min(0f)] public float plannedStartSeconds = 120f;
            public int eventIndex;

            [System.NonSerialized] public bool triggered;
            [System.NonSerialized] public bool invalid;
            [System.NonSerialized] public bool missed;
            [System.NonSerialized] public bool completed;
            [System.NonSerialized] public string invalidReason = "none";
            [System.NonSerialized] public string phase = "pending";
            [System.NonSerialized] public float actualStartSeconds = -1f;
            [System.NonSerialized] public float decelStartSeconds = -1f;
            [System.NonSerialized] public float decelEndSeconds = -1f;
            [System.NonSerialized] public float holdStartSeconds = -1f;
            [System.NonSerialized] public float holdEndSeconds = -1f;
            [System.NonSerialized] public float recoveryStartSeconds = -1f;
            [System.NonSerialized] public float recoveryEndSeconds = -1f;
            [System.NonSerialized] public float completionSeconds = -1f;
            [System.NonSerialized] public string lastObservedPhase = "none";
            [System.NonSerialized] public bool warningLogged;

            public void ResetRuntime()
            {
                triggered = false;
                invalid = false;
                missed = false;
                completed = false;
                invalidReason = enabled ? "none" : "disabled";
                phase = enabled ? "pending" : "invalid";
                actualStartSeconds = -1f;
                decelStartSeconds = -1f;
                decelEndSeconds = -1f;
                holdStartSeconds = -1f;
                holdEndSeconds = -1f;
                recoveryStartSeconds = -1f;
                recoveryEndSeconds = -1f;
                completionSeconds = -1f;
                lastObservedPhase = "none";
                warningLogged = false;
            }
        }

        [Header("Timing")]
        [Min(10f)] public float familiarizationSeconds = 240f;
        [Min(10f)] public float baselineSeconds = 240f;
        [Min(30f)] public float experimentalBlockSeconds = 480f;
        [Min(0f)] public float interBlockNoticeSeconds = 2f;
        [Min(1)] public int numberOfBlocks = 3;

        [Header("Protocol Profile")]
        public ExperimentProtocolProfile protocolProfile;

        [Header("Prepared Music Blocks")]
        public MusicEventController.MusicBlockCondition[] defaultBlockConditions =
        {
            MusicEventController.MusicBlockCondition.SlowFast,
            MusicEventController.MusicBlockCondition.FastSlow,
            MusicEventController.MusicBlockCondition.ControlStable
        };
        public bool executeScheduledMusicEvents;

        [Header("Block Leader Speed Events")]
        public BlockLeaderSpeedEvent[] blockLeaderSpeedEvents =
        {
            new BlockLeaderSpeedEvent { eventIndex = 0, plannedStartSeconds = 120f },
            new BlockLeaderSpeedEvent { eventIndex = 1, plannedStartSeconds = 360f }
        };
        [Min(0f)] public float tempoChangeProtectedWindowSeconds = 30f;
        [Min(0f)] public float completedLeaderEventDisplaySeconds = 5f;

        [Header("Start")]
        public bool autoArmOnSceneStart = true;
        public KeyCode armSessionKey = KeyCode.F8;
        public KeyCode skipPhaseKey = KeyCode.F9;
        public string participantIdOverride = "";

        [Header("References")]
        public TrialScheduler scheduler;
        public MusicEventController music;
        public LeadVehicleController leader;
        public DrivingDataLogger logger;
        public CarFollowingFeedbackController feedbackController;
        public CenterlinePath centerline;
        public Transform participantVehicle;
        public Rigidbody participantRigidbody;
        public VppExternalInputBridge inputBridge;

        [Header("HUD")]
        public bool showHud = true;

        [Header("Route Start")]
        public float participantRightLaneOffsetMeters = 4.4f;
        public float participantSpawnHeightOffsetMeters = 0.08f;
        public bool resetOpenRouteAtPhaseStart = true;
        public bool resetOpenRouteWhenNearEnd = true;
        [Min(25f)] public float openRouteResetMarginMeters = 250f;

        public SessionPhase CurrentPhase { get; private set; } = SessionPhase.Idle;
        public int CurrentBlockIndex { get; private set; } = -1;
        public MusicEventController.MusicBlockCondition CurrentBlockCondition { get; private set; } = MusicEventController.MusicBlockCondition.ControlStable;
        public TrialScheduler.TrialEvent CurrentEvent { get; private set; }
        public bool SessionArmed { get; private set; }
        public bool SessionRunning { get; private set; }

        private Coroutine sessionRoutine;
        private float phaseEndTime;
        private float phaseStartTime;
        private float currentPhaseDuration;
        private bool skipRequested;
        private string participantId;
        private MusicEventController.MusicBlockCondition[] randomizedBlockConditions;
        private SessionPlan sessionPlan;
        private GUIStyle boxStyle;
        private GUIStyle labelStyle;
        private GUIStyle centerBoxStyle;
        private GUIStyle centerLabelStyle;
        private string centerMessage = "";
        private string currentBlockClipWarning = "";
        private bool blockEndingCoasting;
        private bool preparingNextSegment;
        private string participantIdSource = "";
        private int uxfSessionNumber = -1;
        private string uxfSessionId = "";
        private bool warnedUsingFallbackParticipantId;
        private bool debugBypassUxfMetadata;
        private bool loggedDebugUxfMetadataBypass;
        private bool loggedNumericModuloDebugBypass;
        private bool loggedNumericModuloPendingParticipantId;
        private bool loggedNumericModuloInvalidParticipantId;
        private BlockLeaderSpeedEvent activeBlockLeaderEvent;
        private BlockLeaderSpeedEvent recentBlockLeaderEvent;
        private float previousLeaderEventClockSeconds = -1f;
        private float previousV2LeaderClockSeconds = -1f;
        private bool audioPreloadPending;
        private Coroutine audioPreloadRoutine;
        private bool uxfMetadataPending;
        private Coroutine uxfMetadataRoutine;
        private bool fatalSessionError;
        private float nextUxfMetadataRefreshTime;
        private double v2ArmRequestedAtRealtime = -1d;
        private static readonly ProfilerMarker AudioPreloadWaitMarker = new ProfilerMarker("ResearchSim.Startup.AudioPreloadWaitPoll");
        private static readonly ProfilerMarker LegacyScheduleMarker = new ProfilerMarker("ResearchSim.Startup.LegacyScheduleGeneration");
        private static readonly ProfilerMarker LeaderStartCallbackMarker = new ProfilerMarker("ResearchSim.Startup.LeaderStartCallback");
        private const float V2AudioPreloadTimeoutSeconds = 60f;
        private float lastOpenRouteResetTime = -999f;
        private const float RoadSurfaceRaycastHeightMeters = 30f;
        private const float RoadSurfaceRaycastDistanceMeters = 120f;
        private const float RoadSurfaceMinimumUpDot = 0.65f;
        private const float OpenRouteResetCooldownSeconds = 5f;

        private sealed class SessionPlan
        {
            public string profileId = "fallback defaults";
            public string protocolVersion = "V1";
            public bool useV2Protocol;
            public float fallbackExperimentalBlockSeconds = 480f;
            public bool includeFamiliarization = true;
            public float familiarizationSeconds = 240f;
            public bool includeBaseline = true;
            public float baselineSeconds = 240f;
            public BlockOrderMode blockOrderMode = BlockOrderMode.ShuffleByParticipantId;
            public int blockOrderSeed;
            public int counterbalanceIndex = -1;
            public MusicEventController.MusicBlockCondition[] blockConditions = new MusicEventController.MusicBlockCondition[0];
            public string blockSequence = "";
            public bool feedbackEnabled;
            public CarFollowingFeedbackSettings feedbackSettings;
            public bool showFeedbackDuringFamiliarization = true;
            public bool showFeedbackDuringBaseline = true;
            public bool showFeedbackDuringExperimentalBlocks = true;

            public int BlockCount
            {
                get { return blockConditions != null ? blockConditions.Length : 0; }
            }
        }

        private void Awake()
        {
            ResolveReferences();
        }

        public void QuickStartForTesting()
        {
            if (!SessionRunning && !debugBypassUxfMetadata)
                Debug.Log("[ResearchSim] F8 debug quick-start active: UXF participant/session metadata will be ignored for CSV logging.");

            if (!SessionRunning)
                debugBypassUxfMetadata = true;

            if (uxfMetadataRoutine != null)
                StopCoroutine(uxfMetadataRoutine);
            uxfMetadataRoutine = null;
            uxfMetadataPending = false;

            HideUxfStartupUiForTesting();

            if (!SessionRunning && IsConfiguredV2Protocol())
            {
                ResolveReferences();
                string resolvedParticipantId = ResolveParticipantIdForLogging(false);
                participantId = resolvedParticipantId;
                sessionPlan = BuildSessionPlan(participantId);
                randomizedBlockConditions = sessionPlan.blockConditions;
                LogSessionPlanSummary("F8 prepared");
                if (!PrepareLoggerForParticipant(resolvedParticipantId))
                {
                    SetThrottleGate(true);
                    centerMessage = "ERRORE CSV\nImpossibile preparare il file dati debug.\nAvvisare lo sperimentatore.";
                    return;
                }
            }

            if (!SessionArmed && !SessionRunning)
            {
                ArmSession();
                return;
            }

            Debug.Log("[ExperimentSessionController] F8 quick start received. Session already armed/running; waiting for participant movement if not started.");
        }

        public void ArmSession()
        {
            if (SessionArmed || SessionRunning || audioPreloadPending || uxfMetadataPending)
                return;

            ResolveReferences();
            participantId = ResolveParticipantIdForLogging(false);
            sessionPlan = BuildSessionPlan(participantId);
            randomizedBlockConditions = sessionPlan.blockConditions;
            if (IsV2ProtocolActive())
                v2ArmRequestedAtRealtime = Time.realtimeSinceStartupAsDouble;

            if (IsV2ProtocolActive() && music == null)
            {
                Debug.LogError("[ResearchSim] V2 session cannot arm: MusicEventController is missing.");
                centerMessage = "ERRORE AUDIO\nController musica assente.\nAvvisare lo sperimentatore.";
                SetThrottleGate(true);
                return;
            }

            if (IsV2ProtocolActive() && !music.PreparedBlockAudioPreloadComplete)
            {
                audioPreloadPending = true;
                audioPreloadRoutine = StartCoroutine(WaitForV2AudioPreloadThenArm());
                return;
            }

            CompleteArmSession();
        }

        private IEnumerator WaitForV2AudioPreloadThenArm()
        {
            SetThrottleGate(true);
            centerMessage = "Preparazione audio sperimentale...\nAttendere.";
            music.RequestPreparedBlockAudioPreload();
            Debug.Log("[ResearchSim] Waiting for V2 audio preload: " + music.GetPreparedBlockAudioLoadSummary());

            float startedAt = Time.realtimeSinceStartup;
            while (true)
            {
                bool shouldContinue;
                using (AudioPreloadWaitMarker.Auto())
                {
                    shouldContinue =
                        !music.PreparedBlockAudioPreloadComplete &&
                        !music.PreparedBlockAudioPreloadFailed &&
                        Time.realtimeSinceStartup - startedAt < V2AudioPreloadTimeoutSeconds;
                }
                if (!shouldContinue)
                    break;
                yield return null;
            }

            audioPreloadPending = false;
            audioPreloadRoutine = null;
            if (!music.PreparedBlockAudioPreloadComplete)
            {
                string reason = music.PreparedBlockAudioPreloadFailed ? "failed" : "timeout";
                Debug.LogError("[ResearchSim] V2 audio preload " + reason + ": " + music.GetPreparedBlockAudioLoadSummary());
                centerMessage = "ERRORE AUDIO\nPreload V2 non completato.\nAvvisare lo sperimentatore.";
                SetThrottleGate(true);
                yield break;
            }

            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[ResearchSim][Startup] V2 audio preload complete in {0:F1} ms.",
                (Time.realtimeSinceStartup - startedAt) * 1000f));
            centerMessage = "";
            CompleteArmSession();
        }

        private void CompleteArmSession()
        {
            if (ShouldWaitForFinalizedUxfMetadata())
            {
                BeginWaitingForUxfMetadata();
                return;
            }

            RefreshParticipantMetadataAndPlan("metadata finalized");
            if (ShouldBlockSessionStartForInvalidCounterbalancePlan())
            {
                SetThrottleGate(true);
                centerMessage = "ERRORE CONFIGURAZIONE\nParticipant ID non valido per il counterbalancing.\nAvvisare lo sperimentatore.";
                Debug.LogError("[ExperimentSessionController] Cannot arm session: finalized participant ID did not produce a valid counterbalanced block order.");
                return;
            }

            fatalSessionError = false;
            ForceAutomaticTransmissionForSessionStart();
            PlaceParticipantAtRouteStart();
            SetThrottleGate(true);
            preparingNextSegment = false;
            blockEndingCoasting = false;
            PrepareVehicleForParticipantStart();

            if (!IsV2ProtocolActive() && scheduler != null)
            {
                using (LegacyScheduleMarker.Auto())
                {
                    scheduler.numberOfBlocks = Mathf.Max(1, GetActiveExperimentalBlockCount());
                    scheduler.GenerateSchedule(participantId);
                    string folder = Path.Combine(ResearchDataPaths.ProjectRoot, ResearchDataPaths.DataRootFolderName, "CarFollowing");
                    scheduler.SaveScheduleToJson(folder);
                }
            }

            if (!PrepareLoggerForParticipant(participantId))
            {
                centerMessage = "ERRORE CSV\nImpossibile preparare il file dati.\nAvvisare lo sperimentatore.";
                Debug.LogError("[ResearchSim] Session cannot arm because the CSV could not be prepared.");
                return;
            }
            LogSessionPlanSummary("armed");

            if (leader != null)
            {
                Vector3 participantPosition = participantVehicle != null ? participantVehicle.position : Vector3.zero;
                leader.participantRigidbody = participantRigidbody;
                leader.Initialize(centerline, participantPosition);
                leader.ArmForParticipantStart();
                leader.OnDrivingStarted += HandleLeaderStarted;
                leader.OnDecelerationStart += HandleLeaderDecelerationStarted;
            }

            WarnIfRouteIsTooShort();
            CurrentPhase = SessionPhase.WaitingForParticipantStart;
            phaseStartTime = Time.time;
            currentPhaseDuration = 0f;
            currentBlockClipWarning = "";
            centerMessage = "";
            SessionArmed = true;
            SetThrottleGate(false);
            if (IsV2ProtocolActive() && v2ArmRequestedAtRealtime >= 0d)
            {
                double elapsedMs = (Time.realtimeSinceStartupAsDouble - v2ArmRequestedAtRealtime) * 1000d;
                Debug.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "[ResearchSim][Startup] V2 ready for participant movement in {0:F1} ms. CSV prepared={1}; legacy schedule skipped.",
                    elapsedMs,
                    logger != null && logger.IsPrepared));
            }
            Debug.Log("[ExperimentSessionController] Armed. Session timers start when participant begins moving.");
        }

        private static void HideUxfStartupUiForTesting()
        {
            Session[] sessions = FindObjectsByType<Session>(FindObjectsInactive.Include);
            for (int i = 0; i < sessions.Length; i++)
            {
                Session session = sessions[i];
                if (session == null)
                    continue;

                Canvas[] canvases = session.GetComponentsInChildren<Canvas>(true);
                for (int c = 0; c < canvases.Length; c++)
                {
                    if (canvases[c] != null)
                        canvases[c].enabled = false;
                }
            }

            GameObject uxfRig = GameObject.Find("[UXF_Rig]");
            if (uxfRig != null)
            {
                Canvas[] canvases = uxfRig.GetComponentsInChildren<Canvas>(true);
                for (int c = 0; c < canvases.Length; c++)
                {
                    if (canvases[c] != null)
                        canvases[c].enabled = false;
                }
            }
        }

        public void StopSession()
        {
            if (audioPreloadRoutine != null)
                StopCoroutine(audioPreloadRoutine);
            audioPreloadRoutine = null;
            audioPreloadPending = false;
            if (uxfMetadataRoutine != null)
                StopCoroutine(uxfMetadataRoutine);
            uxfMetadataRoutine = null;
            uxfMetadataPending = false;

            if (sessionRoutine != null)
                StopCoroutine(sessionRoutine);

            if (music != null)
                music.StopMusic();
            if (leader != null)
                leader.StopDriving();
            SetThrottleGate(false);
            if (logger != null)
                logger.StopLogging();
            EndUxfSessionsIfActive();

            SessionArmed = false;
            SessionRunning = false;
            preparingNextSegment = false;
            blockEndingCoasting = false;
            CurrentPhase = SessionPhase.Completed;
            PrepareVehicleForParticipantStart();
            centerMessage = BuildCompletionMessage(false);
        }

        private void HandleLeaderStarted(float time)
        {
            using (LeaderStartCallbackMarker.Auto())
            {
                if (!SessionArmed || SessionRunning)
                    return;

                string resolvedParticipantId = ResolveParticipantIdForLogging(true);
                if (ShouldRebuildSessionPlanForResolvedParticipant(resolvedParticipantId))
                {
                    participantId = resolvedParticipantId;
                    sessionPlan = BuildSessionPlan(participantId);
                    randomizedBlockConditions = sessionPlan.blockConditions;
                    LogSessionPlanSummary("started");
                    if (!IsV2ProtocolActive() && scheduler != null)
                    {
                        using (LegacyScheduleMarker.Auto())
                        {
                            scheduler.numberOfBlocks = Mathf.Max(1, GetActiveExperimentalBlockCount());
                            scheduler.GenerateSchedule(participantId);
                            string folder = Path.Combine(ResearchDataPaths.ProjectRoot, ResearchDataPaths.DataRootFolderName, "CarFollowing");
                            scheduler.SaveScheduleToJson(folder);
                        }
                    }
                }

                if (ShouldBlockSessionStartForInvalidCounterbalancePlan())
                {
                    Debug.LogError("[ExperimentSessionController] Cannot start session: CounterbalancedByParticipantNumberModulo did not produce a valid block order for participant_id '" + resolvedParticipantId + "'.");
                    if (leader != null)
                        leader.StopDriving();
                    SessionArmed = false;
                    SessionRunning = false;
                    preparingNextSegment = false;
                    blockEndingCoasting = false;
                    SetThrottleGate(true);
                    centerMessage = "ERRORE CONFIGURAZIONE\nParticipant ID non valido per il counterbalancing.\nAvvisare lo sperimentatore.";
                    return;
                }

                SessionArmed = false;
                SessionRunning = true;

                if (logger != null)
                {
                    logger.participantIdSource = participantIdSource;
                    logger.uxfSessionNumber = uxfSessionNumber;
                    logger.uxfSessionId = uxfSessionId;
                    ApplyProtocolMetadataToLogger();
                    logger.StartLogging(participantId);
                }

                UpdateFeedbackControllerState();

                sessionRoutine = StartCoroutine(RunSession());
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[ExperimentSessionController] Session started at t={0:F2}.", time));
            }
        }

        private bool PrepareLoggerForParticipant(string resolvedParticipantId)
        {
            if (logger == null)
                return false;

            logger.participantIdSource = participantIdSource;
            logger.uxfSessionNumber = uxfSessionNumber;
            logger.uxfSessionId = uxfSessionId;
            ApplyProtocolMetadataToLogger();
            bool prepared = logger.PrepareLogging(resolvedParticipantId);
            if (prepared)
                Debug.Log("[ResearchSim] CSV prepared for participant " + resolvedParticipantId + ".");
            return prepared;
        }

        private bool ShouldWaitForFinalizedUxfMetadata()
        {
            if (!IsConfiguredV2Protocol() ||
                debugBypassUxfMetadata ||
                !string.IsNullOrWhiteSpace(participantIdOverride))
            {
                return false;
            }

            return !TryGetUxfParticipantMetadata(out string _, out int _);
        }

        private void BeginWaitingForUxfMetadata()
        {
            if (uxfMetadataPending)
                return;

            uxfMetadataPending = true;
            SetThrottleGate(true);
            Debug.Log("[ResearchSim] Waiting for UXF participant metadata before preparing CSV.");
            uxfMetadataRoutine = StartCoroutine(WaitForUxfMetadataThenCompleteArm());
        }

        private IEnumerator WaitForUxfMetadataThenCompleteArm()
        {
            while (!debugBypassUxfMetadata &&
                   string.IsNullOrWhiteSpace(participantIdOverride) &&
                   !TryGetUxfParticipantMetadata(out string _, out int _))
            {
                yield return null;
            }

            uxfMetadataPending = false;
            uxfMetadataRoutine = null;
            centerMessage = "";
            CompleteArmSession();
        }

        private void RefreshParticipantMetadataAndPlan(string context)
        {
            string resolvedParticipantId = ResolveParticipantIdForLogging(false);
            if (!ShouldRebuildSessionPlanForResolvedParticipant(resolvedParticipantId))
                return;

            participantId = resolvedParticipantId;
            sessionPlan = BuildSessionPlan(participantId);
            randomizedBlockConditions = sessionPlan.blockConditions;
            LogSessionPlanSummary(context);
        }

        private void RefreshPreparedUxfLoggerBeforeParticipantStart()
        {
            if (!SessionArmed ||
                SessionRunning ||
                debugBypassUxfMetadata ||
                !string.IsNullOrWhiteSpace(participantIdOverride) ||
                Time.unscaledTime < nextUxfMetadataRefreshTime)
            {
                return;
            }

            nextUxfMetadataRefreshTime = Time.unscaledTime + 0.25f;
            if (!TryGetUxfParticipantMetadata(out string currentParticipantId, out int currentSessionNumber))
                return;

            bool metadataChanged =
                !string.Equals(participantId, currentParticipantId, System.StringComparison.Ordinal) ||
                uxfSessionNumber != currentSessionNumber;
            if (!metadataChanged)
                return;

            RefreshParticipantMetadataAndPlan("UXF metadata updated before movement");
            if (!PrepareLoggerForParticipant(participantId))
            {
                SessionArmed = false;
                SetThrottleGate(true);
                centerMessage = "ERRORE CSV\nImpossibile aggiornare il file dati.\nAvvisare lo sperimentatore.";
            }
        }

        private bool ShouldRebuildSessionPlanForResolvedParticipant(string resolvedParticipantId)
        {
            if (!string.Equals(participantId, resolvedParticipantId, System.StringComparison.Ordinal))
                return true;

            if (sessionPlan == null)
                return true;

            if (sessionPlan.blockOrderMode != BlockOrderMode.CounterbalancedByParticipantNumberModulo)
                return false;

            if (debugBypassUxfMetadata)
                return false;

            bool hasResolvedRealParticipantId =
                string.Equals(participantIdSource, "UXF", System.StringComparison.Ordinal) ||
                string.Equals(participantIdSource, "participantIdOverride", System.StringComparison.Ordinal);
            return hasResolvedRealParticipantId && (sessionPlan.blockOrderSeed <= 0 || sessionPlan.counterbalanceIndex < 0);
        }

        private bool ShouldBlockSessionStartForInvalidCounterbalancePlan()
        {
            if (debugBypassUxfMetadata || sessionPlan == null)
                return false;

            if (sessionPlan.blockOrderMode != BlockOrderMode.CounterbalancedByParticipantNumberModulo)
                return false;

            bool hasResolvedRealParticipantId =
                string.Equals(participantIdSource, "UXF", System.StringComparison.Ordinal) ||
                string.Equals(participantIdSource, "participantIdOverride", System.StringComparison.Ordinal);
            return hasResolvedRealParticipantId && (sessionPlan.blockOrderSeed <= 0 || sessionPlan.counterbalanceIndex < 0 || sessionPlan.BlockCount == 0);
        }

        private IEnumerator RunSession()
        {
            if (sessionPlan == null)
                sessionPlan = BuildSessionPlan(participantId);

            bool completedDrivingSegment = false;
            if (sessionPlan.includeFamiliarization)
            {
                yield return RunTimedPhase(SessionPhase.Familiarization, sessionPlan.familiarizationSeconds, false);
                completedDrivingSegment = true;
            }

            if (sessionPlan.includeBaseline)
            {
                yield return PrepareNextDrivingSegment("Baseline");
                yield return RunTimedPhase(SessionPhase.Baseline, sessionPlan.baselineSeconds, true);
                completedDrivingSegment = true;
            }

            for (int block = 0; block < GetActiveExperimentalBlockCount(); block++)
            {
                CurrentBlockIndex = block;
                CurrentBlockCondition = GetBlockCondition(block);

                if (!completedDrivingSegment && block == 0)
                {
                    Debug.Log("[ResearchSim] Skipping redundant first block preparation; vehicle already prepared by ArmSession.");
                }
                else
                {
                    yield return PrepareNextDrivingSegment("ExperimentalBlock " + (block + 1));
                }

                yield return RunExperimentalBlock(block);
                if (fatalSessionError)
                    yield break;
                completedDrivingSegment = true;
            }

            CurrentPhase = SessionPhase.Completed;
            CurrentEvent = null;
            UpdateLoggerPhase();

            if (music != null)
                music.StopMusic();
            if (leader != null)
                leader.StopDriving();
            SetThrottleGate(false);
            if (logger != null)
                logger.StopLogging();
            EndUxfSessionsIfActive();

            SessionRunning = false;
            preparingNextSegment = false;
            blockEndingCoasting = false;
            PrepareVehicleForParticipantStart();
            centerMessage = BuildCompletionMessage(true);
            Debug.Log("[ExperimentSessionController] Car-following session completed.");
        }

        private string BuildCompletionMessage(bool completedAllPlannedSegments)
        {
            const string ParticipantInstruction = "ESPERIMENTO FINITO\nAvvisare lo sperimentatore.";
            if (logger == null)
            {
                return ParticipantInstruction +
                    "\n\nDATA WARNING\nPrimary CSV status unavailable.\n" +
                    "Do not accept this session before checking the file.\nCheck Player.log.";
            }

            DrivingDataLogger.PrimaryCsvStatusSnapshot status = logger.GetPrimaryCsvStatus(true);
            string rows = status.dataRowsWritten.ToString(CultureInfo.InvariantCulture);
            string size = status.primaryCsvBytes >= 0L
                ? FormatMegabytes(status.primaryCsvBytes) + " MB"
                : "unknown";

            if (status.finalPrimaryCsvVerified)
            {
                string completeness = completedAllPlannedSegments
                    ? ""
                    : "\nSession completeness was not verified.";
                return ParticipantInstruction +
                    "\n\nDATA SAVED OK\nPrimary CSV saved.\nRows written: " + rows +
                    "\nPrimary size: " + size +
                    completeness;
            }

            string path = string.IsNullOrWhiteSpace(status.primaryCsvPath)
                ? "unknown"
                : status.primaryCsvPath;
            return ParticipantInstruction +
                "\n\nDATA WARNING\nPrimary CSV was not verified.\nRows written: " + rows +
                "\nPrimary size: " + size +
                "\nPath: " + path +
                "\n\nDo not accept this session before checking the file.\nCheck Player.log.";
        }

        private static string FormatMegabytes(long bytes)
        {
            if (bytes < 0L)
                return "unknown";
            return (bytes / (1024d * 1024d)).ToString("F2", CultureInfo.InvariantCulture);
        }

        private IEnumerator RunTimedPhase(SessionPhase phase, float duration, bool logAsAnalysisPhase)
        {
            CurrentPhase = phase;
            CurrentEvent = null;
            skipRequested = false;
            blockEndingCoasting = false;
            preparingNextSegment = false;
            SetThrottleGate(false);
            BeginPhaseTimer(duration);
            UpdateLoggerPhase();

            if (logger != null && !logAsAnalysisPhase)
                logger.SetEvent(null);
            ClearBlockLeaderEventLogger();

            while (Time.time < phaseEndTime && !skipRequested)
            {
                ResetOpenRouteIfNearEnd(phase.ToString());
                yield return null;
            }

            if (!skipRequested)
                yield return CompleteCurrentPhaseAndWaitForContinue(GetPhaseEndLabel(phase));
        }

        private IEnumerator RunExperimentalBlock(int blockIndex)
        {
            CurrentPhase = SessionPhase.ExperimentalBlock;
            skipRequested = false;
            blockEndingCoasting = false;
            preparingNextSegment = false;
            SetThrottleGate(false);

            CurrentBlockCondition = GetBlockCondition(blockIndex);
            bool blockAudioStarted = music != null && music.StartBlockMusic(CurrentBlockCondition);
            if (IsV2ProtocolActive() && !blockAudioStarted)
            {
                Debug.LogError("[ExperimentSessionController] V2 block cannot start because its audio did not start.");
                SetThrottleGate(true);
                centerMessage = "ERRORE AUDIO BLOCCO\nAvvisare lo sperimentatore.";
                fatalSessionError = true;
                SessionRunning = false;
                if (leader != null)
                    leader.StopDriving();
                if (logger != null)
                    logger.StopLogging();
                EndUxfSessionsIfActive();
                yield break;
            }

            BeginPhaseTimer(GetExperimentalBlockDurationFromCurrentClip());
            UpdateLoggerPhase();
            UpdateBlockClipDiagnostics(blockIndex);
            if (logger != null)
                logger.SetBlock(blockIndex, CurrentBlockCondition.ToString(), music != null ? music.CurrentStimulusId : string.Empty, music);
            if (IsV2ProtocolActive())
            {
                ResetV2LeaderSpeedProfile();
            }
            else
            {
                ResetBlockLeaderEvents();
                ValidateBlockLeaderEventsForCurrentBlock();
                PublishBlockLeaderEventLoggerState(GetBlockLeaderEventClockSeconds());
            }

            List<TrialScheduler.TrialEvent> events = !IsV2ProtocolActive() && scheduler != null
                ? scheduler.GetEventsForBlock(blockIndex)
                : new List<TrialScheduler.TrialEvent>();

            float nextEventTime = Time.time + PickInterEventInterval();
            int eventPointer = 0;

            while (IsExperimentalBlockRunning() && !skipRequested)
            {
                ResetOpenRouteIfNearEnd("ExperimentalBlock " + (blockIndex + 1));

                float leaderEventClockSeconds = GetBlockLeaderEventClockSeconds();
                if (IsV2ProtocolActive())
                {
                    UpdateV2LeaderSpeedProfile(leaderEventClockSeconds);
                }
                else
                {
                    UpdateActiveBlockLeaderEventState(leaderEventClockSeconds);
                    TryTriggerDueBlockLeaderEvents(leaderEventClockSeconds);
                    UpdateActiveBlockLeaderEventState(leaderEventClockSeconds);
                    PublishBlockLeaderEventLoggerState(leaderEventClockSeconds);
                    UpdatePreviousLeaderEventClock(leaderEventClockSeconds);
                }

                if (executeScheduledMusicEvents && eventPointer < events.Count && Time.time >= nextEventTime)
                {
                    TrialScheduler.TrialEvent evt = events[eventPointer];
                    yield return ExecuteEvent(evt);
                    eventPointer++;
                    nextEventTime = Time.time + PickInterEventInterval();
                }

                yield return null;
            }

            if (!skipRequested)
                yield return CompleteCurrentPhaseAndWaitForContinue("blocco");

            SetThrottleGate(false);
            blockEndingCoasting = false;
            CurrentEvent = null;
            if (logger != null)
            {
                logger.SetEvent(null);
                logger.ClearLeaderEvent();
                logger.ClearBlock();
            }
            if (music != null)
                music.StopMusic();
        }

        private IEnumerator PrepareNextDrivingSegment(string phaseLabel)
        {
            CurrentPhase = SessionPhase.WaitingForParticipantStart;
            CurrentEvent = null;
            skipRequested = false;
            blockEndingCoasting = false;
            preparingNextSegment = true;
            SetThrottleGate(true);
            centerMessage = "Preparazione segmento successivo...";
            phaseStartTime = Time.time;
            currentPhaseDuration = 0f;
            currentBlockClipWarning = "";
            UpdateLoggerPhase();

            if (music != null)
                music.StopMusic();
            if (logger != null)
            {
                logger.SetEvent(null);
                logger.ClearLeaderEvent();
            }

            ResetOpenRouteForNextPhase(phaseLabel);
            yield return new WaitForFixedUpdate();
            StopParticipantMotion();
            Physics.SyncTransforms();
            preparingNextSegment = false;
            SetThrottleGate(false);
            centerMessage = "";

            while (leader != null && !leader.IsRunning)
                yield return null;

            centerMessage = "";
        }

        private IEnumerator CompleteCurrentPhaseAndWaitForContinue(string completedLabel)
        {
            blockEndingCoasting = true;
            SetThrottleGate(true);
            if (music != null)
                music.StopMusic();

            centerMessage = "Fine " + completedLabel + ".\nRilascia i comandi e attendi lo sperimentatore.\nSperimentatore: premi Enter per continuare.";
            Debug.Log("[ExperimentSessionController] Phase ending / coasting after " + completedLabel + ". Throttle disabled until experimenter presses Enter.");

            while (!skipRequested && !IsExperimenterContinuePressed())
                yield return null;

            centerMessage = "";
            SetThrottleGate(false);
            blockEndingCoasting = false;
        }

        private IEnumerator ExecuteEvent(TrialScheduler.TrialEvent evt)
        {
            if (evt == null)
                yield break;

            CurrentEvent = evt;
            evt.musicEventTime = Time.time;

            if (music != null)
                music.ExecuteMusicEvent(evt.musicType);

            if (logger != null)
            {
                logger.SetEvent(evt);
                UpdateLoggerPhase();
            }

            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[ExperimentSessionController] Event {0}: {1}, decel={2}, delay={3:F1}s.",
                evt.index,
                evt.ConditionLabel,
                evt.hasLeaderDeceleration,
                evt.decelerationDelaySeconds));

            if (evt.hasLeaderDeceleration)
            {
                yield return new WaitForSeconds(evt.decelerationDelaySeconds);

                if (leader != null)
                {
                    leader.TriggerDeceleration();
                    evt.leadDecelerationTime = leader.LastDecelerationStartTime;
                    if (logger != null)
                        logger.SetEvent(evt);
                }
            }
        }

        private void HandleLeaderDecelerationStarted(float time)
        {
            if (CurrentEvent != null)
            {
                CurrentEvent.leadDecelerationTime = time;
                if (logger != null)
                    logger.SetEvent(CurrentEvent);
            }
        }

        private float PickInterEventInterval()
        {
            if (scheduler == null)
                return 60f;

            float min = Mathf.Max(1f, scheduler.minimumInterEventSeconds);
            float max = Mathf.Max(min, scheduler.maximumInterEventSeconds);
            return Random.Range(min, max);
        }

        private void ResetBlockLeaderEvents()
        {
            activeBlockLeaderEvent = null;
            recentBlockLeaderEvent = null;
            previousLeaderEventClockSeconds = -1f;

            if (blockLeaderSpeedEvents == null)
                return;

            for (int i = 0; i < blockLeaderSpeedEvents.Length; i++)
            {
                if (blockLeaderSpeedEvents[i] != null)
                    blockLeaderSpeedEvents[i].ResetRuntime();
            }
        }

        private void ValidateBlockLeaderEventsForCurrentBlock()
        {
            if (blockLeaderSpeedEvents == null)
                return;

            bool hasProtectedWindow = music != null &&
                                      music.CurrentBlockHasTempoChange &&
                                      music.CurrentTempoChangeTimeSeconds > 0f;
            float protectedStart = 0f;
            float protectedEnd = 0f;
            if (hasProtectedWindow)
            {
                protectedStart = music.CurrentTempoChangeTimeSeconds - tempoChangeProtectedWindowSeconds;
                protectedEnd = music.CurrentTempoChangeTimeSeconds + tempoChangeProtectedWindowSeconds;
            }

            float leaderEventDuration = GetBlockLeaderSpeedEventDurationSeconds();
            for (int i = 0; i < blockLeaderSpeedEvents.Length; i++)
            {
                BlockLeaderSpeedEvent evt = blockLeaderSpeedEvents[i];
                if (evt == null)
                    continue;

                if (!evt.enabled)
                {
                    MarkBlockLeaderEventInvalid(evt, "disabled", false);
                    continue;
                }

                if (!hasProtectedWindow)
                    continue;

                float eventStart = evt.plannedStartSeconds;
                float eventEnd = eventStart + leaderEventDuration;
                bool overlapsProtectedWindow = eventStart < protectedEnd && eventEnd > protectedStart;
                if (overlapsProtectedWindow)
                {
                    MarkBlockLeaderEventInvalid(evt, "protected_window_overlap", true);
                    Debug.LogWarning(string.Format(
                        CultureInfo.InvariantCulture,
                        "[ExperimentSessionController] Leader speed event {0} at {1:F1}-{2:F1}s skipped in {3}: overlaps protected tempo window {4:F1}-{5:F1}s.",
                        evt.eventIndex,
                        eventStart,
                        eventEnd,
                        CurrentBlockCondition,
                        protectedStart,
                        protectedEnd));
                }
            }
        }

        private float GetBlockLeaderSpeedEventDurationSeconds()
        {
            if (leader == null)
                return 16f;

            return Mathf.Max(0f, leader.decelerationDurationSeconds) +
                   Mathf.Max(0f, leader.holdDurationSeconds) +
                   Mathf.Max(0f, leader.returnToCruiseDurationSeconds);
        }

        private float GetBlockLeaderEventClockSeconds()
        {
            if (IsV2ProtocolActive() &&
                music != null &&
                music.CurrentStimulusTimeSeconds >= 0f)
                return music.CurrentStimulusTimeSeconds;

            if (CurrentPhase == SessionPhase.ExperimentalBlock &&
                music != null &&
                music.HasCurrentClip &&
                music.CurrentPlaybackTime >= 0f)
                return music.CurrentPlaybackTime;

            return GetCurrentPhaseElapsedSeconds();
        }

        private bool IsExperimentalBlockRunning()
        {
            if (IsV2ProtocolActive() && music != null && music.CurrentStimulusTimeSeconds >= 0f)
                return music.CurrentStimulusTimeSeconds < currentPhaseDuration;

            return Time.time < phaseEndTime;
        }

        private void TryTriggerDueBlockLeaderEvents(float blockClockSeconds)
        {
            if (blockLeaderSpeedEvents == null)
                return;

            for (int i = 0; i < blockLeaderSpeedEvents.Length; i++)
            {
                BlockLeaderSpeedEvent evt = blockLeaderSpeedEvents[i];
                if (evt == null || evt.triggered || evt.missed || evt.completed || evt.invalid)
                    continue;

                if (!evt.enabled)
                {
                    MarkBlockLeaderEventInvalid(evt, "disabled", false);
                    continue;
                }

                if (!HasLeaderEventClockCrossed(evt.plannedStartSeconds, blockClockSeconds))
                    continue;

                if (leader == null)
                {
                    MarkBlockLeaderEventMissed(evt, "leader_missing");
                    continue;
                }

                if (!leader.IsRunning)
                {
                    MarkBlockLeaderEventMissed(evt, "leader_not_running");
                    continue;
                }

                if (leader.IsSpeedEventActive || leader.CurrentSpeedEventPhase != "none")
                {
                    MarkBlockLeaderEventMissed(evt, "leader_not_cruise");
                    continue;
                }

                leader.TriggerDeceleration();
                if (!leader.IsSpeedEventActive)
                {
                    MarkBlockLeaderEventMissed(evt, "leader_not_cruise");
                    continue;
                }

                evt.triggered = true;
                evt.actualStartSeconds = blockClockSeconds;
                evt.decelStartSeconds = blockClockSeconds;
                evt.phase = "decelerating";
                evt.lastObservedPhase = "decelerating";
                activeBlockLeaderEvent = evt;
                recentBlockLeaderEvent = evt;

                Debug.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "[ExperimentSessionController] Leader speed event {0} triggered at block/audio t={1:F2}s.",
                    evt.eventIndex,
                    blockClockSeconds));
                return;
            }
        }

        private bool HasLeaderEventClockCrossed(float plannedStartSeconds, float currentClockSeconds)
        {
            return previousLeaderEventClockSeconds >= 0f &&
                   previousLeaderEventClockSeconds < plannedStartSeconds &&
                   currentClockSeconds >= plannedStartSeconds;
        }

        private void UpdatePreviousLeaderEventClock(float currentClockSeconds)
        {
            if (currentClockSeconds < 0f)
            {
                previousLeaderEventClockSeconds = -1f;
                return;
            }

            if (previousLeaderEventClockSeconds >= 0f && currentClockSeconds < previousLeaderEventClockSeconds)
            {
                previousLeaderEventClockSeconds = currentClockSeconds;
                return;
            }

            previousLeaderEventClockSeconds = currentClockSeconds;
        }

        private void UpdateActiveBlockLeaderEventState(float blockClockSeconds)
        {
            BlockLeaderSpeedEvent evt = activeBlockLeaderEvent;
            if (evt == null || !evt.triggered || evt.completed || evt.missed)
                return;

            string currentPhase = leader != null ? leader.CurrentSpeedEventPhase : "none";
            if (!string.Equals(evt.lastObservedPhase, currentPhase, System.StringComparison.Ordinal))
            {
                RecordBlockLeaderEventPhaseTransition(evt, evt.lastObservedPhase, currentPhase, blockClockSeconds);
                evt.lastObservedPhase = currentPhase;
            }

            if (currentPhase == "none" && (leader == null || !leader.IsSpeedEventActive))
            {
                CompleteBlockLeaderEvent(evt, blockClockSeconds);
                activeBlockLeaderEvent = null;
                recentBlockLeaderEvent = evt;
            }
            else
            {
                evt.phase = NormalizeLeaderEventPhase(currentPhase);
            }
        }

        private void RecordBlockLeaderEventPhaseTransition(BlockLeaderSpeedEvent evt, string previousPhase, string nextPhase, float blockClockSeconds)
        {
            if (evt == null)
                return;

            if (nextPhase == "hold")
            {
                if (evt.decelEndSeconds < 0f)
                    evt.decelEndSeconds = blockClockSeconds;
                if (evt.holdStartSeconds < 0f)
                    evt.holdStartSeconds = blockClockSeconds;
                evt.phase = "hold";
                return;
            }

            if (nextPhase == "recovery")
            {
                if (previousPhase == "decelerating" && evt.decelEndSeconds < 0f)
                    evt.decelEndSeconds = blockClockSeconds;
                if (previousPhase == "hold" && evt.holdEndSeconds < 0f)
                    evt.holdEndSeconds = blockClockSeconds;
                if (evt.recoveryStartSeconds < 0f)
                    evt.recoveryStartSeconds = blockClockSeconds;
                evt.phase = "recovery";
                return;
            }

            if (nextPhase == "none")
            {
                if (previousPhase == "decelerating" && evt.decelEndSeconds < 0f)
                    evt.decelEndSeconds = blockClockSeconds;
                if (previousPhase == "hold" && evt.holdEndSeconds < 0f)
                    evt.holdEndSeconds = blockClockSeconds;
                if ((previousPhase == "recovery" || evt.recoveryStartSeconds >= 0f) && evt.recoveryEndSeconds < 0f)
                    evt.recoveryEndSeconds = blockClockSeconds;
            }
        }

        private void CompleteBlockLeaderEvent(BlockLeaderSpeedEvent evt, float blockClockSeconds)
        {
            if (evt == null || evt.completed)
                return;

            if (evt.decelEndSeconds < 0f)
                evt.decelEndSeconds = blockClockSeconds;
            if (evt.holdStartSeconds >= 0f && evt.holdEndSeconds < 0f)
                evt.holdEndSeconds = blockClockSeconds;
            if (evt.recoveryEndSeconds < 0f)
                evt.recoveryEndSeconds = blockClockSeconds;

            evt.phase = "completed";
            evt.completed = true;
            evt.completionSeconds = blockClockSeconds;
        }

        private void MarkBlockLeaderEventInvalid(BlockLeaderSpeedEvent evt, string reason, bool warnOnce)
        {
            if (evt == null)
                return;

            evt.invalid = true;
            evt.phase = "invalid";
            evt.invalidReason = string.IsNullOrWhiteSpace(reason) ? "none" : reason;

            if (warnOnce)
                evt.warningLogged = true;
        }

        private void MarkBlockLeaderEventMissed(BlockLeaderSpeedEvent evt, string reason)
        {
            if (evt == null)
                return;

            evt.missed = true;
            evt.phase = "missed";
            evt.invalidReason = string.IsNullOrWhiteSpace(reason) ? "none" : reason;
            recentBlockLeaderEvent = evt;

            if (!evt.warningLogged)
            {
                evt.warningLogged = true;
                Debug.LogWarning(string.Format(
                    CultureInfo.InvariantCulture,
                    "[ExperimentSessionController] Leader speed event {0} at {1:F1}s missed: {2}.",
                    evt.eventIndex,
                    evt.plannedStartSeconds,
                    evt.invalidReason));
            }
        }

        private void PublishBlockLeaderEventLoggerState(float blockClockSeconds)
        {
            if (logger == null)
                return;

            BlockLeaderSpeedEvent evt = activeBlockLeaderEvent ??
                                        GetRecentCompletedBlockLeaderEventForLogging(blockClockSeconds) ??
                                        GetNextBlockLeaderEventForLogging(blockClockSeconds);
            if (evt == null)
            {
                logger.ClearLeaderEvent();
                return;
            }

            logger.SetLeaderEvent(
                evt.eventIndex,
                evt.plannedStartSeconds,
                evt.actualStartSeconds,
                GetBlockLeaderEventPhaseForLogger(evt),
                evt.decelStartSeconds,
                evt.decelEndSeconds,
                evt.holdStartSeconds,
                evt.holdEndSeconds,
                evt.recoveryStartSeconds,
                evt.recoveryEndSeconds,
                evt.invalid || evt.missed ? 0 : 1,
                evt.invalid || evt.missed ? evt.invalidReason : "none");
        }

        private BlockLeaderSpeedEvent GetRecentCompletedBlockLeaderEventForLogging(float blockClockSeconds)
        {
            if (recentBlockLeaderEvent == null || !recentBlockLeaderEvent.completed)
                return null;

            if (recentBlockLeaderEvent.completionSeconds < 0f)
                return null;

            if (blockClockSeconds < recentBlockLeaderEvent.completionSeconds)
                return null;

            float displaySeconds = Mathf.Max(0f, completedLeaderEventDisplaySeconds);
            if (blockClockSeconds - recentBlockLeaderEvent.completionSeconds > displaySeconds)
                return null;

            return recentBlockLeaderEvent;
        }

        private BlockLeaderSpeedEvent GetNextBlockLeaderEventForLogging(float blockClockSeconds)
        {
            if (blockLeaderSpeedEvents == null)
                return null;

            BlockLeaderSpeedEvent next = null;
            for (int i = 0; i < blockLeaderSpeedEvents.Length; i++)
            {
                BlockLeaderSpeedEvent evt = blockLeaderSpeedEvents[i];
                if (evt == null || evt.triggered || evt.missed || evt.completed)
                    continue;

                if (evt.plannedStartSeconds < blockClockSeconds)
                    continue;

                if (next == null || evt.plannedStartSeconds < next.plannedStartSeconds)
                    next = evt;
            }

            return next;
        }

        private static string GetBlockLeaderEventPhaseForLogger(BlockLeaderSpeedEvent evt)
        {
            if (evt == null)
                return "none";
            if (evt.invalid)
                return "invalid";
            if (evt.missed)
                return "missed";
            if (evt.completed)
                return "completed";
            if (evt.triggered)
                return string.IsNullOrWhiteSpace(evt.phase) ? "none" : evt.phase;

            return evt.enabled ? "pending" : "invalid";
        }

        private static string NormalizeLeaderEventPhase(string phase)
        {
            if (phase == "decelerating" || phase == "hold" || phase == "recovery")
                return phase;

            return "none";
        }

        private void ClearBlockLeaderEventLogger()
        {
            activeBlockLeaderEvent = null;
            recentBlockLeaderEvent = null;
            if (logger != null)
                logger.ClearLeaderEvent();
        }

        private void PlaceParticipantAtRouteStart()
        {
            if (centerline == null || centerline.waypoints == null || centerline.waypoints.Length < 2 || participantVehicle == null)
                return;

            Transform first = centerline.waypoints[0];
            Transform second = centerline.waypoints[1];
            if (first == null || second == null)
                return;

            Vector3 direction = second.position - first.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
                return;

            direction.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
            Vector3 startPosition = first.position + right * participantRightLaneOffsetMeters + Vector3.up * Mathf.Max(0.01f, participantSpawnHeightOffsetMeters);
            if (TryProjectParticipantToRoadSurface(startPosition, out Vector3 groundedStartPosition))
                startPosition = groundedStartPosition;

            Quaternion startRotation = Quaternion.LookRotation(direction, Vector3.up);
            ApplyParticipantResetPose(startPosition, startRotation);
        }

        private void ApplyParticipantResetPose(Vector3 position, Quaternion rotation)
        {
            if (participantRigidbody != null)
                StopParticipantMotion();

            participantVehicle.SetPositionAndRotation(position, rotation);

            if (participantRigidbody != null)
            {
                participantRigidbody.position = position;
                participantRigidbody.rotation = rotation;
                StopParticipantMotion();
            }

            Physics.SyncTransforms();
        }

        private void ResetV2LeaderSpeedProfile()
        {
            previousV2LeaderClockSeconds = -1f;
            ClearBlockLeaderEventLogger();
            if (leader != null)
                leader.ForceCruiseState();
        }

        private void UpdateV2LeaderSpeedProfile(float blockClockSeconds)
        {
            if (leader == null || blockClockSeconds < 0f)
                return;

            bool crossedCar1 = previousV2LeaderClockSeconds < V2ProtocolDefinition.LeaderCar1Seconds &&
                               blockClockSeconds >= V2ProtocolDefinition.LeaderCar1Seconds;
            bool crossedCar2 = previousV2LeaderClockSeconds < V2ProtocolDefinition.LeaderCar2Seconds &&
                               blockClockSeconds >= V2ProtocolDefinition.LeaderCar2Seconds;

            if (crossedCar1 &&
                !leader.StartSpeedRamp(80f, V2ProtocolDefinition.LeaderRampDurationSeconds, false))
            {
                Debug.LogWarning("[ExperimentSessionController] V2 CAR_1 leader ramp could not start.");
            }

            if (crossedCar2 &&
                !leader.StartSpeedRamp(70f, V2ProtocolDefinition.LeaderRampDurationSeconds, true))
            {
                Debug.LogWarning("[ExperimentSessionController] V2 CAR_2 leader ramp could not start.");
            }

            previousV2LeaderClockSeconds = blockClockSeconds;
        }

        private void ForceAutomaticTransmissionForSessionStart()
        {
            TransmissionModeManager transmissionModeManager = FindAnyObjectByType<TransmissionModeManager>();
            if (transmissionModeManager != null)
            {
                transmissionModeManager.ForceAutomaticForSessionStart();
                return;
            }

            Debug.LogWarning("[ResearchSim] TransmissionModeManager not found; cannot force Automatic at session start.");
        }

        private void StopParticipantMotion()
        {
            if (participantRigidbody == null)
                return;

#if UNITY_6000_0_OR_NEWER
            participantRigidbody.linearVelocity = Vector3.zero;
#else
            participantRigidbody.velocity = Vector3.zero;
#endif
            participantRigidbody.angularVelocity = Vector3.zero;
        }

        private void PrepareVehicleForParticipantStart()
        {
            if (inputBridge == null && participantVehicle != null)
                inputBridge = participantVehicle.GetComponentInChildren<VppExternalInputBridge>(true);

            if (inputBridge != null)
                inputBridge.HoldIgnitionOffForExperiment();
        }

        private void ResetOpenRouteForNextPhase(string phaseLabel)
        {
            if (!resetOpenRouteAtPhaseStart || centerline == null || centerline.closedLoop)
                return;

            PlaceParticipantAtRouteStart();
            PrepareVehicleForParticipantStart();

            if (leader != null && participantVehicle != null)
                leader.RestartFromParticipant(participantVehicle.position, false);

            Debug.Log("[ExperimentSessionController] RESET_FOR_NEXT_BLOCK: open highway route reset before " + phaseLabel + ".");
        }

        private void ResetOpenRouteIfNearEnd(string phaseLabel)
        {
            if (!resetOpenRouteWhenNearEnd || centerline == null || centerline.closedLoop || participantVehicle == null || leader == null)
                return;

            if (Time.time - lastOpenRouteResetTime < OpenRouteResetCooldownSeconds)
                return;

            float totalLength = leader.GetTotalPathLength();
            if (totalLength <= 0f)
                return;

            float participantDistance = leader.GetDistanceAlongPathForPosition(participantVehicle.position);
            bool participantNearEnd = totalLength - participantDistance <= openRouteResetMarginMeters;
            bool leaderNearEnd = totalLength - leader.DistanceAlongPath <= openRouteResetMarginMeters;
            bool leaderStoppedAtEnd = SessionRunning && !leader.IsRunning && leader.DistanceAlongPath >= totalLength - 0.5f;

            if (!participantNearEnd && !leaderNearEnd && !leaderStoppedAtEnd)
                return;

            PlaceParticipantAtRouteStart();
            PrepareVehicleForParticipantStart();
            leader.RestartFromParticipant(participantVehicle.position, false);
            lastOpenRouteResetTime = Time.time;
            Debug.Log("[ExperimentSessionController] RESET_FOR_NEXT_BLOCK: open highway route reset during " + phaseLabel + " because the test path reached its end.");
        }

        private bool TryProjectParticipantToRoadSurface(Vector3 position, out Vector3 projected)
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
                if (participantVehicle != null && (hitTransform == participantVehicle || hitTransform.IsChildOf(participantVehicle)))
                    continue;

                // The reset ray can cross visual/collision details near the road.
                // Accept only reasonably horizontal surfaces so the car is placed
                // on the road/terrain, not on guardrails or other side geometry.
                if (Vector3.Dot(hit.normal, Vector3.up) < RoadSurfaceMinimumUpDot)
                    continue;

                if (hit.distance >= bestDistance)
                    continue;

                bestDistance = hit.distance;
                projected = hit.point + Vector3.up * Mathf.Max(0.01f, participantSpawnHeightOffsetMeters);
                found = true;
            }

            return found;
        }

        private void ResolveReferences()
        {
            if (centerline == null)
                centerline = FindAnyObjectByType<CenterlinePath>();
            if (scheduler == null)
                scheduler = GetComponent<TrialScheduler>() ?? FindAnyObjectByType<TrialScheduler>();
            if (music == null)
                music = GetComponent<MusicEventController>() ?? FindAnyObjectByType<MusicEventController>();
            if (leader == null)
                leader = FindAnyObjectByType<LeadVehicleController>();
            if (logger == null)
                logger = GetComponent<DrivingDataLogger>() ?? FindAnyObjectByType<DrivingDataLogger>();
            if (feedbackController == null)
                feedbackController = GetComponent<CarFollowingFeedbackController>() ?? FindAnyObjectByType<CarFollowingFeedbackController>();

            if (participantVehicle == null)
            {
                GameObject vehicle = GameObject.Find("Research VPP Vehicle");
                if (vehicle != null)
                    participantVehicle = vehicle.transform;
            }

            if (participantRigidbody == null && participantVehicle != null)
                participantRigidbody = participantVehicle.GetComponent<Rigidbody>();

            if (inputBridge == null && participantVehicle != null)
                inputBridge = participantVehicle.GetComponentInChildren<VppExternalInputBridge>(true);
        }

        private void UpdateFeedbackControllerState()
        {
            if (feedbackController == null)
                return;

            if (sessionPlan == null)
                sessionPlan = BuildSessionPlan(participantId);

            bool suppressFeedback = CurrentPhase == SessionPhase.Idle ||
                CurrentPhase == SessionPhase.WaitingForParticipantStart ||
                CurrentPhase == SessionPhase.Completed ||
                blockEndingCoasting ||
                preparingNextSegment ||
                !SessionRunning;

            feedbackController.SetProtocolState(
                sessionPlan.feedbackEnabled,
                sessionPlan.feedbackSettings,
                CurrentPhase,
                suppressFeedback,
                IsFeedbackAllowedByProfileForPhase(CurrentPhase));
        }

        private bool IsFeedbackAllowedByProfileForPhase(SessionPhase phase)
        {
            if (sessionPlan == null)
                return false;

            switch (phase)
            {
                case SessionPhase.Familiarization:
                    return sessionPlan.showFeedbackDuringFamiliarization;
                case SessionPhase.Baseline:
                    return sessionPlan.showFeedbackDuringBaseline;
                case SessionPhase.ExperimentalBlock:
                    return sessionPlan.showFeedbackDuringExperimentalBlocks;
                default:
                    return false;
            }
        }

        private void UpdateLoggerPhase()
        {
            if (logger == null)
                return;

            logger.phase = CurrentPhase.ToString();
            UpdateLoggerProtocolState();
            if (CurrentEvent != null)
                logger.SetEvent(CurrentEvent);
        }

        private void UpdateLoggerProtocolState()
        {
            if (logger == null)
                return;

            logger.protocolState = GetSessionStateLabel();
            logger.effectiveBlockDurationSeconds = currentPhaseDuration;
            logger.musicClipLengthSeconds = music != null && music.HasCurrentClip ? music.CurrentClipLengthSeconds : -1f;
            logger.throttleGateActive = IsThrottleGateActive();
            logger.debugSkipRequested = skipRequested;
            logger.phaseElapsedSeconds = GetCurrentPhaseElapsedSeconds();
            logger.phaseRemainingSeconds = GetCurrentPhaseRemainingSeconds();
            logger.participantIdSource = participantIdSource;
            logger.uxfSessionNumber = uxfSessionNumber;
            logger.uxfSessionId = uxfSessionId;
            ApplyProtocolMetadataToLogger();
        }

        private void ApplyProtocolMetadataToLogger()
        {
            if (logger == null)
                return;

            if (sessionPlan == null)
                sessionPlan = BuildSessionPlan(participantId);

            logger.protocolProfile = sessionPlan.profileId;
            logger.protocolVersion = sessionPlan.protocolVersion;
            logger.blockOrderMode = sessionPlan.blockOrderMode.ToString();
            logger.blockOrderSeed = sessionPlan.blockOrderSeed;
            logger.counterbalanceIndex = sessionPlan.counterbalanceIndex;
            logger.sessionBlockSequence = sessionPlan.blockSequence;
        }

        private string ResolveParticipantIdForLogging(bool warnIfUsingFallback)
        {
            uxfSessionNumber = -1;
            uxfSessionId = "";

            if (!string.IsNullOrWhiteSpace(participantIdOverride))
            {
                participantIdSource = "participantIdOverride";
                return participantIdOverride.Trim();
            }

            if (!debugBypassUxfMetadata && TryGetUxfParticipantMetadata(out string uxfParticipantId, out int sessionNumber))
            {
                participantIdSource = "UXF";
                uxfSessionNumber = sessionNumber;
                uxfSessionId = uxfParticipantId + "_s" + Mathf.Max(0, sessionNumber).ToString("000", CultureInfo.InvariantCulture);
                ExperimentSession.SetParticipantID(uxfParticipantId);
                return uxfParticipantId;
            }

            if (debugBypassUxfMetadata)
            {
                participantIdSource = "F8DebugFallback";
                uxfSessionNumber = -1;
                uxfSessionId = "none";

                if (!loggedDebugUxfMetadataBypass)
                {
                    loggedDebugUxfMetadataBypass = true;
                    Debug.Log("[ExperimentSessionController] F8 debug quick-start is using participant_id 'F8_DebugSession' and ignoring UXF metadata.");
                }

                return "F8_DebugSession";
            }

            if (IsConfiguredV2Protocol())
            {
                participantIdSource = "UXFPending";
                return "UXF_PENDING";
            }

            participantIdSource = "ExperimentSessionFallback";
            string fallback = ExperimentSession.GetFileSafeParticipantID();
            if (warnIfUsingFallback && !warnedUsingFallbackParticipantId)
            {
                warnedUsingFallbackParticipantId = true;
                Debug.LogWarning("[ExperimentSessionController] UXF participant ID is not available; custom CSV will use fallback participant_id '" + fallback + "'. This is expected when F8/debug bypass is used.");
            }

            return fallback;
        }

        private static bool TryGetUxfParticipantMetadata(out string participantIdentifier, out int sessionNumber)
        {
            participantIdentifier = "";
            sessionNumber = -1;

            Session session = Session.instance;
            if (session == null || !session.hasInitialised || string.IsNullOrWhiteSpace(session.ppid))
                return false;

            participantIdentifier = session.ppid.Trim();
            sessionNumber = session.number;
            return true;
        }

        private void BeginPhaseTimer(float duration)
        {
            phaseStartTime = Time.time;
            currentPhaseDuration = Mathf.Max(0f, duration);
            phaseEndTime = Time.time + currentPhaseDuration;
        }

        private float GetExperimentalBlockDurationFromCurrentClip()
        {
            if (music != null && music.HasCurrentClip && music.CurrentClipLengthSeconds > 0f)
                return music.CurrentClipLengthSeconds;

            float fallbackDuration = sessionPlan != null
                ? sessionPlan.fallbackExperimentalBlockSeconds
                : experimentalBlockSeconds;
            Debug.LogWarning(string.Format(
                CultureInfo.InvariantCulture,
                "[ExperimentSessionController] No valid clip duration for {0}; using configured fallback block duration {1}.",
                CurrentBlockCondition,
                FormatSeconds(fallbackDuration)));
            return fallbackDuration;
        }

        private void UpdateBlockClipDiagnostics(int blockIndex)
        {
            currentBlockClipWarning = "";

            if (music == null)
            {
                currentBlockClipWarning = "Warning: MusicEventController missing.";
                Debug.LogWarning("[ExperimentSessionController] " + currentBlockClipWarning);
                return;
            }

            if (!music.HasCurrentClip)
            {
                currentBlockClipWarning = "Warning: no clip assigned for " + CurrentBlockCondition + ".";
                Debug.LogWarning("[ExperimentSessionController] " + currentBlockClipWarning);
                return;
            }

            float clipLength = music.CurrentClipLengthSeconds;
            float difference = clipLength - experimentalBlockSeconds;
            if (Mathf.Abs(difference) < 0.5f)
                return;

            string direction = difference < 0f ? "shorter than" : "longer than";
            currentBlockClipWarning = string.Format(
                CultureInfo.InvariantCulture,
                "Note: clip is {0} configured duration by {1}.",
                direction,
                FormatSeconds(Mathf.Abs(difference)));

            Debug.LogWarning(string.Format(
                CultureInfo.InvariantCulture,
                "[ExperimentSessionController] Block {0}/{1} {2}: clip length {3}, configured duration {4}, actual block duration {5}. {6}",
                blockIndex + 1,
                GetActiveExperimentalBlockCount(),
                CurrentBlockCondition,
                FormatSeconds(clipLength),
                FormatSeconds(experimentalBlockSeconds),
                FormatSeconds(currentPhaseDuration),
                currentBlockClipWarning));
        }

        private float GetCurrentPhaseElapsedSeconds()
        {
            if (CurrentPhase == SessionPhase.Idle)
                return 0f;

            return Mathf.Max(0f, Time.time - phaseStartTime);
        }

        private float GetCurrentPhaseRemainingSeconds()
        {
            if (currentPhaseDuration <= 0f)
                return 0f;

            return Mathf.Max(0f, phaseEndTime - Time.time);
        }

        private static string FormatSeconds(float seconds)
        {
            if (seconds < 0f || float.IsNaN(seconds) || float.IsInfinity(seconds))
                return "--:--";

            int rounded = Mathf.Max(0, Mathf.RoundToInt(seconds));
            int minutes = rounded / 60;
            int remainingSeconds = rounded % 60;
            return minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + remainingSeconds.ToString("00", CultureInfo.InvariantCulture);
        }

        private string GetSessionStateLabel()
        {
            if (CurrentPhase == SessionPhase.Completed)
                return "completed";
            if (skipRequested)
                return "debug skip";
            if (preparingNextSegment)
                return "preparing next segment";
            if (blockEndingCoasting)
                return "phase ending / coasting";
            if (CurrentPhase == SessionPhase.WaitingForParticipantStart || (SessionArmed && !SessionRunning))
                return "waiting for participant movement";
            if (SessionRunning)
                return "running";

            return "waiting";
        }

        private void SetThrottleGate(bool disabled)
        {
            if (inputBridge == null && participantVehicle != null)
                inputBridge = participantVehicle.GetComponentInChildren<VppExternalInputBridge>(true);

            if (inputBridge != null)
                inputBridge.SetExperimentThrottleDisabled(disabled);
        }

        private bool IsThrottleGateActive()
        {
            return inputBridge != null && inputBridge.experimentThrottleDisabled;
        }

        private static bool IsExperimenterContinuePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.enterKey.wasPressedThisFrame)
                    return true;
                if (Keyboard.current.numpadEnterKey.wasPressedThisFrame)
                    return true;
            }
#endif
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        }

        private string GetMusicStatusLabel()
        {
            if (CurrentPhase == SessionPhase.WaitingForParticipantStart && CurrentBlockIndex >= 0)
                return "waiting";
            if (CurrentPhase != SessionPhase.ExperimentalBlock)
                return "none";
            if (music == null || !music.HasCurrentClip)
                return "none";
            if (music.IsPlaying)
                return "playing";

            return "ended";
        }

        private static string GetPhaseEndLabel(SessionPhase phase)
        {
            switch (phase)
            {
                case SessionPhase.Familiarization:
                    return "fase di familiarizzazione";
                case SessionPhase.Baseline:
                    return "fase baseline";
                case SessionPhase.ExperimentalBlock:
                    return "blocco";
                default:
                    return "fase";
            }
        }

        private SessionPlan BuildSessionPlan(string seedText)
        {
            SessionPlan plan = new SessionPlan();
            MusicEventController.MusicBlockCondition[] sourceConditions;

            if (protocolProfile != null)
            {
                plan.profileId = protocolProfile.ProfileIdOrName;
                plan.protocolVersion = string.IsNullOrWhiteSpace(protocolProfile.protocolVersion)
                    ? "V1"
                    : protocolProfile.protocolVersion.Trim();
                plan.useV2Protocol = protocolProfile.useV2Protocol;
                plan.fallbackExperimentalBlockSeconds = protocolProfile.fallbackExperimentalBlockSeconds > 0f
                    ? Mathf.Max(30f, protocolProfile.fallbackExperimentalBlockSeconds)
                    : experimentalBlockSeconds;
                plan.includeFamiliarization = protocolProfile.includeFamiliarization;
                plan.familiarizationSeconds = Mathf.Max(10f, protocolProfile.familiarizationSeconds);
                plan.includeBaseline = protocolProfile.includeBaseline;
                plan.baselineSeconds = Mathf.Max(10f, protocolProfile.baselineSeconds);
                plan.blockOrderMode = protocolProfile.blockOrderMode;
                plan.feedbackEnabled = protocolProfile.enableFeedback;
                plan.feedbackSettings = protocolProfile.feedbackSettings;
                plan.showFeedbackDuringFamiliarization = protocolProfile.showInFamiliarization;
                plan.showFeedbackDuringBaseline = protocolProfile.showInBaseline;
                plan.showFeedbackDuringExperimentalBlocks = protocolProfile.showInExperimentalBlocks;
                sourceConditions = GetProfileConditions(protocolProfile);
            }
            else
            {
                plan.profileId = "fallback defaults";
                plan.protocolVersion = "V1";
                plan.useV2Protocol = false;
                plan.fallbackExperimentalBlockSeconds = experimentalBlockSeconds;
                plan.includeFamiliarization = true;
                plan.familiarizationSeconds = familiarizationSeconds;
                plan.includeBaseline = true;
                plan.baselineSeconds = baselineSeconds;
                plan.blockOrderMode = BlockOrderMode.ShuffleByParticipantId;
                plan.feedbackEnabled = true;
                plan.feedbackSettings = null;
                plan.showFeedbackDuringFamiliarization = true;
                plan.showFeedbackDuringBaseline = true;
                plan.showFeedbackDuringExperimentalBlocks = true;
                sourceConditions = BuildFallbackConditionSource();
            }

            plan.blockConditions = BuildOrderedBlockConditions(sourceConditions, plan.blockOrderMode, seedText, out int orderSeed, out int counterbalanceIndex);
            plan.blockOrderSeed = orderSeed;
            plan.counterbalanceIndex = counterbalanceIndex;
            plan.blockSequence = FormatConditionSequence(plan.blockConditions);
            return plan;
        }

        private bool IsV2ProtocolActive()
        {
            return sessionPlan != null &&
                   sessionPlan.useV2Protocol &&
                   V2ProtocolDefinition.IsV2(sessionPlan.protocolVersion);
        }

        private bool IsConfiguredV2Protocol()
        {
            if (sessionPlan != null)
                return IsV2ProtocolActive();

            return protocolProfile != null &&
                   protocolProfile.useV2Protocol &&
                   V2ProtocolDefinition.IsV2(protocolProfile.protocolVersion);
        }

        private MusicEventController.MusicBlockCondition[] BuildFallbackConditionSource()
        {
            MusicEventController.MusicBlockCondition[] source =
                defaultBlockConditions != null && defaultBlockConditions.Length > 0
                    ? defaultBlockConditions
                    : new[]
                    {
                        MusicEventController.MusicBlockCondition.SlowFast,
                        MusicEventController.MusicBlockCondition.FastSlow,
                        MusicEventController.MusicBlockCondition.ControlStable
                    };

            MusicEventController.MusicBlockCondition[] result = new MusicEventController.MusicBlockCondition[Mathf.Max(1, numberOfBlocks)];
            for (int i = 0; i < result.Length; i++)
                result[i] = source[i % source.Length];

            return result;
        }

        private static MusicEventController.MusicBlockCondition[] GetProfileConditions(ExperimentProtocolProfile profile)
        {
            if (profile == null || profile.experimentalBlockConditions == null)
                return new MusicEventController.MusicBlockCondition[0];

            MusicEventController.MusicBlockCondition[] result = new MusicEventController.MusicBlockCondition[profile.experimentalBlockConditions.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = profile.experimentalBlockConditions[i];

            return result;
        }

        private MusicEventController.MusicBlockCondition[] BuildOrderedBlockConditions(
            MusicEventController.MusicBlockCondition[] source,
            BlockOrderMode orderMode,
            string seedText,
            out int orderSeed,
            out int counterbalanceIndex)
        {
            orderSeed = string.IsNullOrWhiteSpace(seedText) ? System.Environment.TickCount : StableStringHash(seedText);
            counterbalanceIndex = -1;

            if (source == null || source.Length == 0)
                return new MusicEventController.MusicBlockCondition[0];

            MusicEventController.MusicBlockCondition[] result = CopyConditions(source);
            switch (orderMode)
            {
                case BlockOrderMode.Fixed:
                case BlockOrderMode.ManualProfileOrder:
                    return result;

                case BlockOrderMode.RandomEachSession:
                    orderSeed = System.Environment.TickCount;
                    ShuffleConditions(result, new System.Random(orderSeed));
                    return result;

                case BlockOrderMode.CounterbalancedByParticipantId:
                    if (string.IsNullOrWhiteSpace(seedText))
                        orderSeed = 0;
                    return BuildCounterbalancedOrder(result, orderSeed, out counterbalanceIndex);

                case BlockOrderMode.CounterbalancedByParticipantNumberModulo:
                    return BuildParticipantNumberModuloOrder(
                        result,
                        seedText,
                        out orderSeed,
                        out counterbalanceIndex);

                case BlockOrderMode.ShuffleByParticipantId:
                default:
                    ShuffleConditions(result, new System.Random(orderSeed));
                    return result;
            }
        }

        private static MusicEventController.MusicBlockCondition[] CopyConditions(MusicEventController.MusicBlockCondition[] source)
        {
            MusicEventController.MusicBlockCondition[] result = new MusicEventController.MusicBlockCondition[source.Length];
            for (int i = 0; i < source.Length; i++)
                result[i] = source[i];

            return result;
        }

        private static void ShuffleConditions(MusicEventController.MusicBlockCondition[] result, System.Random rng)
        {
            for (int i = result.Length - 1; i > 0; i--)
            {
                int swapIndex = rng.Next(i + 1);
                MusicEventController.MusicBlockCondition tmp = result[i];
                result[i] = result[swapIndex];
                result[swapIndex] = tmp;
            }
        }

        private static MusicEventController.MusicBlockCondition[] BuildCounterbalancedOrder(
            MusicEventController.MusicBlockCondition[] source,
            int seed,
            out int counterbalanceIndex)
        {
            return BuildCounterbalancedOrderByIndex(source, seed, out counterbalanceIndex);
        }

        private static MusicEventController.MusicBlockCondition[] BuildCounterbalancedOrderByIndex(
            MusicEventController.MusicBlockCondition[] source,
            int requestedIndex,
            out int counterbalanceIndex)
        {
            counterbalanceIndex = -1;

            if (source.Length <= 1)
                return CopyConditions(source);

            if (source.Length == 2)
            {
                counterbalanceIndex = PositiveModulo(requestedIndex, 2);
                if (counterbalanceIndex == 0)
                    return CopyConditions(source);

                return new[] { source[1], source[0] };
            }

            if (source.Length == 3)
            {
                MusicEventController.MusicBlockCondition[][] permutations =
                {
                    new[] { source[0], source[1], source[2] },
                    new[] { source[0], source[2], source[1] },
                    new[] { source[1], source[0], source[2] },
                    new[] { source[1], source[2], source[0] },
                    new[] { source[2], source[0], source[1] },
                    new[] { source[2], source[1], source[0] }
                };
                counterbalanceIndex = PositiveModulo(requestedIndex, permutations.Length);
                return CopyConditions(permutations[counterbalanceIndex]);
            }

            MusicEventController.MusicBlockCondition[] result = CopyConditions(source);
            ShuffleConditions(result, new System.Random(requestedIndex));
            counterbalanceIndex = 0;
            return result;
        }

        private MusicEventController.MusicBlockCondition[] BuildParticipantNumberModuloOrder(
            MusicEventController.MusicBlockCondition[] source,
            string participantIdentifier,
            out int orderSeed,
            out int counterbalanceIndex)
        {
            orderSeed = 0;
            counterbalanceIndex = -1;

            if (debugBypassUxfMetadata)
            {
                if (!loggedNumericModuloDebugBypass)
                {
                    loggedNumericModuloDebugBypass = true;
                    Debug.Log("[ExperimentSessionController] CounterbalancedByParticipantNumberModulo bypassed for F8 debug mode; using profile order.");
                }

                return CopyConditions(source);
            }

            if (IsPendingFallbackParticipantId())
            {
                if (!loggedNumericModuloPendingParticipantId)
                {
                    loggedNumericModuloPendingParticipantId = true;
                    Debug.Log("[ExperimentSessionController] CounterbalancedByParticipantNumberModulo is waiting for a finalized UXF participant ID; using temporary profile order while armed.");
                }

                return CopyConditions(source);
            }

            if (!TryParseParticipantNumberModuloId(participantIdentifier, out int participantNumber, out string normalizedParticipantId))
            {
                if (!loggedNumericModuloInvalidParticipantId)
                {
                    loggedNumericModuloInvalidParticipantId = true;
                    Debug.LogError("CounterbalancedByParticipantNumberModulo requires a participant ID like P001, P5, p41, or 001. Could not parse: " + (participantIdentifier ?? "<null>"));
                }

                return new MusicEventController.MusicBlockCondition[0];
            }

            orderSeed = participantNumber;
            MusicEventController.MusicBlockCondition[] ordered = BuildCounterbalancedOrderByIndex(
                source,
                participantNumber - 1,
                out counterbalanceIndex);
            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[ExperimentSessionController] CounterbalancedByParticipantNumberModulo: participantId={0}, normalized={1}, participantNumber={2}, counterbalanceIndex={3}.",
                participantIdentifier,
                normalizedParticipantId,
                participantNumber,
                counterbalanceIndex));
            return ordered;
        }

        private bool IsPendingFallbackParticipantId()
        {
            bool pendingParticipantId =
                string.Equals(participantIdSource, "UXFPending", System.StringComparison.Ordinal) ||
                string.Equals(participantIdSource, "ExperimentSessionFallback", System.StringComparison.Ordinal);
            return pendingParticipantId &&
                !SessionRunning &&
                !debugBypassUxfMetadata;
        }

        private static bool TryParseParticipantNumberModuloId(string rawParticipantId, out int participantNumber, out string normalizedParticipantId)
        {
            participantNumber = 0;
            normalizedParticipantId = "";

            if (string.IsNullOrWhiteSpace(rawParticipantId))
                return false;

            string candidate = rawParticipantId.Trim().ToUpperInvariant();
            string digits;
            if (candidate.StartsWith("P", System.StringComparison.Ordinal))
            {
                if (candidate.Length < 2 || candidate.Length > 4)
                    return false;

                digits = candidate.Substring(1);
            }
            else
            {
                if (candidate.Length < 1 || candidate.Length > 3)
                    return false;

                digits = candidate;
            }

            for (int i = 0; i < digits.Length; i++)
            {
                if (!char.IsDigit(digits[i]))
                    return false;
            }

            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out participantNumber))
                return false;

            if (participantNumber < 1)
                return false;

            normalizedParticipantId = "P" + participantNumber.ToString("000", CultureInfo.InvariantCulture);
            return true;
        }

        private static int PositiveModulo(int value, int modulo)
        {
            if (modulo <= 0)
                return 0;

            int result = value % modulo;
            return result < 0 ? result + modulo : result;
        }

        private static string FormatConditionSequence(MusicEventController.MusicBlockCondition[] conditions)
        {
            if (conditions == null || conditions.Length == 0)
                return "none";

            string result = conditions[0].ToString();
            for (int i = 1; i < conditions.Length; i++)
                result += ">" + conditions[i];

            return result;
        }

        private int GetActiveExperimentalBlockCount()
        {
            if (sessionPlan != null)
                return Mathf.Max(0, sessionPlan.BlockCount);

            return Mathf.Max(1, numberOfBlocks);
        }

        private void LogSessionPlanSummary(string context)
        {
            if (sessionPlan == null)
                return;

            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[ExperimentSessionController] Protocol {0}: profile={1}, familiarization={2} ({3:F1}s), baseline={4} ({5:F1}s), blockOrderMode={6}, seed={7}, counterbalanceIndex={8}, blocks={9}.",
                context,
                sessionPlan.profileId,
                sessionPlan.includeFamiliarization,
                sessionPlan.familiarizationSeconds,
                sessionPlan.includeBaseline,
                sessionPlan.baselineSeconds,
                sessionPlan.blockOrderMode,
                sessionPlan.blockOrderSeed,
                sessionPlan.counterbalanceIndex,
                sessionPlan.blockSequence));
        }

        private static int StableStringHash(string text)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < text.Length; i++)
                    hash = hash * 31 + text[i];
                return hash;
            }
        }

        private MusicEventController.MusicBlockCondition GetBlockCondition(int blockIndex)
        {
            if (randomizedBlockConditions == null || randomizedBlockConditions.Length == 0)
            {
                if (sessionPlan == null)
                    sessionPlan = BuildSessionPlan(participantId);
                randomizedBlockConditions = sessionPlan.blockConditions;
            }

            if (randomizedBlockConditions == null || randomizedBlockConditions.Length == 0)
                return MusicEventController.MusicBlockCondition.ControlStable;

            return randomizedBlockConditions[Mathf.Clamp(blockIndex, 0, randomizedBlockConditions.Length - 1)];
        }

        private void WarnIfRouteIsTooShort()
        {
            if (leader == null)
                return;

            float pathLength = leader.GetTotalPathLength();
            if (pathLength <= 0f)
                return;

            float plannedBlockDuration = sessionPlan != null
                ? sessionPlan.fallbackExperimentalBlockSeconds
                : experimentalBlockSeconds;
            float minutesAtCruise = pathLength / Mathf.Max(0.1f, leader.cruiseSpeedKmh / 3.6f) / 60f;
            if (minutesAtCruise < plannedBlockDuration / 60f)
            {
                Debug.LogWarning(string.Format(
                    CultureInfo.InvariantCulture,
                    "[ExperimentSessionController] Current open route is about {0:F1} min at {1:F0} km/h. It is enough for functional tests, but too short for a {2:F1} min experimental block without a route redesign/reset strategy.",
                    minutesAtCruise,
                    leader.cruiseSpeedKmh,
                    plannedBlockDuration / 60f));
            }
        }

        private void OnGUI()
        {
            EnsureGuiStyles();

            if (showHud)
            {
                GUILayout.BeginArea(new Rect(16, 16, 500, 310), boxStyle);
                GUILayout.Label("Car-following experiment", labelStyle);
                GUILayout.Label("Phase: " + CurrentPhase, labelStyle);
                GUILayout.Label("State: " + GetSessionStateLabel(), labelStyle);
                GUILayout.Label("Throttle: " + (IsThrottleGateActive() ? "disabled" : "enabled"), labelStyle);
                if (blockEndingCoasting)
                    GUILayout.Label("Continue: waiting for experimenter Enter", labelStyle);
                GUILayout.Label("Test: F8 skip UXF/arm, F9 skip phase", labelStyle);
                if (currentPhaseDuration > 0f)
                {
                    GUILayout.Label(
                        "Time: " + FormatSeconds(GetCurrentPhaseElapsedSeconds()) + " / " + FormatSeconds(currentPhaseDuration),
                        labelStyle);
                    GUILayout.Label("Remaining: " + FormatSeconds(GetCurrentPhaseRemainingSeconds()), labelStyle);
                }
                else
                {
                    GUILayout.Label("Time: waiting", labelStyle);
                    GUILayout.Label("Remaining: --:--", labelStyle);
                }

                if (CurrentBlockIndex >= 0)
                {
                    string stimulus = music != null && !string.IsNullOrEmpty(music.CurrentStimulusId)
                        ? music.CurrentStimulusId
                        : CurrentBlockCondition.ToString();
                    GUILayout.Label("Block: " + (CurrentBlockIndex + 1) + " / " + GetActiveExperimentalBlockCount(), labelStyle);
                    GUILayout.Label("Condition: " + CurrentBlockCondition, labelStyle);
                    GUILayout.Label("Stimulus: " + stimulus, labelStyle);
                }
                GUILayout.Label("Music: " + GetMusicStatusLabel(), labelStyle);
                if (CurrentPhase == SessionPhase.ExperimentalBlock)
                {
                    string clipName = music != null && !string.IsNullOrEmpty(music.CurrentClipDisplayName)
                        ? music.CurrentClipDisplayName
                        : "none";
                    float clipLength = music != null ? music.CurrentClipLengthSeconds : -1f;
                    GUILayout.Label("Clip: " + clipName, labelStyle);
                    GUILayout.Label("Clip length: " + FormatSeconds(clipLength), labelStyle);
                    GUILayout.Label("Block duration: " + FormatSeconds(currentPhaseDuration), labelStyle);
                    GUILayout.Label("Configured duration: " + FormatSeconds(experimentalBlockSeconds), labelStyle);
                    if (!string.IsNullOrEmpty(currentBlockClipWarning))
                        GUILayout.Label(currentBlockClipWarning, labelStyle);
                }
                if (CurrentEvent != null)
                    GUILayout.Label("Event: " + CurrentEvent.index + " " + CurrentEvent.ConditionLabel, labelStyle);
                if (leader != null)
                    GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "Leader: {0:F0} km/h", leader.CurrentSpeedKmh), labelStyle);
                GUILayout.EndArea();
            }

            if (!string.IsNullOrEmpty(centerMessage))
            {
                bool showCompletionDataStatus = CurrentPhase == SessionPhase.Completed;
                float messageWidth = showCompletionDataStatus ? Mathf.Min(760f, Screen.width - 40f) : 520f;
                float messageHeight = showCompletionDataStatus ? Mathf.Min(420f, Screen.height - 40f) : 140f;
                Rect messageRect = new Rect(
                    (Screen.width - messageWidth) * 0.5f,
                    (Screen.height - messageHeight) * 0.5f,
                    messageWidth,
                    messageHeight);
                GUILayout.BeginArea(messageRect, centerBoxStyle);
                GUILayout.Label(centerMessage, centerLabelStyle);
                GUILayout.EndArea();
            }
        }

        private void EnsureGuiStyles()
        {
            if (boxStyle != null)
                return;

            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.alignment = TextAnchor.UpperLeft;
            boxStyle.padding = new RectOffset(12, 12, 10, 10);
            boxStyle.normal.textColor = Color.white;

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 14;
            labelStyle.normal.textColor = Color.white;

            centerBoxStyle = new GUIStyle(GUI.skin.box);
            centerBoxStyle.alignment = TextAnchor.MiddleCenter;
            centerBoxStyle.padding = new RectOffset(18, 18, 18, 18);
            centerBoxStyle.normal.textColor = Color.white;

            centerLabelStyle = new GUIStyle(GUI.skin.label);
            centerLabelStyle.fontSize = 20;
            centerLabelStyle.alignment = TextAnchor.MiddleCenter;
            centerLabelStyle.wordWrap = true;
            centerLabelStyle.normal.textColor = Color.white;
        }

        private static void EndUxfSessionsIfActive()
        {
            Session[] sessions = FindObjectsByType<Session>(FindObjectsInactive.Include);
            for (int i = 0; i < sessions.Length; i++)
            {
                Session session = sessions[i];
                if (session == null || !session.hasInitialised)
                    continue;

                try { session.End(); }
                catch (System.Exception exception) { Debug.LogWarning("[ExperimentSessionController] UXF session end failed: " + exception.Message); }
            }
        }
    }
}
