using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ResearchSim
{
    public sealed class HybridVehicleInput : MonoBehaviour
    {
        [Header("Input System")]
        public InputActionAsset actionsAsset;
        public string actionMapName = "Driving";
        public string steeringActionName = "Steering";
        public string throttleActionName = "Throttle";
        public string brakeActionName = "Brake";

        [Header("RCC Bridge")]
        public bool applyToRccByReflection = true;
        public MonoBehaviour rccControllerOverride;

        public float Steering { get; private set; }
        public float Throttle { get; private set; }
        public float Brake { get; private set; }

        private InputActionMap runtimeActionMap;
        private InputAction steeringAction;
        private InputAction throttleAction;
        private InputAction brakeAction;
        private MonoBehaviour rccController;

        private void OnEnable()
        {
            ResolveActions();
            EnableAction(steeringAction);
            EnableAction(throttleAction);
            EnableAction(brakeAction);
        }

        private void OnDisable()
        {
            DisableAction(steeringAction);
            DisableAction(throttleAction);
            DisableAction(brakeAction);

            if (runtimeActionMap != null)
            {
                runtimeActionMap.Dispose();
                runtimeActionMap = null;
            }
        }

        private void Update()
        {
            RefreshInputValues();

            if (applyToRccByReflection)
                ApplyInputToRcc();
        }

        public void RefreshInputValues()
        {
            float steering = ReadAction(steeringAction);
            float throttle = Mathf.Clamp01(ReadAction(throttleAction));
            float brake = Mathf.Clamp01(ReadAction(brakeAction));

#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                float keyboardSteering = 0f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                    keyboardSteering -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                    keyboardSteering += 1f;

                steering = SelectStrongestAxis(steering, keyboardSteering);
                throttle = Mathf.Max(throttle, keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f);
                brake = Mathf.Max(brake, keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                steering = SelectStrongestAxis(steering, gamepad.leftStick.x.ReadValue());
                throttle = Mathf.Max(throttle, gamepad.rightTrigger.ReadValue());
                brake = Mathf.Max(brake, gamepad.leftTrigger.ReadValue());
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            steering = SelectStrongestAxis(steering, Input.GetAxisRaw("Horizontal"));
            float vertical = Input.GetAxisRaw("Vertical");
            throttle = Mathf.Max(throttle, Mathf.Clamp01(vertical));
            bool legacyBrakeKeyPressed = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
            brake = Mathf.Max(brake, legacyBrakeKeyPressed ? Mathf.Clamp01(-vertical) : 0f);
#endif

            Steering = Mathf.Clamp(steering, -1f, 1f);
            Throttle = throttle;
            Brake = brake;
        }

        private void ResolveActions()
        {
            if (actionsAsset != null)
            {
                InputActionMap map = actionsAsset.FindActionMap(actionMapName, false);
                if (map != null)
                {
                    steeringAction = map.FindAction(steeringActionName, false);
                    throttleAction = map.FindAction(throttleActionName, false);
                    brakeAction = map.FindAction(brakeActionName, false);
                    return;
                }
            }

            CreateRuntimeFallbackActions();
        }

        private void CreateRuntimeFallbackActions()
        {
            runtimeActionMap = new InputActionMap("DrivingRuntimeFallback");

            steeringAction = runtimeActionMap.AddAction("Steering", InputActionType.Value, expectedControlLayout: "Axis");
            steeringAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Negative", "<Keyboard>/leftArrow")
                .With("Positive", "<Keyboard>/d")
                .With("Positive", "<Keyboard>/rightArrow");
            steeringAction.AddBinding("<Gamepad>/leftStick/x");
            steeringAction.AddBinding("<Joystick>/stick/x");

            throttleAction = runtimeActionMap.AddAction("Throttle", InputActionType.Value, expectedControlLayout: "Axis");
            throttleAction.AddBinding("<Keyboard>/w");
            throttleAction.AddBinding("<Keyboard>/upArrow");
            throttleAction.AddBinding("<Gamepad>/rightTrigger");
            throttleAction.AddBinding("<Joystick>/trigger");

            brakeAction = runtimeActionMap.AddAction("Brake", InputActionType.Value, expectedControlLayout: "Axis");
            brakeAction.AddBinding("<Keyboard>/s");
            brakeAction.AddBinding("<Keyboard>/downArrow");
            brakeAction.AddBinding("<Gamepad>/leftTrigger");

            runtimeActionMap.Enable();
        }

        private static void EnableAction(InputAction action)
        {
            if (action != null && !action.enabled)
                action.Enable();
        }

        private static void DisableAction(InputAction action)
        {
            if (action != null && action.enabled)
                action.Disable();
        }

        private static float ReadAction(InputAction action)
        {
            if (action == null)
                return 0f;

            try
            {
                return action.ReadValue<float>();
            }
            catch
            {
                return 0f;
            }
        }

        private static float SelectStrongestAxis(float current, float candidate)
        {
            return Mathf.Abs(candidate) > Mathf.Abs(current) ? candidate : current;
        }

        private void ApplyInputToRcc()
        {
            if (rccController == null)
                rccController = ResolveRccController();

            if (rccController == null)
                return;

            SetFloatMember(rccController, Steering, "steerInput", "steeringInput", "horizontalInput");
            SetFloatMember(rccController, Throttle, "throttleInput", "gasInput", "accelInput", "accelerationInput", "verticalInput");
            SetFloatMember(rccController, Brake, "brakeInput", "footBrakeInput");
        }

        private MonoBehaviour ResolveRccController()
        {
            if (rccControllerOverride != null)
                return rccControllerOverride;

            MonoBehaviour[] behaviours = GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour == this)
                    continue;

                string typeName = behaviour.GetType().FullName;
                if (typeName.Contains("RCC") || typeName.Contains("RealisticCarController") || typeName.Contains("BoneCracker"))
                    return behaviour;
            }

            return null;
        }

        private static void SetFloatMember(object target, float value, params string[] memberNames)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            System.Type type = target.GetType();

            for (int i = 0; i < memberNames.Length; i++)
            {
                PropertyInfo property = type.GetProperty(memberNames[i], Flags);
                if (property != null && property.CanWrite && IsNumericType(property.PropertyType))
                {
                    property.SetValue(target, System.Convert.ChangeType(value, property.PropertyType));
                    return;
                }

                FieldInfo field = type.GetField(memberNames[i], Flags);
                if (field != null && IsNumericType(field.FieldType))
                {
                    field.SetValue(target, System.Convert.ChangeType(value, field.FieldType));
                    return;
                }
            }
        }

        private static bool IsNumericType(System.Type type)
        {
            return type == typeof(float) || type == typeof(double) || type == typeof(int);
        }
    }
}
