# VNPC Specification

## 1. 文書情報

- Framework名：VNPC（VRChat NPC Framework）
- 現行仕様Version：v0.1.6
- 対象：VRChat Worlds SDK / UdonSharp
- Unity：VRChatがサポートするUnity 2022.3系

本書はv0.1.6で実装するRuntimeおよびEditor仕様を定義する。将来候補は「未実装」に明記し、正式仕様とは区別する。

## 2. 目的

カフェや交流Worldへ、店員、MOB、案内役などのHumanoid NPCを比較的少ない設定で配置するためのFrameworkとする。

実装済みの中心機能：

- 共有Waypointを使用するループ移動
- 指定地点周辺の決定論的な巡回
- 条件に合うPlayerへの追従
- Idle／Walk／Run Animation
- Local Playerへの視線追従
- Player近接時の移動停止
- LinkageArea内の決定論的な巡回
- 選択肢ベースの会話
- 全Character共通Dialogue UIの自動生成と位置プレビュー
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
- Character別Dialogueデータと表示位置
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

### 5.4 Character Ownership移行時のMoveState再構築

#### 5.4.1 基本方針

- CharacterのOwnershipがLocal Playerへ移行した時、新OwnerはVRCObjectSyncが反映した現在の`transform.position`と`transform.rotation`を起点として移動経路を再構築する。
- Character位置、回転および目的地を独自のSynced Variableとして同期しない。
- Remote側は移動AIとTransform操作を実行しないが、Animation速度測定に使用しているTransform差分から、最後に正常と判定した水平移動方向と移動時刻をローカルに保持する。
- 移動方向の測定ではAnimation速度測定と同じ異常値基準を使用し、VRCObjectSyncの大きな補正またはTeleportに相当する差分を経路判定へ使用しない。
- `OnOwnershipTransferred`は全Clientで受信するが、MoveStateを再構築するのは移行後にCharacter OwnerとなったClientだけとする。
- Ownership移行を検出してから再構築が完了するまで、古い`destination`による`UpdateMovement`を実行しない。

#### 5.4.2 破棄するローカル状態

新Ownerは、Remote期間中に更新されなかった可能性がある次の状態をそのまま使用しない。

- `destination`
- `hasDestination`
- `pointIndex`
- `areaIndex`
- `waiting`
- `waitUntil`
- `followPlayerId`

Ownership移行時は速度計算用の`previousPosition`を現在位置へ合わせ、`smoothedSpeed`を0へ初期化する。

#### 5.4.3 PathLoop

- `startIndex`、正規化した`step`およびWaypoint数から、実際に巡回する有向区間列を再生成する。
- 現在位置から各有向区間への最短距離を求め、最も近い区間の終点を次のWaypointとする。
- 複数区間が同距離の場合、最後に正常と判定した水平移動方向との内積が最も大きい区間を優先する。
- 有効な移動方向がない場合はCharacterの前方向との内積を使用し、それでも同順位なら巡回順が最も早い区間を使用する。
- 現在位置がWaypointの`arrivalDistance`以内の場合は到着済みとみなし、Ownership移行時点から`waitTime`の待機を開始した後、`step`方向の次Waypointへ進む。
- Waypointが0個の場合は移動せず、1個の場合はそのWaypointだけを目的地として扱う。

#### 5.4.4 PointArea

- `areaCenter`、`areaRadius`および`areaDirectionCount`から固定候補点を再生成する。
- `step`に従う有向区間列を生成し、PathLoopと同じ距離、移動方向、前方向の優先規則で現在区間と次候補を決定する。
- 現在位置が候補点の`arrivalDistance`以内の場合は到着済みとみなし、`waitTime`の待機後に次候補へ進む。

#### 5.4.5 PlayerFollow

- 過去の`followPlayerId`を破棄する。
- Ownership取得後にPlayer探索を即時実行し、通常の距離、角度および同順位規則から追従対象を選び直す。
- 候補がない場合または最上位候補が同順位の場合は移動しない。

#### 5.4.6 LinkageArea

