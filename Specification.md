# VNPC Specification

## 1. 文書情報

- Framework名：VNPC（VRChat NPC Framework）
- 現行仕様Version：v0.1.2
- 対象：VRChat Worlds SDK / UdonSharp
- Unity：VRChatがサポートするUnity 2022.3系

本書はv0.1.2で実装するRuntimeおよびEditor仕様を定義する。将来候補は「未実装」に明記し、正式仕様とは区別する。

## 2. 目的

カフェや交流Worldへ、店員、MOB、案内役などのHumanoid NPCを比較的少ない設定で配置するためのFrameworkとする。

実装済みの中心機能：

- 共有Waypointを使用するループ移動
- 指定地点周辺の決定論的な巡回
- 条件に合うPlayerへの追従
- Idle／Walk／Run Animation
- Local Playerへの視線追従
- Player近接時の移動停止
- 選択肢ベースの会話
- 会話中の単一話者ロック
- GlobalFlagのネットワーク同期
- Character設定の`.vnpc` Export／Import

## 3. 公開コンポーネント

### 3.1 VNPC_Manager

World内で共有される次の情報を管理する。

- `VNPC_Character[] characters`
- `Transform[] paths`
- GlobalFlag
- Characterごとの会話相手Player ID
- 会話Timeout
- GlobalFlagおよび会話状態のネットワーク同期

### 3.2 VNPC_Character

NPC固有の次の処理を管理する。

- MoveStyleと移動設定
- Direct Movement
- Player探索と近接停止
- PlayerFollow対象選択
- Animator速度更新
- LookAt
- Local Dialogue UI
- Choice Command
- Managerへの会話・GlobalFlag操作要求

## 4. GameObject構成

通常NPCの必須構成は次のとおりとする。

```text
NPC
├ Animator
├ VRCObjectSync
└ VNPC_Character
```

- `Animator`と`VRCObjectSync`は`RequireComponent`およびAuto Setupの対象とする。
- `NavMeshAgent`は使用しない。
- Player検知用Trigger Colliderは生成しない。
- `Interact`を使用する場合、World制作者が操作用Colliderを用意する。
- Animatorの`Apply Root Motion`は生成時に無効化する。

## 5. ネットワーク設計

### 5.1 Character Transform

- Transform同期は`VRCObjectSync`へ委譲する。
- `VNPC_Character`は`BehaviourSyncMode.None`とする。
- CharacterのOwnerだけが移動先決定とTransform操作を実行する。
- Remote側は移動AIを実行せず、VRCObjectSyncが反映したTransformを使用する。
- Remote側でTransformを補間、補正、上書きしない。

### 5.2 Manager

- `VNPC_Manager`は`BehaviourSyncMode.Manual`とする。
- Managerの同期対象は次の2項目とする。
  - `globalFlags : int`
  - `communicatingPlayerIds : int[]`
- 同期値を変更できるのはManager Ownerのみとする。
- Remoteからの操作はParameter付きNetwork EventでManager Ownerへ要求する。
- ManagerのOwnershipを会話Playerへ移さない。
- Manual Sync値を変更した後は`RequestSerialization()`を呼ぶ。

### 5.3 Late Joiner

- Late JoinerはManagerの最新同期値を受信する。
- `OnDeserialization`でCharacterへManager状態の変更を通知する。
- VRCObjectSyncによるCharacter Transformも通常の同期結果を使用する。

## 6. GlobalFlag

- GlobalFlagはManagerの`int`を使用するbit flagとする。
- 使用可能bitは0～30とする。
- 対応Command：
  - SetGlobalFlag
  - ClearGlobalFlag
  - ToggleGlobalFlag
- 非Ownerからの変更要求はManager OwnerへNetwork Eventで送信する。
- 会話終了時に実行済みGlobalFlagを巻き戻さない。

## 7. Path管理

- Managerの`paths[]`へPath親Transformを登録する。
- Path親Transformの直下の子をWaypointとして使用する。
- Waypointの接続順はPath親Transform内のSibling Index順とする。
- Waypointは静的なScene参照であり、個別同期しない。
- Path IDはManagerの配列Indexとする。
- Point IndexはPathの子数で循環させる。

```text
Path Root
├ P0
├ P1
├ P2
└ P3
```

### 7.1 Path Scene View編集補助（v0.1.2）

