#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using VNPC;
using VRC.SDK3.Components;

[CustomEditor(typeof(VNPC_Character))]
public class VNPC_CharacterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        VNPC_Character character = (VNPC_Character)target;
        Animator animator = character.GetComponent<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            EditorGUILayout.HelpBox("Humanoid Avatarを持つAnimatorが必要です。", MessageType.Warning);
        if (character.manager == null)
        {
            EditorGUILayout.HelpBox("VNPC_Managerが設定されていません。", MessageType.Warning);
            if (GUILayout.Button("Find / Create VNPC Manager")) FindOrCreateManager(character);
        }
        DrawDefaultInspector();
        if (GUILayout.Button("Validate and Auto Setup")) Setup(character);
    }

    private static void Setup(VNPC_Character character)
    {
        GameObject go = character.gameObject;
        Undo.SetCurrentGroupName("Setup VNPC Character");
        if (go.GetComponent<Animator>() == null) Undo.AddComponent<Animator>(go);
        if (go.GetComponent<NavMeshAgent>() == null) Undo.AddComponent<NavMeshAgent>(go);
        if (go.GetComponent<VRCObjectSync>() == null) Undo.AddComponent<VRCObjectSync>(go);
        if (character.manager == null) FindOrCreateManager(character);
        Register(character);
        EditorUtility.SetDirty(character);
    }

    private static void FindOrCreateManager(VNPC_Character character)
    {
        VNPC_Manager[] managers = Object.FindObjectsOfType<VNPC_Manager>();
        VNPC_Manager manager;
        if (managers.Length == 1) manager = managers[0];
        else
        {
            GameObject go = new GameObject("VNPC_Manager");
            Undo.RegisterCreatedObjectUndo(go, "Create VNPC Manager");
            manager = Undo.AddComponent<VNPC_Manager>(go);
        }
        Undo.RecordObject(character, "Assign VNPC Manager");
        character.manager = manager;
        Register(character);
    }

    private static void Register(VNPC_Character character)
    {
        if (character.manager == null) return;
        VNPC_Character[] old = character.manager.characters ?? new VNPC_Character[0];
        foreach (VNPC_Character item in old) if (item == character) return;
        Undo.RecordObject(character.manager, "Register VNPC Character");
        VNPC_Character[] next = new VNPC_Character[old.Length + 1];
        for (int i = 0; i < old.Length; i++) next[i] = old[i];
        next[old.Length] = character;
        character.manager.characters = next;
        EditorUtility.SetDirty(character.manager);
    }
}
#endif
