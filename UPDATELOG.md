# VNPC Update Log

## v0.1.1

v0.1.0のDirect Movementを基礎として、プレイヤー探索、視線制限、会話排他、Animator生成、Preset、UdonSharp Editor連携を現在の実装へ統合しました。

### Runtime

- `VNPC_Character`を`BehaviourSyncMode.None`、`VNPC_Manager`をManual Syncとして責務を分離しました。
- CharacterのTransform操作をOwnerのみに限定し、Remoteは`VRCObjectSync`の受信TransformからAnimation速度を再現します。
- プレイヤー検知をTrigger Collider方式から、`VRCPlayerApi.GetPlayers`と`stopDistance`による仮想球判定へ変更しました。
- NPC数とPlayer数の合計が20以下では最大10回/秒、20を超える場合は最大4回/秒へ探索頻度を制限しました。
- Character IDを利用して探索タイミングを分散します。
- Direct Movementの回転速度を追加し、初期値を180度/秒としました。

### PlayerFollow

- Character前方の検索距離・検索角度内から追従候補を選択します。
- 距離を第1優先、正面からの角度を第2優先として最寄り候補を選択します。
- 距離差0.05m以内かつ角度差3度以内を同順位とし、同順位または候補なしの場合は追従しません。
- Follow対象本人も`stopDistance`判定から除外しません。

### LookAt

- Local PlayerへのLookAtをローカル処理として実装しました。
- Character正面を基準とした首の水平回転を左右最大60度へ制限しました。

### Animation

- Transform差分から測定した速度へ平滑化を適用し、`Speed` Parameterへ設定します。
- 瞬間的な大移動をAnimation速度の異常値として除外します。
- Walk／Run参考速度の中点とヒステリシスからRun遷移閾値を生成します。
- Animator Controller生成方式をIdle／Walk／RunのState方式へ統一しました。
- 生成先を`Assets/PeaceKunihiro/VNPC/Settings`へ統一しました。
- 生成名を`VNPC_<Character名>.controller`とし、重複時は連番を付けます。

### Communication

- 同一Characterと同時に会話できるPlayerを1人に制限しました。
- Managerの`communicatingPlayerIds`同期配列で会話相手を管理します。
- 会話開始・終了要求はManager Ownerが排他的に処理します。
- Network Eventの送信者を検証し、ManagerのOwnershipを会話Playerへ移しません。
- 会話中は該当Characterだけ移動を停止し、`VRCObjectSync`は継続します。
- 会話終了、距離外、Player退出、参照無効、Timeoutで会話ロックを解除します。
- 会話解除後はMoveStyleに応じて目的地または追従対象を再計算します。
- 会話終了時も実行済みGlobalFlagを維持します。

### Manager and Auto Setup

- Managerが0個の場合はInspectorへ作成操作を表示します。
- Managerが1個の場合はCharacterへ自動割り当て・登録します。
- Managerが複数の場合は自動選択せず、ユーザーによる明示指定を要求します。
- Manager登録時に重複したCharacter IDを検出し、未使用IDへ補正します。
- Auto Setup対象をAnimatorとVRCObjectSyncに限定し、NavMeshAgentとTrigger Colliderを追加しません。

### Editor and Preset

- Custom InspectorをUdonSharp専用Editor APIとProxy反映処理へ対応しました。
- `.vnpc`の`frameworkVersion`を`0.1.1`へ更新しました。
- Importは専用DTOからPortable項目だけをコピーし、Scene参照とAnimationClipを維持します。
- Message、Choice、Command配列の長さ・範囲をImport時に補正します。
- AnimationClip未設定時はAsset名とClip名をImported Referenceとして保持・表示します。

## v0.1.0

VNPCの移動・Animation・Editor設定を見直し、NavMeshに依存しない軽量なNPC Frameworkへ変更しました。

### Movement