- `VNPC_Manager`でPathとして指定された親Transformごとに、直下のWaypoint間をScene View上の接続線で表示する。
- 接続線はEditor専用処理から`Handles.DrawLine`を使用して描画する。
- RuntimeおよびUdon処理へ`UnityEditor` API、LineRenderer、接続線用GameObjectを含めない。
- 接続線はPathの基本順序を表し、Character固有の`step`は反映しない。
- 非アクティブなWaypointもRuntimeの`GetChild`処理と一致させて表示対象とする。
- `paths[]`内の`null`は安全に無視する。

Waypoint数ごとの描画規則：

| Waypoint数 | 接続線 |
|---:|---|
| 0 | 表示しない |
| 1 | 接続線を表示しない |
| 2 | 2点間を1本だけ表示する |
| 3以上 | Sibling Index順に隣接点を結び、最後と最初も結ぶ |

- 3点以上のPathでは、各Waypointから伸びる接続線は前後の隣接Waypointに対する最大2本とする。
- Scene Viewを過度に占有しないよう、原則として対象の`VNPC_Manager`選択中だけ表示する。
- Waypoint番号を表示し、必要に応じて進行方向を識別できるEditor表示を行う。

### 7.2 分岐Pathの扱い（v0.1.2）

- 分岐、合流、条件付き接続およびWaypointごとの複数接続先は正式サポートしない。
- Pathは全体が一本の巡回順序になるよう配置する。
- 同一地点を複数回通過させる場合は、同じ座標へ別のWaypoint GameObjectを配置する。
- 上記は条件分岐ではなく、Sibling Indexで定義された一筆書きの巡回経路として扱う。
- 明示的なLink Object、Waypoint隣接配列および経路探索Graphはv0.1.2へ導入しない。

## 8. Direct Movement

### 8.1 共通処理

Owner側で次の処理を行う。

```text
目的地取得
↓
目的地方向へQuaternion.RotateTowards
↓
Vector3.MoveTowardsで位置更新
↓
arrivalDistance以内で到着
```

主設定：

- `moveSpeed`：初期値1.5m/s
- `turnSpeed`：初期値180度/秒
- `waitTime`：初期値3秒
- `arrivalDistance`：初期値0.15m
- `startIndex`
- `step`

- Root Motionは使用しない。
- Static Object、壁、家具、他NPCを回避しない。
- 目的地まで直線移動する。
- World制作者が安全なWaypointと領域を配置する。

### 8.2 MoveStyle.None

- 自動移動を行わない。
- LookAt、Dialogue、Animation速度測定は継続する。

### 8.3 MoveStyle.PathLoop

- Managerの指定Pathを使用する。
- `startIndex`から開始する。
- 到着ごとに`step`を加算する。
- `step == 0`の場合は1として扱う。
- Point IndexはWaypoint数で循環する。
- 到着後は`waitTime`だけ待機する。

### 8.4 MoveStyle.PointArea

- `areaCenter`が設定されていればその位置を中心とする。
- 未設定の場合はStart時のCharacter位置を中心とする。
- 半径`areaRadius`の円周上へ固定候補を生成する。
- 候補方向数は`areaDirectionCount`とし、1～24とする。
- `step`に従い候補Indexを循環する。
- RuntimeでNavMesh Sampleやランダム点再抽選を行わない。

### 8.5 MoveStyle.PlayerFollow

検索設定：

- `followDistance`：初期値2m
- `followSearchDistance`：初期値10m
- `followSearchAngle`：初期値60度

選択規則：

1. Character前方から`followSearchAngle`以内を候補とする。
2. `followSearchDistance`以内を候補とする。
3. 距離が最も近いPlayerを優先する。
4. 距離差0.05m以内の場合は正面からの角度が小さいPlayerを優先する。
5. 距離差0.05m以内かつ角度差3度以内なら同順位とする。
6. 最上位が同順位、または候補なしの場合は追従しない。

- Follow対象選択はCharacter Ownerのみ行う。
- 対象が`followDistance`以内なら追従移動を行わない。
- Follow対象本人もPlayer近接停止判定から除外しない。
- `stopDistance > followDistance`の場合、Inspectorへ接近できない旨を表示する。

### 8.6 MoveStyle.LinkageArea（v0.1.2）