- 現在位置がLinkageArea外の場合は、通常処理と同じく最寄り頂点を目的地として領域内へ復帰する。
- 現在位置が領域内の場合は、同じLinkageArea頂点とHalton候補生成式から有効候補を再生成し、現在位置から線分全体が領域内となる新しい目的地を選択する。
- 最後に正常と判定した水平移動方向がある場合は、その方向に最も近い有効候補を優先する。有効な方向がない場合および同順位の場合は候補Indexで決定論的に選択する。
- 以前のOwnerが選択していた候補地点および`areaIndex`の完全一致は保証せず、安全な領域内移動の継続を保証対象とする。
- 有効候補がない場合は既定の再試行待機へ移行する。

#### 5.4.7 会話・停止状態

- Managerの会話ロック中にOwnershipが移行した場合は、Characterの移動を停止したままにする。
- 会話ロック解除時に、その時点のTransformからMoveStyle別の再構築を実行する。
- Player近接停止中でも経路は再構築できるが、Playerが`stopDistance`外へ出るまでTransform操作を開始しない。
- Transformだけでは以前のOwnerの待機残時間を復元できないため、到着点にいる場合はOwnership移行時点から`waitTime`を再開始する。

#### 5.4.8 再構築の前提と非対象

- Sceneで設定されたPath、Area、`startIndex`、`step`および各種移動設定が全Clientで同一であることを前提とする。
- 実行時に変更されたMoveStyleはTransformだけから判別できない。`ChangeMoveStyle`を使用する場合は、Character Ownerだけのローカル変更にせず、Ownership移行後も全Clientが同じMoveStyleを取得できる別の状態管理を必要とする。
- GlobalFlagと会話ロックはManagerのSynced Variableを引き続き使用し、CharacterのMoveState再構築対象には含めない。
- 再構築は以前のOwnerの内部状態を完全複製する処理ではなく、同期済み現在位置から不自然な逆走や開始地点への復帰を避けて有効な移動を再開する処理とする。

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

### 7.1 Path Scene View編集補助（v0.1.3）

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
- Scene ViewのGizmosが有効な間は、`VNPC_Manager`の選択状態にかかわらず常時表示する。
- Waypoint番号を表示し、必要に応じて進行方向を識別できるEditor表示を行う。
- Pathごとに`Use Waypoint Material Color`と`Fallback Color`を設定できるようにする。
- `Fallback Color`の初期値は赤とする。
- Material Colorを使用する場合は、WaypointをSibling Index順に検索し、最初に見つかった直下Rendererの`sharedMaterial`から取得する。
- Material Propertyは`_BaseColor`、`_Color`の順に検索する。
- Renderer、Materialまたは対応Color Propertyがない場合は`Fallback Color`を使用する。
- Material Colorを使用しない場合は常に`Fallback Color`を使用する。
- 複数Pathの自動色設定とFallback ColorはPath Indexごとに独立して保持する。
- Scene描画はEditor専用とし、VRChatへのビルドには線、矢印、Label、Rendererを含めない。

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

全Characterで1つのローカルWorld Space Canvasを共有する。文字描画はUdonへ公開された`TMPro.TMP_Text`、選択操作は`UnityEngine.UI.Button`を使用する。

Manager側：

- `dialogueWindow : Transform`
- `dialogueText : TMP_Text`
- `dialogueScrollRect : ScrollRect`
- `choiceButtons : Button[]`
- `choiceLabels : TMP_Text[]`
- `dialogueFacingOffset : Vector3`

Character側：

- `dialogueAnchor : Transform`
- `dialogueOffset : Vector3`
- `messages : string[]`
- `messageChoiceStarts : int[]`
- `messageChoiceCounts : int[]`
- `choiceTexts : string[]`
- `choiceNextMessages : int[]`
- `choiceCommands : int[]`
- `choiceParameters : int[]`

Choice ButtonのOnClickはManagerのBacking `UdonBehaviour.SendCustomEvent`へ接続し、`SelectChoice0`～`SelectChoice7`を送信する。Managerは受け取った選択番号をローカルで会話中のCharacterへ転送する。UdonSharp ProxyをOnClickの送信先へ直接指定しない。

