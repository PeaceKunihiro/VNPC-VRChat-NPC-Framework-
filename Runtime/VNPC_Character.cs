using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

namespace VNPC
{
    public enum VNPCMoveStyle { None, PathLoop, PointArea, PlayerFollow }

    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [RequireComponent(typeof(Animator), typeof(VRC.SDK3.Components.VRCObjectSync))]
    public class VNPC_Character : UdonSharpBehaviour
    {
        [Header("General")]
        public VNPC_Manager manager;
        public int characterId;

        [Header("Movement")]
        public VNPCMoveStyle moveStyle = VNPCMoveStyle.PathLoop;
        public int pathId;
        public int startIndex;
        public int step = 1;
        public float moveSpeed = 1.5f;
        public float turnSpeed = 180f;
        public float waitTime = 3f;
        public float arrivalDistance = 0.15f;
        public Transform areaCenter;
        public float areaRadius = 3f;
        [Range(1, 24)] public int areaDirectionCount = 24;
        public float followDistance = 2f;
        public float followSearchDistance = 10f;
        [Range(0f, 180f)] public float followSearchAngle = 60f;

        [Header("Player Avoidance")]
        public float stopDistance = 1.5f;

        [Header("Animations")]
        public AnimationClip idleAnimation;
        public AnimationClip walkAnimation;
        public AnimationClip runAnimation;
        public float walkSpeedReference = 2f;
        public float runSpeedReference = 4f;
        public float idleEnterSpeed = 0.05f;
        public float idleExitSpeed = 0.1f;
        public float speedSmoothing = 8f;
        [HideInInspector] public RuntimeAnimatorController generatedAnimatorController;
        [HideInInspector] public string importedIdleAsset;
        [HideInInspector] public string importedIdleClip;
        [HideInInspector] public string importedWalkAsset;
        [HideInInspector] public string importedWalkClip;
        [HideInInspector] public string importedRunAsset;
        [HideInInspector] public string importedRunClip;

        [Header("Player Interaction")]
        public bool lookAtPlayer = true;
        public float lookDistance = 5f;
        [Range(0f, 1f)] public float lookWeight = 0.65f;
        [Range(0f, 60f)] public float maxLookYaw = 60f;

        [Header("Dialogue UI (local only)")]
        public GameObject dialoguePanel;
        public Text dialogueText;
        public Button[] choiceButtons;
        public Text[] choiceLabels;
        [TextArea] public string[] messages;
        public int[] messageChoiceStarts;
        public int[] messageChoiceCounts;
        public string[] choiceTexts;
        public int[] choiceNextMessages;
        [Tooltip("0=None, 1=SetFlag, 2=ClearFlag, 3=ToggleFlag, 4=PlayAction, 5=EnableObject, 6=DisableObject, 7=ChangeMoveStyle")]
        public int[] choiceCommands;
        public int[] choiceParameters;
        public GameObject[] commandObjects;

        private const float DistanceTieEpsilon = 0.05f;
        private const float AngleTieEpsilon = 3f;
        private const float DialogueRequestTimeout = 3f;
        private const string SpeedParameter = "Speed";
        private const string ActionParameter = "ActionID";

        private Animator animator;
        private VRCPlayerApi localPlayer;
        private VRCPlayerApi[] players = new VRCPlayerApi[16];
        private int pointIndex;
        private int areaIndex;
        private int followPlayerId = -1;
        private int currentMessage = -1;
        private float waitUntil;
        private float nextPlayerScan;
        private float dialogueRequestStarted;
        private float smoothedSpeed;
        private bool waiting;
        private bool playerBlocked;
        private bool hasDestination;
        private bool dialogueRequestPending;
        private bool dialogueActive;
        private bool communicationWasLocked;
        private Vector3 areaOrigin;
        private Vector3 destination;
        private Vector3 previousPosition;

        private void Start()
        {
            animator = GetComponent<Animator>();
            localPlayer = Networking.LocalPlayer;
            pointIndex = startIndex;
            areaOrigin = areaCenter != null ? areaCenter.position : transform.position;
            previousPosition = transform.position;
            communicationWasLocked = manager != null && manager.IsCharacterCommunicating(characterId);
            if (dialoguePanel != null) dialoguePanel.SetActive(false);
            if (IsController()) RecalculateDestination(false);
            ScheduleNextPlayerScan();
        }