- `linkageArea`に指定した親Transformの直下の子を多角形頂点として使用する。
- 頂点数は3以上とし、Sibling Index順に辺を構成して最後と最初を接続する。
- 頂点はXZ平面へ投影し、Ray Casting法で多角形の内外を判定する。
- 凸多角形と凹多角形へ対応し、境界から0.01m以内は内側として扱う。
- 自己交差、連続する同一座標および面積を持たない多角形は設定不備とする。
- 候補地点は多角形のBounds内へHalton列で決定論的に生成する。
- `linkageCandidateCount`は3～64、初期値24とする。
- 候補Indexは`step`に従って循環し、`step == 0`は1として扱う。
- 現在地から候補地点までを16分割して検査し、全検査点が多角形内にある候補だけを目的地とする。
- 候補探索回数には上限を設け、適切な候補がなければ移動しない。
- Characterが領域外にいる場合は、最も近い頂点へ復帰してから領域内候補を選択する。
- 目的地のY座標は全頂点の平均値とし、v0.1.2では水平な移動領域を前提とする。
- NavMesh、ColliderおよびRuntimeの非決定的な乱数は使用しない。

## 9. Player探索と近接停止

### 9.1 検知方式

- Trigger Colliderを使用しない。
- `VRCPlayerApi.GetPlayers`で取得したPlayerとの距離を使用する。
- Player配列は再利用し、必要時だけ容量を拡張する。
- Character中心の仮想球として判定する。
- `stopDistance`以内に1人以上いれば移動を停止する。
- 範囲内に誰もいなければ移動を再開する。
- 方向や衝突コースは判定しない。
- Character Ownerだけが移動停止へ反映する。

### 9.2 探索頻度

```text
Manager登録NPC数 + Player数 <= 20
→ 基本間隔0.1秒、最大10回/秒

Manager登録NPC数 + Player数 > 20
→ 基本間隔0.25秒、最大4回/秒
```

- Character IDから追加offsetを求め、同一Frameへの探索集中を軽減する。
- PlayerFollow候補選択も同じPlayer配列を使用する。

## 10. Animation

### 10.1 AnimationClip

Characterへ次を設定する。

- `idleAnimation`
- `walkAnimation`
- `runAnimation`

- ClipはIn Placeを前提とする。
- Walk未設定時はIdle ClipをWalk Stateへ使用する。
- Run未設定時はRun Stateを生成しない。

### 10.2 実移動速度

Owner／Remoteの両方で次を計算する。

```text
measuredSpeed =
Distance(currentPosition, previousPosition) / Time.deltaTime
```

- `speedSmoothing`を係数として平滑化する。
- `max(20m/s, moveSpeed * 8)`を超える瞬間値は異常値として直前速度を使用する。
- Ownership移行時は前回位置と平滑化速度を初期化する。
- Animatorの内部Float Parameter `Speed`へ平滑化速度を設定する。

### 10.3 Animator State

Editorで次のStateを生成する。

```text
Base Layer
├ Idle
├ Walk
└ Run（Run Clip設定時のみ）
```

内部Parameter：

- `Speed : Float`
- `ActionID : Int`

Idle／Walk遷移：

- Idle → Walk：`Speed > idleExitSpeed`
- Walk → Idle：`Speed < idleEnterSpeed`
- 初期値：`idleEnterSpeed = 0.05`、`idleExitSpeed = 0.1`

Walk／Run遷移：

```text
midpoint = (walkSpeedReference + runSpeedReference) / 2
hysteresis = max(0.05, abs(run - walk) * 0.25)

Walk → Run : Speed > midpoint + hysteresis
Run → Walk : Speed < midpoint - hysteresis
```

参考速度初期値：

- Walk：2m/s
- Run：4m/s

### 10.4 Animator生成物

- Inspectorの`Generate / Rebuild Animator`から生成する。
- 保存先：`Assets/PeaceKunihiro/VNPC/Settings`
- 基本名：`VNPC_<Character名>.controller`
- 同名時：UnityのUnique Asset Pathによる連番名
- Character名のファイル名禁止文字は`_`へ置換する。
- Characterが保持する生成Controller参照がSettings外の場合は上書きしない。
- 生成ControllerをAnimatorへ割り当てる。
- `Apply Root Motion`を無効化する。
- StateのWrite DefaultsはOffとする。

