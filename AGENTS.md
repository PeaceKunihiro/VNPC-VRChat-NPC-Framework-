# VRC_REFERENCE.md

## Confirmed APIs

### VRCObjectSync
Official:
https://creators.vrchat.com/worlds/components/vrc_objectsync/

Usage in VNPC:
- NPC Transform synchronization
- VNPC design: only the object owner controls NPC movement
- Do not manually duplicate Transform synchronization with UdonSynced variables

### VRCPlayerApi
Official:
https://creators.vrchat.com/worlds/udon/players/

Used:
- GetPosition()
- GetTrackingData()

### Player Trigger Events
Official:
https://creators.vrchat.com/worlds/udon/players/player-collisions/

Used:
- OnPlayerTriggerEnter
- OnPlayerTriggerStay
- OnPlayerTriggerExit

Usage in VNPC:
- Detect players entering the NPC avoidance trigger
- Stop NPC movement when a player is on a collision course
- Trigger colliders are detection-only and must not physically push players

### UdonSharp
Official:
https://creators.vrchat.com/worlds/udon/udonsharp/

Important:
- Runtime code must not use UnityEditor
- Check Class Exposure Tree before adding uncertain Unity APIs

### UdonSharp Class Exposure Tree
Official:
https://creators.vrchat.com/worlds/udon/udonsharp/class-exposure-tree/

Unity menu:
VRChat SDK > Udon Sharp > Class Exposure Tree