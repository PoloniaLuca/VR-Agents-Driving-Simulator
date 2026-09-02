using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ResearchSim
{
    /// <summary>
    /// Utility class for computing response metrics from car-following data.
    /// Intended for post-session analysis. Can be used either at runtime or
    /// as a standalone tool in the editor.
    /// </summary>
    public static class ResponseMetrics
    {
        /// <summary>
        /// Computes the response delay: time between leader deceleration onset
        /// and the first significant participant deceleration.
        /// </summary>
        /// <param name="leaderDecelStartTime">Timestamp when leader began decelerating.</param>
        /// <param name="participantAccelerationSamples">
        /// List of (timestamp, longitudinalAcceleration) samples after the leader decel start.
        /// </param>
        /// <param name="decelerationThreshold">
        /// Acceleration threshold in m/s² (negative value, e.g. -0.3f).
        /// </param>
        /// <returns>Response delay in seconds, or NaN if no response detected.</returns>
        public static float ComputeResponseDelay(
            float leaderDecelStartTime,
            List<(float time, float acceleration)> participantAccelerationSamples,
            float decelerationThreshold = -0.3f)
        {
            if (participantAccelerationSamples == null || participantAccelerationSamples.Count == 0)
                return float.NaN;

            for (int i = 0; i < participantAccelerationSamples.Count; i++)
            {
                var sample = participantAccelerationSamples[i];
                if (sample.time > leaderDecelStartTime && sample.acceleration < decelerationThreshold)
                    return sample.time - leaderDecelStartTime;
            }

            return float.NaN;
        }

        /// <summary>
        /// Computes the response delay based on first brake input.
        /// </summary>
        public static float ComputeBrakeResponseDelay(
            float leaderDecelStartTime,
            List<(float time, float brakeInput)> brakeInputSamples,
            float brakeThreshold = 0.05f)
        {
            if (brakeInputSamples == null || brakeInputSamples.Count == 0)
                return float.NaN;

            for (int i = 0; i < brakeInputSamples.Count; i++)
            {
                var sample = brakeInputSamples[i];
                if (sample.time > leaderDecelStartTime && sample.brakeInput > brakeThreshold)
                    return sample.time - leaderDecelStartTime;
            }

            return float.NaN;
        }

        /// <summary>
        /// Computes the response delay based on first throttle release.
        /// </summary>
        public static float ComputeThrottleReleaseDelay(
            float leaderDecelStartTime,
            List<(float time, float throttleInput)> throttleSamples,
            float releaseThreshold = 0.1f)
        {
            if (throttleSamples == null || throttleSamples.Count == 0)
                return float.NaN;

            // Find the first sample after decel start where throttle drops below threshold
            // (assuming throttle was above threshold before)
            bool wasAboveThreshold = false;

            for (int i = 0; i < throttleSamples.Count; i++)
            {
                var sample = throttleSamples[i];
                if (sample.time <= leaderDecelStartTime)
                {
                    wasAboveThreshold = sample.throttleInput > releaseThreshold;
                    continue;
                }

                if (wasAboveThreshold && sample.throttleInput <= releaseThreshold)
                    return sample.time - leaderDecelStartTime;

                wasAboveThreshold = sample.throttleInput > releaseThreshold;
            }

            return float.NaN;
        }

        /// <summary>
        /// Computes the minimum time headway in a time window after an event.
        /// </summary>
        public static float ComputeMinimumHeadway(
            List<(float time, float headway)> headwaySamples,
            float windowStartTime,
            float windowDuration = 15f)
        {
            float minHeadway = float.MaxValue;
            float windowEnd = windowStartTime + windowDuration;

            if (headwaySamples == null)
                return float.NaN;

            for (int i = 0; i < headwaySamples.Count; i++)
            {
                var sample = headwaySamples[i];
                if (sample.time >= windowStartTime && sample.time <= windowEnd)
                {
                    if (!float.IsNaN(sample.headway) && sample.headway < minHeadway)
                        minHeadway = sample.headway;
                }
            }

            return minHeadway < float.MaxValue ? minHeadway : float.NaN;
        }

        /// <summary>
        /// Computes the Standard Deviation of Lateral Position (SDLP).
        /// </summary>
        public static float ComputeSDLP(
            List<float> lateralPositionSamples)
        {
            if (lateralPositionSamples == null || lateralPositionSamples.Count < 2)
                return float.NaN;

            // Compute mean
            double sum = 0;
            int count = 0;
            for (int i = 0; i < lateralPositionSamples.Count; i++)
            {
                if (!float.IsNaN(lateralPositionSamples[i]))
                {
                    sum += lateralPositionSamples[i];
                    count++;
                }
            }

            if (count < 2)
                return float.NaN;

            double mean = sum / count;

            // Compute variance
            double sumSqDiff = 0;
            for (int i = 0; i < lateralPositionSamples.Count; i++)
            {
                if (!float.IsNaN(lateralPositionSamples[i]))
                {
                    double diff = lateralPositionSamples[i] - mean;
                    sumSqDiff += diff * diff;
                }
            }

            double variance = sumSqDiff / (count - 1);
            return (float)System.Math.Sqrt(variance);
        }

        /// <summary>
        /// Computes the standard deviation of speed in a time window.
        /// </summary>
        public static float ComputeSpeedVariability(
            List<(float time, float speedKmh)> speedSamples,
            float windowStart,
            float windowDuration = 15f)
        {
            List<float> windowSpeeds = new List<float>();
            float windowEnd = windowStart + windowDuration;

            if (speedSamples == null)
                return float.NaN;

            for (int i = 0; i < speedSamples.Count; i++)
            {
                var sample = speedSamples[i];
                if (sample.time >= windowStart && sample.time <= windowEnd)
                    windowSpeeds.Add(sample.speedKmh);
            }

            if (windowSpeeds.Count < 2)
                return float.NaN;

            double sum = 0;
            for (int i = 0; i < windowSpeeds.Count; i++)
                sum += windowSpeeds[i];
            double mean = sum / windowSpeeds.Count;

            double sumSqDiff = 0;
            for (int i = 0; i < windowSpeeds.Count; i++)
            {
                double diff = windowSpeeds[i] - mean;
                sumSqDiff += diff * diff;
            }

            return (float)System.Math.Sqrt(sumSqDiff / (windowSpeeds.Count - 1));
        }

        /// <summary>
        /// Computes steering variability (SD of steering input) in a time window.
        /// </summary>
        public static float ComputeSteeringVariability(
            List<(float time, float steeringInput)> steeringSamples,
            float windowStart,
            float windowDuration = 15f)
        {
            List<float> windowValues = new List<float>();
            float windowEnd = windowStart + windowDuration;

            if (steeringSamples == null)
                return float.NaN;

            for (int i = 0; i < steeringSamples.Count; i++)
            {
                var sample = steeringSamples[i];
                if (sample.time >= windowStart && sample.time <= windowEnd)
                    windowValues.Add(sample.steeringInput);
            }

            if (windowValues.Count < 2)
                return float.NaN;

            double sum = 0;
            for (int i = 0; i < windowValues.Count; i++)
                sum += windowValues[i];
            double mean = sum / windowValues.Count;

            double sumSqDiff = 0;
            for (int i = 0; i < windowValues.Count; i++)
            {
                double diff = windowValues[i] - mean;
                sumSqDiff += diff * diff;
            }

            return (float)System.Math.Sqrt(sumSqDiff / (windowValues.Count - 1));
        }
    }
}
