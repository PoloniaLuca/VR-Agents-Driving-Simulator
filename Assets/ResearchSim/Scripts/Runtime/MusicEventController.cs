using System;
using Unity.Profiling;
using UnityEngine;

namespace ResearchSim
{
    /// <summary>
    /// Owns music playback and timestamping for car-following events. It never
    /// reads input and never controls vehicle movement.
    /// </summary>
    public sealed class MusicEventController : MonoBehaviour
    {
        public enum MusicBlockCondition
        {
            SlowFast,
            FastSlow,
            ControlStable
        }

        [Serializable]
        public sealed class BlockMusicClip
        {
            public MusicBlockCondition condition;
            public string stimulusId;
            public AudioClip clip;
            public bool hasTempoChange;
            [Min(0f)] public float tempoChangeTimeSeconds = 240f;
            public float preBpm = 96f;
            public float postBpm = 120f;
            public string transitionType = "sudden";
        }

        private enum TempoState
        {
            None,
            Slow,
            Fast
        }

        [Header("Audio Sources")]
        public AudioSource primarySource;
        public AudioSource secondarySource;

        [Header("Slow Tempo Clips")]
        public AudioClip[] slowTempoClips;

        [Header("Fast Tempo Clips")]
        public AudioClip[] fastTempoClips;

        [Header("Prepared Block Music")]
        public BlockMusicClip[] blockMusicClips;

        [Header("Playback")]
        [Range(0f, 1f)] public float volume = 0.75f;
        [Min(0.01f)] public float sameTempoCrossfadeSeconds = 0.15f;

        public bool IsPlaying
        {
            get
            {
                return (primarySource != null && primarySource.isPlaying) ||
                       (secondarySource != null && secondarySource.isPlaying);
            }
        }

        public float LastEventTime { get; private set; } = -1f;
        public double LastEventDspTime { get; private set; } = -1d;
        public TrialScheduler.MusicEventType LastEventType { get; private set; }
        public string CurrentClipName { get; private set; } = string.Empty;
        public MusicBlockCondition CurrentBlockCondition { get; private set; } = MusicBlockCondition.ControlStable;
        public string CurrentStimulusId { get; private set; } = string.Empty;
        public bool CurrentBlockHasTempoChange { get; private set; }
        public float CurrentTempoChangeTimeSeconds { get; private set; } = -1f;
        public float CurrentPreBpm { get; private set; } = -1f;
        public float CurrentPostBpm { get; private set; } = -1f;
        public string CurrentTransitionType { get; private set; } = "none";
        public double AudioDspStartTimeSeconds { get; private set; } = -1d;
        public double CurrentAudioDspTimeSeconds { get { return AudioSettings.dspTime; } }
        public string StimulusClockSource { get { return stimulusClockStarted ? "dsp_time" : "unavailable"; } }
        public float CurrentStimulusTimeSeconds
        {
            get
            {
                if (!stimulusClockStarted || AudioDspStartTimeSeconds < 0d)
                    return -1f;

                double elapsed = frozenStimulusTimeSeconds >= 0d
                    ? frozenStimulusTimeSeconds
                    : AudioSettings.dspTime - AudioDspStartTimeSeconds;
                float result = Mathf.Max(0f, (float)elapsed);
                return currentBlockClipLengthSeconds > 0f
                    ? Mathf.Min(result, currentBlockClipLengthSeconds)
                    : result;
            }
        }
        public bool PreparedBlockAudioPreloadComplete { get { return AreAllPreparedBlockClipsLoaded(); } }
        public bool PreparedBlockAudioPreloadFailed { get { return HasPreparedBlockClipLoadFailure(); } }
        public string CurrentAudioClipLoadState
        {
            get { return GetAudioDataLoadStateLabel(currentBlockClip != null ? currentBlockClip.loadState : AudioDataLoadState.Unloaded); }
        }
        public float CurrentPlaybackTime
        {
            get { return activeSource != null && activeSource.clip != null ? activeSource.time : -1f; }
        }
        public bool HasCurrentClip
        {
            get { return activeSource != null && activeSource.clip != null; }
        }
        public float CurrentClipLengthSeconds
        {
            get { return HasCurrentClip ? activeSource.clip.length : -1f; }
        }
        public string CurrentClipDisplayName
        {
            get { return HasCurrentClip ? activeSource.clip.name : CurrentClipName; }
        }

        public event Action<TrialScheduler.MusicEventType, float> OnMusicEvent;

        private TempoState currentTempo = TempoState.None;
        private AudioSource activeSource;
        private AudioSource fadingOutSource;
        private AudioSource fadingInSource;
        private int currentSlowIndex;
        private int currentFastIndex;
        private float crossfadeProgress;
        private bool crossfading;
        private bool stimulusClockStarted;
        private double frozenStimulusTimeSeconds = -1d;
        private AudioClip currentBlockClip;
        private float currentBlockClipLengthSeconds = -1f;
        private static readonly ProfilerMarker FirstAudioPlayMarker = new ProfilerMarker("ResearchSim.Startup.FirstAudioSourcePlay");

