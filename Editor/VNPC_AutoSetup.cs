#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VNPC;

[InitializeOnLoad]
public static class VNPC_AutoSetup
{
    static VNPC_AutoSetup() { ObjectFactory.componentWasAdded += OnComponentAdded; }

    private static void OnComponentAdded(Component component)
    {
        VNPC_Character character = component as VNPC_Character;
        if (character == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.delayCall += () =>
        {
            if (character == null) return;
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(character);
            // RequireComponent adds runtime dependencies; the inspector resolves Manager registration.
            Object.DestroyImmediate(editor);
            VNPC_Manager[] managers = Object.FindObjectsOfType<VNPC_Manager>();
            if (character.manager == null && managers.Length == 1)
            {
                Undo.RecordObject(character, "Assign VNPC Manager");
                character.manager = managers[0];
                VNPC_Character[] old = managers[0].characters ?? new VNPC_Character[0];
                VNPC_Character[] next = new VNPC_Character[old.Length + 1];
                for (int i = 0; i < old.Length; i++) next[i] = old[i];
                next[old.Length] = character;
                Undo.RecordObject(managers[0], "Register VNPC Character");
                managers[0].characters = next;
                EditorUtility.SetDirty(managers[0]);
            }
        };
    }
}
#endif