        private void Update()
        {
            UpdateMeasuredSpeed();
            UpdateDialogueState();
            if (Time.time >= nextPlayerScan)
            {
                ScanPlayers();
                ScheduleNextPlayerScan();
            }
            if (IsController()) UpdateMovement();
        }

        private void ScheduleNextPlayerScan()
        {
            int playerCount = VRCPlayerApi.GetPlayerCount();
            int npcCount = manager == null ? 1 : manager.GetCharacterCount();
            float interval = npcCount + playerCount > 20 ? 0.25f : 0.1f;
            float offset = Mathf.Abs(characterId % 10) * interval * 0.1f;
            nextPlayerScan = Time.time + interval + offset;
        }

        private void EnsurePlayerCapacity(int count)
        {
            if (players != null && players.Length >= count) return;
            int size = players == null || players.Length == 0 ? 16 : players.Length;
            while (size < count) size *= 2;
            players = new VRCPlayerApi[size];
        }

        private void ScanPlayers()
        {
            int count = VRCPlayerApi.GetPlayerCount();
            EnsurePlayerCapacity(count);
            VRCPlayerApi.GetPlayers(players);
            playerBlocked = false;
            float stopDistanceSquared = stopDistance * stopDistance;
            for (int i = 0; i < count; i++)
            {
                VRCPlayerApi player = players[i];
                if (!Utilities.IsValid(player)) continue;
                if ((player.GetPosition() - transform.position).sqrMagnitude <= stopDistanceSquared)
                {
                    playerBlocked = true;
                    break;
                }
            }
            if (moveStyle == VNPCMoveStyle.PlayerFollow && IsController()) SelectFollowPlayer(count);
        }

        private void SelectFollowPlayer(int count)
        {
            int bestId = -1;
            float bestDistance = float.MaxValue;
            float bestAngle = float.MaxValue;
            bool tied = false;
            for (int i = 0; i < count; i++)
            {
                VRCPlayerApi player = players[i];
                if (!Utilities.IsValid(player)) continue;
                Vector3 offset = player.GetPosition() - transform.position;
                float distance = offset.magnitude;
                if (distance > followSearchDistance || distance <= 0.001f) continue;
                float angle = Vector3.Angle(transform.forward, offset);
                if (angle > followSearchAngle) continue;
                if (distance < bestDistance - DistanceTieEpsilon)
                {
                    bestId = player.playerId;
                    bestDistance = distance;
                    bestAngle = angle;
                    tied = false;
                }
                else if (Mathf.Abs(distance - bestDistance) <= DistanceTieEpsilon)
                {
                    if (angle < bestAngle - AngleTieEpsilon)
                    {
                        bestId = player.playerId;
                        bestAngle = angle;
                        tied = false;
                    }
                    else if (Mathf.Abs(angle - bestAngle) <= AngleTieEpsilon) tied = true;
                }
            }
            followPlayerId = tied ? -1 : bestId;
            if (followPlayerId >= 0)
            {
                VRCPlayerApi target = VRCPlayerApi.GetPlayerById(followPlayerId);
                if (Utilities.IsValid(target)) { destination = target.GetPosition(); hasDestination = true; }
            }
            else hasDestination = false;
        }

        private void UpdateMovement()
        {
            bool communicationLocked = manager != null && manager.IsCharacterCommunicating(characterId);
            if (moveStyle == VNPCMoveStyle.None || communicationLocked || playerBlocked) return;
            if (waiting)
            {
                if (Time.time < waitUntil) return;
                waiting = false;
                RecalculateDestination(true);
            }
            if (moveStyle == VNPCMoveStyle.PlayerFollow)
            {
                if (followPlayerId < 0) return;
                VRCPlayerApi target = VRCPlayerApi.GetPlayerById(followPlayerId);
                if (!Utilities.IsValid(target)) { followPlayerId = -1; hasDestination = false; return; }
                destination = target.GetPosition();
                hasDestination = true;
                if (Vector3.Distance(transform.position, destination) <= followDistance) return;
            }
            if (!hasDestination) { RecalculateDestination(false); if (!hasDestination) return; }
            MoveTowardsDestination();
        }