- Manager Inspectorの`Create Shared Dialogue UI`から、共通Dialogue UIをEditor上で自動生成できる。
- 自動生成対象はWorld Space Canvas、`VRCUiShape`、`GraphicRaycaster`、TMP本文、本文背景、Outline、ScrollRect、縦Scrollbar、Choice Button最大8個および必要時のEventSystemとする。
- 本文の初期表示領域は英数20文字相当×3行とし、表示領域を超える長文では縦Scrollbarを使用する。
- 本文の背景色と縁取りはMessage AreaのImageおよびOutlineで構成し、個別のDialogue Panel参照を要求しない。
- 自動生成するDialogue Windowの初期Scaleは`0.001`とする。
- 既存のDialogue Windowが割り当てられている場合は自動生成せず、既存UIを維持する。
- 手動構成する場合は`UI > Text - TextMeshPro (VRC)`および`Button - TextMeshPro (VRC)`で作成したWorld Space Canvas構成を標準とする。
- Dialogue Canvasには`VRCUiShape`と`GraphicRaycaster`を配置し、SceneにEventSystemを1つ用意する。
- Dialogue CanvasのLayerは`UI`以外、Render ModeはWorld Spaceとする。
- Dialogue TextとChoice LabelsにはCanvas用の`TextMeshProUGUI`を指定する。
- Choice ButtonのNavigationはNoneとする。
- VRC版TMPは通常のTMP ComponentをVRChat向けCanvas設定で生成するもので、独自のText Component型ではない。
- Canvas、TMP文字列、選択肢表示、現在表示中Character参照は同期しない。
- `dialogueAnchor`指定時はAnchor Transformを基準として`dialogueOffset`をローカル座標で適用する。
- `dialogueAnchor`未指定時はCharacter座標へ`dialogueOffset`をワールド座標差分として加算する。
- 会話成立時にLocal PlayerのHeadへ向け、その後は会話終了まで位置と回転を固定する。
- Message切り替え時はScrollRectを先頭へ戻す。

### 12.2 会話開始

- `Interact`から会話開始を要求する。
- Messageが空、Manager未設定、Local Player未取得の場合は開始しない。
- Local Playerが`stopDistance`外の場合は開始しない。
- CharacterはManager OwnerへCharacter ID付き会話要求を送る。
- Manager OwnerはNetwork Event送信者を会話Playerとして検証する。
- 対象Characterが未使用の場合だけPlayer IDを登録する。
- 同じCharacterに対する2人目以降の要求は拒否する。
- 同じPlayerが別Characterと会話中の場合、新しい要求を拒否する。
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
- 共通Dialogue Windowを非表示にし、Dialogue Textと全Choice Labelを空文字へ初期化する。
- 全Choice Buttonを非表示にし、ローカルの表示対象Character参照を解除する。
- ScrollRectを先頭へ戻す。
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

Manager Inspectorの作成支援対象：

- 全Character共通Dialogue UI
- Dialogue Window、TMP本文、ScrollRect、Choice Button、Choice LabelのManagerへの割り当て
- Sceneに存在しない場合のEventSystem
- 既存Choice ButtonのBacking UdonBehaviour方式へのイベント修復

Auto Setup対象外：

- NavMeshAgent
- Trigger Collider
- 複数Managerからの自動選択

Inspector警告：

- Humanoid Avatarを持たないAnimator
- Manager未設定
- Manager複数
- `stopDistance > followDistance`
- Dialogue WindowがWorld Space Canvasでない
- Dialogue Canvasに`VRCUiShape`がない
- Dialogue Canvasに`GraphicRaycaster`がない、またはLayerがUIになっている
- SceneにEventSystemがない
- Dialogue TextまたはChoice Labelが`TextMeshProUGUI`でない
- Choice ButtonのNavigationがNoneでない

## 16. Portable Preset

### 16.1 ファイル形式