## 11. LookAt

- 対象は各ClientのLocal Playerとする。
- `lookAtPlayer`が有効な場合だけ動作する。
- `lookDistance`以内の場合だけHead Boneを回転する。
- AnimatorのHumanoid Head Boneを使用する。
- `LateUpdate`でAnimation適用後に回転する。
- Character正面を基準として水平角度を計算する。
- 水平回転は左右最大60度とする。
- `maxLookYaw`は0～60度とする。
- `lookWeight`で現在回転と目標回転を補間する。
- LookAtは同期しない。

## 12. Dialogue

### 12.1 UIとデータ

実装は`UnityEngine.UI`を使用する。

- `dialoguePanel : GameObject`
- `dialogueText : Text`
- `choiceButtons : Button[]`
- `choiceLabels : Text[]`
- `messages : string[]`
- `messageChoiceStarts : int[]`
- `messageChoiceCounts : int[]`
- `choiceTexts : string[]`
- `choiceNextMessages : int[]`
- `choiceCommands : int[]`
- `choiceParameters : int[]`

Choice ButtonのOnClickは同じCharacterの`SelectChoice0`～`SelectChoice7`へ接続する。

### 12.2 会話開始

- `Interact`から会話開始を要求する。
- Messageが空、Manager未設定、Local Player未取得の場合は開始しない。
- Local Playerが`stopDistance`外の場合は開始しない。
- CharacterはManager OwnerへCharacter ID付き会話要求を送る。
- Manager OwnerはNetwork Event送信者を会話Playerとして検証する。
- 対象Characterが未使用の場合だけPlayer IDを登録する。
- 同じCharacterに対する2人目以降の要求は拒否する。
- Local側は同期されたPlayer IDが自分と一致した後にMessage 0を表示する。
- 要求確認Timeoutは3秒とする。

### 12.3 会話同期状態

Managerの配列：

```text
communicatingPlayerIds[index]
-1    : 会話なし
0以上 : 会話中Player ID
```

- 配列IndexはManagerのCharacters配列Indexとする。
- Character IDからManager配列Indexを検索する。
- Character IDはManager内で一意である必要がある。
- 会話中は対象Characterだけ移動を停止する。
- VRCObjectSyncは常時有効とする。
- 他Characterは移動を継続する。
- 会話中は実速度が0になるためAnimatorはIdleへ遷移する。

### 12.4 会話終了

次の場合に終了する。

- 次Messageが負数または範囲外
- Close操作
- 話者が`stopDistance`外へ移動
- 話者が退出
- Player参照が無効
- Character参照が無効
- `communicationTimeout`経過（初期値120秒）

- 通常の終了要求は現在の話者本人だけ受理する。
- Manager Ownerは距離外、退出、無効参照、Timeoutを強制解除できる。
- LocalのMessage Index、会話中状態、要求中状態を初期化する。
- 実行済みGlobalFlagは初期化しない。

### 12.5 移動復帰

- 会話ロックが有効から無効へ変化した時だけ再計算する。
- 待機状態と待機終了時刻を破棄する。
- PathLoop：保持中のPoint Indexに対応するWaypointを再設定する。
- PointArea：次の固定候補へ進める。
- PlayerFollow：対象を破棄してPlayer探索を再実行する。

## 13. Choice Command

Command番号：

| ID | Command | Parameter |
|---:|---|---|
| 0 | None | 未使用 |
| 1 | SetGlobalFlag | bit番号 |
| 2 | ClearGlobalFlag | bit番号 |
| 3 | ToggleGlobalFlag | bit番号 |
| 4 | PlayAction | ActionID |
| 5 | EnableObject | commandObjects Index |
| 6 | DisableObject | commandObjects Index |
| 7 | ChangeMoveStyle | VNPCMoveStyle番号 |

- Message遷移前にCommandを実行する。
- GlobalFlag CommandはManagerへ要求する。
- Enable／Disable対象はScene ObjectでありPresetへ保存しない。
- ChangeMoveStyle後は目的地を再計算する。

## 14. Manager割り当てとCharacter登録

```text
Manager 0個
→ 自動割り当てしない
→ InspectorへCreate VNPC Managerを表示

Manager 1個
→ Characterへ自動割り当て
→ Manager.charactersへ自動登録

Manager 2個以上
→ 自動選択しない
→ Inspectorで警告し、D&Dによる明示指定を要求
```

