using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace VNPC
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class VNPC_Manager : UdonSharpBehaviour
    {
        [Header("Characters")]
        public VNPC_Character[] characters;

        [Header("Shared Paths (children are waypoints)")]
        public Transform[] paths;

        [UdonSynced, SerializeField, Tooltip("World-wide boolean flags stored as bits 0-30.")]
        private int globalFlags;

        public int GlobalFlags => globalFlags;

        public bool GetGlobalFlag(int bit)
        {
            return bit >= 0 && bit < 31 && (globalFlags & (1 << bit)) != 0;
        }

        public void SetGlobalFlag(int bit) { SetGlobalFlagValue(bit, true); }
        public void ClearGlobalFlag(int bit) { SetGlobalFlagValue(bit, false); }

        public void ToggleGlobalFlag(int bit)
        {
            if (bit < 0 || bit >= 31) return;
            TakeOwnership();
            globalFlags ^= 1 << bit;
            RequestSerialization();
            NotifyCharacters();
        }

        public void SetGlobalFlagValue(int bit, bool value)
        {
            if (bit < 0 || bit >= 31) return;
            int next = value ? globalFlags | (1 << bit) : globalFlags & ~(1 << bit);
            if (next == globalFlags) return;
            TakeOwnership();
            globalFlags = next;
            RequestSerialization();
            NotifyCharacters();
        }

        public override void OnDeserialization()
        {
            NotifyCharacters();
        }

        private void TakeOwnership()
        {
            if (!Networking.IsOwner(gameObject) && Networking.LocalPlayer != null)
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        private void NotifyCharacters()
        {
            if (characters == null) return;
            for (int i = 0; i < characters.Length; i++)
                if (characters[i] != null) characters[i].OnGlobalFlagsChanged();
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
    }
}