- `NavMeshAgent`を通常移動の必須コンポーネントから削除しました。
- `PathLoop`、`PointArea`、`PlayerFollow`をDirect Movementへ変更しました。
- `VNPC_Character`が`Vector3.MoveTowards`を使用してTransformを移動します。
- Root Motionは使用せず、AnimationはIn Placeを前提とします。
- NPC位置・回転の同期には引き続き`VRCObjectSync`を使用します。
- Static Object、壁、家具、他NPCへの経路探索・障害物回避は行いません。
- World制作者が安全なWaypointと移動領域を配置する設計へ変更しました。

### Player Detection

- プレイヤーがNPCの検知範囲内にいる場合、NPCの移動を停止できるようにしました。
- プレイヤーが範囲外へ移動するとNPCは移動を再開します。
- Static Object、壁、家具、他VNPCは停止判定の対象外です。

### Animation

- `VNPC_Character`からIdle／Walk／RunのAnimationClipを直接設定できるようにしました。
- Animator Parameter名の手入力を通常設定から外しました。
- Transformの実移動量から速度を計算し、Idle／Walk／Runを切り替える方式へ変更しました。
- OwnerとRemoteの両方で、実際のTransform移動を基準にAnimationを制御します。
- Run Animationが未設定の場合はWalk Animationを継続します。

### Animator Generator

- 設定されたAnimationClipから専用Animator Controllerを生成するEditor機能を追加しました。
- Inspectorへ`Generate / Rebuild Animator`を追加しました。
- 生成ControllerはIdle／Walk／RunのState構成を使用します。
- Runtime UdonからAnimator Controllerを生成しません。
- ユーザー作成の既存Animator Controllerを無条件に上書きしない設計としました。

### Portable Preset

- VNPC Character設定をProject間で移動するための`.vnpc` Export／Importを追加しました。
- `.vnpc`の内容はJSON形式です。
- 次のVersion情報を保存します。

```json
{
  "format": "VNPCCharacter",
  "formatVersion": 1,
  "frameworkVersion": "0.1.0"
}
```

- 未知の新しい`formatVersion`は警告またはImport拒否の対象とします。
- Manager、Transform、GameObject、Dialogue UIなどのScene依存参照は保存しません。
- Path IDはWorldごとに意味が変わるため保存対象外です。
- Runtimeでは`.vnpc`ファイルを読み書きしません。

### Animation Reference in Preset

- AnimationClipのUnity Object参照、GUID、AssetPathは保存しません。
- 参考情報としてAnimation Role、Source Asset File Name、Source Clip Nameのみ保存します。
- FBX内AnimationClipを識別できるよう、Asset名とClip名を分けて保持します。
- Import先ではAnimationClipを自動割り当てせず、InspectorへImported Referenceとして表示します。

### Inspector and Auto Setup

- `VNPC_CharacterEditor`の設定項目を機能別に整理しました。
- Portable Settingsへ`.vnpc`のExport／Import操作を追加しました。
- Auto Setupの必須構成をAnimator、VRCObjectSync、VNPC_Characterへ変更しました。
- Auto Setupで`NavMeshAgent`を追加しないようにしました。
- Scene内にManagerが1個だけ存在する場合は自動割り当てします。
- Managerが複数存在する場合は自動生成・自動選択せず、ユーザーによる指定を要求します。

### UdonSharp Compatibility

- RuntimeからUnityEditor API、AssetDatabase、ファイルI/O、Animator Controller生成を分離しました。
- Animator生成、Preset入出力、Asset情報取得はすべてEditor側で処理します。
- 既存のDialogue、GlobalFlag、VRCObjectSyncによる同期機能を維持しました。

### Validation Targets

- PathLoop／PointAreaのDirect Movement
- 停止中のIdle、移動中のWalk、高速移動中のRun
- VRCObjectSyncによるRemote位置同期
- Remote側のTransform差分によるAnimation切り替え
- プレイヤー検知による停止・再開
- `.vnpc`のExport／Import
- Import時のScene参照維持
- AnimationClip未設定でのImport
- Imported Animation ReferenceのInspector表示
