#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VNPC;

[Serializable]
internal class VNPCPreset
{
    public string format = "VNPCCharacter";
    public int formatVersion = 1;
    public string frameworkVersion = "0.1.5";
    public int moveStyle;
    public int startIndex;
    public int step;
    public float moveSpeed;
    public float turnSpeed;
    public float waitTime;
    public float arrivalDistance;
    public float areaRadius;
    public int areaDirectionCount;
    public int linkageCandidateCount;
    public float followDistance;
    public float followSearchDistance;
    public float followSearchAngle;
    public float stopDistance;
    public float walkSpeedReference;
    public float runSpeedReference;
    public float idleEnterSpeed;
    public float idleExitSpeed;
    public float speedSmoothing;
    public bool lookAtPlayer;
    public float lookDistance;
    public float lookWeight;
    public float maxLookYaw;
    public string[] messages;
    public int[] messageChoiceStarts;
    public int[] messageChoiceCounts;
    public string[] choiceTexts;
    public int[] choiceNextMessages;
    public int[] choiceCommands;
    public int[] choiceParameters;
    public string idleSourceAsset;
    public string idleSourceClip;
    public string walkSourceAsset;
    public string walkSourceClip;
    public string runSourceAsset;
    public string runSourceClip;
}

public static class VNPC_PresetUtility
{
    public static void Export(VNPC_Character character)
    {
        string path = EditorUtility.SaveFilePanel("Export VNPC Character", "", character.gameObject.name + ".vnpc", "vnpc");
        if (string.IsNullOrEmpty(path)) return;
        VNPCPreset preset = CreatePreset(character);
        File.WriteAllText(path, JsonUtility.ToJson(preset, true));
        AssetDatabase.Refresh();
    }

    public static void Import(VNPC_Character character)
    {
        string path = EditorUtility.OpenFilePanel("Import VNPC Character", "", "vnpc");
        if (string.IsNullOrEmpty(path)) return;
        VNPCPreset preset;
        try { preset = JsonUtility.FromJson<VNPCPreset>(File.ReadAllText(path)); }
        catch (Exception exception) { EditorUtility.DisplayDialog("VNPC Import", exception.Message, "OK"); return; }
        if (preset == null || preset.format != "VNPCCharacter")
        {
            EditorUtility.DisplayDialog("VNPC Import", "VNPCCharacter形式ではありません。", "OK");
            return;
        }
        if (preset.formatVersion > 1)
        {
            EditorUtility.DisplayDialog("VNPC Import", "未対応の新しいformatVersionです。", "OK");
            return;
        }
        ApplyPreset(character, preset);
    }

    private static VNPCPreset CreatePreset(VNPC_Character c)
    {
        VNPCPreset p = new VNPCPreset
        {
            moveStyle = (int)c.moveStyle, startIndex = c.startIndex, step = c.step,
            moveSpeed = c.moveSpeed, turnSpeed = c.turnSpeed, waitTime = c.waitTime,
            arrivalDistance = c.arrivalDistance, areaRadius = c.areaRadius, areaDirectionCount = c.areaDirectionCount,
            linkageCandidateCount = c.linkageCandidateCount,
            followDistance = c.followDistance, followSearchDistance = c.followSearchDistance, followSearchAngle = c.followSearchAngle,
            stopDistance = c.stopDistance, walkSpeedReference = c.walkSpeedReference, runSpeedReference = c.runSpeedReference,
            idleEnterSpeed = c.idleEnterSpeed, idleExitSpeed = c.idleExitSpeed, speedSmoothing = c.speedSmoothing,
            lookAtPlayer = c.lookAtPlayer, lookDistance = c.lookDistance, lookWeight = c.lookWeight, maxLookYaw = c.maxLookYaw,
            messages = c.messages, messageChoiceStarts = c.messageChoiceStarts, messageChoiceCounts = c.messageChoiceCounts,
            choiceTexts = c.choiceTexts, choiceNextMessages = c.choiceNextMessages, choiceCommands = c.choiceCommands,
            choiceParameters = c.choiceParameters
        };
        FillAnimationReference(c.idleAnimation, c.importedIdleAsset, c.importedIdleClip, out p.idleSourceAsset, out p.idleSourceClip);
        FillAnimationReference(c.walkAnimation, c.importedWalkAsset, c.importedWalkClip, out p.walkSourceAsset, out p.walkSourceClip);
        FillAnimationReference(c.runAnimation, c.importedRunAsset, c.importedRunClip, out p.runSourceAsset, out p.runSourceClip);
        return p;
    }

