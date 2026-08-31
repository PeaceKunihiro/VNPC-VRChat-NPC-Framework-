#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VNPC;

public static class VNPC_AnimatorBuilder
{
    private const string SettingsFolder = "Assets/PeaceKunihiro/VNPC/Settings";
    private const string SpeedParameter = "Speed";
    private const string ActionParameter = "ActionID";

    public static void GenerateOrRebuild(VNPC_Character character)
    {
        if (character == null) return;
        EnsureFolders();

        AnimatorController controller = character.generatedAnimatorController as AnimatorController;
        string existingPath = controller == null ? string.Empty : AssetDatabase.GetAssetPath(controller);
        if (controller == null || !existingPath.StartsWith(SettingsFolder + "/"))
        {
            string safeName = Sanitize(character.gameObject.name);
            string path = AssetDatabase.GenerateUniqueAssetPath(SettingsFolder + "/VNPC_" + safeName + ".controller");
            controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        }

        Undo.RecordObject(controller, "Rebuild VNPC Animator");
        RebuildController(controller, character);

        Animator animator = character.GetComponent<Animator>();
        if (animator == null) animator = Undo.AddComponent<Animator>(character.gameObject);
        Undo.RecordObject(animator, "Assign VNPC Animator");
        animator.applyRootMotion = false;
        animator.runtimeAnimatorController = controller;

        Undo.RecordObject(character, "Store VNPC Animator");
        character.generatedAnimatorController = controller;
        EditorUtility.SetDirty(animator);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    private static void RebuildController(AnimatorController controller, VNPC_Character character)
    {
        for (int i = controller.parameters.Length - 1; i >= 0; i--)
            controller.RemoveParameter(i);
        controller.AddParameter(SpeedParameter, AnimatorControllerParameterType.Float);
        controller.AddParameter(ActionParameter, AnimatorControllerParameterType.Int);

        AnimatorControllerLayer layer;
        if (controller.layers.Length == 0)
        {
            controller.AddLayer("Base Layer");
            layer = controller.layers[0];
        }
        else layer = controller.layers[0];

        AnimatorStateMachine machine = layer.stateMachine;
        ChildAnimatorState[] oldStates = machine.states;
        for (int i = 0; i < oldStates.Length; i++) machine.RemoveState(oldStates[i].state);

        AnimationClip idleClip = character.idleAnimation;
        AnimationClip walkClip = character.walkAnimation != null ? character.walkAnimation : idleClip;
        AnimationClip runClip = character.runAnimation;

        AnimatorState idle = machine.AddState("Idle", new Vector3(200f, 100f));
        idle.motion = idleClip;
        idle.writeDefaultValues = false;
        AnimatorState walk = machine.AddState("Walk", new Vector3(450f, 100f));
        walk.motion = walkClip;
        walk.writeDefaultValues = false;
        machine.defaultState = idle;

        float idleEnter = Mathf.Max(0f, character.idleEnterSpeed);
        float idleExit = Mathf.Max(idleEnter, character.idleExitSpeed);
        AddTransition(idle, walk, AnimatorConditionMode.Greater, idleExit);
        AddTransition(walk, idle, AnimatorConditionMode.Less, idleEnter);

        if (runClip != null)
        {
            AnimatorState run = machine.AddState("Run", new Vector3(700f, 100f));
            run.motion = runClip;
            run.writeDefaultValues = false;
            float midpoint = (character.walkSpeedReference + character.runSpeedReference) * 0.5f;
            float hysteresis = Mathf.Max(0.05f, Mathf.Abs(character.runSpeedReference - character.walkSpeedReference) * 0.25f);
            AddTransition(walk, run, AnimatorConditionMode.Greater, midpoint + hysteresis);
            AddTransition(run, walk, AnimatorConditionMode.Less, midpoint - hysteresis);
        }
    }

    private static void AddTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, float threshold)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = 0.15f;
        transition.AddCondition(mode, threshold, SpeedParameter);
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Character";
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalid.Length; i++) value = value.Replace(invalid[i], '_');
        return string.IsNullOrWhiteSpace(value) ? "Character" : value;
    }

    private static void EnsureFolders()
    {
        EnsureFolder("Assets", "PeaceKunihiro");
        EnsureFolder("Assets/PeaceKunihiro", "VNPC");
        EnsureFolder("Assets/PeaceKunihiro/VNPC", "Settings");
    }

    private static void EnsureFolder(string parent, string name)
    {
        string path = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
    }
}
#endif
