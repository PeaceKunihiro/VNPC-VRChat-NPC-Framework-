# VNPC（VRChat NPC Framework）

交流ワールド向けの、UdonSharp製Humanoid NPCフレームワークです。

## 必要環境

- Unity / VRChat Worlds SDK
- UdonSharp

## 導入

1. 空のGameObjectへ `VNPC_Manager` を追加します。
2. Humanoidモデルへ `VNPC_Character` を追加します。
3. Inspectorの `Validate and Auto Setup` を押します。
4. Managerの `Paths` にPath親Transformを登録し、その子として安全なWaypointを並べます。
5. CharacterのMove Style、Path ID、速度、待機時間を設定します。

`VNPC_Character` の追加時に `Animator` と `VRCObjectSync` が揃います。Managerがシーンに1個だけあれば自動登録され、複数ある場合はInspectorから明示指定します。移動はDirect Movementで、NavMeshや静的障害物回避は使用しません。

## Animator parameters

Idle、Walk、RunのAnimationClipをCharacterへ設定し、`Generate / Rebuild Animator`を押すと、`Assets/PeaceKunihiro/VNPC/Settings`へ専用Controllerを生成します。既存のユーザーControllerは上書きしません。内部Parameterは以下です。

- `Speed` (Float)
- `ActionID` (Int)

## Dialogue UI

会話は各プレイヤーのローカルUIです。`Messages` とフラットな `Choice` 配列を設定し、各ButtonのOnClickを同じCharacterの `SelectChoice0` ～ `SelectChoice7` へ接続します。`Next Message` が負数なら会話を閉じます。

Command番号は `0: None / 1: SetFlag / 2: ClearFlag / 3: ToggleFlag / 4: PlayAction / 5: EnableObject / 6: DisableObject / 7: ChangeMoveStyle` です。GlobalFlagのみManagerから同期され、NPC TransformはVRCObjectSyncへ委譲されます。

同一NPCと同時に会話できるプレイヤーは1人です。会話中は対象NPCだけが停止し、終了・距離外・退出・Timeout時にロックを解除して移動先を再計算します。

## Portable preset

Inspectorの `Export .vnpc` / `Import .vnpc` から、Scene参照を含まない設定JSONをProject間で移動できます。AnimationClipは自動復元せず、元Asset名とClip名だけを参考情報として表示します。
