using UnityEngine;
using UnityEngine.InputSystem;
using System;

namespace DrivingSim
{
    /// <summary>
    /// Reads input from the player (keyboard, gamepad, etc.)
    /// and exposes it through the ICarInput interface.
    ///
    /// This class is responsible ONLY for collecting input values.
    /// It does not know anything about physics or car behaviour.
    /// </summary>
    public class UserCarInput : MonoBehaviour, ICarInput
    {
        [Header("Input backend")]
        [Tooltip("If enabled, uses the new Input System (Input Actions). If disabled, uses the old Input Manager axes/buttons below.")]
        [SerializeField] private bool useNewInputSystem = true;

        [Header("Old Input Manager (legacy)")]
        [Tooltip("Horizontal axis name for steering (e.g. 'Horizontal')")]
        [SerializeField] private string steeringAxis = "Horizontal";

        [Tooltip("Vertical axis name for throttle/brake (e.g. 'Vertical')")]
        [SerializeField] private string throttleAxis = "Vertical";

        [Header("Button names")]
        [Tooltip("Input button name for handbrake")]
        [SerializeField] private string handbrakeButton = "Jump";

        [Tooltip("Input button name to toggle reverse gear")]
        [SerializeField] private string toggleReverseButton = "Reverse";

        [Header("Axis splitting (old Input Manager only)")]
        [Tooltip("If true, 'throttleAxis' will be split into separate throttle and brake values.")]
        [SerializeField] private bool splitThrottleAndBrake = true;

        [Header("New Input System (Input Actions)")]
        [Tooltip("Input Action used for steering. Expected value type: float in range [-1, 1].")]
        [SerializeField] private InputActionReference steeringAction;

        [Tooltip("Input Action used for throttle. Expected value type: float in range [0, 1].")]
        [SerializeField] private InputActionReference throttleAction;

        [Tooltip("Input Action used for brake. Expected value type: float in range [0, 1].")]
        [SerializeField] private InputActionReference brakeAction;

        [Tooltip("Input Action used for handbrake. Expected value type: button.")]
        [SerializeField] private InputActionReference handbrakeAction;

        [Tooltip("Input Action used to toggle reverse gear. Expected value type: button.")]
        [SerializeField] private InputActionReference toggleReverseAction;

        /// <inheritdoc/>
        public float Steering { get; private set; }

        /// <inheritdoc/>
        public float Throttle { get; private set; }

        /// <inheritdoc/>
        public float Brake { get; private set; }

        /// <inheritdoc/>
        public float Handbrake { get; private set; }

        /// <inheritdoc/>
        public bool ToggleReversePressed { get; private set; }

        private void OnEnable()
        {
            if (useNewInputSystem)
            {
                EnableActions();
            }
        }

        private void OnDisable()
        {
            if (useNewInputSystem)
            {
                DisableActions();
            }
        }

        private void EnableActions()
        {
            steeringAction?.action?.Enable();
            throttleAction?.action?.Enable();
            brakeAction?.action?.Enable();
            handbrakeAction?.action?.Enable();
            toggleReverseAction?.action?.Enable();
        }

        private void DisableActions()
        {
            steeringAction?.action?.Disable();
            throttleAction?.action?.Disable();
            brakeAction?.action?.Disable();
            handbrakeAction?.action?.Disable();
            toggleReverseAction?.action?.Disable();
        }

        private void Update()
        {
            if (useNewInputSystem)
            {
                ReadSteeringNew();
                ReadThrottleAndBrakeNew();
                ReadButtonsNew();
            }
            else
            {
                ReadSteeringOld();
                ReadThrottleAndBrakeOld();
                ReadButtonsOld();
            }
        }

        /// <summary>
        /// Reads steering input using the old Input Manager axis.
        /// </summary>
        private void ReadSteeringOld()
        {
            Steering = SafeGetAxis(steeringAxis);
        }

        /// <summary>
        /// Reads steering input from the new Input System.
        /// </summary>
        private void ReadSteeringNew()
        {
            if (steeringAction != null && steeringAction.action != null)
            {
                Steering = steeringAction.action.ReadValue<float>();
            }
            else
            {
                Steering = 0f;
            }
        }

        /// <summary>
        /// Reads throttle and brake values using the old Input Manager axis.
        /// </summary>
        private void ReadThrottleAndBrakeOld()
        {
            float axis = SafeGetAxis(throttleAxis);

            if (splitThrottleAndBrake)
            {
                // Positive axis = throttle, negative axis = brake.
                Throttle = Mathf.Clamp01(axis);
                Brake = Mathf.Clamp01(-axis);
            }
            else
            {
                // Treat axis as throttle only, brake must be mapped elsewhere if needed.
                Throttle = Mathf.Clamp01(axis);
                Brake = 0f;
            }
        }

        /// <summary>
        /// Reads throttle and brake values from the new Input System.
        /// </summary>
        private void ReadThrottleAndBrakeNew()
        {
            float throttle = 0f;
            float brake = 0f;

            if (throttleAction != null && throttleAction.action != null)
            {
                throttle = Mathf.Clamp01(throttleAction.action.ReadValue<float>());
            }

            if (brakeAction != null && brakeAction.action != null)
            {
                brake = Mathf.Clamp01(brakeAction.action.ReadValue<float>());
            }

            Throttle = throttle;
            Brake = brake;
        }

        /// <summary>
        /// Reads button-like inputs using the old Input Manager.
        /// </summary>
        private void ReadButtonsOld()
        {
            Handbrake = SafeGetButton(handbrakeButton) ? 1f : 0f;

            ToggleReversePressed = SafeGetButtonDown(toggleReverseButton);
        }

        /// <summary>
        /// Safely reads an axis from the legacy Input Manager, returning 0 if the old
        /// system is not available or the axis is not defined.
        /// </summary>
        private float SafeGetAxis(string axisName)
        {
            try
            {
                return Input.GetAxis(axisName);
            }
            catch (InvalidOperationException)
            {
                return 0f;
            }
            catch (ArgumentException)
            {
                return 0f;
            }
        }

        /// <summary>
        /// Safely reads a button from the legacy Input Manager, returning false if the old
        /// system is not available or the button is not defined.
        /// </summary>
        private bool SafeGetButton(string buttonName)
        {
            try
            {
                return Input.GetButton(buttonName);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Safely reads a button-down event from the legacy Input Manager, returning false
        /// if the old system is not available or the button is not defined.
        /// </summary>
        private bool SafeGetButtonDown(string buttonName)
        {
            try
            {
                return Input.GetButtonDown(buttonName);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Reads button-like inputs from the new Input System.
        /// </summary>
        private void ReadButtonsNew()
        {
            Handbrake = (handbrakeAction != null && handbrakeAction.action != null && handbrakeAction.action.IsPressed()) ? 1f : 0f;

            ToggleReversePressed = toggleReverseAction != null && toggleReverseAction.action != null && toggleReverseAction.action.triggered;
        }
    }
}
