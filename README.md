# VNPC（VRChat NPC Framework）

交流ワールド向けの、UdonSharp製Humanoid NPCフレームワークです。

## 必要環境

- Unity / VRChat Worlds SDK
- UdonSharp
- シーンにBake済みのNavMesh

## 導入

1. 空のGameObjectへ `VNPC_Manager` を追加します。
2. Humanoidモデルへ `VNPC_Character` を追加します。
3. Inspectorの `Validate and Auto Setup` を押します。
4. Managerの `Paths` にPath親Transformを登録し、その子としてWaypointを並べます。
5. CharacterのMove Style、Path ID、速度、待機時間を設定します。

`VNPC_Character` の追加時に `Animator`、`NavMeshAgent`、`VRCObjectSync` が揃います。Managerがシーンに1個だけあれば自動登録され、不在時はInspectorの `Find / Create VNPC Manager` から生成できます。

## Animator parameters

既存Animator Controllerは上書きしません。必要に応じて以下のParameterをController側へ用意してください（Inspectorで名前を変更できます）。

- `Speed` (Float)
- `IsMoving` (Bool)
- `ActionID` (Int)

## Dialogue UI

会話は各プレイヤーのローカルUIです。`Messages` とフラットな `Choice` 配列を設定し、各ButtonのOnClickを同じCharacterの `SelectChoice0` ～ `SelectChoice7` へ接続します。`Next Message` が負数なら会話を閉じます。

Command番号は `0: None / 1: SetFlag / 2: ClearFlag / 3: ToggleFlag / 4: PlayAction / 5: EnableObject / 6: DisableObject / 7: ChangeMoveStyle` です。GlobalFlagのみManagerから同期され、NPC TransformはVRCObjectSyncへ委譲されます。