        private void Awake()
        {
            EnsureAudioSources();
        }

        private void Update()
        {
            if (crossfading)
                UpdateCrossfade();
        }

        public void StartMusic(bool startSlow)
        {
            EnsureAudioSources();
            StopMusic();

            currentTempo = startSlow ? TempoState.Slow : TempoState.Fast;
            AudioClip clip = startSlow ? GetSlowClip() : GetFastClip();
            if (clip == null)
            {
                Debug.LogWarning("[MusicEvent] No clip assigned for initial tempo: " + currentTempo);
                return;
            }

            activeSource = primarySource;
            PlayClip(activeSource, clip, volume, true);
            CurrentClipName = clip.name;
            Debug.Log("[MusicEvent] Music started: " + currentTempo + " / " + CurrentClipName);
        }

        public bool StartBlockMusic(MusicBlockCondition condition)
        {
            EnsureAudioSources();
            StopMusic();
            ResetStimulusClock();

            BlockMusicClip blockClip = FindBlockClip(condition);
            if (blockClip == null || blockClip.clip == null)
            {
                Debug.LogWarning("[MusicEvent] No prepared block clip assigned for " + condition + ".");
                CurrentBlockCondition = condition;
                CurrentStimulusId = blockClip != null && !string.IsNullOrWhiteSpace(blockClip.stimulusId)
                    ? blockClip.stimulusId
                    : condition.ToString();
                CurrentClipName = string.Empty;
                SetCurrentTempoMetadata(blockClip);
                return false;
            }

            if (blockClip.clip.loadState != AudioDataLoadState.Loaded)
            {
                Debug.LogError("[MusicEvent] Prepared block clip is not loaded for " + condition + ": " + blockClip.clip.loadState + ".");
                return false;
            }

            CurrentBlockCondition = condition;
            CurrentStimulusId = string.IsNullOrWhiteSpace(blockClip.stimulusId) ? blockClip.clip.name : blockClip.stimulusId;
            CurrentClipName = blockClip.clip.name;
            SetCurrentTempoMetadata(blockClip);
            activeSource = primarySource;
            currentBlockClip = blockClip.clip;
            currentBlockClipLengthSeconds = blockClip.clip.length;
            PrepareSource(activeSource, blockClip.clip, volume, false);
            using (FirstAudioPlayMarker.Auto())
            {
                AudioDspStartTimeSeconds = AudioSettings.dspTime;
                stimulusClockStarted = true;
                activeSource.Play();
            }
            Debug.Log("[MusicEvent] Block music started: " + condition + " / " + CurrentStimulusId);
            return true;
        }

        public void RequestPreparedBlockAudioPreload()
        {
            if (blockMusicClips == null)
                return;

            for (int i = 0; i < blockMusicClips.Length; i++)
            {
                AudioClip clip = blockMusicClips[i] != null ? blockMusicClips[i].clip : null;
                if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                    clip.LoadAudioData();
            }
        }

        public bool AreAllPreparedBlockClipsLoaded()
        {
            if (blockMusicClips == null || blockMusicClips.Length == 0)
                return false;

            for (int i = 0; i < blockMusicClips.Length; i++)
            {
                AudioClip clip = blockMusicClips[i] != null ? blockMusicClips[i].clip : null;
                if (clip == null || clip.loadState != AudioDataLoadState.Loaded)
                    return false;
            }

            return true;
        }

        public bool HasPreparedBlockClipLoadFailure()
        {
            if (blockMusicClips == null || blockMusicClips.Length == 0)
                return true;

            for (int i = 0; i < blockMusicClips.Length; i++)
            {
                AudioClip clip = blockMusicClips[i] != null ? blockMusicClips[i].clip : null;
                if (clip == null || clip.loadState == AudioDataLoadState.Failed)
                    return true;
            }

            return false;
        }

        public string GetPreparedBlockAudioLoadSummary()
        {
            if (blockMusicClips == null || blockMusicClips.Length == 0)
                return "no_prepared_clips";

            string summary = "";
            for (int i = 0; i < blockMusicClips.Length; i++)
            {
                BlockMusicClip blockClip = blockMusicClips[i];
                string label = blockClip != null ? blockClip.condition.ToString() : "missing";
                AudioDataLoadState state = blockClip != null && blockClip.clip != null
                    ? blockClip.clip.loadState
                    : AudioDataLoadState.Failed;
                if (i > 0)
                    summary += ";";
                summary += label + "=" + GetAudioDataLoadStateLabel(state);
            }

            return summary;
        }

