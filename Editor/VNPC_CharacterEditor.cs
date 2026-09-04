#if UNITY_EDITOR
using UdonSharpEditor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VNPC;

[CustomEditor(typeof(VNPC_Character))]
public class VNPC_CharacterEditor : Editor
{
    private static bool movementExpanded = true;
    private static bool avoidanceExpanded = true;
    private static bool animationsExpanded = true;
    private static bool interactionExpanded = true;
    private bool dialoguePreviewEnabled;
    private GameObject dialoguePreviewObject;
    private int dialoguePreviewSourceId;

    private void OnDisable()
    {
        DestroyDialoguePreview();
    }

    public override void OnInspectorGUI()
    {
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

        VNPC_Character character = (VNPC_Character)target;
        Animator animator = character.GetComponent<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            EditorGUILayout.HelpBox("Humanoid Avatarを持つAnimatorが必要です。", MessageType.Warning);

        VNPC_Manager[] managers = Object.FindObjectsOfType<VNPC_Manager>();
        if (character.manager == null)
        {
            if (managers.Length > 1)
                EditorGUILayout.HelpBox("VNPC_Managerが複数あります。Manager欄へ使用するManagerをD&Dしてください。", MessageType.Warning);
            else
                EditorGUILayout.HelpBox("VNPC_Managerが設定されていません。", MessageType.Warning);
            if (managers.Length == 0 && GUILayout.Button("Create VNPC Manager"))
            {
                VNPC_Manager manager = VNPC_EditorSetup.CreateManager();
                Undo.RecordObject(character, "Assign VNPC Manager");
                character.manager = manager;
                VNPC_EditorSetup.RegisterWithExplicitManager(character);
            }
        }
        if (character.moveStyle == VNPCMoveStyle.PlayerFollow && character.stopDistance > character.followDistance)
            EditorGUILayout.HelpBox("Stop DistanceがFollow Distanceより大きいため、NPCはFollow Distanceまで接近できません。", MessageType.Info);
        if (character.moveStyle == VNPCMoveStyle.LinkageArea && !character.IsLinkageAreaValid())
            EditorGUILayout.HelpBox("Linkage Areaには、自己交差しない3頂点以上の有効なXZ多角形を指定してください。", MessageType.Warning);
        serializedObject.Update();
        EditorGUILayout.LabelField("General", EditorStyles.boldLabel);
        DrawProperty("manager");
        DrawProperty("characterId");

        movementExpanded = EditorGUILayout.Foldout(movementExpanded, "Movement", true);
        if (movementExpanded)
        {
            EditorGUI.indentLevel++;
            DrawProperty("moveStyle");
            DrawProperty("pathId");
            DrawProperty("startIndex");
            DrawProperty("step");
            DrawProperty("moveSpeed");
            DrawProperty("turnSpeed");
            DrawProperty("waitTime");
            DrawProperty("arrivalDistance");
            DrawProperty("areaCenter");
            DrawProperty("areaRadius");
            DrawProperty("areaDirectionCount");
            DrawProperty("linkageArea");
            DrawProperty("linkageCandidateCount");
            DrawProperty("followDistance");
            DrawProperty("followSearchDistance");
            DrawProperty("followSearchAngle");
            EditorGUI.indentLevel--;
        }

        avoidanceExpanded = EditorGUILayout.Foldout(avoidanceExpanded, "Player Avoidance", true);
        if (avoidanceExpanded)
        {
            EditorGUI.indentLevel++;
            DrawProperty("stopDistance");
            EditorGUI.indentLevel--;
        }

        animationsExpanded = EditorGUILayout.Foldout(animationsExpanded, "Animations", true);
        if (animationsExpanded)
        {
            EditorGUI.indentLevel++;
            DrawProperty("idleAnimation");
            DrawProperty("walkAnimation");
            DrawProperty("runAnimation");
            DrawProperty("walkSpeedReference");
            DrawProperty("runSpeedReference");
            DrawProperty("idleEnterSpeed");
            DrawProperty("idleExitSpeed");
            DrawProperty("speedSmoothing");
            EditorGUI.indentLevel--;
        }

        interactionExpanded = EditorGUILayout.Foldout(interactionExpanded, "Player Interaction", true);
        if (interactionExpanded)
        {
            EditorGUI.indentLevel++;
            DrawProperty("lookAtPlayer");
            DrawProperty("lookDistance");
            DrawProperty("lookWeight");
            DrawProperty("maxLookYaw");
            DrawProperty("dialogueAnchor");
            DrawProperty("dialogueOffset");
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.LabelField("Dialogue", EditorStyles.boldLabel);
        DrawProperty("messages");
        DrawProperty("messageChoiceStarts");
        DrawProperty("messageChoiceCounts");
        DrawProperty("choiceTexts");
        DrawProperty("choiceNextMessages");
        DrawProperty("choiceCommands");
        DrawProperty("choiceParameters");
        DrawProperty("commandObjects");
        serializedObject.ApplyModifiedProperties();

        DrawDialoguePositionPreview(character);

        DrawImportedReferences(character);
        EditorGUILayout.Space();
        if (GUILayout.Button("Validate and Auto Setup")) VNPC_EditorSetup.Setup(character);
        if (GUILayout.Button("Generate / Rebuild Animator")) VNPC_AnimatorBuilder.GenerateOrRebuild(character);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Portable Settings", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Export .vnpc")) VNPC_PresetUtility.Export(character);
        if (GUILayout.Button("Import .vnpc")) VNPC_PresetUtility.Import(character);
        EditorGUILayout.EndHorizontal();
    }

    private void OnSceneGUI()
    {
        if (!dialoguePreviewEnabled || Application.isPlaying)
        {
            DestroyDialoguePreview();
            return;
        }
        VNPC_Character character = (VNPC_Character)target;
        DrawDialoguePositionHandle(character);
        UpdateDialoguePreview(character, SceneView.currentDrawingSceneView);
    }

    private void DrawDialoguePositionPreview(VNPC_Character character)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Dialogue Position Preview", EditorStyles.boldLabel);

        if (character.dialogueAnchor == null)
        {
            if (GUILayout.Button("Create Dialogue Anchor"))
            {
                GameObject anchorObject = new GameObject("DialogueAnchor");
                Undo.RegisterCreatedObjectUndo(anchorObject, "Create Dialogue Anchor");
                anchorObject.transform.SetParent(character.transform, false);
                anchorObject.transform.localPosition = character.dialogueOffset;
                Undo.RecordObject(character, "Assign Dialogue Anchor");
                character.dialogueAnchor = anchorObject.transform;
                character.dialogueOffset = Vector3.zero;
                EditorUtility.SetDirty(character);
                EditorGUIUtility.PingObject(anchorObject);
            }
        }

        bool nextPreview = GUILayout.Toggle(dialoguePreviewEnabled, "Preview Dialogue Window", "Button");
        if (nextPreview != dialoguePreviewEnabled)
        {
            dialoguePreviewEnabled = nextPreview;
            if (!dialoguePreviewEnabled) DestroyDialoguePreview();
            SceneView.RepaintAll();
        }

        if (!dialoguePreviewEnabled) return;
        if (character.manager == null || character.manager.dialogueWindow == null)
            EditorGUILayout.HelpBox("ManagerのDialogue Windowを設定または自動生成するとプレビューできます。", MessageType.Warning);
        else
            EditorGUILayout.HelpBox("Sceneビューカメラへ向けたEditor専用複製です。SceneおよびBuildには保存されません。", MessageType.Info);
    }

    private void UpdateDialoguePreview(VNPC_Character character, SceneView sceneView)
    {
        if (character == null || character.manager == null || character.manager.dialogueWindow == null)
        {
            DestroyDialoguePreview();
            return;
        }

        GameObject source = character.manager.dialogueWindow.gameObject;
        if (dialoguePreviewObject == null || dialoguePreviewSourceId != source.GetInstanceID())
        {
            DestroyDialoguePreview();
            dialoguePreviewObject = Instantiate(source);
            dialoguePreviewObject.name = "VNPC_DialoguePreview";
            SetPreviewHideFlags(dialoguePreviewObject.transform);
            GraphicRaycaster[] raycasters = dialoguePreviewObject.GetComponentsInChildren<GraphicRaycaster>(true);
            for (int i = 0; i < raycasters.Length; i++) raycasters[i].enabled = false;
            dialoguePreviewObject.SetActive(true);
            dialoguePreviewSourceId = source.GetInstanceID();
        }

        TMP_Text previewText = FindPreviewDialogueText(dialoguePreviewObject);
        if (previewText != null)
            previewText.text = character.messages != null && character.messages.Length > 0
                ? character.messages[0]
                : "Dialogue Preview\n01234567890123456789\nLine 3";

        Vector3 position = character.GetDialoguePosition();
        dialoguePreviewObject.transform.position = position;
        if (sceneView != null && sceneView.camera != null)
        {
            Vector3 direction = sceneView.camera.transform.position - position;
            if (direction.sqrMagnitude > 0.0001f)
                dialoguePreviewObject.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up)
                    * Quaternion.Euler(character.manager.dialogueFacingOffset);
        }
    }

    private static TMP_Text FindPreviewDialogueText(GameObject preview)
    {
        TMP_Text[] texts = preview.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
            if (texts[i].name == "DialogueText") return texts[i];
        return texts.Length == 0 ? null : texts[0];
    }

    private static void SetPreviewHideFlags(Transform root)
    {
        root.gameObject.hideFlags = HideFlags.HideAndDontSave;
        for (int i = 0; i < root.childCount; i++) SetPreviewHideFlags(root.GetChild(i));
    }

    private static void DrawDialoguePositionHandle(VNPC_Character character)
    {
        Vector3 current = character.GetDialoguePosition();
        EditorGUI.BeginChangeCheck();
        Vector3 next = Handles.PositionHandle(current, Quaternion.identity);
        if (!EditorGUI.EndChangeCheck()) return;

        if (character.dialogueAnchor != null)
        {
            Undo.RecordObject(character, "Move Dialogue Preview");
            character.dialogueOffset = character.dialogueAnchor.InverseTransformPoint(next);
            EditorUtility.SetDirty(character);
        }
        else
        {
            Undo.RecordObject(character, "Move Dialogue Preview");
            character.dialogueOffset = next - character.transform.position;
            EditorUtility.SetDirty(character);
        }
    }

    private void DestroyDialoguePreview()
    {
        if (dialoguePreviewObject != null) DestroyImmediate(dialoguePreviewObject);
        dialoguePreviewObject = null;
        dialoguePreviewSourceId = 0;
    }

    private void DrawProperty(string propertyName)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null) EditorGUILayout.PropertyField(property, true);
    }

    private static void DrawImportedReferences(VNPC_Character character)
    {
        if (string.IsNullOrEmpty(character.importedIdleAsset) && string.IsNullOrEmpty(character.importedWalkAsset) && string.IsNullOrEmpty(character.importedRunAsset)) return;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Imported Animation References", EditorStyles.boldLabel);
        DrawReference("Idle", character.importedIdleAsset, character.importedIdleClip);
        DrawReference("Walk", character.importedWalkAsset, character.importedWalkClip);
        DrawReference("Run", character.importedRunAsset, character.importedRunClip);
    }

    private static void DrawReference(string role, string asset, string clip)
    {
        if (string.IsNullOrEmpty(asset) && string.IsNullOrEmpty(clip)) return;
        EditorGUILayout.LabelField(role, asset + " / " + clip);
    }
}
#endif