        private void MoveTowardsDestination()
        {
            Vector3 offset = destination - transform.position;
            Vector3 flat = new Vector3(offset.x, 0f, offset.z);
            if (flat.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(flat, transform.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, Mathf.Max(0f, turnSpeed) * Time.deltaTime);
            }
            transform.position = Vector3.MoveTowards(transform.position, destination, Mathf.Max(0f, moveSpeed) * Time.deltaTime);
            if (Vector3.Distance(transform.position, destination) > arrivalDistance) return;
            transform.position = destination;
            if (moveStyle == VNPCMoveStyle.PlayerFollow) return;
            hasDestination = false;
            waiting = true;
            waitUntil = Time.time + Mathf.Max(0f, waitTime);
        }

        private void RecalculateDestination(bool advance)
        {
            if (moveStyle == VNPCMoveStyle.PathLoop)
            {
                int count = manager == null ? 0 : manager.GetPathPointCount(pathId);
                if (count == 0) { hasDestination = false; return; }
                if (advance) pointIndex += step == 0 ? 1 : step;
                destination = manager.GetPathPoint(pathId, pointIndex);
                hasDestination = true;
            }
            else if (moveStyle == VNPCMoveStyle.PointArea)
            {
                int directions = Mathf.Max(1, areaDirectionCount);
                if (advance) areaIndex = (areaIndex + (step == 0 ? 1 : step) + directions) % directions;
                float angle = areaIndex * 360f / directions * Mathf.Deg2Rad;
                Vector3 center = areaCenter != null ? areaCenter.position : areaOrigin;
                destination = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * areaRadius;
                hasDestination = true;
            }
            else hasDestination = false;
        }

        private void RecalculateAfterCommunication()
        {
            waiting = false;
            waitUntil = 0f;
            if (moveStyle == VNPCMoveStyle.PlayerFollow)
            {
                followPlayerId = -1;
                hasDestination = false;
                ScanPlayers();
            }
            else RecalculateDestination(moveStyle == VNPCMoveStyle.PointArea);
        }

        private void UpdateMeasuredSpeed()
        {
            float delta = Mathf.Max(Time.deltaTime, 0.0001f);
            float measured = Vector3.Distance(transform.position, previousPosition) / delta;
            previousPosition = transform.position;
            if (measured > Mathf.Max(20f, moveSpeed * 8f)) measured = smoothedSpeed;
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, measured, Mathf.Clamp01(speedSmoothing * delta));
            if (animator != null) animator.SetFloat(SpeedParameter, smoothedSpeed);
        }

        private void LateUpdate()
        {
            if (!lookAtPlayer || animator == null || localPlayer == null) return;
            if (Vector3.Distance(transform.position, localPlayer.GetPosition()) > lookDistance) return;
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;
            Vector3 target = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Vector3 localDirection = transform.InverseTransformDirection(target - head.position);
            float yawLimit = Mathf.Clamp(maxLookYaw, 0f, 60f);
            float yaw = Mathf.Clamp(Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg, -yawLimit, yawLimit);
            float horizontal = new Vector2(localDirection.x, localDirection.z).magnitude;
            float pitch = -Mathf.Atan2(localDirection.y, horizontal) * Mathf.Rad2Deg;
            Quaternion desired = transform.rotation * Quaternion.Euler(pitch, yaw, 0f);
            head.rotation = Quaternion.Slerp(head.rotation, desired, lookWeight);
        }

        private bool IsController()
        {
            return Networking.LocalPlayer == null || Networking.IsOwner(gameObject);
        }

        public override void Interact() { StartDialogue(); }

        public void StartDialogue()
        {
            if (dialogueActive || dialogueRequestPending || manager == null || localPlayer == null || messages == null || messages.Length == 0) return;
            if (Vector3.Distance(transform.position, localPlayer.GetPosition()) > stopDistance) return;
            dialogueRequestPending = true;
            dialogueRequestStarted = Time.time;
            manager.RequestCommunication(characterId);
        }

        private void UpdateDialogueState()
        {
            if (manager == null || localPlayer == null) return;
            int speaker = manager.GetCommunicatingPlayerId(characterId);
            if (dialogueRequestPending)
            {
                if (speaker == localPlayer.playerId)
                {
                    dialogueRequestPending = false;
                    dialogueActive = true;
                    ShowMessage(0);
                }
                else if (speaker >= 0 || Time.time - dialogueRequestStarted > DialogueRequestTimeout)
                    dialogueRequestPending = false;
            }
            if (dialogueActive && speaker != localPlayer.playerId) CloseDialogueLocal();
            if (dialogueActive && Vector3.Distance(transform.position, localPlayer.GetPosition()) > stopDistance) CloseDialogue();
        }

