using System;
using System.Globalization;
using Unity.Profiling;
using UnityEngine;

namespace ResearchSim
{
    /// <summary>
    /// Deterministic kinematic lead vehicle for the car-following task. It uses
    /// the project path only; it does not use RCCP, VPP input, or a second
    /// vehicle physics model.
    /// </summary>
    public sealed class LeadVehicleController : MonoBehaviour
    {
        [Header("Path")]
        public CenterlinePath centerline;
        public float lateralOffsetMeters;
        public float heightOffsetMeters;

        [Header("Participant Start Sync")]
        public Rigidbody participantRigidbody;
        [Min(0.1f)] public float participantStartSpeedKmh = 5f;
        [Min(0f)] public float participantStartGraceSeconds = 1f;
        [Min(0f)] public float participantStartDistanceMeters = 0.75f;
        [Min(0f)] public float participantStartSustainSeconds = 0.2f;

        [Header("Speed")]
        [Min(1f)] public float cruiseSpeedKmh = 70f;
        [Min(0f)] public float startSpeedKmh = 0f;
        [Min(0.1f)] public float cruiseAccelerationMps2 = 1.8f;
        [Min(1f)] public float decelerationTargetSpeedKmh = 55f;
        [Min(0f)] public float decelerationDurationSeconds = 4f;
        [Min(0f)] public float holdDurationSeconds = 6f;
        [Min(0f)] public float returnToCruiseDurationSeconds = 6f;

        [Header("Initial Gap")]
        [Min(5f)] public float initialDistanceAheadMeters = 45f;

        public float CurrentSpeedMps { get; private set; }
        public float CurrentSpeedKmh { get { return CurrentSpeedMps * 3.6f; } }
        public float DistanceAlongPath { get; private set; }
        public bool IsArmed { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsDecelerating { get; private set; }
        public bool IsHoldingTargetSpeed { get; private set; }
        public bool IsReturningToCruise { get; private set; }
        public bool IsSpeedEventActive { get { return IsDecelerating || IsHoldingTargetSpeed || IsReturningToCruise; } }
        public bool IsInDecelerationEvent { get; private set; }
        public string CurrentSpeedEventPhase { get { return GetCurrentSpeedEventPhase(); } }
        public float LastDecelerationStartTime { get; private set; } = -1f;
        public float LastDecelerationEndTime { get; private set; } = -1f;
        public float LastHoldStartTime { get; private set; } = -1f;
        public float LastHoldEndTime { get; private set; } = -1f;
        public float LastRecoveryStartTime { get; private set; } = -1f;
        public float LastRecoveryEndTime { get; private set; } = -1f;

        public event Action<float> OnDrivingStarted;
        public event Action<float> OnDecelerationStart;
        public event Action<float> OnCruiseRestored;

        private enum DriveState
        {
            Cruise,
            Decelerating,
            HoldingTargetSpeed,
            ReturningToCruise,
            RampingToExternalTarget,
            HoldingExternalTarget
        }

        private DriveState driveState = DriveState.Cruise;
        private float decelStateStartTime;
        private float cruiseSpeedMps;
        private float startSpeedMps;
        private float decelTargetSpeedMps;
        private float externalRampStartSpeedMps;
        private float externalTargetSpeedMps;
        private float externalRampDurationSeconds;
        private bool externalRampSettlesAsCruise;
        private string externalSpeedEventPhase = "none";
        private Rigidbody kinematicBody;
        private float armedAtTime = -1f;
        private float movementCandidateSince = -1f;
        private Vector3 armedParticipantPosition;
        private bool warnedMissingParticipantRigidbody;
        private static readonly ProfilerMarker LeaderStartMarker = new ProfilerMarker("ResearchSim.Startup.LeaderStart");

        private void Awake()
        {
            RefreshDerivedValues();
            kinematicBody = EnsureKinematicBody();
        }

        private void OnValidate()
        {
            RefreshDerivedValues();
        }

        private void FixedUpdate()
        {
            if (centerline == null || centerline.Count < 2)
                return;

            if (IsArmed && !IsRunning && ParticipantHasStarted())
                StartDriving();

            if (!IsRunning)
                return;

            UpdateSpeed();
            DistanceAlongPath += CurrentSpeedMps * Time.fixedDeltaTime;
            ClampOrWrapDistance();
            ApplyPose();
        }

        public void Initialize(CenterlinePath path, Vector3 participantPosition)
        {
            centerline = path;
            RefreshDerivedValues();
            DistanceAlongPath = GetDistanceAlongPathForPosition(participantPosition) + initialDistanceAheadMeters;
            CurrentSpeedMps = 0f;
            driveState = DriveState.Cruise;
            IsRunning = false;
            IsArmed = false;
            IsDecelerating = false;
            IsInDecelerationEvent = false;
            ClearSpeedEventFlags();
            ClearSpeedEventTimes();
            ResetParticipantStartDetection(participantPosition);
            ApplyPose();
        }

        public void ArmForParticipantStart()
        {
            if (centerline == null || centerline.Count < 2)
            {
                Debug.LogWarning("[LeadVehicle] Cannot arm: no valid centerline.");
                return;
            }

            IsArmed = true;
            IsRunning = false;
            CurrentSpeedMps = 0f;
            ResetParticipantStartDetection(GetParticipantPosition());
            Debug.Log("[LeadVehicle] Armed. Waiting for participant movement before starting.");
        }

        public void StartDriving()
        {
            if (IsRunning)
                return;

            using (LeaderStartMarker.Auto())
            {
                RefreshDerivedValues();
                IsArmed = false;
                IsRunning = true;
                CurrentSpeedMps = startSpeedMps;
                SafeInvoke(OnDrivingStarted, Time.time, "OnDrivingStarted");
            }
            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[LeadVehicle] Started at t={0:F2}. Ramping from {1:F1} to {2:F1} km/h.",
                Time.time,
                CurrentSpeedKmh,
                cruiseSpeedKmh));
        }

