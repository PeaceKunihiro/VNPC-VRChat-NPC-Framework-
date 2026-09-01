#if UNITY_EDITOR
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VNPC;

[CustomEditor(typeof(VNPC_Manager))]
public class VNPC_ManagerEditor : Editor
{
    private bool pathVisualizationExpanded = true;

    public override void OnInspectorGUI()
    {
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

        serializedObject.Update();
        SerializedProperty paths = serializedObject.FindProperty("paths");
        DrawPropertiesExcluding(serializedObject, "m_Script", "pathSceneColors", "useWaypointMaterialColors");

        SerializedProperty colors = serializedObject.FindProperty("pathSceneColors");
        SerializedProperty materialModes = serializedObject.FindProperty("useWaypointMaterialColors");
        EnsureVisualizationArraySizes(paths.arraySize, colors, materialModes);

        pathVisualizationExpanded = EditorGUILayout.Foldout(pathVisualizationExpanded, "Path Visualization", true);
        if (pathVisualizationExpanded)
        {
            EditorGUI.indentLevel++;
            if (paths.arraySize == 0)
                EditorGUILayout.HelpBox("PathsへPath親Transformを登録すると、Pathごとの表示色を設定できます。", MessageType.Info);
            for (int i = 0; i < paths.arraySize; i++)
            {
                SerializedProperty path = paths.GetArrayElementAtIndex(i);
                SerializedProperty useMaterial = materialModes.GetArrayElementAtIndex(i);
                SerializedProperty color = colors.GetArrayElementAtIndex(i);
                string pathName = path.objectReferenceValue == null ? "未設定" : path.objectReferenceValue.name;

                EditorGUILayout.LabelField("Path " + i + " - " + pathName, EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(useMaterial, new GUIContent("Use Waypoint Material Color"));
                EditorGUILayout.PropertyField(color, new GUIContent("Fallback Color"));
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static void EnsureVisualizationArraySizes(int pathCount, SerializedProperty colors, SerializedProperty materialModes)
    {
        int oldColorCount = colors.arraySize;
        int oldModeCount = materialModes.arraySize;
        colors.arraySize = pathCount;
        materialModes.arraySize = pathCount;

        for (int i = oldColorCount; i < pathCount; i++)
            colors.GetArrayElementAtIndex(i).colorValue = Color.red;
        for (int i = oldModeCount; i < pathCount; i++)
            materialModes.GetArrayElementAtIndex(i).boolValue = true;
    }
}
#endif