        public void ExecuteMusicEvent(TrialScheduler.MusicEventType eventType)
        {
            EnsureAudioSources();

            float eventTime = Time.time;
            double dspTime = AudioSettings.dspTime;

            switch (eventType)
            {
                case TrialScheduler.MusicEventType.Sham:
                    break;

                case TrialScheduler.MusicEventType.SameTempoChange:
                    ExecuteSameTempoChange();
                    break;

                case TrialScheduler.MusicEventType.SlowToFast:
                    ExecuteTempoChange(TempoState.Fast);
                    break;

                case TrialScheduler.MusicEventType.FastToSlow:
                    ExecuteTempoChange(TempoState.Slow);
                    break;
            }

            LastEventType = eventType;
            LastEventTime = eventTime;
            LastEventDspTime = dspTime;
            CurrentClipName = activeSource != null && activeSource.clip != null ? activeSource.clip.name : string.Empty;

            try { OnMusicEvent?.Invoke(eventType, eventTime); }
            catch (Exception e) { Debug.LogWarning("[MusicEvent] Event listener error: " + e.Message); }

            Debug.Log(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "[MusicEvent] {0} at t={1:F3}, dsp={2:F6}, clip={3}",
                eventType,
                eventTime,
                dspTime,
                CurrentClipName));
        }

        public void StopMusic()
        {
            FreezeStimulusClock();
            crossfading = false;
            StopSource(primarySource);
            StopSource(secondarySource);
            activeSource = null;
            currentTempo = TempoState.None;
            CurrentClipName = string.Empty;
            CurrentStimulusId = string.Empty;
            ClearCurrentTempoMetadata();
        }

        public string GetTempoStateLabel()
        {
            return currentTempo.ToString();
        }

        private void ExecuteTempoChange(TempoState targetTempo)
        {
            AudioClip clip = targetTempo == TempoState.Fast ? GetFastClip() : GetSlowClip();
            if (clip == null)
            {
                Debug.LogWarning("[MusicEvent] No clip available for tempo change to " + targetTempo);
                return;
            }

            AudioSource next = GetInactiveSource();
            PlayClip(next, clip, volume, true);
            StopSource(activeSource);
            activeSource = next;
            currentTempo = targetTempo;
        }

        private void ExecuteSameTempoChange()
        {
            AudioClip clip;
            if (currentTempo == TempoState.Fast)
            {
                AdvanceFastClip();
                clip = GetFastClip();
            }
            else
            {
                AdvanceSlowClip();
                clip = GetSlowClip();
            }

            if (clip == null)
            {
                Debug.LogWarning("[MusicEvent] Same-tempo event requested but no clip is assigned.");
                return;
            }

            AudioSource next = GetInactiveSource();
            PlayClip(next, clip, 0f, true);
            fadingOutSource = activeSource;
            fadingInSource = next;
            activeSource = next;
            crossfadeProgress = 0f;
            crossfading = true;
        }

        private void UpdateCrossfade()
        {
            crossfadeProgress += Time.deltaTime / Mathf.Max(0.01f, sameTempoCrossfadeSeconds);
            float t = Mathf.Clamp01(crossfadeProgress);

            if (fadingOutSource != null)
                fadingOutSource.volume = volume * (1f - t);
            if (fadingInSource != null)
                fadingInSource.volume = volume * t;

            if (t < 1f)
                return;

            crossfading = false;
            StopSource(fadingOutSource);
            fadingOutSource = null;
            fadingInSource = null;
        }

        private void AdvanceSlowClip()
        {
            if (slowTempoClips != null && slowTempoClips.Length > 1)
                currentSlowIndex = (currentSlowIndex + 1) % slowTempoClips.Length;
        }

        private void AdvanceFastClip()
        {
            if (fastTempoClips != null && fastTempoClips.Length > 1)
                currentFastIndex = (currentFastIndex + 1) % fastTempoClips.Length;
        }

        private AudioClip GetSlowClip()
        {
            if (slowTempoClips == null || slowTempoClips.Length == 0)
                return null;
            return slowTempoClips[Mathf.Clamp(currentSlowIndex, 0, slowTempoClips.Length - 1)];
        }

        private AudioClip GetFastClip()
        {
            if (fastTempoClips == null || fastTempoClips.Length == 0)
                return null;
            return fastTempoClips[Mathf.Clamp(currentFastIndex, 0, fastTempoClips.Length - 1)];
        }

        private AudioSource GetInactiveSource()
        {
            return activeSource == secondarySource ? primarySource : secondarySource;
        }

