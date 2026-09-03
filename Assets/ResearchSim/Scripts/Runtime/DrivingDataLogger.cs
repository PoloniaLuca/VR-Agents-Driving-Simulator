using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UXF;

namespace ResearchSim
{
    /// <summary>
    /// Fixed-rate CSV logger for car-following data. Null references produce
    /// NaN fields and warnings instead of stopping the simulation.
    /// </summary>
    public class DrivingDataLogger : MonoBehaviour
    {
        private const string Header =
            "timestamp,elapsed,participant_id,phase," +
            "block_order_index,block_condition,block_stimulus_id,music_playback_time_s," +
            "stimulus_time_s,stimulus_clock_source,audio_dsp_start_time_s,audio_dsp_time_s,audio_sim_drift_s,audio_clip_load_state,audio_preload_complete," +
            "protocol_version,block_duration_s,audio_file_name,audio_control_type,has_musical_tempo,has_tempo_change,tempo_plan," +
            "tempo_segment_index,current_bpm,previous_bpm,next_bpm,tempo_event_index,tempo_event_label,tempo_event_time_s,time_to_tempo_event_s," +
            "tempo_change_marker,transition_type,event_marker,marker_type,event_is_pseudo_control,analysis_window,t1_time_s,t2_time_s,time_to_t1_s,time_to_t2_s," +
            "legacy_tempo_change_time_s,legacy_time_to_tempo_change_s,legacy_tempo_change_marker,legacy_tempo_phase,legacy_critical_window,legacy_pre_bpm,legacy_post_bpm,legacy_transition_type," +
            "participant_x,participant_y,participant_z,participant_heading_deg," +
            "participant_speed_mps,participant_speed_kmh,participant_accel_mps2," +
            "steering_input,throttle_input,brake_input,lateral_offset_m," +
            "leader_x,leader_y,leader_z,leader_speed_mps,leader_speed_kmh,leader_braking," +
            "distance_to_leader_m,time_headway_s,time_to_collision_s," +
            "event_index,event_type,event_has_decel,music_event_time,leader_decel_time," +
            "leader_event_index,leader_event_planned_start_s,leader_event_actual_start_s,leader_event_phase," +
            "leader_event_decel_start_s,leader_event_decel_end_s,leader_event_hold_start_s,leader_event_hold_end_s," +
            "leader_event_recovery_start_s,leader_event_recovery_end_s,leader_event_valid,leader_event_invalid_reason," +
            "leader_speed_profile,leader_speed_event_index,leader_speed_event_label,leader_speed_event_time_s,leader_speed_event_phase,leader_target_speed_kmh,leader_ramp_duration_s," +
            "protocol_state,effective_block_duration_s,music_clip_length_s,throttle_gate_active,debug_skip_requested,phase_elapsed_s,phase_remaining_s," +
            "participant_id_source,uxf_session_number,uxf_session_id," +
            "protocol_profile,block_order_mode,block_order_seed,counterbalance_index,session_block_sequence," +
            "feedback_enabled,feedback_state,feedback_target_distance_m,feedback_distance_error_m,feedback_closing_speed_mps," +
            "feedback_too_close,feedback_too_far,feedback_closing_too_fast";

        [Header("Participant")]
        public Transform participantVehicle;
        public Rigidbody participantRigidbody;
        public HybridVehicleInput hybridInput;
        public MonoBehaviour vppStandardInput;

        [Header("Leader")]
        public LeadVehicleController leadVehicle;

        [Header("Feedback")]
        public CarFollowingFeedbackController feedbackController;

        [Header("Experiment")]
        public CenterlinePath centerline;
        public string outputSubfolder = "CarFollowing";
        [Min(0.1f)] public float flushIntervalSeconds = 1f;

        [NonSerialized] public string participantId = "unknown";
        [NonSerialized] public string phase = "Idle";
        [NonSerialized] public int eventIndex = -1;
        [NonSerialized] public string eventType = "";
        [NonSerialized] public bool eventHasDeceleration;
        [NonSerialized] public float musicEventTime = -1f;
        [NonSerialized] public float leaderDecelerationTime = -1f;
        [NonSerialized] public int leaderEventIndex = -1;
        [NonSerialized] public float leaderEventPlannedStartSeconds = -1f;
        [NonSerialized] public float leaderEventActualStartSeconds = -1f;
        [NonSerialized] public string leaderEventPhase = "none";
        [NonSerialized] public float leaderEventDecelStartSeconds = -1f;
        [NonSerialized] public float leaderEventDecelEndSeconds = -1f;
        [NonSerialized] public float leaderEventHoldStartSeconds = -1f;
        [NonSerialized] public float leaderEventHoldEndSeconds = -1f;
        [NonSerialized] public float leaderEventRecoveryStartSeconds = -1f;
        [NonSerialized] public float leaderEventRecoveryEndSeconds = -1f;
        [NonSerialized] public int leaderEventValid = -1;
        [NonSerialized] public string leaderEventInvalidReason = "no_event";
        [NonSerialized] public int blockOrderIndex = -1;
        [NonSerialized] public string blockCondition = "";
        [NonSerialized] public string blockStimulusId = "";
        [NonSerialized] public string protocolState = "";
        [NonSerialized] public float effectiveBlockDurationSeconds = -1f;
        [NonSerialized] public float musicClipLengthSeconds = -1f;
        [NonSerialized] public bool throttleGateActive;
        [NonSerialized] public bool debugSkipRequested;
        [NonSerialized] public float phaseElapsedSeconds = -1f;
        [NonSerialized] public float phaseRemainingSeconds = -1f;
        [NonSerialized] public string participantIdSource = "";
        [NonSerialized] public int uxfSessionNumber = -1;
        [NonSerialized] public string uxfSessionId = "";
        [NonSerialized] public string protocolProfile = "";
        [NonSerialized] public string protocolVersion = "V1";
        [NonSerialized] public string blockOrderMode = "";
        [NonSerialized] public int blockOrderSeed;
        [NonSerialized] public int counterbalanceIndex = -1;
        [NonSerialized] public string sessionBlockSequence = "";
        [NonSerialized] public float tempoChangeTimeSeconds = -1f;
        [NonSerialized] public float timeToTempoChangeSeconds = float.NaN;
        [NonSerialized] public int tempoChangeMarker;
        [NonSerialized] public string tempoPhase = "none";
        [NonSerialized] public string criticalWindow = "none";
        [NonSerialized] public float preBpm = -1f;
        [NonSerialized] public float postBpm = -1f;
        [NonSerialized] public string transitionType = "none";

