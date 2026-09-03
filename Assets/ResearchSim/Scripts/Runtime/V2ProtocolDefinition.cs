using UnityEngine;

namespace ResearchSim
{
    public static class V2ProtocolDefinition
    {
        public const string ProtocolVersion = "V2_12Min_2TempoChanges";
        public const float FallbackBlockDurationSeconds = 720f;
        public const float T1Seconds = 240f;
        public const float T2Seconds = 480f;
        public const float LeaderCar1Seconds = 300f;
        public const float LeaderCar2Seconds = 540f;
        public const float LeaderRampDurationSeconds = 5f;

        public struct Marker
        {
            public readonly string label;
            public readonly float timeSeconds;
            public readonly string type;

            public Marker(string label, float timeSeconds, string type)
            {
                this.label = label;
                this.timeSeconds = timeSeconds;
                this.type = type;
            }
        }

        public static readonly Marker[] Markers =
        {
            new Marker("PRE_T1", 225f, "window_boundary"),
            new Marker("T1", T1Seconds, "tempo_event"),
            new Marker("POST_T1", 255f, "window_boundary"),
            new Marker("CAR_1", LeaderCar1Seconds, "leader_speed_event"),
            new Marker("PRE_T2", 465f, "window_boundary"),
            new Marker("T2", T2Seconds, "tempo_event"),
            new Marker("POST_T2", 495f, "window_boundary"),
            new Marker("CAR_2", LeaderCar2Seconds, "leader_speed_event"),
            new Marker("END", FallbackBlockDurationSeconds, "block_end")
        };

        public static bool IsV2(string protocolVersion)
        {
            return string.Equals(protocolVersion, ProtocolVersion, System.StringComparison.Ordinal);
        }

        public static string GetStimulusId(MusicEventController.MusicBlockCondition condition)
        {
            switch (condition)
            {
                case MusicEventController.MusicBlockCondition.SlowFast:
                    return "spring_vivaldi_tempo_increase_60_120_140_12min";
                case MusicEventController.MusicBlockCondition.FastSlow:
                    return "spring_vivaldi_tempo_decrease_140_120_60_12min";
                default:
                    return "pink_noise";
            }
        }

        public static string GetAudioFileName(MusicEventController.MusicBlockCondition condition)
        {
            return GetStimulusId(condition) + ".mp3";
        }

        public static string GetAudioControlType(MusicEventController.MusicBlockCondition condition)
        {
            return condition == MusicEventController.MusicBlockCondition.ControlStable
                ? "pink_noise"
                : "music";
        }

        public static bool HasMusicalTempo(MusicEventController.MusicBlockCondition condition)
        {
            return condition != MusicEventController.MusicBlockCondition.ControlStable;
        }

        public static string GetTempoPlan(MusicEventController.MusicBlockCondition condition)
        {
            switch (condition)
            {
                case MusicEventController.MusicBlockCondition.SlowFast:
                    return "60>120>140";
                case MusicEventController.MusicBlockCondition.FastSlow:
                    return "140>120>60";
                default:
                    return "none";
            }
        }

        public static string GetTransitionType(MusicEventController.MusicBlockCondition condition)
        {
            return HasMusicalTempo(condition) ? "sudden" : "none";
        }

        public static int GetTempoSegmentIndex(float blockTimeSeconds)
        {
            if (blockTimeSeconds < T1Seconds)
                return 0;
            return blockTimeSeconds < T2Seconds ? 1 : 2;
        }

        public static float GetCurrentBpm(MusicEventController.MusicBlockCondition condition, int segmentIndex)
        {
            if (!HasMusicalTempo(condition))
                return -1f;

            if (condition == MusicEventController.MusicBlockCondition.SlowFast)
                return segmentIndex <= 0 ? 60f : segmentIndex == 1 ? 120f : 140f;

            return segmentIndex <= 0 ? 140f : segmentIndex == 1 ? 120f : 60f;
        }

        public static float GetPreviousBpm(MusicEventController.MusicBlockCondition condition, int segmentIndex)
        {
            return segmentIndex <= 0 ? -1f : GetCurrentBpm(condition, segmentIndex - 1);
        }

        public static float GetNextBpm(MusicEventController.MusicBlockCondition condition, int segmentIndex)
        {
            return segmentIndex >= 2 ? -1f : GetCurrentBpm(condition, segmentIndex + 1);
        }

        public static string GetAnalysisWindow(float blockTimeSeconds)
        {
            if (blockTimeSeconds >= 225f && blockTimeSeconds < T1Seconds)
                return "PRE_T1";
            if (blockTimeSeconds >= T1Seconds && blockTimeSeconds < 255f)
                return "POST_T1";
            if (blockTimeSeconds >= 465f && blockTimeSeconds < T2Seconds)
                return "PRE_T2";
            if (blockTimeSeconds >= T2Seconds && blockTimeSeconds < 495f)
                return "POST_T2";
            return "none";
        }

        public static float GetTimeToNearestTempoEvent(float blockTimeSeconds)
        {
            float distanceToT1 = Mathf.Abs(blockTimeSeconds - T1Seconds);
            float distanceToT2 = Mathf.Abs(blockTimeSeconds - T2Seconds);
            return distanceToT1 <= distanceToT2
                ? blockTimeSeconds - T1Seconds
                : blockTimeSeconds - T2Seconds;
        }

        public static int GetLeaderEventIndex(float blockTimeSeconds)
        {
            if (blockTimeSeconds < LeaderCar1Seconds)
                return -1;
            return blockTimeSeconds < LeaderCar2Seconds ? 1 : 2;
        }

        public static string GetLeaderEventLabel(int eventIndex)
        {
            if (eventIndex == 1)
                return "CAR_1";
            if (eventIndex == 2)
                return "CAR_2";
            return "none";
        }

        public static float GetLeaderEventTime(int eventIndex)
        {
            if (eventIndex == 1)
                return LeaderCar1Seconds;
            if (eventIndex == 2)
                return LeaderCar2Seconds;
            return -1f;
        }

        public static float GetLeaderTargetSpeedKmh(float blockTimeSeconds)
        {
            return blockTimeSeconds >= LeaderCar1Seconds && blockTimeSeconds < LeaderCar2Seconds ? 80f : 70f;
        }
    }
}
