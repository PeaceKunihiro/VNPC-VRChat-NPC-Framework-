#if UNITY_EDITOR
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VNPC;
using VRC.SDK3.Components;

[CustomEditor(typeof(VNPC_Character))]
public class VNPC_CharacterEditor : Editor
{
    private static bool movementExpanded = true;
    private static bool avoidanceExpanded = true;
    private static bool animationsExpanded = true;
    private static bool interactionExpanded = true;

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
                character.ApplyProxyModifications();
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
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.LabelField("Dialogue UI (local only)", EditorStyles.boldLabel);
        DrawProperty("dialoguePanel");
        DrawProperty("dialogueText");
        DrawProperty("choiceButtons");
        DrawProperty("choiceLabels");
        DrawProperty("messages");
        DrawProperty("messageChoiceStarts");
        DrawProperty("messageChoiceCounts");
        DrawProperty("choiceTexts");
        DrawProperty("choiceNextMessages");
        DrawProperty("choiceCommands");
        DrawProperty("choiceParameters");
        DrawProperty("commandObjects");
        serializedObject.ApplyModifiedProperties();

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
