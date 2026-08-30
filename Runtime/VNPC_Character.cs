using UdonSharp;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

namespace VNPC
{
    public enum VNPCMoveStyle { None, PathLoop, PointArea, PlayerFollow }

    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [RequireComponent(typeof(Animator), typeof(NavMeshAgent), typeof(VRC.SDK3.Components.VRCObjectSync))]
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
        public float waitTime = 3f;
        public float arrivalDistance = 0.15f;
        public Transform areaCenter;
        public float areaRadius = 3f;
        [Range(1, 24)] public int areaDirectionCount = 24;
        public float followDistance = 2f;
        public float repathInterval = 0.5f;

        [Header("Animation")]
        public string speedParameter = "Speed";
        public string movingParameter = "IsMoving";
        public string actionParameter = "ActionID";

        [Header("Player Interaction")]
        public bool lookAtPlayer = true;
        public float lookDistance = 5f;
        public float playerPollInterval = 0.25f;
        [Range(0f, 1f)] public float lookWeight = 0.65f;

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

        private NavMeshAgent agent;
        private Animator animator;
        private VRCPlayerApi localPlayer;
        private int pointIndex;
        private int areaIndex;
        private int currentMessage = -1;
        private float waitUntil;
        private float nextPlayerPoll;
        private float nextRepath;
        private bool waiting;
        private bool playerNear;
        private Vector3 areaOrigin;
        private Vector3 previousPosition;

        private void Start()
        {
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponent<Animator>();
            localPlayer = Networking.LocalPlayer;
            pointIndex = startIndex;
            areaOrigin = areaCenter != null ? areaCenter.position : transform.position;
            previousPosition = transform.position;
            ConfigureAgent();
            if (dialoguePanel != null) dialoguePanel.SetActive(false);
            if (IsController()) SetNextDestination(false);
        }

        private void Update()
        {
            UpdateAnimator();
            if (Time.time >= nextPlayerPoll)
            {
                nextPlayerPoll = Time.time + Mathf.Max(0.1f, playerPollInterval);
                playerNear = localPlayer != null && Vector3.Distance(transform.position, localPlayer.GetPosition()) <= lookDistance;
            }
            if (!IsController() || agent == null || !agent.enabled) return;
            UpdateMovement();
        }

        private void LateUpdate()
        {
            if (!lookAtPlayer || !playerNear || animator == null || localPlayer == null) return;
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;
            Vector3 target = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Quaternion desired = Quaternion.LookRotation(target - head.position, transform.up);
            head.rotation = Quaternion.Slerp(head.rotation, desired, lookWeight);
        }

        private bool IsController()
        {
            return Networking.LocalPlayer == null || Networking.IsOwner(gameObject);
        }

        private void ConfigureAgent()
        {
            if (agent == null) return;
            agent.enabled = IsController();
            if (!agent.enabled) return;
            agent.speed = moveSpeed;
            agent.stoppingDistance = moveStyle == VNPCMoveStyle.PlayerFollow ? followDistance : arrivalDistance;
        }

        private void UpdateMovement()
        {
            if (moveStyle == VNPCMoveStyle.None) { agent.isStopped = true; return; }
            if (moveStyle == VNPCMoveStyle.PlayerFollow)
            {
                if (localPlayer != null && Time.time >= nextRepath)
                {
                    nextRepath = Time.time + Mathf.Max(0.1f, repathInterval);
                    agent.stoppingDistance = followDistance;
                    agent.SetDestination(localPlayer.GetPosition());
                }
                return;
            }
            if (waiting)
            {
                if (Time.time >= waitUntil) { waiting = false; SetNextDestination(true); }
                return;
            }
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + arrivalDistance)
            {
                waiting = true;
                waitUntil = Time.time + Mathf.Max(0f, waitTime);
                agent.isStopped = true;
            }
        }

        private void SetNextDestination(bool advance)
        {
            if (agent == null || !agent.isOnNavMesh) return;
            Vector3 destination;
            if (moveStyle == VNPCMoveStyle.PathLoop)
            {
                if (manager == null || manager.GetPathPointCount(pathId) == 0) return;
                if (advance) pointIndex += step == 0 ? 1 : step;
                destination = manager.GetPathPoint(pathId, pointIndex);
            }
            else if (moveStyle == VNPCMoveStyle.PointArea)
            {
                if (advance) areaIndex = (areaIndex + (step == 0 ? 1 : step) + areaDirectionCount) % areaDirectionCount;
                float angle = areaIndex * 360f / Mathf.Max(1, areaDirectionCount) * Mathf.Deg2Rad;
                Vector3 center = areaCenter != null ? areaCenter.position : areaOrigin;
                destination = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * areaRadius;
                NavMeshHit hit;
                if (NavMesh.SamplePosition(destination, out hit, Mathf.Max(1f, areaRadius * 0.5f), NavMesh.AllAreas)) destination = hit.position;
            }
            else return;
            agent.isStopped = false;
            agent.stoppingDistance = arrivalDistance;
            agent.SetDestination(destination);
        }

        private void UpdateAnimator()
        {
            if (animator == null) return;
            float delta = Mathf.Max(Time.deltaTime, 0.0001f);
            float speed = IsController() && agent != null && agent.enabled
                ? agent.velocity.magnitude
                : Vector3.Distance(transform.position, previousPosition) / delta;
            previousPosition = transform.position;
            if (!string.IsNullOrEmpty(speedParameter)) animator.SetFloat(speedParameter, speed);
            if (!string.IsNullOrEmpty(movingParameter)) animator.SetBool(movingParameter, speed > 0.05f);
        }

        public override void Interact() { StartDialogue(); }

        public void StartDialogue()
        {
            if (messages == null || messages.Length == 0) return;
            ShowMessage(0);
        }

        public void CloseDialogue()
        {
            currentMessage = -1;
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
            if (currentMessage < 0) return;
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
            if (command == 4 && animator != null && !string.IsNullOrEmpty(actionParameter)) animator.SetInteger(actionParameter, parameter);
            if ((command == 5 || command == 6) && commandObjects != null && parameter >= 0 && parameter < commandObjects.Length && commandObjects[parameter] != null)
                commandObjects[parameter].SetActive(command == 5);
            if (command == 7 && parameter >= 0 && parameter <= (int)VNPCMoveStyle.PlayerFollow)
                moveStyle = (VNPCMoveStyle)parameter;
        }

        public void OnGlobalFlagsChanged() { }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            ConfigureAgent();
            if (IsController()) SetNextDestination(false);
        }
    }
}