        public bool IsLogging { get { return loggingActive && writer != null; } }
        public bool IsPrepared { get { return writer != null; } }
        public string CurrentFilePath { get; private set; }

        [Serializable]
        public sealed class PrimaryCsvStatusSnapshot
        {
            public string primaryCsvPath;
            public bool primaryCsvExists;
            public long primaryCsvBytes;
            public int dataRowsWritten;
            public bool writerIsOpen;
            public bool loggingActive;
            public string lastFlushAttemptUtc;
            public bool lastFlushSuccess;
            public string lastFlushError;
            public bool finalFlushAttempted;
            public bool finalFlushSuccess;
            public bool writerDisposeAttempted;
            public bool writerDisposeSuccess;
            public bool finalPrimaryCsvVerified;
            public string finalStatusMessage;
        }

        private static readonly ProfilerMarker CsvPrepareMarker = new ProfilerMarker("ResearchSim.Startup.CsvPrepareOpenHeader");
        private static readonly ProfilerMarker FirstCsvRowMarker = new ProfilerMarker("ResearchSim.Startup.FirstCsvRow");
        private StreamWriter writer;
        private readonly StringBuilder line = new StringBuilder(512);
        private bool loggingActive;
        private bool firstRowPending;
        private string preparedParticipantId = "";
        private int dataRowsWritten;
        private float startTime;
        private Vector3 previousVelocity;
        private float participantAcceleration;
        private double nextFlushTime;
        private bool warnedMissingParticipant;
        private bool warnedMissingLeader;
        private MusicEventController music;
        private bool currentBlockHasTempoChange;
        private bool tempoChangeMarkerEmitted;
        private float previousMusicPlaybackTime = -1f;
        private readonly bool[] v2MarkerEmitted = new bool[V2ProtocolDefinition.Markers.Length];
        private float previousV2StimulusTime = -1f;
        private bool copiedCompletedCsvToUxf;
        private string cachedUxfSessionFolderPath;
        private bool attemptedUxfSessionFolderCache;
        private bool loggedUxfCopyUnavailable;
        private bool primaryCsvExists;
        private long primaryCsvBytes = -1L;
        private DateTime lastFlushAttemptUtc;
        private bool lastFlushSuccess;
        private string lastFlushError = "";
        private bool finalFlushAttempted;
        private bool finalFlushSuccess;
        private bool writerDisposeAttempted;
        private bool writerDisposeSuccess;
        private bool finalPrimaryCsvVerified;
        private string finalStatusMessage = "Primary CSV not finalized.";
        private double nextPrimaryCsvStatusRefreshTime;

        private void Awake()
        {
            ResolveReferences();
        }

        private void FixedUpdate()
        {
            if (!loggingActive || writer == null)
                return;

            UpdateAcceleration();
            if (firstRowPending)
            {
                using (FirstCsvRowMarker.Auto())
                    WriteRow();
                firstRowPending = false;
            }
            else
            {
                WriteRow();
            }

            if (Time.realtimeSinceStartupAsDouble >= nextFlushTime)
            {
                FlushWriter(false);
                nextFlushTime = Time.realtimeSinceStartupAsDouble + Mathf.Max(0.1f, flushIntervalSeconds);
            }
        }

        private void OnDestroy()
        {
            StopLogging();
        }

        private void OnApplicationQuit()
        {
            StopLogging();
        }

        public bool PrepareLogging(string participantIdentifier)
        {
            ResolveReferences();
            string safeParticipantId = string.IsNullOrWhiteSpace(participantIdentifier)
                ? ExperimentSession.GetFileSafeParticipantID()
                : MakeFileSafe(participantIdentifier);
            string replacedStalePath = "";

            if (writer != null &&
                !loggingActive &&
                string.Equals(preparedParticipantId, safeParticipantId, StringComparison.Ordinal))
            {
                return true;
            }

            if (writer != null)
            {
                if (loggingActive || dataRowsWritten > 0)
                {
                    Debug.LogError("[DrivingDataLogger] Refusing to replace a CSV after data logging has started: " + CurrentFilePath);
                    return false;
                }

                replacedStalePath = CurrentFilePath;
                CloseWriter(true);
            }

            try
            {
                using (CsvPrepareMarker.Auto())
                {
                    participantId = safeParticipantId;
                    string folder = Path.Combine(ResearchDataPaths.ProjectRoot, ResearchDataPaths.DataRootFolderName, outputSubfolder);
                    Directory.CreateDirectory(folder);
                    CurrentFilePath = Path.Combine(
                        folder,
                        participantId + "_car_following_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".csv");

                    ResetPrimaryCsvStatus();
                    writer = new StreamWriter(CurrentFilePath, false, new UTF8Encoding(false));
                    Debug.Log("[DrivingDataLogger] CSV primary path created: " + Path.GetFullPath(CurrentFilePath));
                    writer.WriteLine(Header);
                    if (!FlushWriter(false, false))
                        throw new IOException("CSV header flush failed: " + lastFlushError);
                    RefreshPrimaryCsvFileStatus(true);
                    Debug.Log("[DrivingDataLogger] CSV header written and flushed.");
                }

                preparedParticipantId = participantId;
                loggingActive = false;
                dataRowsWritten = 0;
                copiedCompletedCsvToUxf = false;
                cachedUxfSessionFolderPath = "";
                attemptedUxfSessionFolderCache = false;
                loggedUxfCopyUnavailable = false;
                if (!string.IsNullOrWhiteSpace(replacedStalePath))
                {
                    Debug.Log(
                        "[ResearchSim] Replacing empty stale CSV prepared before UXF metadata finalized: " +
                        replacedStalePath + " -> " + CurrentFilePath + ".");
                }
                Debug.Log("[DrivingDataLogger] CSV prepared before participant start: " + CurrentFilePath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DrivingDataLogger] Could not prepare logging: " + e.Message);
                if (writer != null)
                {
                    writerDisposeAttempted = true;
                    try
                    {
                        writer.Dispose();
                        writerDisposeSuccess = true;
                    }
                    catch (Exception disposeException)
                    {
                        writerDisposeSuccess = false;
                        lastFlushError = ShortError(disposeException);
                        Debug.LogError("[DrivingDataLogger] CSV writer dispose failed after preparation error: " + lastFlushError);
                    }
                }
                writer = null;
                preparedParticipantId = "";
                RefreshPrimaryCsvFileStatus(true);
                return false;
            }
        }

        public void StartLogging(string participantIdentifier)
        {
            string safeParticipantId = string.IsNullOrWhiteSpace(participantIdentifier)
                ? ExperimentSession.GetFileSafeParticipantID()
                : MakeFileSafe(participantIdentifier);

            if (writer == null || !string.Equals(preparedParticipantId, safeParticipantId, StringComparison.Ordinal))
            {
                if (!PrepareLogging(safeParticipantId))
                    return;
            }

            if (loggingActive)
                return;

            participantId = safeParticipantId;
            loggingActive = true;
            firstRowPending = true;
            startTime = Time.time;
            previousVelocity = GetParticipantVelocity();
            nextFlushTime = Time.realtimeSinceStartupAsDouble + Mathf.Max(0.1f, flushIntervalSeconds);
            if (ShouldCopyCompletedCsvToUxfSessionFolder())
                CacheUxfSessionFolderForCompletedCsvCopy();
            Debug.Log("[DrivingDataLogger] CSV logging started: " + CurrentFilePath);
        }

        public void StopLogging()
        {
            if (writer == null)
                return;

            string completedFilePath = CurrentFilePath;
            bool completedLogging = loggingActive;
            CloseWriter(!completedLogging);
            if (!completedLogging)
                return;

            Debug.Log("[DrivingDataLogger] Logging stopped: " + completedFilePath);
            CopyCompletedCsvToUxfSessionFolder(completedFilePath);
        }

        private void CloseWriter(bool deletePreparedFile)
        {
            string path = CurrentFilePath;
            bool finalizingLoggedCsv = !deletePreparedFile;

            if (finalizingLoggedCsv)
            {
                finalFlushAttempted = true;
                finalFlushSuccess = FlushWriter(true);
            }
            else
            {
                FlushWriter(false, false);
            }

            writerDisposeAttempted = true;
            try
            {
                writer.Dispose();
                writerDisposeSuccess = true;
                if (finalizingLoggedCsv)
                    Debug.Log("[DrivingDataLogger] CSV writer disposed succeeded.");
            }
            catch (Exception exception)
            {
                writerDisposeSuccess = false;
                lastFlushError = ShortError(exception);
                Debug.LogError("[DrivingDataLogger] CSV writer dispose failed: " + lastFlushError);
            }

            writer = null;
            loggingActive = false;
            firstRowPending = false;
            preparedParticipantId = "";
            RefreshPrimaryCsvFileStatus(true);

            if (finalizingLoggedCsv)
                VerifyFinalPrimaryCsv();

            if (deletePreparedFile && !string.IsNullOrWhiteSpace(path))
            {
                if (dataRowsWritten > 0)
                {
                    Debug.LogError("[DrivingDataLogger] Refusing to delete a CSV that contains data rows: " + path);
                    return;
                }

                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[DrivingDataLogger] Could not remove unused prepared CSV: " + e.Message);
                }
            }
        }