- 明示設定済み参照は自動解除しない。
- Managerへ登録する際、Character IDが他Characterと重複していれば`最大ID + 1`へ変更する。
- Character.managerとManager.charactersの対応を維持する。

## 15. Custom InspectorとAuto Setup

### 15.1 UdonSharp Editor API

- Custom Inspectorの先頭でUdonSharp Behaviour Headerを描画する。
- `SerializedObject`／`SerializedProperty`を使用する。
- Editor時のProxy変更は現行UdonSharpの自動反映を使用し、obsoleteな`ApplyProxyModifications()`を呼び出さない。
- Play Mode中の実行UdonBehaviour更新が必要なEditor機能を追加する場合だけ、現行APIの`CopyProxyToUdon`を検討する。
- UdonSharpBehaviour生成にはUdonSharp対応Undo APIを使用する。
- Editorコードは`Editor`フォルダかつ`UNITY_EDITOR`条件内へ配置する。

### 15.2 Inspector Foldout（v0.1.2）

- Movement、Player Avoidance、Animations、Player Interactionを個別に折りたためるようにする。
- Foldoutの初期状態は展開とする。
- Foldout状態はEditor内だけで保持し、Runtime/Udonのフィールドへ追加しない。
- Foldoutを閉じても設定値を変更または初期化しない。
- General、Dialogue UI、Imported Animation Referencesおよび各操作ボタンはFoldout対象外とする。
- 各設定値は`SerializedProperty`から描画する。

### 15.3 Auto Setup

Auto Setup対象：

- Animator
- VRCObjectSync
- Managerが1個の場合の自動割り当て
- 明示ManagerへのCharacter登録
- 重複Character IDの補正

Auto Setup対象外：

- NavMeshAgent
- Trigger Collider
- 複数Managerからの自動選択
- Dialogue UIの自動生成

Inspector警告：

- Humanoid Avatarを持たないAnimator
- Manager未設定
- Manager複数
- `stopDistance > followDistance`

## 16. Portable Preset

### 16.1 ファイル形式

- 拡張子：`.vnpc`
- 内容：JSON
- Editor限定で読み書きする。

```json
{
  "format": "VNPCCharacter",
  "formatVersion": 1,
  "frameworkVersion": "0.1.2"
}
```

- `format`が`VNPCCharacter`以外の場合は拒否する。
- `formatVersion > 1`の場合は拒否する。
- 将来の旧Version Migrationを追加できるDTO構造とする。

### 16.2 保存対象

- MoveStyle
- Start Index、Step
- Move Speed、Turn Speed、Wait Time、Arrival Distance
- PointAreaの半径と方向数
- LinkageAreaの候補地点数
- PlayerFollow設定
- Stop Distance
- Animation速度参考値と平滑化設定
- LookAt設定
- Message、Choice、Command配列
- Animation参考情報

### 16.3 保存対象外

- Character ID
- Manager参照
- Path ID
- Area Center
- Linkage Area
- Dialogue Panel、Text、Button
- Command Object
- Animator Controller
- AnimationClip Object参照
- GUID
- AssetPath
- その他Scene Object参照

Import時、保存対象外の既存参照を変更しない。

### 16.4 Animation参考情報

各Roleについて次を保存する。

- Source Asset File Name
- Source Clip Name

- AnimationClipが設定されていればAssetDatabaseから取得する。
- 未設定でImported Referenceが存在する場合はその情報を再Exportする。
- Import時にAnimationClipを自動割り当てしない。
- InspectorへImported Animation Referencesとして表示する。

### 16.5 Import検証

- 数値を有効範囲へClampする。
- MoveStyle番号を検証する。
- Message数に合わせてMessage Choice配列をResizeする。
- Choice数に合わせてNext Message、Command、Parameter配列をResizeする。
- Next Message Indexを`-1`～最終Message IndexへClampする。
- Command番号を0～7へ制限する。
- Import操作をUndo対象とする。

## 17. フォルダ構成

Unity Project内の想定配置：

```text
Assets
└ PeaceKunihiro
  └ VNPC
    ├ Editor
    ├ Runtime
    └ Settings
```

