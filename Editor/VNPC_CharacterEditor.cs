#if UNITY_EDITOR
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VNPC;
using VRC.SDK3.Components;

[CustomEditor(typeof(VNPC_Character))]
public class VNPC_CharacterEditor : Editor
{
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

        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script");
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