        public void CloseDialogue()
        {
            if (dialogueActive && manager != null) manager.RequestCommunicationEnd(characterId);
            CloseDialogueLocal();
        }

        private void CloseDialogueLocal()
        {
            currentMessage = -1;
            dialogueActive = false;
            dialogueRequestPending = false;
            if (dialoguePanel != null) dialoguePanel.SetActive(false);
        }

        private void ShowMessage(int index)
        {
            if (index < 0 || messages == null || index >= messages.Length) { CloseDialogue(); return; }
            currentMessage = index;
            if (dialoguePanel != null) dialoguePanel.SetActive(true);
            if (dialogueText != null) dialogueText.text = messages[index];
            int start = messageChoiceStarts != null && index < messageChoiceStarts.Length ? messageChoiceStarts[index] : 0;
            int count = messageChoiceCounts != null && index < messageChoiceCounts.Length ? messageChoiceCounts[index] : 0;
            for (int i = 0; choiceButtons != null && i < choiceButtons.Length; i++)
            {
                bool visible = i < count && start + i < (choiceTexts == null ? 0 : choiceTexts.Length);
                if (choiceButtons[i] != null) choiceButtons[i].gameObject.SetActive(visible);
                if (visible && choiceLabels != null && i < choiceLabels.Length && choiceLabels[i] != null) choiceLabels[i].text = choiceTexts[start + i];
            }
        }

        public void SelectChoice0() { SelectChoice(0); }
        public void SelectChoice1() { SelectChoice(1); }
        public void SelectChoice2() { SelectChoice(2); }
        public void SelectChoice3() { SelectChoice(3); }
        public void SelectChoice4() { SelectChoice(4); }
        public void SelectChoice5() { SelectChoice(5); }
        public void SelectChoice6() { SelectChoice(6); }
        public void SelectChoice7() { SelectChoice(7); }

        private void SelectChoice(int localIndex)
        {
            if (!dialogueActive || currentMessage < 0) return;
            int start = messageChoiceStarts != null && currentMessage < messageChoiceStarts.Length ? messageChoiceStarts[currentMessage] : 0;
            int choice = start + localIndex;
            if (choiceTexts == null || choice < 0 || choice >= choiceTexts.Length) return;
            ExecuteCommand(choice);
            int next = choiceNextMessages != null && choice < choiceNextMessages.Length ? choiceNextMessages[choice] : -1;
            ShowMessage(next);
        }

        private void ExecuteCommand(int choice)
        {
            int command = choiceCommands != null && choice < choiceCommands.Length ? choiceCommands[choice] : 0;
            int parameter = choiceParameters != null && choice < choiceParameters.Length ? choiceParameters[choice] : 0;
            if (manager != null)
            {
                if (command == 1) manager.SetGlobalFlag(parameter);
                else if (command == 2) manager.ClearGlobalFlag(parameter);
                else if (command == 3) manager.ToggleGlobalFlag(parameter);
            }
            if (command == 4 && animator != null) animator.SetInteger(ActionParameter, parameter);
            if ((command == 5 || command == 6) && commandObjects != null && parameter >= 0 && parameter < commandObjects.Length && commandObjects[parameter] != null)
                commandObjects[parameter].SetActive(command == 5);
            if (command == 7 && parameter >= 0 && parameter <= (int)VNPCMoveStyle.PlayerFollow)
            {
                moveStyle = (VNPCMoveStyle)parameter;
                RecalculateDestination(false);
            }
        }

        public void OnManagerStateChanged()
        {
            if (manager == null) return;
            bool locked = manager.IsCharacterCommunicating(characterId);
            if (communicationWasLocked && !locked && IsController()) RecalculateAfterCommunication();
            communicationWasLocked = locked;
            if (localPlayer != null && dialogueActive && manager.GetCommunicatingPlayerId(characterId) != localPlayer.playerId) CloseDialogueLocal();
        }

        public void OnGlobalFlagsChanged() { }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            previousPosition = transform.position;
            smoothedSpeed = 0f;
            if (IsController()) RecalculateDestination(false);
        }
    }
}
