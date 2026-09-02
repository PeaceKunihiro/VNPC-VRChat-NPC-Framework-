#if UNITY_EDITOR
using UdonSharpEditor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VNPC;
using VRC.SDK3.Components;

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
        DrawDialogueWarnings((VNPC_Manager)target);
    }

    private static void DrawDialogueWarnings(VNPC_Manager manager)
    {
        if (manager.dialoguePanel == null)
        {
            EditorGUILayout.HelpBox("共通Dialogue Panelが設定されていません。", MessageType.Warning);
            return;
        }

        Canvas canvas = manager.dialoguePanel.GetComponentInParent<Canvas>();
        if (canvas == null)
            EditorGUILayout.HelpBox("Dialogue PanelはVRC用World Space Canvasの配下へ配置してください。", MessageType.Warning);
        else
        {
            if (canvas.renderMode != RenderMode.WorldSpace)
                EditorGUILayout.HelpBox("Dialogue CanvasのRender ModeはWorld Spaceにしてください。", MessageType.Warning);
            if (canvas.GetComponent<VRCUiShape>() == null)
                EditorGUILayout.HelpBox("Dialogue CanvasにVRC UI Shapeがありません。", MessageType.Warning);
            if (canvas.GetComponent<GraphicRaycaster>() == null)
                EditorGUILayout.HelpBox("Dialogue CanvasにGraphic Raycasterがありません。", MessageType.Warning);
            if (canvas.gameObject.layer == LayerMask.NameToLayer("UI"))
                EditorGUILayout.HelpBox("Dialogue CanvasのLayerはUI以外にしてください。", MessageType.Warning);
            if (manager.dialogueWindow == null)
                EditorGUILayout.HelpBox("Dialogue Window未設定時はPanel自体を移動します。通常はWorld Space CanvasのTransformを指定してください。", MessageType.Info);
        }
        if (Object.FindObjectOfType<EventSystem>() == null)
            EditorGUILayout.HelpBox("SceneにEventSystemがありません。VRC UIの操作にはEventSystemが必要です。", MessageType.Warning);
        if (manager.dialogueText != null && !(manager.dialogueText is TextMeshProUGUI))
            EditorGUILayout.HelpBox("Dialogue TextにはCanvas用のTextMeshProUGUIを指定してください。", MessageType.Warning);
        for (int i = 0; manager.choiceLabels != null && i < manager.choiceLabels.Length; i++)
            if (manager.choiceLabels[i] != null && !(manager.choiceLabels[i] is TextMeshProUGUI))
            {
                EditorGUILayout.HelpBox("Choice LabelsにはCanvas用のTextMeshProUGUIを指定してください。", MessageType.Warning);
                break;
            }
        for (int i = 0; manager.choiceButtons != null && i < manager.choiceButtons.Length; i++)
            if (manager.choiceButtons[i] != null && manager.choiceButtons[i].navigation.mode != Navigation.Mode.None)
            {
                EditorGUILayout.HelpBox("Choice ButtonsのNavigationはNoneにしてください。", MessageType.Warning);
                break;
            }
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
