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
            if (character != null) VNPC_EditorSetup.Setup(character);
        };
    }
}
#endif