        public PrimaryCsvStatusSnapshot GetPrimaryCsvStatus(bool refreshFileSize = false)
        {
            if (refreshFileSize)
                RefreshPrimaryCsvFileStatus(false);

            return new PrimaryCsvStatusSnapshot
            {
                primaryCsvPath = CurrentFilePath ?? "",
                primaryCsvExists = primaryCsvExists,
                primaryCsvBytes = primaryCsvBytes,
                dataRowsWritten = dataRowsWritten,
                writerIsOpen = writer != null,
                loggingActive = loggingActive,
                lastFlushAttemptUtc = lastFlushAttemptUtc == default(DateTime)
                    ? ""
                    : lastFlushAttemptUtc.ToString("o", CultureInfo.InvariantCulture),
                lastFlushSuccess = lastFlushSuccess,
                lastFlushError = lastFlushError ?? "",
                finalFlushAttempted = finalFlushAttempted,
                finalFlushSuccess = finalFlushSuccess,
                writerDisposeAttempted = writerDisposeAttempted,
                writerDisposeSuccess = writerDisposeSuccess,
                finalPrimaryCsvVerified = finalPrimaryCsvVerified,
                finalStatusMessage = finalStatusMessage ?? ""
            };
        }

        private void ResetPrimaryCsvStatus()
        {
            primaryCsvExists = false;
            primaryCsvBytes = -1L;
            lastFlushAttemptUtc = default(DateTime);
            lastFlushSuccess = false;
            lastFlushError = "";
            finalFlushAttempted = false;
            finalFlushSuccess = false;
            writerDisposeAttempted = false;
            writerDisposeSuccess = false;
            finalPrimaryCsvVerified = false;
            finalStatusMessage = "Primary CSV not finalized.";
            nextPrimaryCsvStatusRefreshTime = 0d;
        }

        private bool FlushWriter(bool finalFlush, bool periodicFlush = true)
        {
            lastFlushAttemptUtc = DateTime.UtcNow;
            lastFlushSuccess = false;
            lastFlushError = "";

            if (writer == null)
            {
                lastFlushError = "Writer is not open.";
                if (finalFlush)
                    Debug.LogError("[DrivingDataLogger] CSV final flush failed: " + lastFlushError);
                else if (periodicFlush)
                    Debug.LogWarning("[DrivingDataLogger] CSV periodic flush failed: " + lastFlushError);
                else
                    Debug.LogError("[DrivingDataLogger] CSV flush failed: " + lastFlushError);
                return false;
            }

            try
            {
                writer.Flush();
                lastFlushSuccess = true;
                RefreshPrimaryCsvFileStatus(true);
                if (finalFlush)
                    Debug.Log("[DrivingDataLogger] CSV final flush succeeded.");
                return true;
            }
            catch (Exception exception)
            {
                lastFlushError = ShortError(exception);
                if (finalFlush)
                    Debug.LogError("[DrivingDataLogger] CSV final flush failed: " + lastFlushError);
                else if (periodicFlush)
                    Debug.LogWarning("[DrivingDataLogger] CSV periodic flush failed: " + lastFlushError);
                else
                    Debug.LogError("[DrivingDataLogger] CSV flush failed: " + lastFlushError);
                return false;
            }
        }

        private void RefreshPrimaryCsvFileStatus(bool force)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (!force && now < nextPrimaryCsvStatusRefreshTime)
                return;

            nextPrimaryCsvStatusRefreshTime = now + 1d;
            primaryCsvExists = false;
            primaryCsvBytes = -1L;
            if (string.IsNullOrWhiteSpace(CurrentFilePath))
                return;

