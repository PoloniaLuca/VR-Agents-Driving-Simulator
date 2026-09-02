using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ResearchSim
{
    /// <summary>
    /// Generates the pseudorandom music/deceleration event schedule for the
    /// car-following experiment. It does not depend on input or vehicle physics.
    /// </summary>
    public class TrialScheduler : MonoBehaviour
    {
        public enum MusicEventType
        {
            Sham,
            SameTempoChange,
            SlowToFast,
            FastToSlow
        }

        [Serializable]
        public class TrialEvent
        {
            public int index;
            public int blockIndex;
            public MusicEventType musicType;
            public bool hasLeaderDeceleration;
            public float decelerationDelaySeconds;
            public float musicEventTime = -1f;
            public float leadDecelerationTime = -1f;

            public string ConditionLabel
            {
                get { return musicType + (hasLeaderDeceleration ? "_Decel" : "_NoDecel"); }
            }
        }

        [Serializable]
        public class ScheduleData
        {
            public int seed;
            public string participantId;
            public string generatedAt;
            public List<TrialEvent> events = new List<TrialEvent>();
        }

        [Header("Schedule")]
        [Min(1)] public int repetitionsPerCell = 4;
        [Min(1)] public int numberOfBlocks = 3;
        public int seedOverride;

        [Header("Timing")]
        [Min(20f)] public float minimumInterEventSeconds = 50f;
        [Min(30f)] public float maximumInterEventSeconds = 75f;
        public float[] decelerationDelaysSeconds = { 2f, 3.5f, 5f, 6.5f };

        public ScheduleData Schedule { get; private set; }
        public int TotalEvents { get { return Schedule != null ? Schedule.events.Count : 0; } }
        public string LastValidationReport { get; private set; } = string.Empty;

        public void GenerateSchedule(string participantId)
        {
            int seed = seedOverride != 0 ? seedOverride : GenerateTimeSeed();
            System.Random rng = new System.Random(seed);
            List<TrialEvent> pool = BuildEventPool(rng);
            List<TrialEvent> ordered = PseudoRandomize(pool, rng);

            int eventsPerBlock = Mathf.CeilToInt((float)ordered.Count / Mathf.Max(1, numberOfBlocks));
            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].index = i;
                ordered[i].blockIndex = Mathf.Min(i / eventsPerBlock, Mathf.Max(0, numberOfBlocks - 1));
            }

            Schedule = new ScheduleData
            {
                seed = seed,
                participantId = string.IsNullOrWhiteSpace(participantId) ? "unknown" : participantId,
                generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                events = ordered
            };

            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[TrialScheduler] Generated {0} car-following events with seed {1}.",
                ordered.Count,
                seed));

            LastValidationReport = ValidateSchedule(Schedule);
            Debug.Log("[TrialScheduler] Validation:\n" + LastValidationReport);
        }

        public List<TrialEvent> GetEventsForBlock(int blockIndex)
        {
            List<TrialEvent> result = new List<TrialEvent>();
            if (Schedule == null || Schedule.events == null)
                return result;

            for (int i = 0; i < Schedule.events.Count; i++)
            {
                TrialEvent evt = Schedule.events[i];
                if (evt != null && evt.blockIndex == blockIndex)
                    result.Add(evt);
            }

            return result;
        }

        public string SaveScheduleToJson(string outputFolder)
        {
            if (Schedule == null)
                return null;

            try
            {
                Directory.CreateDirectory(outputFolder);
                string fileName = string.Format(
                    CultureInfo.InvariantCulture,
                    "schedule_{0}_{1}.json",
                    MakeFileSafe(Schedule.participantId),
                    DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
                string path = Path.Combine(outputFolder, fileName);

                StringBuilder json = new StringBuilder(4096);
                json.AppendLine("{");
                json.AppendFormat(CultureInfo.InvariantCulture, "  \"seed\": {0},\n", Schedule.seed);
                json.AppendFormat("  \"participantId\": \"{0}\",\n", EscapeJson(Schedule.participantId));
                json.AppendFormat("  \"generatedAt\": \"{0}\",\n", EscapeJson(Schedule.generatedAt));
                json.AppendFormat(CultureInfo.InvariantCulture, "  \"totalEvents\": {0},\n", TotalEvents);
                json.AppendLine("  \"events\": [");

                for (int i = 0; i < Schedule.events.Count; i++)
                {
                    TrialEvent evt = Schedule.events[i];
                    json.Append("    { ");
                    json.AppendFormat(CultureInfo.InvariantCulture, "\"index\": {0}, ", evt.index);
                    json.AppendFormat(CultureInfo.InvariantCulture, "\"blockIndex\": {0}, ", evt.blockIndex);
                    json.AppendFormat("\"musicType\": \"{0}\", ", evt.musicType);
                    json.AppendFormat("\"hasLeaderDeceleration\": {0}, ", evt.hasLeaderDeceleration ? "true" : "false");
                    json.AppendFormat(CultureInfo.InvariantCulture, "\"decelerationDelaySeconds\": {0:F1}", evt.decelerationDelaySeconds);
                    json.Append(i < Schedule.events.Count - 1 ? " },\n" : " }\n");
                }

                json.AppendLine("  ]");
                json.AppendLine("}");

                File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
                Debug.Log("[TrialScheduler] Schedule saved: " + path);
                return path;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TrialScheduler] Could not save schedule: " + e.Message);
                return null;
            }
        }

        private List<TrialEvent> BuildEventPool(System.Random rng)
        {
            List<TrialEvent> pool = new List<TrialEvent>();
            MusicEventType[] musicTypes =
            {
                MusicEventType.Sham,
                MusicEventType.SameTempoChange,
                MusicEventType.SlowToFast,
                MusicEventType.FastToSlow
            };

            for (int typeIndex = 0; typeIndex < musicTypes.Length; typeIndex++)
            {
                for (int decel = 0; decel <= 1; decel++)
                {
                    for (int rep = 0; rep < repetitionsPerCell; rep++)
                    {
                        pool.Add(new TrialEvent
                        {
                            musicType = musicTypes[typeIndex],
                            hasLeaderDeceleration = decel == 1,
                            decelerationDelaySeconds = PickDecelerationDelay(rng)
                        });
                    }
                }
            }

            return pool;
        }

        private float PickDecelerationDelay(System.Random rng)
        {
            if (decelerationDelaysSeconds == null || decelerationDelaysSeconds.Length == 0)
                return 3.5f;

            int index = rng.Next(decelerationDelaysSeconds.Length);
            return Mathf.Max(0f, decelerationDelaysSeconds[index]);
        }

        private List<TrialEvent> PseudoRandomize(List<TrialEvent> pool, System.Random rng)
        {
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                List<TrialEvent> candidate = new List<TrialEvent>(pool);
                Shuffle(candidate, rng);
                if (SatisfiesConstraints(candidate))
                    return candidate;
            }

            List<TrialEvent> fallback = new List<TrialEvent>(pool);
            Shuffle(fallback, rng);
            Debug.LogWarning("[TrialScheduler] No perfect constrained order found; using best effort schedule.");
            return fallback;
        }

        private static bool SatisfiesConstraints(List<TrialEvent> events)
        {
            for (int i = 0; i < events.Count - 2; i++)
            {
                if (events[i].musicType == events[i + 1].musicType &&
                    events[i + 1].musicType == events[i + 2].musicType)
                    return false;

                if (events[i].hasLeaderDeceleration &&
                    events[i + 1].hasLeaderDeceleration &&
                    events[i + 2].hasLeaderDeceleration)
                    return false;

                if (!events[i].hasLeaderDeceleration &&
                    !events[i + 1].hasLeaderDeceleration &&
                    !events[i + 2].hasLeaderDeceleration)
                    return false;
            }

            return true;
        }

        public string ValidateSchedule(ScheduleData schedule)
        {
            if (schedule == null || schedule.events == null || schedule.events.Count == 0)
                return "No schedule generated.";

            StringBuilder report = new StringBuilder(1024);
            report.AppendFormat(CultureInfo.InvariantCulture, "events={0}, blocks={1}, seed={2}\n", schedule.events.Count, numberOfBlocks, schedule.seed);

            MusicEventType[] musicTypes =
            {
                MusicEventType.Sham,
                MusicEventType.SameTempoChange,
                MusicEventType.SlowToFast,
                MusicEventType.FastToSlow
            };

            bool ok = true;
            for (int typeIndex = 0; typeIndex < musicTypes.Length; typeIndex++)
            {
                MusicEventType type = musicTypes[typeIndex];
                int noDecel = CountEvents(schedule.events, type, false, -1);
                int decel = CountEvents(schedule.events, type, true, -1);
                report.AppendFormat(CultureInfo.InvariantCulture, "{0}: no_decel={1}, decel={2}\n", type, noDecel, decel);

                if (noDecel != repetitionsPerCell || decel != repetitionsPerCell)
                    ok = false;
            }

            for (int block = 0; block < numberOfBlocks; block++)
            {
                int blockTotal = 0;
                int blockDecel = 0;
                for (int i = 0; i < schedule.events.Count; i++)
                {
                    TrialEvent evt = schedule.events[i];
                    if (evt == null || evt.blockIndex != block)
                        continue;

                    blockTotal++;
                    if (evt.hasLeaderDeceleration)
                        blockDecel++;
                }

                report.AppendFormat(CultureInfo.InvariantCulture, "block {0}: total={1}, decel={2}, no_decel={3}\n", block + 1, blockTotal, blockDecel, blockTotal - blockDecel);
            }

            int longestSameTypeRun = GetLongestMusicTypeRun(schedule.events);
            int longestSameDecelRun = GetLongestDecelerationRun(schedule.events);
            report.AppendFormat(CultureInfo.InvariantCulture, "longest_same_music_type_run={0}\n", longestSameTypeRun);
            report.AppendFormat(CultureInfo.InvariantCulture, "longest_same_decel_state_run={0}\n", longestSameDecelRun);

            if (longestSameTypeRun > 2 || longestSameDecelRun > 2)
                ok = false;

            report.Append(ok ? "status=OK" : "status=CHECK");
            return report.ToString();
        }

        private static int CountEvents(List<TrialEvent> events, MusicEventType type, bool hasDeceleration, int blockIndex)
        {
            int count = 0;
            for (int i = 0; i < events.Count; i++)
            {
                TrialEvent evt = events[i];
                if (evt == null)
                    continue;

                if (evt.musicType == type && evt.hasLeaderDeceleration == hasDeceleration &&
                    (blockIndex < 0 || evt.blockIndex == blockIndex))
                    count++;
            }

            return count;
        }

        private static int GetLongestMusicTypeRun(List<TrialEvent> events)
        {
            int longest = 0;
            int current = 0;
            MusicEventType? previous = null;

            for (int i = 0; i < events.Count; i++)
            {
                TrialEvent evt = events[i];
                if (evt == null)
                    continue;

                current = previous.HasValue && previous.Value == evt.musicType ? current + 1 : 1;
                previous = evt.musicType;
                longest = Mathf.Max(longest, current);
            }

            return longest;
        }

        private static int GetLongestDecelerationRun(List<TrialEvent> events)
        {
            int longest = 0;
            int current = 0;
            bool? previous = null;

            for (int i = 0; i < events.Count; i++)
            {
                TrialEvent evt = events[i];
                if (evt == null)
                    continue;

                current = previous.HasValue && previous.Value == evt.hasLeaderDeceleration ? current + 1 : 1;
                previous = evt.hasLeaderDeceleration;
                longest = Mathf.Max(longest, current);
            }

            return longest;
        }

        private static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }

        private static int GenerateTimeSeed()
        {
            unchecked
            {
                long ticks = DateTime.UtcNow.Ticks;
                int seed = (int)(ticks ^ (ticks >> 32)) ^ Environment.TickCount;
                return seed == 0 ? 1 : Math.Abs(seed);
            }
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }

        private static string MakeFileSafe(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value;
        }
    }
}