- 拡張子：`.vnpc`
- 内容：JSON
- Editor限定で読み書きする。

```json
{
  "format": "VNPCCharacter",
  "formatVersion": 1,
  "frameworkVersion": "0.1.5"
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
- Dialogue Window、Text、ScrollRect、Button
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
9. 必要に応じてManager Inspectorから共通Dialogue UIを生成する。既存UIを使用する場合は参照を設定し、必要に応じて`Repair Dialogue UI Events`を実行する。
10. 必要に応じてCharacter InspectorでDialogue Anchorを作成し、Scene Viewのプレビューから表示位置を調整する。
11. VRChatのBuild & TestでOwner／Remote動作を確認する。

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
23. Managerの選択状態にかかわらずPathのWaypoint番号と接続線がScene Viewへ表示されること
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
34. Managerの選択状態にかかわらずPathがScene Viewへ表示されること
35. Waypoint Materialの`_BaseColor`または`_Color`がPath線へ反映されること
36. Material Colorを取得できない場合にPath別Fallback Colorが使用されること
37. 複数Pathの色をManager Inspectorから個別に設定できること
38. Path描画物がRuntimeおよびVRChat Buildへ含まれないこと
39. Dialogue TextとChoice LabelsへTextMeshProUGUIを指定できること
40. TMP(VRC) CanvasとButton(VRC)の構成不備がInspectorへ警告されること
41. TMPへ変更後もMessage表示とChoice Label更新がローカルで動作すること
42. Manager Inspectorから共通Dialogue UIを生成し、必要な参照が自動設定されること
43. 3行を超える本文で縦Scrollbarを使用でき、Message切り替えと会話終了時に先頭へ戻ること
44. Dialogue Windowの背景とOutlineが表示され、個別Dialogue Panel参照を必要としないこと
45. Choice ButtonのOnClickがBacking UdonBehaviour経由で動作し、VRChat SDKのUnityEventFilterで削除されないこと
46. `Repair Dialogue UI Events`が旧VNPC Listenerを修復し、無関係なListenerを維持すること
47. Dialogue Anchor指定時にOffsetがAnchorのローカル座標として適用されること
48. Character InspectorでDialogue Windowをプレビューし、位置調整できること
49. Dialogueプレビュー用ObjectがSceneおよびVRChat Buildへ保存されないこと
50. Ownership移行後に旧Ownerのローカル`destination`を使用して移動しないこと
51. PathLoopの途中でOwnershipが移行しても、開始Waypointへ逆走せず現在区間から巡回を継続すること
52. PathLoopおよびPointAreaの到着点でOwnershipが移行した場合、`waitTime`経過後に次の候補へ進むこと
53. PointAreaの途中でOwnershipが移行しても、現在位置に最も近い有向区間から巡回を継続すること
54. PlayerFollow中のOwnership移行後に追従対象が即時再選択されること
55. LinkageArea内のOwnership移行後に、領域外を横切らない新しい目的地が選択されること
56. LinkageArea外でOwnershipが移行した場合に最寄り頂点へ復帰すること
57. 会話中のOwnership移行では移動せず、会話解除後の現在位置から経路を再構築すること
58. 異常なTransform差分をOwnership移行時の進行方向判定へ使用しないこと
59. 2Client以上のBuild & TestでOwner退出およびOwnership再移譲後も移動が継続すること

## 20. 未実装・将来候補

次は現行v0.1.6の正式仕様へ含めない。

- NavMeshによるStatic障害物回避
- NPC同士の衝突回避
- NPC同士のGreeting／会話
- Reaction Animation専用設定
- Talk Animation専用State
- 複数Idle Pattern
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
10. 同一Playerが同時に会話できるCharacterは1体とする。
11. RuntimeからEditor APIとファイルI/Oを分離する。
12. Preset ImportでScene依存参照を破壊しない。
13. 利用者が内部Animator Parameterを手入力しなくても動作できるようにする。
14. Character Ownership取得時はVRCObjectSyncが反映した現在TransformからMoveStateを再構築し、Transform同期を重複実装しない。