            try
            {
                FileInfo file = new FileInfo(CurrentFilePath);
                primaryCsvExists = file.Exists;
                primaryCsvBytes = file.Exists ? file.Length : -1L;
            }
            catch (Exception exception)
            {
                lastFlushError = "File status: " + ShortError(exception);
            }
        }

        private void VerifyFinalPrimaryCsv()
        {
            RefreshPrimaryCsvFileStatus(true);
            finalPrimaryCsvVerified =
                !string.IsNullOrWhiteSpace(CurrentFilePath) &&
                primaryCsvExists &&
                primaryCsvBytes > 0L &&
                dataRowsWritten > 0 &&
                finalFlushSuccess &&
                writerDisposeSuccess;
            finalStatusMessage = finalPrimaryCsvVerified
                ? "Primary CSV saved."
                : "Primary CSV was not verified.";

            string logMessage = string.Format(
                CultureInfo.InvariantCulture,
                "[DrivingDataLogger] CSV primary verification: rows={0}, bytes={1}, ok={2}.",
                dataRowsWritten,
                primaryCsvBytes,
                finalPrimaryCsvVerified);
            if (finalPrimaryCsvVerified)
                Debug.Log(logMessage);
            else
                Debug.LogError(logMessage + " Path: " + (CurrentFilePath ?? "<unknown>"));
        }

        private static string ShortError(Exception exception)
        {
            if (exception == null || string.IsNullOrWhiteSpace(exception.Message))
                return "Unknown error.";

            string message = exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return message.Length <= 240 ? message : message.Substring(0, 240);
        }

        public void SetEvent(TrialScheduler.TrialEvent evt)
        {
            if (evt == null)
            {
                eventIndex = -1;
                eventType = "";
                eventHasDeceleration = false;
                musicEventTime = -1f;
                leaderDecelerationTime = -1f;
                return;
            }

            eventIndex = evt.index;
            eventType = evt.musicType.ToString();
            eventHasDeceleration = evt.hasLeaderDeceleration;
            musicEventTime = evt.musicEventTime;
            leaderDecelerationTime = evt.leadDecelerationTime;
        }

        public void SetLeaderEvent(
            int index,
            float plannedStartSeconds,
            float actualStartSeconds,
            string phase,
            float decelStartSeconds,
            float decelEndSeconds,
            float holdStartSeconds,
            float holdEndSeconds,
            float recoveryStartSeconds,
            float recoveryEndSeconds,
            int valid,
            string invalidReason)
        {
            leaderEventIndex = index;
            leaderEventPlannedStartSeconds = plannedStartSeconds;
            leaderEventActualStartSeconds = actualStartSeconds;
            leaderEventPhase = string.IsNullOrWhiteSpace(phase) ? "none" : phase;
            leaderEventDecelStartSeconds = decelStartSeconds;
            leaderEventDecelEndSeconds = decelEndSeconds;
            leaderEventHoldStartSeconds = holdStartSeconds;
            leaderEventHoldEndSeconds = holdEndSeconds;
            leaderEventRecoveryStartSeconds = recoveryStartSeconds;
            leaderEventRecoveryEndSeconds = recoveryEndSeconds;
            leaderEventValid = valid;
            leaderEventInvalidReason = string.IsNullOrWhiteSpace(invalidReason) ? "none" : invalidReason;
        }

        public void ClearLeaderEvent()
        {
            SetLeaderEvent(-1, -1f, -1f, "none", -1f, -1f, -1f, -1f, -1f, -1f, -1, "no_event");
        }

        public void SetBlock(int orderIndex, string condition, string stimulusId, MusicEventController musicController)
        {
            blockOrderIndex = orderIndex;
            blockCondition = condition ?? string.Empty;
            blockStimulusId = stimulusId ?? string.Empty;
            music = musicController;
            ConfigureTempoChangeFromCurrentBlock();
            ResetV2MarkerState();
        }

        public void ClearBlock()
        {
            blockOrderIndex = -1;
            blockCondition = "";
            blockStimulusId = "";
            music = null;
            ClearTempoChangeState();
            ResetV2MarkerState();
            ClearLeaderEvent();
        }

