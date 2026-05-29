using UnityEditor;
using UnityEngine;

namespace DrivingSim
{
    [CustomEditor(typeof(CarWheels))]
    public class CarWheelsEditor : Editor
    {
        private SerializedProperty body;
        private SerializedProperty frontLeft;
        private SerializedProperty frontRight;
        private SerializedProperty rearLeft;
        private SerializedProperty rearRight;

        private bool bodyFoldout = true;
        private bool wheelSetupFoldout = true;

        private void OnEnable()
        {
            body = serializedObject.FindProperty("body");
            frontLeft = serializedObject.FindProperty("frontLeft");
            frontRight = serializedObject.FindProperty("frontRight");
            rearLeft = serializedObject.FindProperty("rearLeft");
            rearRight = serializedObject.FindProperty("rearRight");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour((CarWheels)target), typeof(CarWheels), false);
            }

            EditorGUILayout.Space();

            bodyFoldout = EditorGUILayout.Foldout(bodyFoldout, "Car body", true);
            if (bodyFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(body, new GUIContent("Body", "Root transform of the car body (usually the object with the Rigidbody)."));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            wheelSetupFoldout = EditorGUILayout.Foldout(wheelSetupFoldout, "Wheel setup", true);
            if (wheelSetupFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(frontLeft, new GUIContent("Front Left"));
                EditorGUILayout.PropertyField(frontRight, new GUIContent("Front Right"));
                EditorGUILayout.PropertyField(rearLeft, new GUIContent("Rear Left"));
                EditorGUILayout.PropertyField(rearRight, new GUIContent("Rear Right"));
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}

