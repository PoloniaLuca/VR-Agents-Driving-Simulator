using UnityEditor;
using UnityEngine;

namespace DrivingSim
{
    [CustomEditor(typeof(CarController))]
    public class CarControllerEditor : Editor
    {
        private SerializedProperty inputSource;

        private SerializedProperty frontLeftWheelCollider;
        private SerializedProperty frontRightWheelCollider;
        private SerializedProperty rearLeftWheelCollider;
        private SerializedProperty rearRightWheelCollider;

        private SerializedProperty frontLeftWheelMesh;
        private SerializedProperty frontRightWheelMesh;
        private SerializedProperty rearLeftWheelMesh;
        private SerializedProperty rearRightWheelMesh;

        private SerializedProperty maxMotorTorque;
        private SerializedProperty maxBrakeTorque;
        private SerializedProperty maxHandbrakeTorque;
        private SerializedProperty maxSteeringAngle;

        private SerializedProperty frontWheelDrive;
        private SerializedProperty rearWheelDrive;

        private SerializedProperty enableSpeedLimit;
        private SerializedProperty maxDisplaySpeedKmh;
        private SerializedProperty limitHardness;

        private bool referencesFoldout = true;
        private bool wheelCollidersFoldout = true;
        private bool wheelVisualsFoldout = true;
        private bool engineDrivetrainFoldout = true;
        private bool driveConfigFoldout = true;
        private bool speedLimitFoldout = true;

        private void OnEnable()
        {
            inputSource = serializedObject.FindProperty("inputSource");

            frontLeftWheelCollider = serializedObject.FindProperty("frontLeftWheelCollider");
            frontRightWheelCollider = serializedObject.FindProperty("frontRightWheelCollider");
            rearLeftWheelCollider = serializedObject.FindProperty("rearLeftWheelCollider");
            rearRightWheelCollider = serializedObject.FindProperty("rearRightWheelCollider");

            frontLeftWheelMesh = serializedObject.FindProperty("frontLeftWheelMesh");
            frontRightWheelMesh = serializedObject.FindProperty("frontRightWheelMesh");
            rearLeftWheelMesh = serializedObject.FindProperty("rearLeftWheelMesh");
            rearRightWheelMesh = serializedObject.FindProperty("rearRightWheelMesh");

            maxMotorTorque = serializedObject.FindProperty("maxMotorTorque");
            maxBrakeTorque = serializedObject.FindProperty("maxBrakeTorque");
            maxHandbrakeTorque = serializedObject.FindProperty("maxHandbrakeTorque");
            maxSteeringAngle = serializedObject.FindProperty("maxSteeringAngle");

            frontWheelDrive = serializedObject.FindProperty("frontWheelDrive");
            rearWheelDrive = serializedObject.FindProperty("rearWheelDrive");
            
            enableSpeedLimit = serializedObject.FindProperty("enableSpeedLimit");
            maxDisplaySpeedKmh = serializedObject.FindProperty("maxDisplaySpeedKmh");
            limitHardness = serializedObject.FindProperty("limitHardness");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour((CarController)target), typeof(CarController), false);
            }

            EditorGUILayout.Space();

            referencesFoldout = EditorGUILayout.Foldout(referencesFoldout, "References", true);
            if (referencesFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(inputSource, new GUIContent("Input Source", "Source of input for this car. Must implement ICarInput."));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            wheelCollidersFoldout = EditorGUILayout.Foldout(wheelCollidersFoldout, "Wheel colliders", true);
            if (wheelCollidersFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(frontLeftWheelCollider, new GUIContent("Front Left Wheel Collider"));
                EditorGUILayout.PropertyField(frontRightWheelCollider, new GUIContent("Front Right Wheel Collider"));
                EditorGUILayout.PropertyField(rearLeftWheelCollider, new GUIContent("Rear Left Wheel Collider"));
                EditorGUILayout.PropertyField(rearRightWheelCollider, new GUIContent("Rear Right Wheel Collider"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            wheelVisualsFoldout = EditorGUILayout.Foldout(wheelVisualsFoldout, "Wheel visuals", true);
            if (wheelVisualsFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(frontLeftWheelMesh, new GUIContent("Front Left Wheel Mesh"));
                EditorGUILayout.PropertyField(frontRightWheelMesh, new GUIContent("Front Right Wheel Mesh"));
                EditorGUILayout.PropertyField(rearLeftWheelMesh, new GUIContent("Rear Left Wheel Mesh"));
                EditorGUILayout.PropertyField(rearRightWheelMesh, new GUIContent("Rear Right Wheel Mesh"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            engineDrivetrainFoldout = EditorGUILayout.Foldout(engineDrivetrainFoldout, "Engine & drivetrain", true);
            if (engineDrivetrainFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(maxMotorTorque, new GUIContent("Max Motor Torque"));
                EditorGUILayout.PropertyField(maxBrakeTorque, new GUIContent("Max Brake Torque"));
                EditorGUILayout.PropertyField(maxHandbrakeTorque, new GUIContent("Max Handbrake Torque"));
                EditorGUILayout.PropertyField(maxSteeringAngle, new GUIContent("Max Steering Angle"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            driveConfigFoldout = EditorGUILayout.Foldout(driveConfigFoldout, "Drive configuration", true);
            if (driveConfigFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(frontWheelDrive, new GUIContent("Front Wheel Drive"));
                EditorGUILayout.PropertyField(rearWheelDrive, new GUIContent("Rear Wheel Drive"));
                EditorGUI.indentLevel--;
            }

            
            EditorGUILayout.Space();
            speedLimitFoldout = EditorGUILayout.Foldout(speedLimitFoldout, "Speed Limit Settings", true);
            if (speedLimitFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(enableSpeedLimit);
                EditorGUILayout.PropertyField(maxDisplaySpeedKmh, new GUIContent("Max Speed (km/h)"));
                EditorGUILayout.PropertyField(limitHardness);
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}