        private BlockMusicClip FindBlockClip(MusicBlockCondition condition)
        {
            if (blockMusicClips == null)
                return null;

            for (int i = 0; i < blockMusicClips.Length; i++)
            {
                BlockMusicClip candidate = blockMusicClips[i];
                if (candidate != null && candidate.condition == condition)
                    return candidate;
            }

            return null;
        }

        private void SetCurrentTempoMetadata(BlockMusicClip blockClip)
        {
            if (blockClip == null || !blockClip.hasTempoChange)
            {
                ClearCurrentTempoMetadata();
                return;
            }

            if (!IsValidTempoChange(blockClip))
            {
                ClearCurrentTempoMetadata();
                return;
            }

            CurrentBlockHasTempoChange = true;
            CurrentTempoChangeTimeSeconds = blockClip.tempoChangeTimeSeconds;
            CurrentPreBpm = blockClip.preBpm;
            CurrentPostBpm = blockClip.postBpm;
            CurrentTransitionType = string.IsNullOrWhiteSpace(blockClip.transitionType) ? "unknown" : blockClip.transitionType.Trim();
        }

        private bool IsValidTempoChange(BlockMusicClip blockClip)
        {
            string stimulus = blockClip != null && !string.IsNullOrWhiteSpace(blockClip.stimulusId)
                ? blockClip.stimulusId
                : blockClip != null ? blockClip.condition.ToString() : "unknown";

            if (blockClip == null || blockClip.clip == null || blockClip.clip.length <= 0f)
            {
                Debug.LogWarning("[MusicEvent] Tempo-change metadata ignored for " + stimulus + ": missing or invalid AudioClip length.");
                return false;
            }

            if (blockClip.tempoChangeTimeSeconds <= 0f)
            {
                Debug.LogWarning("[MusicEvent] Tempo-change metadata ignored for " + stimulus + ": tempoChangeTimeSeconds must be > 0.");
                return false;
            }

            if (blockClip.tempoChangeTimeSeconds >= blockClip.clip.length)
            {
                Debug.LogWarning("[MusicEvent] Tempo-change metadata ignored for " + stimulus + ": tempoChangeTimeSeconds must be less than AudioClip.length.");
                return false;
            }

            return true;
        }

        private void ClearCurrentTempoMetadata()
        {
            CurrentBlockHasTempoChange = false;
            CurrentTempoChangeTimeSeconds = -1f;
            CurrentPreBpm = -1f;
            CurrentPostBpm = -1f;
            CurrentTransitionType = "none";
        }

        private static void PlayClip(AudioSource source, AudioClip clip, float sourceVolume, bool loop)
        {
            if (source == null || clip == null)
                return;

            PrepareSource(source, clip, sourceVolume, loop);
            source.Play();
        }

        private static void PrepareSource(AudioSource source, AudioClip clip, float sourceVolume, bool loop)
        {
            source.clip = clip;
            source.loop = loop;
            source.volume = sourceVolume;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.time = 0f;
        }

        private void FreezeStimulusClock()
        {
            if (!stimulusClockStarted || frozenStimulusTimeSeconds >= 0d || AudioDspStartTimeSeconds < 0d)
                return;

            frozenStimulusTimeSeconds = Math.Max(0d, AudioSettings.dspTime - AudioDspStartTimeSeconds);
            if (currentBlockClipLengthSeconds > 0f)
                frozenStimulusTimeSeconds = Math.Min(frozenStimulusTimeSeconds, currentBlockClipLengthSeconds);
        }

        private void ResetStimulusClock()
        {
            stimulusClockStarted = false;
            AudioDspStartTimeSeconds = -1d;
            frozenStimulusTimeSeconds = -1d;
            currentBlockClip = null;
            currentBlockClipLengthSeconds = -1f;
        }

        private static string GetAudioDataLoadStateLabel(AudioDataLoadState state)
        {
            switch (state)
            {
                case AudioDataLoadState.Loaded:
                    return "loaded";
                case AudioDataLoadState.Loading:
                    return "loading";
                case AudioDataLoadState.Failed:
                    return "failed";
                default:
                    return "unloaded";
            }
        }

        private static void StopSource(AudioSource source)
        {
            if (source == null)
                return;

            source.Stop();
            source.clip = null;
        }

        private void EnsureAudioSources()
        {
            AudioSource[] sources = GetComponents<AudioSource>();

            if (primarySource == null)
                primarySource = sources.Length > 0 ? sources[0] : gameObject.AddComponent<AudioSource>();

            if (secondarySource == null)
            {
                sources = GetComponents<AudioSource>();
                secondarySource = sources.Length > 1 ? sources[1] : gameObject.AddComponent<AudioSource>();
            }

            ConfigureSource(primarySource);
            ConfigureSource(secondarySource);
        }

        private static void ConfigureSource(AudioSource source)
        {
            if (source == null)
                return;

            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.maxDistance = 1f;
        }
    }
}
