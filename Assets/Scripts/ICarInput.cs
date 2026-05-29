using UnityEngine;

namespace DrivingSim
{
    /// <summary>
    /// Abstraction over any car input source.
    /// Implementations can read from user controls, AI, replay data, etc.
    /// </summary>
    public interface ICarInput
    {
        /// <summary>
        /// Steering input in the range [-1, 1].
        /// Negative = steer left, positive = steer right.
        /// </summary>
        float Steering { get; }

        /// <summary>
        /// Throttle input in the range [0, 1].
        /// Represents how much the driver is pressing the accelerator.
        /// </summary>
        float Throttle { get; }

        /// <summary>
        /// Brake input in the range [0, 1].
        /// Represents how much the driver is pressing the brake pedal.
        /// </summary>
        float Brake { get; }

        /// <summary>
        /// Handbrake input in the range [0, 1].
        /// </summary>
        float Handbrake { get; }

        /// <summary>
        /// Returns true while the user requests toggling reverse gear.
        /// Can be ignored by implementations that do not support it.
        /// </summary>
        bool ToggleReversePressed { get; }
    }
}