- RuntimeへUdonSharpBehaviourを配置する。
- EditorへInspector、Animator Builder、Preset Utility、Auto Setupを配置する。
- Settingsへ生成Animator Controllerを配置する。

## 18. 利用手順

1. Sceneへ`VNPC_Manager`を配置する。
2. ManagerのPathsへPath親Transformを登録する。
3. Humanoid Modelへ`VNPC_Character`を追加する。
4. Auto Setup結果のAnimatorとVRCObjectSyncを確認する。
5. Manager、Character ID、MoveStyleを確認する。
6. 移動設定と安全なWaypoint／Areaを設定する。
7. Idle／Walk／Run AnimationClipを設定する。
8. `Generate / Rebuild Animator`を実行する。
9. 必要に応じてDialogue UIとButton Eventを設定する。
10. VRChatのBuild & TestでOwner／Remote動作を確認する。

## 19. 検証項目

1. UdonSharp compile errorがないこと
2. CharacterにNavMeshAgentが要求されないこと
3. OwnerだけがTransformを操作すること
4. PathLoopがWaypoint間をDirect Movementすること
5. PointAreaが固定候補間をDirect Movementすること
6. PlayerFollowが距離・角度・同順位規則に従うこと
7. Player近接時に対象Characterが停止すること
8. Player不在時に移動を再開すること
9. VRCObjectSyncでRemoteへ位置・回転が反映されること
10. RemoteでTransform差分からAnimationが切り替わること
11. 首の水平回転が左右60度を超えないこと
12. Idle／Walk／Run Stateが生成されること
13. ユーザーControllerを上書きしないこと
14. 同一Characterの会話が単一Playerへ排他されること
15. 会話中は対象Characterだけ停止すること
16. 距離外、退出、Timeoutで会話ロックが解除されること
17. 会話終了後にMoveStyle別の目的地が再計算されること
18. GlobalFlagが会話終了で巻き戻らないこと
19. `.vnpc` Export／Importが成功すること
20. ImportでScene参照とAnimationClipが維持されること
21. AnimationClip未設定でもImported Referenceを確認できること
22. Managerが複数の場合に自動選択されないこと
23. Manager選択中にPathのWaypoint番号と接続線がScene Viewへ表示されること
24. Waypoint数0、1、2、3以上で規定どおりに接続線が描画されること
25. 非アクティブなWaypointを含め、Runtimeと同じSibling Index順で表示されること
26. `paths[]`に`null`が含まれてもEditor例外が発生しないこと
27. Path編集補助がRuntime/UdonへEditor APIや表示用Objectを持ち込まないこと
28. 4区分のInspector Foldoutを個別に開閉でき、閉じても値が維持されること
29. LinkageAreaが3頂点未満の場合に移動しないこと
30. 凸・凹LinkageAreaの内外および境界を判定できること
31. LinkageAreaの目的地と移動線上の検査点が領域内に収まること
32. LinkageArea外から最寄り頂点へ復帰できること
33. 同じLinkageArea設定から決定論的な候補地点が得られること

## 20. 未実装・将来候補

次は現行v0.1.2の正式仕様へ含めない。

- NavMeshによるStatic障害物回避
- NPC同士の衝突回避
- NPC同士のGreeting／会話
- Reaction Animation専用設定
- Talk Animation専用State
- 複数Idle Pattern
- Dialogue UI自動生成
- Manager専用Custom Inspector
- Player探索のManager一括共有
- 実機Profilerに基づく大規模NPC最適化

## 21. 設計原則

1. Character Transform同期を独自Synced Variableで再実装しない。
2. VRCObjectSyncを会話中も停止しない。
3. 移動AIとTransform操作はOwnerだけが行う。
4. Remoteは受信Transformから表示用Animationを再現する。
5. GlobalFlagと会話ロックだけをManagerへ集約して同期する。
6. NavMeshへ依存せず、安全なDirect Movement経路をWorld制作者が用意する。
7. Player探索を毎Frame実行しない。
8. DialogueのMessage進行とUIはPlayerごとのローカル状態とする。
9. 同一Characterの会話相手は同時に1人とする。
10. RuntimeからEditor APIとファイルI/Oを分離する。
11. Preset ImportでScene依存参照を破壊しない。
12. 利用者が内部Animator Parameterを手入力しなくても動作できるようにする。