        private void WriteRow()
        {
            try
            {
                line.Clear();
                AppendFloat(Time.time);
                AppendFloat(Time.time - startTime);
                AppendString(participantId);
                AppendString(phase);
                AppendInt(blockOrderIndex);
                AppendString(blockCondition);
                AppendString(blockStimulusId);
                float musicPlaybackTime = music != null ? music.CurrentPlaybackTime : -1f;
                UpdateTempoChangeFields(musicPlaybackTime);
                AppendFloat(musicPlaybackTime);
                float stimulusTime = GetOfficialStimulusTime(out string stimulusClockSource);
                WriteStimulusTimingFields(stimulusTime, stimulusClockSource);
                WriteV2ProtocolFields(stimulusTime);
                AppendFloat(tempoChangeTimeSeconds);
                AppendFloat(timeToTempoChangeSeconds);
                AppendInt(tempoChangeMarker);
                AppendString(tempoPhase);
                AppendString(criticalWindow);
                AppendFloat(preBpm);
                AppendFloat(postBpm);
                AppendString(transitionType);

                Vector3 participantPosition = participantVehicle != null ? participantVehicle.position : Vector3.zero;
                if (participantVehicle == null && !warnedMissingParticipant)
                {
                    warnedMissingParticipant = true;
                    Debug.LogWarning("[DrivingDataLogger] Participant vehicle reference missing; writing NaN/zero fields.");
                }

                AppendFloat(participantVehicle != null ? participantPosition.x : float.NaN);
                AppendFloat(participantVehicle != null ? participantPosition.y : float.NaN);
                AppendFloat(participantVehicle != null ? participantPosition.z : float.NaN);
                AppendFloat(participantVehicle != null ? participantVehicle.eulerAngles.y : float.NaN);

                float speedMps = GetParticipantVelocity().magnitude;
                AppendFloat(speedMps);
                AppendFloat(speedMps * 3.6f);
                AppendFloat(participantAcceleration);

                ReadInputs(out float steering, out float throttle, out float brake);
                AppendFloat(steering);
                AppendFloat(throttle);
                AppendFloat(brake);

                float lateralOffset = centerline != null && participantVehicle != null
                    ? centerline.GetSignedDistanceFromCenterLine(participantPosition)
                    : float.NaN;
                AppendFloat(lateralOffset);

                WriteLeaderFields(participantPosition, speedMps);

                AppendInt(eventIndex);
                AppendString(eventType);
                AppendInt(eventHasDeceleration ? 1 : 0);
                AppendFloat(musicEventTime);
                AppendFloat(leaderDecelerationTime);
                AppendInt(leaderEventIndex);
                AppendFloat(leaderEventPlannedStartSeconds);
                AppendFloat(leaderEventActualStartSeconds);
                AppendString(leaderEventPhase);
                AppendFloat(leaderEventDecelStartSeconds);
                AppendFloat(leaderEventDecelEndSeconds);
                AppendFloat(leaderEventHoldStartSeconds);
                AppendFloat(leaderEventHoldEndSeconds);
                AppendFloat(leaderEventRecoveryStartSeconds);
                AppendFloat(leaderEventRecoveryEndSeconds);
                AppendInt(leaderEventValid);
                AppendString(leaderEventInvalidReason);
                WriteV2LeaderEventFields(stimulusTime);
                AppendString(protocolState);
                AppendFloat(effectiveBlockDurationSeconds);
                AppendFloat(musicClipLengthSeconds);
                AppendInt(throttleGateActive ? 1 : 0);
                AppendInt(debugSkipRequested ? 1 : 0);
                AppendFloat(phaseElapsedSeconds);
                AppendFloat(phaseRemainingSeconds);
                AppendString(participantIdSource);
                AppendInt(uxfSessionNumber);
                AppendString(uxfSessionId);
                AppendString(protocolProfile);
                AppendString(blockOrderMode);
                AppendInt(blockOrderSeed);
                AppendInt(counterbalanceIndex);
                AppendString(sessionBlockSequence);
                WriteFeedbackFields();

                writer.WriteLine(line.ToString());
                dataRowsWritten++;
                tempoChangeMarker = 0;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DrivingDataLogger] Write failed: " + e.Message);
            }
        }

        private void ConfigureTempoChangeFromCurrentBlock()
        {
            currentBlockHasTempoChange = music != null && music.CurrentBlockHasTempoChange;
            tempoChangeMarkerEmitted = false;
            previousMusicPlaybackTime = -1f;

            if (!currentBlockHasTempoChange)
            {
                ClearTempoChangeValues();
                return;
            }

            tempoChangeTimeSeconds = music.CurrentTempoChangeTimeSeconds;
            if (tempoChangeTimeSeconds <= 0f ||
                (music.HasCurrentClip && music.CurrentClipLengthSeconds > 0f && tempoChangeTimeSeconds >= music.CurrentClipLengthSeconds))
            {
                currentBlockHasTempoChange = false;
                ClearTempoChangeValues();
                return;
            }

            timeToTempoChangeSeconds = float.NaN;
            tempoChangeMarker = 0;
            tempoPhase = "pre";
            criticalWindow = "outside";
            preBpm = music.CurrentPreBpm;
            postBpm = music.CurrentPostBpm;
            transitionType = string.IsNullOrWhiteSpace(music.CurrentTransitionType) ? "unknown" : music.CurrentTransitionType;
        }

        private void ClearTempoChangeState()
        {
            currentBlockHasTempoChange = false;
            tempoChangeMarkerEmitted = false;
            previousMusicPlaybackTime = -1f;
            ClearTempoChangeValues();
        }

        private void ClearTempoChangeValues()
        {
            tempoChangeTimeSeconds = -1f;
            timeToTempoChangeSeconds = float.NaN;
            tempoChangeMarker = 0;
            tempoPhase = "none";
            criticalWindow = "none";
            preBpm = -1f;
            postBpm = -1f;
            transitionType = "none";
        }

        private void UpdateTempoChangeFields(float playbackTime)
        {
            tempoChangeMarker = 0;

            if (!currentBlockHasTempoChange || tempoChangeTimeSeconds < 0f)
            {
                if (!currentBlockHasTempoChange)
                    ClearTempoChangeValues();
                return;
            }

            if (playbackTime < 0f)
            {
                timeToTempoChangeSeconds = float.NaN;
                tempoPhase = "none";
                criticalWindow = "none";
                return;
            }

            timeToTempoChangeSeconds = playbackTime - tempoChangeTimeSeconds;
            tempoPhase = playbackTime < tempoChangeTimeSeconds ? "pre" : "post";

            float preWindowStart = tempoChangeTimeSeconds - 15f;
            float postWindowEnd = tempoChangeTimeSeconds + 15f;
            if (playbackTime >= preWindowStart && playbackTime < tempoChangeTimeSeconds)
                criticalWindow = "pre15";
            else if (playbackTime >= tempoChangeTimeSeconds && playbackTime < postWindowEnd)
                criticalWindow = "post15";
            else
                criticalWindow = "outside";

            if (!tempoChangeMarkerEmitted &&
                previousMusicPlaybackTime >= 0f &&
                previousMusicPlaybackTime < tempoChangeTimeSeconds &&
                playbackTime >= tempoChangeTimeSeconds)
            {
                tempoChangeMarker = 1;
                tempoChangeMarkerEmitted = true;
            }

            previousMusicPlaybackTime = playbackTime;
        }

        private void WriteLeaderFields(Vector3 participantPosition, float participantSpeedMps)
        {
            if (leadVehicle == null)
            {
                if (!warnedMissingLeader)
                {
                    warnedMissingLeader = true;
                    Debug.LogWarning("[DrivingDataLogger] Lead vehicle reference missing; writing NaN leader fields.");
                }

                for (int i = 0; i < 9; i++)
                    AppendFloat(float.NaN);
                return;
            }

            Vector3 leaderPosition = leadVehicle.transform.position;
            AppendFloat(leaderPosition.x);
            AppendFloat(leaderPosition.y);
            AppendFloat(leaderPosition.z);
            AppendFloat(leadVehicle.CurrentSpeedMps);
            AppendFloat(leadVehicle.CurrentSpeedKmh);
            AppendInt(leadVehicle.IsDecelerating ? 1 : 0);

            float distance = participantVehicle != null ? Vector3.Distance(participantPosition, leaderPosition) : float.NaN;
            AppendFloat(distance);

            float headway = participantSpeedMps > 0.5f ? distance / participantSpeedMps : float.NaN;
            AppendFloat(headway);

            float closingSpeed = participantSpeedMps - leadVehicle.CurrentSpeedMps;
            float ttc = closingSpeed > 0.1f ? distance / closingSpeed : float.NaN;
            AppendFloat(ttc);
        }

        private void ResetV2MarkerState()
        {
            for (int i = 0; i < v2MarkerEmitted.Length; i++)
                v2MarkerEmitted[i] = false;
            previousV2StimulusTime = -1f;
        }

        private void WriteStimulusTimingFields(float stimulusTime, string stimulusClockSource)
        {
            AppendFloat(stimulusTime);
            AppendString(stimulusClockSource);
            AppendDouble(music != null ? music.AudioDspStartTimeSeconds : -1d);
            AppendDouble(music != null ? music.CurrentAudioDspTimeSeconds : AudioSettings.dspTime);
            AppendFloat(stimulusTime >= 0f && phaseElapsedSeconds >= 0f
                ? stimulusTime - phaseElapsedSeconds
                : float.NaN);
            AppendString(music != null ? music.CurrentAudioClipLoadState : "unknown");
            AppendInt(music != null && music.PreparedBlockAudioPreloadComplete ? 1 : 0);
        }

        private void WriteV2ProtocolFields(float stimulusTime)
        {
            bool isV2 = V2ProtocolDefinition.IsV2(protocolVersion) && blockOrderIndex >= 0;

            AppendString(protocolVersion);
            AppendFloat(effectiveBlockDurationSeconds);
            AppendString(
                isV2 && TryGetCurrentBlockCondition(out MusicEventController.MusicBlockCondition audioCondition)
                    ? V2ProtocolDefinition.GetAudioFileName(audioCondition)
                    : music != null ? music.CurrentClipDisplayName : string.Empty);

            if (!isV2 || !TryGetCurrentBlockCondition(out MusicEventController.MusicBlockCondition condition))
            {
                AppendString("none");
                AppendInt(0);
                AppendInt(currentBlockHasTempoChange ? 1 : 0);
                AppendString("none");
                AppendInt(-1);
                AppendFloat(-1f);
                AppendFloat(-1f);
                AppendFloat(-1f);
                AppendInt(-1);
                AppendString("none");
                AppendFloat(-1f);
                AppendFloat(float.NaN);
                AppendInt(0);
                AppendString(transitionType);
                AppendString("none");
                AppendString("none");
                AppendInt(0);
                AppendString("none");
                AppendFloat(V2ProtocolDefinition.T1Seconds);
                AppendFloat(V2ProtocolDefinition.T2Seconds);
                AppendFloat(float.NaN);
                AppendFloat(float.NaN);
                return;
            }

            int segmentIndex = V2ProtocolDefinition.GetTempoSegmentIndex(stimulusTime);
            bool hasMusicalTempo = V2ProtocolDefinition.HasMusicalTempo(condition);
            string eventMarker = "none";
            string markerType = "none";
            int tempoEventIndex = -1;
            string tempoEventLabel = "none";
            float tempoEventTime = -1f;
            int v2TempoChangeMarker = 0;

            for (int i = 0; i < V2ProtocolDefinition.Markers.Length; i++)
            {
                V2ProtocolDefinition.Marker marker = V2ProtocolDefinition.Markers[i];
                float crossingThreshold = marker.label == "END"
                    ? marker.timeSeconds - Mathf.Max(0.03f, Time.fixedDeltaTime * 1.5f)
                    : marker.timeSeconds;
                if (v2MarkerEmitted[i] ||
                    previousV2StimulusTime < 0f ||
                    previousV2StimulusTime >= crossingThreshold ||
                    stimulusTime < crossingThreshold)
                    continue;

                v2MarkerEmitted[i] = true;
                eventMarker = marker.label;
                markerType = marker.type;
                if (marker.label == "T1" || marker.label == "T2")
                {
                    tempoEventIndex = marker.label == "T1" ? 1 : 2;
                    tempoEventLabel = marker.label;
                    tempoEventTime = marker.timeSeconds;
                    if (hasMusicalTempo)
                    {
                        v2TempoChangeMarker = 1;
                        markerType = "real_tempo_change";
                    }
                    else
                        markerType = "pseudo_control";
                }
                break;
            }

            AppendString(V2ProtocolDefinition.GetAudioControlType(condition));
            AppendInt(hasMusicalTempo ? 1 : 0);
            AppendInt(hasMusicalTempo ? 1 : 0);
            AppendString(V2ProtocolDefinition.GetTempoPlan(condition));
            AppendInt(segmentIndex);
            AppendFloat(V2ProtocolDefinition.GetCurrentBpm(condition, segmentIndex));
            AppendFloat(V2ProtocolDefinition.GetPreviousBpm(condition, segmentIndex));
            AppendFloat(V2ProtocolDefinition.GetNextBpm(condition, segmentIndex));
            AppendInt(tempoEventIndex);
            AppendString(tempoEventLabel);
            AppendFloat(tempoEventTime);
            AppendFloat(V2ProtocolDefinition.GetTimeToNearestTempoEvent(stimulusTime));
            AppendInt(v2TempoChangeMarker);
            AppendString(V2ProtocolDefinition.GetTransitionType(condition));
            AppendString(eventMarker);
            AppendString(markerType);
            AppendInt(!hasMusicalTempo && eventMarker != "none" ? 1 : 0);
            AppendString(V2ProtocolDefinition.GetAnalysisWindow(stimulusTime));
            AppendFloat(V2ProtocolDefinition.T1Seconds);
            AppendFloat(V2ProtocolDefinition.T2Seconds);
            AppendFloat(stimulusTime - V2ProtocolDefinition.T1Seconds);
            AppendFloat(stimulusTime - V2ProtocolDefinition.T2Seconds);
            previousV2StimulusTime = stimulusTime;
        }

        private void WriteV2LeaderEventFields(float stimulusTime)
        {
            bool isV2 = V2ProtocolDefinition.IsV2(protocolVersion) && blockOrderIndex >= 0;
            if (!isV2)
            {
                AppendString("none");
                AppendInt(-1);
                AppendString("none");
                AppendFloat(-1f);
                AppendString("none");
                AppendFloat(-1f);
                AppendFloat(-1f);
                return;
            }

            int speedEventIndex = V2ProtocolDefinition.GetLeaderEventIndex(stimulusTime);
            AppendString("70>80>70");
            AppendInt(speedEventIndex);
            AppendString(V2ProtocolDefinition.GetLeaderEventLabel(speedEventIndex));
            AppendFloat(V2ProtocolDefinition.GetLeaderEventTime(speedEventIndex));
            AppendString(leadVehicle != null ? leadVehicle.CurrentSpeedEventPhase : "none");
            AppendFloat(V2ProtocolDefinition.GetLeaderTargetSpeedKmh(stimulusTime));
            AppendFloat(V2ProtocolDefinition.LeaderRampDurationSeconds);
        }

        private float GetOfficialStimulusTime(out string clockSource)
        {
            bool isV2Block = V2ProtocolDefinition.IsV2(protocolVersion) && blockOrderIndex >= 0;
            if (isV2Block && music != null && music.CurrentStimulusTimeSeconds >= 0f)
            {
                clockSource = music.StimulusClockSource;
                return music.CurrentStimulusTimeSeconds;
            }

            if (isV2Block && phaseElapsedSeconds >= 0f)
            {
                clockSource = "phase_elapsed_fallback";
                return phaseElapsedSeconds;
            }

            clockSource = "unavailable";
            return -1f;
        }

        private bool TryGetCurrentBlockCondition(out MusicEventController.MusicBlockCondition condition)
        {
            return System.Enum.TryParse(blockCondition, true, out condition);
        }

        private void WriteFeedbackFields()
        {
            if (feedbackController == null)
            {
                AppendInt(0);
                AppendString(CarFollowingFeedbackController.FeedbackState.Off.ToString());
                AppendFloat(float.NaN);
                AppendFloat(float.NaN);
                AppendFloat(float.NaN);
                AppendInt(0);
                AppendInt(0);
                AppendInt(0, false);
                return;
            }

            AppendInt(feedbackController.IsFeedbackEnabled ? 1 : 0);
            AppendString(feedbackController.CurrentState.ToString());
            AppendFloat(feedbackController.TargetDistanceMeters);
            AppendFloat(feedbackController.DistanceErrorMeters);
            AppendFloat(feedbackController.ClosingSpeedMps);
            AppendInt(feedbackController.TooClose ? 1 : 0);
            AppendInt(feedbackController.TooFar ? 1 : 0);
            AppendInt(feedbackController.ClosingTooFast ? 1 : 0, false);
        }

        private void CopyCompletedCsvToUxfSessionFolder(string sourcePath)
        {
            if (copiedCompletedCsvToUxf)
                return;

            if (!ShouldCopyCompletedCsvToUxfSessionFolder())
            {
                LogUxfCopyUnavailable("[ResearchSim] UXF session metadata is not active for this car-following CSV; CSV remains in fallback folder only.");
                return;
            }

            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                Debug.LogWarning("[ResearchSim] Car-following CSV source file is unavailable; CSV was not copied to UXF.");
                return;
            }

            string sessionFolder = cachedUxfSessionFolderPath;
            Session session = null;

            if (!string.IsNullOrWhiteSpace(sessionFolder))
            {
                Debug.Log("[ResearchSim] Using cached UXF session folder for car-following CSV copy: " + Path.GetFullPath(sessionFolder));
            }
            else
            {
                Debug.Log("[ResearchSim] Cached UXF session folder unavailable; trying live UXF session discovery.");
                if (!TryGetActiveUxfSessionFolder(out sessionFolder, out session))
                {
                    LogUxfCopyUnavailable("[ResearchSim] UXF session folder unavailable; car-following CSV remains in fallback folder only.");
                    return;
                }
            }

            try
            {
                Directory.CreateDirectory(sessionFolder);
                string destinationPath = Path.Combine(sessionFolder, Path.GetFileName(sourcePath));
                File.Copy(sourcePath, destinationPath, true);
                copiedCompletedCsvToUxf = true;
                TryWriteUxfCopyManifest(sourcePath, destinationPath, session);
                Debug.Log("[ResearchSim] Copied car-following CSV to UXF session folder: " + Path.GetFullPath(destinationPath));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[ResearchSim] Failed to copy car-following CSV to UXF session folder: " + exception.Message);
            }
        }

        private void CacheUxfSessionFolderForCompletedCsvCopy()
        {
            if (attemptedUxfSessionFolderCache)
                return;

            attemptedUxfSessionFolderCache = true;
            cachedUxfSessionFolderPath = "";

            if (TryGetActiveUxfSessionFolder(out string sessionFolder, out Session _))
            {
                cachedUxfSessionFolderPath = sessionFolder;
                Debug.Log("[ResearchSim] Cached UXF session folder for car-following CSV copy: " + Path.GetFullPath(cachedUxfSessionFolderPath));
                return;
            }

            Debug.Log("[ResearchSim] UXF session folder not available yet; will retry at StopLogging. CSV fallback remains active.");
        }

        private bool ShouldCopyCompletedCsvToUxfSessionFolder()
        {
            return string.Equals(participantIdSource, "UXF", StringComparison.Ordinal) &&
                   uxfSessionNumber >= 0 &&
                   !string.IsNullOrWhiteSpace(uxfSessionId);
        }

        private void LogUxfCopyUnavailable(string message)
        {
            if (loggedUxfCopyUnavailable)
                return;

            loggedUxfCopyUnavailable = true;
            Debug.LogWarning(message);
        }

        private bool TryGetActiveUxfSessionFolder(out string sessionFolder, out Session selectedSession)
        {
            sessionFolder = "";
            selectedSession = null;
            FileSaver selectedFileSaver = null;

            Session[] sessions = FindObjectsByType<Session>(FindObjectsInactive.Include);
            for (int i = 0; i < sessions.Length; i++)
            {
                Session session = sessions[i];
                if (session == null || !session.hasInitialised || string.IsNullOrWhiteSpace(session.ppid))
                    continue;

                FileSaver fileSaver = FindActiveFileSaver(session);
                if (fileSaver == null)
                    continue;

                if (ParticipantMatchesSession(session))
                {
                    selectedSession = session;
                    selectedFileSaver = fileSaver;
                    break;
                }

                if (selectedSession == null)
                {
                    selectedSession = session;
                    selectedFileSaver = fileSaver;
                }
            }

            if (selectedSession == null || selectedFileSaver == null)
                return false;

            sessionFolder = selectedFileSaver.GetSessionPath(selectedSession);
            return !string.IsNullOrWhiteSpace(sessionFolder);
        }

        private static FileSaver FindActiveFileSaver(Session session)
        {
            foreach (DataHandler dataHandler in session.ActiveDataHandlers)
            {
                if (dataHandler is FileSaver fileSaver && fileSaver.active)
                    return fileSaver;
            }

            return null;
        }

        private bool ParticipantMatchesSession(Session session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.ppid))
                return false;

            string sessionParticipant = session.ppid.Trim();
            return string.Equals(sessionParticipant, participantId, StringComparison.Ordinal) ||
                   string.Equals(MakeFileSafe(sessionParticipant), participantId, StringComparison.Ordinal);
        }

        private void TryWriteUxfCopyManifest(string sourcePath, string destinationPath, Session session)
        {
            try
            {
                string manifestPath = Path.Combine(Path.GetDirectoryName(destinationPath), "car_following_data_manifest.json");
                string manifest =
                    "{\n" +
                    "  \"original_custom_csv_path\": \"" + EscapeJson(Path.GetFullPath(sourcePath)) + "\",\n" +
                    "  \"uxf_copy_csv_path\": \"" + EscapeJson(Path.GetFullPath(destinationPath)) + "\",\n" +
                    "  \"participant_id\": \"" + EscapeJson(participantId) + "\",\n" +
                    "  \"uxf_session_id\": \"" + EscapeJson(uxfSessionId) + "\",\n" +
                    "  \"uxf_session_number\": " + (session != null ? session.number : uxfSessionNumber).ToString(CultureInfo.InvariantCulture) + ",\n" +
                    "  \"protocol_version\": \"" + EscapeJson(protocolVersion) + "\",\n" +
                    "  \"protocol_profile\": \"" + EscapeJson(protocolProfile) + "\",\n" +
                    "  \"schema\": \"ResearchSim continuous car-following CSV with V2 tempo markers, analysis windows, and leader speed events\",\n" +
                    "  \"timing\": \"stimulus_time_s is the official V2 clock for music markers and analysis windows; phase_elapsed_s is the simulation clock; music_playback_time_s is diagnostic AudioSource.time; audio_sim_drift_s is stimulus_time_s minus phase_elapsed_s.\",\n" +
                    "  \"timestamp\": \"" + EscapeJson(DateTime.Now.ToString("o", CultureInfo.InvariantCulture)) + "\",\n" +
                    "  \"note\": \"Custom car-following CSV is the main behavioral dataset. This file is copied here for UXF session organization.\"\n" +
                    "}\n";

                File.WriteAllText(manifestPath, manifest, new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[ResearchSim] Could not write car-following UXF copy manifest: " + exception.Message);
            }
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        private void UpdateAcceleration()
        {
            Vector3 currentVelocity = GetParticipantVelocity();
            float dt = Time.fixedDeltaTime;
            if (dt > 0.0001f)
            {
                Vector3 forward = participantVehicle != null ? participantVehicle.forward : Vector3.forward;
                participantAcceleration = Vector3.Dot(currentVelocity - previousVelocity, forward) / dt;
            }
            previousVelocity = currentVelocity;
        }

        private Vector3 GetParticipantVelocity()
        {
            if (participantRigidbody == null)
                return Vector3.zero;
#if UNITY_6000_0_OR_NEWER
            return participantRigidbody.linearVelocity;
#else
            return participantRigidbody.velocity;
#endif
        }

        private void ReadInputs(out float steering, out float throttle, out float brake)
        {
            if (hybridInput != null)
            {
                hybridInput.RefreshInputValues();
                steering = Mathf.Clamp(hybridInput.Steering, -1f, 1f);
                throttle = Mathf.Clamp01(hybridInput.Throttle);
                brake = Mathf.Clamp01(hybridInput.Brake);
                return;
            }

            steering = Mathf.Clamp(ReadFloatMember(vppStandardInput, 0f, "externalSteer", "steerInput"), -1f, 1f);
            throttle = Mathf.Clamp01(ReadFloatMember(vppStandardInput, 0f, "externalThrottle", "throttleInput"));
            brake = Mathf.Clamp01(ReadFloatMember(vppStandardInput, 0f, "externalBrake", "brakeInput"));
        }

        private void ResolveReferences()
        {
            if (centerline == null)
                centerline = FindAnyObjectByType<CenterlinePath>();

            if (leadVehicle == null)
                leadVehicle = FindAnyObjectByType<LeadVehicleController>();

            if (feedbackController == null)
                feedbackController = FindAnyObjectByType<CarFollowingFeedbackController>();

            if (participantVehicle == null)
            {
                GameObject vehicle = GameObject.Find("Research VPP Vehicle");
                if (vehicle != null)
                    participantVehicle = vehicle.transform;
            }

            if (participantRigidbody == null && participantVehicle != null)
                participantRigidbody = participantVehicle.GetComponent<Rigidbody>();

            if (hybridInput == null && participantVehicle != null)
                hybridInput = participantVehicle.GetComponentInChildren<HybridVehicleInput>();

            if (vppStandardInput == null && participantVehicle != null)
                vppStandardInput = FindVppStandardInput(participantVehicle);
        }

        private static MonoBehaviour FindVppStandardInput(Transform root)
        {
            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour != null && behaviour.GetType().FullName == "VehiclePhysics.VPStandardInput")
                    return behaviour;
            }

            return null;
        }

        private static float ReadFloatMember(object target, float fallback, params string[] names)
        {
            if (target == null)
                return fallback;

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            Type type = target.GetType();
            for (int i = 0; i < names.Length; i++)
            {
                PropertyInfo prop = type.GetProperty(names[i], Flags);
                if (prop != null && IsNumeric(prop.PropertyType))
                    return Convert.ToSingle(prop.GetValue(target), CultureInfo.InvariantCulture);

                FieldInfo field = type.GetField(names[i], Flags);
                if (field != null && IsNumeric(field.FieldType))
                    return Convert.ToSingle(field.GetValue(target), CultureInfo.InvariantCulture);
            }

            return fallback;
        }

        private static bool IsNumeric(Type type)
        {
            return type == typeof(float) || type == typeof(double) || type == typeof(int);
        }

        private void AppendFloat(float value, bool addComma = true)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                line.Append("NaN");
            else
                line.Append(value.ToString("F4", CultureInfo.InvariantCulture));

            if (addComma)
                line.Append(',');
        }

        private void AppendString(string value, bool addComma = true)
        {
            line.Append(EscapeCsv(value));
            if (addComma)
                line.Append(',');
        }

        private void AppendInt(int value, bool addComma = true)
        {
            line.Append(value.ToString(CultureInfo.InvariantCulture));
            if (addComma)
                line.Append(',');
        }

        private void AppendDouble(double value, bool addComma = true)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                line.Append("NaN");
            else
                line.Append(value.ToString("F6", CultureInfo.InvariantCulture));

            if (addComma)
                line.Append(',');
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
                return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string MakeFileSafe(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
        }
    }
}