        public void StopDriving()
        {
            IsArmed = false;
            IsRunning = false;
            CurrentSpeedMps = 0f;
            driveState = DriveState.Cruise;
            ClearSpeedEventFlags();
            IsInDecelerationEvent = false;
        }

        public void TriggerDeceleration()
        {
            TriggerDeceleration(false);
        }

        public void TriggerDeceleration(bool force)
        {
            if (!IsRunning)
                return;

            if (!force && driveState != DriveState.Cruise)
                return;

            RefreshDerivedValues();
            driveState = DriveState.Decelerating;
            decelStateStartTime = Time.time;
            IsDecelerating = true;
            IsHoldingTargetSpeed = false;
            IsReturningToCruise = false;
            IsInDecelerationEvent = true;
            LastDecelerationStartTime = Time.time;
            LastDecelerationEndTime = -1f;
            LastHoldStartTime = -1f;
            LastHoldEndTime = -1f;
            LastRecoveryStartTime = -1f;
            LastRecoveryEndTime = -1f;
            SafeInvoke(OnDecelerationStart, Time.time, "OnDecelerationStart");

            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[LeadVehicle] Deceleration at t={0:F2}: {1:F1} km/h to {2:F1} km/h.",
                Time.time,
                CurrentSpeedKmh,
                decelerationTargetSpeedKmh));
        }

        public bool StartSpeedRamp(float targetSpeedKmh, float durationSeconds, bool settleAsCruise)
        {
            if (!IsRunning)
                return false;

            if (driveState != DriveState.Cruise && driveState != DriveState.HoldingExternalTarget)
                return false;

            RefreshDerivedValues();
            externalRampStartSpeedMps = CurrentSpeedMps;
            externalTargetSpeedMps = Mathf.Max(1f, targetSpeedKmh) / 3.6f;
            externalRampDurationSeconds = Mathf.Max(0f, durationSeconds);
            externalRampSettlesAsCruise = settleAsCruise;
            decelStateStartTime = Time.time;
            driveState = DriveState.RampingToExternalTarget;
            externalSpeedEventPhase = externalTargetSpeedMps >= externalRampStartSpeedMps ? "ramp_up" : "ramp_down";
            IsDecelerating = externalTargetSpeedMps < externalRampStartSpeedMps;
            IsHoldingTargetSpeed = false;
            IsReturningToCruise = false;
            IsInDecelerationEvent = IsDecelerating;

            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[LeadVehicle] V2 speed ramp at t={0:F2}: {1:F1} to {2:F1} km/h over {3:F1}s.",
                Time.time,
                externalRampStartSpeedMps * 3.6f,
                targetSpeedKmh,
                externalRampDurationSeconds));
            return true;
        }

        public void ResetToParticipant(Vector3 participantPosition)
        {
            ForceCruiseState();
            DistanceAlongPath = GetDistanceAlongPathForPosition(participantPosition) + initialDistanceAheadMeters;
            ApplyPose();
        }

        public void RestartFromParticipant(Vector3 participantPosition, bool startImmediately)
        {
            RefreshDerivedValues();
            ForceCruiseState();
            DistanceAlongPath = GetDistanceAlongPathForPosition(participantPosition) + initialDistanceAheadMeters;
            CurrentSpeedMps = 0f;
            IsArmed = !startImmediately;
            IsRunning = startImmediately;
            IsDecelerating = false;
            IsInDecelerationEvent = false;
            ClearSpeedEventFlags();
            ClearSpeedEventTimes();
            ResetParticipantStartDetection(participantPosition);
            ApplyPose();
        }

        public void ForceCruiseState()
        {
            driveState = DriveState.Cruise;
            CurrentSpeedMps = IsRunning ? Mathf.Min(CurrentSpeedMps, cruiseSpeedMps) : 0f;
            ClearSpeedEventFlags();
            IsInDecelerationEvent = false;
        }

        public float GetTotalPathLength()
        {
            if (centerline == null || centerline.waypoints == null || centerline.waypoints.Length < 2)
                return 0f;

            float length = 0f;
            int segmentCount = centerline.closedLoop ? centerline.waypoints.Length : centerline.waypoints.Length - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                Transform a = centerline.waypoints[i];
                Transform b = centerline.waypoints[(i + 1) % centerline.waypoints.Length];
                if (a != null && b != null)
                    length += Vector3.Distance(a.position, b.position);
            }

            return length;
        }

        public float GetDistanceAlongPathForPosition(Vector3 worldPosition)
        {
            if (centerline == null || centerline.waypoints == null || centerline.waypoints.Length < 2)
                return 0f;

            float bestDistance = 0f;
            float bestSqrDistance = float.PositiveInfinity;
            float accumulated = 0f;
            int segmentCount = centerline.closedLoop ? centerline.waypoints.Length : centerline.waypoints.Length - 1;

            for (int i = 0; i < segmentCount; i++)
            {
                Transform startTransform = centerline.waypoints[i];
                Transform endTransform = centerline.waypoints[(i + 1) % centerline.waypoints.Length];
                if (startTransform == null || endTransform == null)
                    continue;

                Vector3 start = startTransform.position;
                Vector3 end = endTransform.position;
                Vector3 segment = end - start;
                float segmentLength = segment.magnitude;
                if (segmentLength <= 0.001f)
                    continue;

                float t = Mathf.Clamp01(Vector3.Dot(worldPosition - start, segment) / (segmentLength * segmentLength));
                Vector3 projected = start + segment * t;
                float sqrDistance = (worldPosition - projected).sqrMagnitude;
                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    bestDistance = accumulated + segmentLength * t;
                }

                accumulated += segmentLength;
            }

            return bestDistance;
        }

        private bool ParticipantHasStarted()
        {
            if (participantRigidbody == null)
            {
                if (!warnedMissingParticipantRigidbody)
                {
                    warnedMissingParticipantRigidbody = true;
                    Debug.LogWarning("[LeadVehicle] Participant Rigidbody missing; cannot detect participant movement.");
                }

                return false;
            }

            if (Time.time - armedAtTime < participantStartGraceSeconds)
                return false;

            Vector3 velocity = GetParticipantVelocity();
            velocity.y = 0f;
            float speedKmh = velocity.magnitude * 3.6f;

            Vector3 currentPosition = GetParticipantPosition();
            currentPosition.y = armedParticipantPosition.y;
            float movedMeters = Vector3.Distance(currentPosition, armedParticipantPosition);
            bool movementCandidate = speedKmh >= participantStartSpeedKmh && movedMeters >= participantStartDistanceMeters;

            if (!movementCandidate)
            {
                movementCandidateSince = -1f;
                return false;
            }

            if (movementCandidateSince < 0f)
                movementCandidateSince = Time.time;

            bool sustained = Time.time - movementCandidateSince >= participantStartSustainSeconds;
            if (sustained)
            {
                Debug.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "[LeadVehicle] Participant movement confirmed: speed={0:F1} km/h, moved={1:F2} m.",
                    speedKmh,
                    movedMeters));
            }

            return sustained;
        }

        private void ResetParticipantStartDetection(Vector3 participantPosition)
        {
            armedAtTime = Time.time;
            movementCandidateSince = -1f;
            armedParticipantPosition = participantPosition;
            warnedMissingParticipantRigidbody = false;
        }

        private Vector3 GetParticipantPosition()
        {
            return participantRigidbody != null ? participantRigidbody.position : Vector3.zero;
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

        private void UpdateSpeed()
        {
            switch (driveState)
            {
                case DriveState.Cruise:
                    CurrentSpeedMps = Mathf.MoveTowards(
                        CurrentSpeedMps,
                        cruiseSpeedMps,
                        Mathf.Max(0.1f, cruiseAccelerationMps2) * Time.fixedDeltaTime);
                    break;

                case DriveState.Decelerating:
                {
                    if (decelerationDurationSeconds <= 0f)
                    {
                        CurrentSpeedMps = decelTargetSpeedMps;
                        EnterHoldOrRecovery();
                        break;
                    }

                    float t = Mathf.Clamp01((Time.time - decelStateStartTime) / decelerationDurationSeconds);
                    CurrentSpeedMps = Mathf.Lerp(cruiseSpeedMps, decelTargetSpeedMps, Smooth01(t));
                    if (t >= 1f)
                        EnterHoldOrRecovery();
                    break;
                }

                case DriveState.HoldingTargetSpeed:
                {
                    CurrentSpeedMps = decelTargetSpeedMps;
                    if (holdDurationSeconds <= 0f || Time.time - decelStateStartTime >= holdDurationSeconds)
                        EnterRecovery();
                    break;
                }

                case DriveState.ReturningToCruise:
                {
                    if (returnToCruiseDurationSeconds <= 0f)
                    {
                        CurrentSpeedMps = cruiseSpeedMps;
                        CompleteSpeedEvent();
                        break;
                    }

                    float t = Mathf.Clamp01((Time.time - decelStateStartTime) / returnToCruiseDurationSeconds);
                    CurrentSpeedMps = Mathf.Lerp(decelTargetSpeedMps, cruiseSpeedMps, Smooth01(t));
                    if (t >= 1f)
                        CompleteSpeedEvent();
                    break;
                }

                case DriveState.RampingToExternalTarget:
                {
                    if (externalRampDurationSeconds <= 0f)
                    {
                        CurrentSpeedMps = externalTargetSpeedMps;
                        CompleteExternalSpeedRamp();
                        break;
                    }

                    float t = Mathf.Clamp01((Time.time - decelStateStartTime) / externalRampDurationSeconds);
                    CurrentSpeedMps = Mathf.Lerp(externalRampStartSpeedMps, externalTargetSpeedMps, t);
                    if (t >= 1f)
                        CompleteExternalSpeedRamp();
                    break;
                }

                case DriveState.HoldingExternalTarget:
                    CurrentSpeedMps = externalTargetSpeedMps;
                    break;
            }
        }

        private void CompleteExternalSpeedRamp()
        {
            CurrentSpeedMps = externalTargetSpeedMps;
            IsDecelerating = false;
            IsHoldingTargetSpeed = !externalRampSettlesAsCruise;
            IsReturningToCruise = false;
            IsInDecelerationEvent = false;

            if (externalRampSettlesAsCruise)
            {
                driveState = DriveState.Cruise;
                cruiseSpeedMps = externalTargetSpeedMps;
                cruiseSpeedKmh = externalTargetSpeedMps * 3.6f;
                externalSpeedEventPhase = "hold_70";
                SafeInvoke(OnCruiseRestored, Time.time, "OnCruiseRestored");
                return;
            }

            driveState = DriveState.HoldingExternalTarget;
            externalSpeedEventPhase = "hold_80";
        }

        private void EnterHoldOrRecovery()
        {
            CurrentSpeedMps = decelTargetSpeedMps;
            LastDecelerationEndTime = Time.time;
            IsDecelerating = false;

            if (holdDurationSeconds <= 0f)
            {
                LastHoldStartTime = -1f;
                LastHoldEndTime = -1f;
                EnterRecovery();
                return;
            }

            driveState = DriveState.HoldingTargetSpeed;
            decelStateStartTime = Time.time;
            IsHoldingTargetSpeed = true;
            LastHoldStartTime = Time.time;
        }

        private void EnterRecovery()
        {
            if (IsHoldingTargetSpeed)
                LastHoldEndTime = Time.time;

            driveState = DriveState.ReturningToCruise;
            decelStateStartTime = Time.time;
            IsDecelerating = false;
            IsHoldingTargetSpeed = false;
            IsReturningToCruise = true;
            LastRecoveryStartTime = Time.time;
        }

        private void CompleteSpeedEvent()
        {
            driveState = DriveState.Cruise;
            CurrentSpeedMps = cruiseSpeedMps;
            ClearSpeedEventFlags();
            IsInDecelerationEvent = false;
            LastRecoveryEndTime = Time.time;
            SafeInvoke(OnCruiseRestored, Time.time, "OnCruiseRestored");
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[LeadVehicle] Cruise restored at t={0:F2}.", Time.time));
        }

        private void ClearSpeedEventFlags()
        {
            IsDecelerating = false;
            IsHoldingTargetSpeed = false;
            IsReturningToCruise = false;
            externalSpeedEventPhase = "none";
        }

        private void ClearSpeedEventTimes()
        {
            LastDecelerationStartTime = -1f;
            LastDecelerationEndTime = -1f;
            LastHoldStartTime = -1f;
            LastHoldEndTime = -1f;
            LastRecoveryStartTime = -1f;
            LastRecoveryEndTime = -1f;
        }

        private string GetCurrentSpeedEventPhase()
        {
            if (externalSpeedEventPhase != "none")
                return externalSpeedEventPhase;

            if (IsDecelerating)
                return "decelerating";
            if (IsHoldingTargetSpeed)
                return "hold";
            if (IsReturningToCruise)
                return "recovery";

            return "none";
        }

        private static float Smooth01(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private void ClampOrWrapDistance()
        {
            float totalLength = GetTotalPathLength();
            if (totalLength <= 0f)
                return;

            if (centerline.closedLoop)
            {
                while (DistanceAlongPath > totalLength)
                    DistanceAlongPath -= totalLength;
            }
            else if (DistanceAlongPath > totalLength)
            {
                DistanceAlongPath = totalLength;
                StopDriving();
                Debug.LogWarning("[LeadVehicle] Reached end of open path. Stopping leader.");
            }
        }

        private void ApplyPose()
        {
            if (!SamplePath(DistanceAlongPath, out Vector3 position, out Vector3 tangent))
                return;

            if (Mathf.Abs(lateralOffsetMeters) > 0.001f)
            {
                Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
                position += right * lateralOffsetMeters;
            }

            position.y += heightOffsetMeters;
            Quaternion rotation = tangent.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(tangent, Vector3.up)
                : transform.rotation;

            if (kinematicBody != null && kinematicBody.isKinematic)
            {
                kinematicBody.MovePosition(position);
                kinematicBody.MoveRotation(rotation);
                return;
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        private bool SamplePath(float distance, out Vector3 position, out Vector3 tangent)
        {
            position = transform.position;
            tangent = transform.forward;

            if (centerline == null || centerline.waypoints == null || centerline.waypoints.Length < 2)
                return false;

            float accumulated = 0f;
            int segmentCount = centerline.closedLoop ? centerline.waypoints.Length : centerline.waypoints.Length - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                Transform startTransform = centerline.waypoints[i];
                Transform endTransform = centerline.waypoints[(i + 1) % centerline.waypoints.Length];
                if (startTransform == null || endTransform == null)
                    continue;

                Vector3 start = startTransform.position;
                Vector3 end = endTransform.position;
                float segmentLength = Vector3.Distance(start, end);
                if (segmentLength <= 0.001f)
                    continue;

                if (accumulated + segmentLength >= distance)
                {
                    float t = Mathf.Clamp01((distance - accumulated) / segmentLength);
                    position = Vector3.Lerp(start, end, t);
                    tangent = (end - start).normalized;
                    return true;
                }

                accumulated += segmentLength;
            }

            Transform last = centerline.waypoints[centerline.waypoints.Length - 1];
            Transform previous = centerline.waypoints[centerline.waypoints.Length - 2];
            if (last == null || previous == null)
                return false;

            position = last.position;
            tangent = (last.position - previous.position).normalized;
            return true;
        }

        private void RefreshDerivedValues()
        {
            cruiseSpeedMps = Mathf.Max(1f, cruiseSpeedKmh) / 3.6f;
            startSpeedMps = Mathf.Clamp(startSpeedKmh / 3.6f, 0f, cruiseSpeedMps);
            decelTargetSpeedMps = Mathf.Max(1f, decelerationTargetSpeedKmh) / 3.6f;
        }

        private Rigidbody EnsureKinematicBody()
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null)
                rb = gameObject.AddComponent<Rigidbody>();

            rb.isKinematic = true;
            rb.useGravity = false;
            rb.detectCollisions = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            return rb;
        }

        private static void SafeInvoke(Action<float> callback, float time, string label)
        {
            try { callback?.Invoke(time); }
            catch (Exception e) { Debug.LogWarning("[LeadVehicle] " + label + " listener error: " + e.Message); }
        }
    }
}