    private static void ApplyPreset(VNPC_Character c, VNPCPreset p)
    {
        Undo.RecordObject(c, "Import VNPC Preset");
        c.moveStyle = p.moveStyle >= 0 && p.moveStyle <= (int)VNPCMoveStyle.LinkageArea ? (VNPCMoveStyle)p.moveStyle : VNPCMoveStyle.None;
        c.startIndex = p.startIndex; c.step = p.step; c.moveSpeed = Mathf.Max(0f, p.moveSpeed); c.turnSpeed = Mathf.Max(0f, p.turnSpeed);
        c.waitTime = Mathf.Max(0f, p.waitTime); c.arrivalDistance = Mathf.Max(0f, p.arrivalDistance);
        c.areaRadius = Mathf.Max(0f, p.areaRadius); c.areaDirectionCount = Mathf.Clamp(p.areaDirectionCount, 1, 24);
        c.linkageCandidateCount = p.linkageCandidateCount <= 0 ? 24 : Mathf.Clamp(p.linkageCandidateCount, 3, 64);
        c.followDistance = Mathf.Max(0f, p.followDistance); c.followSearchDistance = Mathf.Max(0f, p.followSearchDistance);
        c.followSearchAngle = Mathf.Clamp(p.followSearchAngle, 0f, 180f); c.stopDistance = Mathf.Max(0f, p.stopDistance);
        c.walkSpeedReference = Mathf.Max(0f, p.walkSpeedReference); c.runSpeedReference = Mathf.Max(0f, p.runSpeedReference);
        c.idleEnterSpeed = Mathf.Max(0f, p.idleEnterSpeed); c.idleExitSpeed = Mathf.Max(c.idleEnterSpeed, p.idleExitSpeed);
        c.speedSmoothing = Mathf.Max(0f, p.speedSmoothing); c.lookAtPlayer = p.lookAtPlayer;
        c.lookDistance = Mathf.Max(0f, p.lookDistance); c.lookWeight = Mathf.Clamp01(p.lookWeight); c.maxLookYaw = Mathf.Clamp(p.maxLookYaw, 0f, 60f);
        c.messages = p.messages ?? new string[0];
        c.choiceTexts = p.choiceTexts ?? new string[0];
        c.messageChoiceStarts = ResizeAndClamp(p.messageChoiceStarts, c.messages.Length, 0, c.choiceTexts.Length);
        c.messageChoiceCounts = ResizeAndClamp(p.messageChoiceCounts, c.messages.Length, 0, c.choiceTexts.Length);
        for (int i = 0; i < c.messages.Length; i++)
            c.messageChoiceCounts[i] = Mathf.Min(c.messageChoiceCounts[i], c.choiceTexts.Length - c.messageChoiceStarts[i]);
        c.choiceNextMessages = ResizeAndClamp(p.choiceNextMessages, c.choiceTexts.Length, -1, Mathf.Max(-1, c.messages.Length - 1));
        c.choiceCommands = ResizeCommands(p.choiceCommands, c.choiceTexts.Length);
        c.choiceParameters = Resize(p.choiceParameters, c.choiceTexts.Length);
        c.importedIdleAsset = p.idleSourceAsset; c.importedIdleClip = p.idleSourceClip;
        c.importedWalkAsset = p.walkSourceAsset; c.importedWalkClip = p.walkSourceClip;
        c.importedRunAsset = p.runSourceAsset; c.importedRunClip = p.runSourceClip;
        EditorUtility.SetDirty(c);
    }

    private static int[] ResizeCommands(int[] source, int length)
    {
        int[] result = Resize(source, length);
        for (int i = 0; i < result.Length; i++) result[i] = result[i] >= 0 && result[i] <= 7 ? result[i] : 0;
        return result;
    }

    private static int[] Resize(int[] source, int length)
    {
        int[] result = new int[Mathf.Max(0, length)];
        if (source != null) Array.Copy(source, result, Mathf.Min(source.Length, result.Length));
        return result;
    }

    private static int[] ResizeAndClamp(int[] source, int length, int min, int max)
    {
        int[] result = Resize(source, length);
        for (int i = 0; i < result.Length; i++) result[i] = Mathf.Clamp(result[i], min, max);
        return result;
    }

    private static void FillAnimationReference(AnimationClip clip, string fallbackAsset, string fallbackClip, out string assetName, out string clipName)
    {
        if (clip == null) { assetName = fallbackAsset ?? string.Empty; clipName = fallbackClip ?? string.Empty; return; }
        assetName = Path.GetFileName(AssetDatabase.GetAssetPath(clip));
        clipName = clip.name;
    }
}
#endif
