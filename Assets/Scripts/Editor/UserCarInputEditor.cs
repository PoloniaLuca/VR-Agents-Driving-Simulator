using UnityEditor;
using UnityEngine;

namespace DrivingSim
{
    [CustomEditor(typeof(UserCarInput))]
    public class UserCarInputEditor : Editor
    {
        private SerializedProperty useNewInputSystem;

        private SerializedProperty steeringAxis;
        private SerializedProperty throttleAxis;
        private SerializedProperty handbrakeButton;
        private SerializedProperty toggleReverseButton;
        private SerializedProperty splitThrottleAndBrake;

        private SerializedProperty steeringAction;
        private SerializedProperty throttleAction;
        private SerializedProperty brakeAction;
        private SerializedProperty handbrakeAction;
        private SerializedProperty toggleReverseAction;

        private bool backendFoldout = true;
        private bool oldInputFoldout = true;
        private bool newInputFoldout = true;

        private void OnEnable()
        {
            useNewInputSystem = serializedObject.FindProperty("useNewInputSystem");

            steeringAxis = serializedObject.FindProperty("steeringAxis");
            throttleAxis = serializedObject.FindProperty("throttleAxis");
            handbrakeButton = serializedObject.FindProperty("handbrakeButton");
            toggleReverseButton = serializedObject.FindProperty("toggleReverseButton");
            splitThrottleAndBrake = serializedObject.FindProperty("splitThrottleAndBrake");

            steeringAction = serializedObject.FindProperty("steeringAction");
            throttleAction = serializedObject.FindProperty("throttleAction");
            brakeAction = serializedObject.FindProperty("brakeAction");
            handbrakeAction = serializedObject.FindProperty("handbrakeAction");
            toggleReverseAction = serializedObject.FindProperty("toggleReverseAction");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour((UserCarInput)target), typeof(UserCarInput), false);
            }

            EditorGUILayout.Space();

            backendFoldout = EditorGUILayout.Foldout(backendFoldout, "Input backend", true);
            if (backendFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(useNewInputSystem, new GUIContent("Use New Input System", "If enabled, uses the new Input System (Input Actions). If disabled, uses the old Input Manager axes/buttons below."));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            oldInputFoldout = EditorGUILayout.Foldout(oldInputFoldout, "Old Input Manager (legacy)", true);
            if (oldInputFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(steeringAxis, new GUIContent("Steering Axis"));
                EditorGUILayout.PropertyField(throttleAxis, new GUIContent("Throttle Axis"));

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Button names", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(handbrakeButton, new GUIContent("Handbrake Button"));
                EditorGUILayout.PropertyField(toggleReverseButton, new GUIContent("Toggle Reverse Button"));

                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(splitThrottleAndBrake, new GUIContent("Split Throttle And Brake", "If true, 'throttleAxis' will be split into separate throttle and brake values."));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            newInputFoldout = EditorGUILayout.Foldout(newInputFoldout, "New Input System (Input Actions)", true);
            if (newInputFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(steeringAction, new GUIContent("Steering Action", "Input Action used for steering. Expected value type: float in range [-1, 1]."));
                EditorGUILayout.PropertyField(throttleAction, new GUIContent("Throttle Action", "Input Action used for throttle. Expected value type: float in range [0, 1]."));
                EditorGUILayout.PropertyField(brakeAction, new GUIContent("Brake Action", "Input Action used for brake. Expected value type: float in range [0, 1]."));

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Button actions", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(handbrakeAction, new GUIContent("Handbrake Action", "Input Action used for handbrake. Expected value type: button."));
                EditorGUILayout.PropertyField(toggleReverseAction, new GUIContent("Toggle Reverse Action", "Input Action used to toggle reverse gear. Expected value type: button."));
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}

