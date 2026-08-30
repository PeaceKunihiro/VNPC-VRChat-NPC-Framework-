#if UNITY_EDITOR
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VNPC;
using VRC.SDK3.Components;

public static class VNPC_EditorSetup
{
    public static void Setup(VNPC_Character character)
    {
        if (character == null) return;
        GameObject go = character.gameObject;
        Undo.SetCurrentGroupName("Setup VNPC Character");
        if (go.GetComponent<Animator>() == null) Undo.AddComponent<Animator>(go);
        if (go.GetComponent<VRCObjectSync>() == null) Undo.AddComponent<VRCObjectSync>(go);

        VNPC_Manager[] managers = Object.FindObjectsOfType<VNPC_Manager>();
        if (character.manager == null && managers.Length == 1)
        {
            Undo.RecordObject(character, "Assign VNPC Manager");
            character.manager = managers[0];
            character.ApplyProxyModifications();
        }
        RegisterWithExplicitManager(character);
        EditorUtility.SetDirty(character);
    }

    public static VNPC_Manager CreateManager()
    {
        GameObject go = new GameObject("VNPC_Manager");
        Undo.RegisterCreatedObjectUndo(go, "Create VNPC Manager");
        return UdonSharpUndo.AddComponent<VNPC_Manager>(go);
    }

    public static void RegisterWithExplicitManager(VNPC_Character character)
    {
        if (character == null || character.manager == null) return;
        VNPC_Character[] old = character.manager.characters ?? new VNPC_Character[0];
        EnsureUniqueCharacterId(character, old);
        for (int i = 0; i < old.Length; i++) if (old[i] == character)
        {
            character.ApplyProxyModifications();
            return;
        }
        Undo.RecordObject(character.manager, "Register VNPC Character");
        VNPC_Character[] next = new VNPC_Character[old.Length + 1];
        for (int i = 0; i < old.Length; i++) next[i] = old[i];
        next[old.Length] = character;
        character.manager.characters = next;
        character.manager.ApplyProxyModifications();
        EditorUtility.SetDirty(character.manager);
    }

    private static void EnsureUniqueCharacterId(VNPC_Character character, VNPC_Character[] registered)
    {
        bool duplicate = false;
        int maxId = -1;
        for (int i = 0; i < registered.Length; i++)
        {
            VNPC_Character item = registered[i];
            if (item == null || item == character) continue;
            maxId = Mathf.Max(maxId, item.characterId);
            if (item.characterId == character.characterId) duplicate = true;
        }
        if (!duplicate) return;
        Undo.RecordObject(character, "Assign Unique VNPC Character ID");
        character.characterId = maxId + 1;
        EditorUtility.SetDirty(character);
    }
}
#endif
