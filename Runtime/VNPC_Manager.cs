using UdonSharp;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace VNPC
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class VNPC_Manager : UdonSharpBehaviour
    {
        public VNPC_Character[] characters;
        [Tooltip("Each path Transform contains its waypoints as children.")]
        public Transform[] paths;
        [HideInInspector] public Color[] pathSceneColors = new Color[0];
        [HideInInspector] public bool[] useWaypointMaterialColors = new bool[0];
        [Min(1f)] public float communicationTimeout = 120f;

        [Header("Dialogue UI (local only)")]
        [Tooltip("The generated World Space Canvas root positioned near the speaking character.")]
        public Transform dialogueWindow;
        public TMP_Text dialogueText;
        public ScrollRect dialogueScrollRect;
        public Button[] choiceButtons;
        public TMP_Text[] choiceLabels;
        [Tooltip("Euler offset applied after facing the local player. World Space Canvas usually requires Y=180.")]
        public Vector3 dialogueFacingOffset = new Vector3(0f, 180f, 0f);

        [UdonSynced, SerializeField] private int globalFlags;
        [UdonSynced, SerializeField] private int[] communicatingPlayerIds = new int[0];

        private float[] communicationStartedAt = new float[0];
        private float nextCommunicationValidation;
        private VNPC_Character localDialogueCharacter;

        public int GlobalFlags => globalFlags;

        private void Start()
        {
            EnsureCommunicationArrays();
            ClearDialogueUI();
            NotifyCharacters();
        }

        private void Update()
        {
            if (!Networking.IsOwner(gameObject) || Time.time < nextCommunicationValidation) return;
            nextCommunicationValidation = Time.time + 0.25f;
            ValidateCommunications();
        }

        private void EnsureCommunicationArrays()
        {
            int count = characters == null ? 0 : characters.Length;
            if (communicatingPlayerIds == null || communicatingPlayerIds.Length != count)
            {
                int[] next = new int[count];
                for (int i = 0; i < count; i++) next[i] = -1;
                if (communicatingPlayerIds != null)
                {
                    int copyCount = Mathf.Min(count, communicatingPlayerIds.Length);
                    for (int i = 0; i < copyCount; i++) next[i] = communicatingPlayerIds[i];
                }
                communicatingPlayerIds = next;
            }
            if (communicationStartedAt == null || communicationStartedAt.Length != count)
            {
                communicationStartedAt = new float[count];
                for (int i = 0; i < count; i++)
                    if (communicatingPlayerIds[i] >= 0) communicationStartedAt[i] = Time.time;
            }
        }

        public bool GetGlobalFlag(int bit)
        {
            return bit >= 0 && bit < 31 && (globalFlags & (1 << bit)) != 0;
        }

        public void SetGlobalFlag(int bit) { SubmitGlobalFlagCommand(1, bit); }
        public void ClearGlobalFlag(int bit) { SubmitGlobalFlagCommand(2, bit); }
        public void ToggleGlobalFlag(int bit) { SubmitGlobalFlagCommand(3, bit); }

        private void SubmitGlobalFlagCommand(int command, int bit)
        {
            if (bit < 0 || bit >= 31) return;
            if (Networking.IsOwner(gameObject)) ApplyGlobalFlagCommand(command, bit);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ApplyGlobalFlagCommand), command, bit);
        }

        [NetworkCallable(maxEventsPerSecond: 10)]
        public void ApplyGlobalFlagCommand(int command, int bit)
        {
            if (!Networking.IsOwner(gameObject) || bit < 0 || bit >= 31) return;
            int next = globalFlags;
            if (command == 1) next |= 1 << bit;
            else if (command == 2) next &= ~(1 << bit);
            else if (command == 3) next ^= 1 << bit;
            else return;
            if (next == globalFlags) return;
            globalFlags = next;
            RequestSerialization();
            NotifyCharacters();
        }

        public void RequestCommunication(int characterId)
        {
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ApplyCommunicationRequest), characterId);
        }

        public void RequestCommunicationEnd(int characterId)
        {
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(ApplyCommunicationEnd), characterId);
        }

        [NetworkCallable(maxEventsPerSecond: 10)]
        public void ApplyCommunicationRequest(int characterId)
        {
            if (!Networking.IsOwner(gameObject)) return;
            EnsureCommunicationArrays();
            int index = FindCharacterIndex(characterId);
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (index < 0 || !Utilities.IsValid(caller) || communicatingPlayerIds[index] >= 0) return;
            for (int i = 0; i < communicatingPlayerIds.Length; i++)
                if (communicatingPlayerIds[i] == caller.playerId) return;
            VNPC_Character character = characters[index];
            if (character == null || Vector3.Distance(character.transform.position, caller.GetPosition()) > character.stopDistance) return;
            communicatingPlayerIds[index] = caller.playerId;
            communicationStartedAt[index] = Time.time;
            RequestSerialization();
            NotifyCharacters();
        }

        [NetworkCallable(maxEventsPerSecond: 10)]
        public void ApplyCommunicationEnd(int characterId)
        {
            if (!Networking.IsOwner(gameObject)) return;
            EnsureCommunicationArrays();
            int index = FindCharacterIndex(characterId);
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (index < 0 || !Utilities.IsValid(caller) || communicatingPlayerIds[index] != caller.playerId) return;
            ClearCommunication(index);
        }

        public int GetCommunicatingPlayerId(int characterId)
        {
            EnsureCommunicationArrays();
            int index = FindCharacterIndex(characterId);
            return index < 0 ? -1 : communicatingPlayerIds[index];
        }

        public bool IsCharacterCommunicating(int characterId)
        {
            return GetCommunicatingPlayerId(characterId) >= 0;
        }

        private int FindCharacterIndex(int characterId)
        {
            if (characters == null) return -1;
            for (int i = 0; i < characters.Length; i++)
                if (characters[i] != null && characters[i].characterId == characterId) return i;
            return -1;
        }

        private void ValidateCommunications()
        {
            EnsureCommunicationArrays();
            for (int i = 0; i < communicatingPlayerIds.Length; i++)
            {
                int playerId = communicatingPlayerIds[i];
                if (playerId < 0) continue;
                VRCPlayerApi player = VRCPlayerApi.GetPlayerById(playerId);
                VNPC_Character character = characters[i];
                bool invalid = !Utilities.IsValid(player) || character == null;
                bool tooFar = !invalid && Vector3.Distance(character.transform.position, player.GetPosition()) > character.stopDistance;
                bool timedOut = Time.time - communicationStartedAt[i] >= communicationTimeout;
                if (invalid || tooFar || timedOut) ClearCommunication(i);
            }
        }

        private void ClearCommunication(int index)
        {
            if (index < 0 || index >= communicatingPlayerIds.Length || communicatingPlayerIds[index] < 0) return;
            communicatingPlayerIds[index] = -1;
            communicationStartedAt[index] = 0f;
            RequestSerialization();
            NotifyCharacters();
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!Networking.IsOwner(gameObject) || player == null) return;
            EnsureCommunicationArrays();
            for (int i = 0; i < communicatingPlayerIds.Length; i++)
                if (communicatingPlayerIds[i] == player.playerId) ClearCommunication(i);
        }

        public override void OnDeserialization()
        {
            EnsureCommunicationArrays();
            NotifyCharacters();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            EnsureCommunicationArrays();
            if (!Networking.IsOwner(gameObject)) return;
            for (int i = 0; i < communicationStartedAt.Length; i++)
                if (communicatingPlayerIds[i] >= 0) communicationStartedAt[i] = Time.time;
        }

        private void NotifyCharacters()
        {
            if (characters == null) return;
            for (int i = 0; i < characters.Length; i++)
                if (characters[i] != null) characters[i].OnManagerStateChanged();
        }

        public void ShowDialogue(VNPC_Character character, int messageIndex)
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (character == null || !Utilities.IsValid(localPlayer)) return;
            if (GetCommunicatingPlayerId(character.characterId) != localPlayer.playerId) return;
            if (character.messages == null || messageIndex < 0 || messageIndex >= character.messages.Length) return;

            if (localDialogueCharacter != character)
            {
                localDialogueCharacter = character;
                PositionDialogueWindow(character, localPlayer);
            }

            if (dialogueText != null) dialogueText.text = character.messages[messageIndex];
            if (dialogueScrollRect != null) dialogueScrollRect.verticalNormalizedPosition = 1f;
            int start = character.messageChoiceStarts != null && messageIndex < character.messageChoiceStarts.Length
                ? character.messageChoiceStarts[messageIndex] : 0;
            int count = character.messageChoiceCounts != null && messageIndex < character.messageChoiceCounts.Length
                ? character.messageChoiceCounts[messageIndex] : 0;
            for (int i = 0; choiceButtons != null && i < choiceButtons.Length; i++)
            {
                bool visible = i < count && start + i < (character.choiceTexts == null ? 0 : character.choiceTexts.Length);
                if (choiceButtons[i] != null) choiceButtons[i].gameObject.SetActive(visible);
                if (choiceLabels != null && i < choiceLabels.Length && choiceLabels[i] != null)
                    choiceLabels[i].text = visible ? character.choiceTexts[start + i] : "";
            }
            if (dialogueWindow != null) dialogueWindow.gameObject.SetActive(true);
        }

        public void HideDialogue(VNPC_Character character)
        {
            if (character != null && localDialogueCharacter != character) return;
            localDialogueCharacter = null;
            ClearDialogueUI();
        }

        private void PositionDialogueWindow(VNPC_Character character, VRCPlayerApi localPlayer)
        {
            Transform window = dialogueWindow;
            if (window == null) return;
            Vector3 position = character.GetDialoguePosition();
            window.position = position;
            Vector3 headPosition = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Vector3 direction = headPosition - position;
            if (direction.sqrMagnitude > 0.0001f)
                window.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up) * Quaternion.Euler(dialogueFacingOffset);
        }

        private void ClearDialogueUI()
        {
            if (dialogueText != null) dialogueText.text = "";
            for (int i = 0; choiceButtons != null && i < choiceButtons.Length; i++)
                if (choiceButtons[i] != null) choiceButtons[i].gameObject.SetActive(false);
            for (int i = 0; choiceLabels != null && i < choiceLabels.Length; i++)
                if (choiceLabels[i] != null) choiceLabels[i].text = "";
            if (dialogueScrollRect != null) dialogueScrollRect.verticalNormalizedPosition = 1f;
            if (dialogueWindow != null) dialogueWindow.gameObject.SetActive(false);
        }

        public void SelectChoice0() { SelectChoice(0); }
        public void SelectChoice1() { SelectChoice(1); }
        public void SelectChoice2() { SelectChoice(2); }
        public void SelectChoice3() { SelectChoice(3); }
        public void SelectChoice4() { SelectChoice(4); }
        public void SelectChoice5() { SelectChoice(5); }
        public void SelectChoice6() { SelectChoice(6); }
        public void SelectChoice7() { SelectChoice(7); }

        private void SelectChoice(int index)
        {
            if (localDialogueCharacter != null) localDialogueCharacter.SelectDialogueChoice(index);
        }

        public int GetPathPointCount(int pathId)
        {
            if (paths == null || pathId < 0 || pathId >= paths.Length || paths[pathId] == null) return 0;
            return paths[pathId].childCount;
        }

        public Vector3 GetPathPoint(int pathId, int pointId)
        {
            int count = GetPathPointCount(pathId);
            if (count == 0) return transform.position;
            pointId = ((pointId % count) + count) % count;
            return paths[pathId].GetChild(pointId).position;
        }

        public int GetCharacterCount()
        {
            return characters == null ? 0 : characters.Length;
        }
    }
}
