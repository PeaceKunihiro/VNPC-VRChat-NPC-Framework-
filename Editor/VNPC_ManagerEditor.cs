#if UNITY_EDITOR
using UdonSharpEditor;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VNPC;
using VRC.SDK3.Components;

[CustomEditor(typeof(VNPC_Manager))]
public class VNPC_ManagerEditor : Editor
{
    private const int ChoiceButtonCount = 8;
    private const float DialogueFontSize = 32f;
    private const float DialogueWidth = 680f;
    private const float DialogueHeight = 152f;
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
        VNPC_Manager manager = (VNPC_Manager)target;
        DrawDialogueWarnings(manager);
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(manager.dialogueWindow != null))
            if (GUILayout.Button("Create Shared Dialogue UI")) CreateSharedDialogueUI(manager);
        if (manager.dialogueWindow != null)
            EditorGUILayout.HelpBox("既存UIを保持するため自動生成を無効にしています。再生成する場合はDialogue Window参照を解除してから実行してください。", MessageType.Info);
    }

    private static void DrawDialogueWarnings(VNPC_Manager manager)
    {
        if (manager.dialogueWindow == null)
        {
            EditorGUILayout.HelpBox("共通Dialogue Windowが設定されていません。", MessageType.Warning);
            return;
        }

        Canvas canvas = manager.dialogueWindow.GetComponentInParent<Canvas>();
        if (canvas == null)
            EditorGUILayout.HelpBox("Dialogue WindowはVRC用World Space Canvasにしてください。", MessageType.Warning);
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

    private static void CreateSharedDialogueUI(VNPC_Manager manager)
    {
        Undo.SetCurrentGroupName("Create VNPC Shared Dialogue UI");
        GameObject root = CreateUIObject("VNPC_DialogueWindow", manager.transform);
        Canvas canvas = Undo.AddComponent<Canvas>(root);
        canvas.renderMode = RenderMode.WorldSpace;
        Undo.AddComponent<CanvasScaler>(root);
        Undo.AddComponent<GraphicRaycaster>(root);
        Undo.AddComponent<VRCUiShape>(root);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(DialogueWidth, DialogueHeight + 420f);
        rootRect.localScale = Vector3.one * 0.002f;

        GameObject messageArea = CreateUIObject("MessageArea", root.transform);
        RectTransform messageRect = messageArea.GetComponent<RectTransform>();
        SetTopRect(messageRect, DialogueWidth, DialogueHeight, 0f);
        Image background = Undo.AddComponent<Image>(messageArea);
        background.color = new Color(0.05f, 0.05f, 0.07f, 0.88f);
        Outline outline = Undo.AddComponent<Outline>(messageArea);
        outline.effectColor = new Color(0.75f, 0.85f, 1f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);
        ScrollRect scrollRect = Undo.AddComponent<ScrollRect>(messageArea);
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 24f;

        GameObject viewport = CreateUIObject("Viewport", messageArea.transform);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        SetStretchRect(viewportRect, 12f, 12f, 12f, 12f);
        Image viewportImage = Undo.AddComponent<Image>(viewport);
        viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
        Undo.AddComponent<RectMask2D>(viewport);

        GameObject textObject = CreateUIObject("DialogueText", viewport.transform);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = Vector2.zero;
        TextMeshProUGUI dialogueText = Undo.AddComponent<TextMeshProUGUI>(textObject);
        dialogueText.text = "";
        dialogueText.fontSize = DialogueFontSize;
        dialogueText.color = Color.white;
        dialogueText.enableWordWrapping = true;
        dialogueText.overflowMode = TextOverflowModes.Overflow;
        dialogueText.alignment = TextAlignmentOptions.TopLeft;
        dialogueText.raycastTarget = false;
        dialogueText.margin = new Vector4(6f, 4f, 26f, 4f);
        ContentSizeFitter textFitter = Undo.AddComponent<ContentSizeFitter>(textObject);
        textFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Scrollbar scrollbar = CreateVerticalScrollbar(messageArea.transform);
        scrollRect.viewport = viewportRect;
        scrollRect.content = textRect;
        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        scrollRect.verticalScrollbarSpacing = 4f;

        GameObject choicesObject = CreateUIObject("ChoiceContainer", root.transform);
        RectTransform choicesRect = choicesObject.GetComponent<RectTransform>();
        SetTopRect(choicesRect, DialogueWidth, 400f, -(DialogueHeight + 8f));
        VerticalLayoutGroup layout = Undo.AddComponent<VerticalLayoutGroup>(choicesObject);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        Button[] buttons = new Button[ChoiceButtonCount];
        TMP_Text[] labels = new TMP_Text[ChoiceButtonCount];
        for (int i = 0; i < ChoiceButtonCount; i++)
        {
            GameObject buttonObject = CreateUIObject("ChoiceButton" + i, choicesObject.transform);
            Image buttonImage = Undo.AddComponent<Image>(buttonObject);
            buttonImage.color = new Color(0.14f, 0.17f, 0.22f, 0.95f);
            Button button = Undo.AddComponent<Button>(buttonObject);
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            LayoutElement element = Undo.AddComponent<LayoutElement>(buttonObject);
            element.preferredHeight = 44f;

            GameObject labelObject = CreateUIObject("Label", buttonObject.transform);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            SetStretchRect(labelRect, 10f, 10f, 4f, 4f);
            TextMeshProUGUI label = Undo.AddComponent<TextMeshProUGUI>(labelObject);
            label.text = "";
            label.fontSize = 26f;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            AddChoiceListener(button, manager, i);
            buttons[i] = button;
            labels[i] = label;
            buttonObject.SetActive(false);
        }

        if (Object.FindObjectOfType<EventSystem>() == null)
        {
            GameObject eventSystemObject = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(eventSystemObject, "Create VNPC EventSystem");
            Undo.AddComponent<EventSystem>(eventSystemObject);
            Undo.AddComponent<StandaloneInputModule>(eventSystemObject);
        }

        Undo.RecordObject(manager, "Assign VNPC Shared Dialogue UI");
        manager.dialogueWindow = root.transform;
        manager.dialogueText = dialogueText;
        manager.dialogueScrollRect = scrollRect;
        manager.choiceButtons = buttons;
        manager.choiceLabels = labels;
        EditorUtility.SetDirty(manager);
        root.SetActive(false);
        Selection.activeGameObject = root;
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject result = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(result, "Create " + name);
        result.transform.SetParent(parent, false);
        return result;
    }

    private static Scrollbar CreateVerticalScrollbar(Transform parent)
    {
        GameObject scrollbarObject = CreateUIObject("VerticalScrollbar", parent);
        RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.anchoredPosition = new Vector2(-6f, 0f);
        scrollbarRect.sizeDelta = new Vector2(18f, -12f);
        Image background = Undo.AddComponent<Image>(scrollbarObject);
        background.color = new Color(0f, 0f, 0f, 0.35f);
        Scrollbar scrollbar = Undo.AddComponent<Scrollbar>(scrollbarObject);
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        GameObject slidingArea = CreateUIObject("Sliding Area", scrollbarObject.transform);
        RectTransform slidingRect = slidingArea.GetComponent<RectTransform>();
        SetStretchRect(slidingRect, 2f, 2f, 2f, 2f);
        GameObject handleObject = CreateUIObject("Handle", slidingArea.transform);
        RectTransform handleRect = handleObject.GetComponent<RectTransform>();
        SetStretchRect(handleRect, 0f, 0f, 0f, 0f);
        Image handleImage = Undo.AddComponent<Image>(handleObject);
        handleImage.color = new Color(0.75f, 0.85f, 1f, 0.95f);
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handleImage;
        return scrollbar;
    }

    private static void SetTopRect(RectTransform rect, float width, float height, float y)
    {
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void SetStretchRect(RectTransform rect, float left, float right, float top, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void AddChoiceListener(Button button, VNPC_Manager manager, int index)
    {
        if (index == 0) UnityEventTools.AddPersistentListener(button.onClick, manager.SelectChoice0);
        else if (index == 1) UnityEventTools.AddPersistentListener(button.onClick, manager.SelectChoice1);
        else if (index == 2) UnityEventTools.AddPersistentListener(button.onClick, manager.SelectChoice2);
        else if (index == 3) UnityEventTools.AddPersistentListener(button.onClick, manager.SelectChoice3);
        else if (index == 4) UnityEventTools.AddPersistentListener(button.onClick, manager.SelectChoice4);
        else if (index == 5) UnityEventTools.AddPersistentListener(button.onClick, manager.SelectChoice5);
        else if (index == 6) UnityEventTools.AddPersistentListener(button.onClick, manager.SelectChoice6);
        else if (index == 7) UnityEventTools.AddPersistentListener(button.onClick, manager.SelectChoice7);
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
