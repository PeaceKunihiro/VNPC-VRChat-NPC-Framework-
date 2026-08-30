# VNPC Specification

## 1. 概要

### 1.1 名称
**VNPC（VRChat NPC Framework）**

VRChatワールド上へ、店員・MOB・案内役などとして利用できるHumanoid NPCを実装するためのUdonベースNPCフレームワーク。

### 1.2 目的
カフェや交流ワールド等で、以下のような「生活感のあるNPC」を比較的少ない設定で配置できることを目的とする。

- ワールド内を一定ルールで移動する
- 待機・歩行等のモーションを再生する
- プレイヤーへ視線やリアクションを返す
- 他NPCとの接触を回避し、必要に応じて仕草を行う
- 選択肢ベースの会話を行う
- 会話結果等からワールド共通フラグを変更する

利用者側では、原則としてHumanoidモデルへ `VNPC_Character` を追加するだけでNPCとして利用開始できるUXを目標とする。

---

## 2. 基本設計方針

### 2.1 公開コンポーネント
利用者が主に操作するコンポーネントは以下の2種類とする。

- `VNPC_Manager`
- `VNPC_Character`

内部処理の都合でUnity / VRChat標準コンポーネントを使用するが、可能な限り `VNPC_Character` 追加時に自動設定する。

### 2.2 責務分離

#### VNPC_Manager
ワールド全体で共有されるNPCシステム情報を管理する。

- NPC一覧
- 共有Path定義
- GlobalFlag
- GlobalFlagのネットワーク同期
- NPC登録
- NPC間で共有する参照情報

#### VNPC_Character
各NPC固有のローカル挙動を管理する。

- 移動方式
- 移動速度
- Path選択
- 現在Waypoint
- 待機
- Animator制御
- プレイヤー近接反応
- NPC近接反応
- 会話
- 選択肢
- ローカルState
- ManagerへのGlobalFlag操作要求

---

## 3. ネットワーク同期仕様

### 3.1 基本方針
ネットワーク同期は必要最小限とする。

同期対象は原則として以下の2系統のみ。

1. **NPC Transform**
   - `VRCObjectSync` を利用する
2. **GlobalFlag**
   - `VNPC_Manager` のSynced Variableで管理する

以下は原則として同期しない。

- NPCのIdle / Move / Greeting等の表示State
- プレイヤーへの視線状態
- プレイヤー近接リアクション
- 会話中かどうか
- 会話の現在ページ
- プレイヤーごとの選択肢進行
- NPCごとのローカル行動State

### 3.2 NPC位置同期

各NPC GameObjectへ `VRCObjectSync` を付与する。

```text
NPC
├ Animator
├ NavMeshAgent
├ VRCObjectSync
└ VNPC_Character
```

NPC位置はUdonSynced Vector3等で独自同期しない。

### 3.3 NPC移動AIの実行主体

NPCの移動先判断・NavMeshAgent制御は、原則としてNPCを制御するOwner側のみ実行する。

```text
Owner
 ├ VNPC_Character
 ├ 移動AI
 └ NavMeshAgent
        ↓
   Transform移動
        ↓
   VRCObjectSync
        ↓
 Remote Client
```

Remote側ではNPC移動AIによるTransform操作を行わず、`VRCObjectSync` の受信結果を使用する。

### 3.4 GlobalFlag

ワールド全体で結果を共有する必要がある状態のみGlobalFlagとして同期する。

例：

- 特定イベントの完了
- NPCの解禁
- 特定会話の達成
- ワールド設備の状態
- 特殊イベントの開始
- 会話分岐条件

ON/OFF主体の場合はビットフラグ化を検討する。

```text
bit 0 : Event_A_Complete
bit 1 : NPC_B_Unlock
bit 2 : Room_C_Unlock
bit 3 : SpecialDialogue_Enable
```

GlobalFlag変更は `VNPC_Manager` に集約する。

---

## 4. VNPC_Manager

### 4.1 役割

`VNPC_Manager` はNPCシステム全体の共有データを保持する。

想定構造：

```text
VNPC_Manager
├ Characters[]
├ Paths[]
├ GlobalFlags
└ Global Commands
```

### 4.2 Character管理

ManagerはScene内の `VNPC_Character` を一覧管理する。

想定Inspector：

```text
Characters
[0] NPC_A
[1] NPC_B
[2] NPC_C
[+][-]
```

`VNPC_Character` 追加時にScene内のManagerを自動検索し、Managerが1つのみ存在する場合は自動登録することを目標とする。

Managerが存在しない場合はInspector上で警告し、生成操作を提供する。

```text
VNPC_Managerが存在しません。

[Create VNPC Manager]
```

---

## 5. Path管理

### 5.1 基本方針

複数NPCが同一Pathを共有できるよう、Path定義は `VNPC_Manager` 側で管理する。

Pathごとの独立同期UdonBehaviourは原則作成しない。

固定Pathはワールド内の静的データであるため、ネットワーク同期不要。

各NPC側では以下のみ保持する。

- 使用Path ID
- 現在Point Index
- 進行方向
- Step
- 移動速度
- 待機時間
- その他NPC固有の移動設定

### 5.2 MoveStyle

以下を想定する。

#### PathLoop
複数Waypointを結んだループ経路。

```text
P0 → P1 → P2 → P3 → P0
```

NPCごとに開始位置・進行方向・速度・待機時間等を変更可能とする。

例：

```text
NPC A : Start 0 / Step +1
NPC B : Start 2 / Step +1
NPC C : Start 3 / Step -1
```

同じPathでも位相をずらして使用可能とする。

#### PointArea
特定地点を中心とした半径N[m]内を移動・待機する。

完全ランダムな目的地生成は避け、固定角度刻みの候補地点を利用する方式を優先する。

初期案：

- 角度：15°刻み
- 360° / 15° = 24方向
- 必要に応じて複数Radiusを持つ

例：

```text
Radius 1 = 1.5m
Radius 2 = 3.0m
Radius 3 = 4.5m

Angle = 0°, 15°, 30° ... 345°
```

候補地点を事前生成または初期化時に生成し、Runtimeでは主としてIndex操作で次目的地を決定する。

目的：

- ランダム地点再抽選の削減
- NavMesh探索回数の削減
- デバッグ容易性向上
- NPCごとの挙動再現性向上

#### LinkageArea
複数頂点で囲んだ領域内を移動する方式。

初期案では以下のいずれかを検討する。

1. Editor上で有効な候補地点を事前生成する
2. ポリゴン内部判定を行い候補地点を生成する

Runtimeでのランダム点生成・再抽選を極力減らす。

#### PlayerFollow
特定プレイヤーを追従する。

設定候補：

- Target Mode
- Follow Distance
- Move Speed
- Stop Distance
- Repath Interval

プレイヤー位置への `SetDestination()` 更新は毎フレーム行わず、一定間隔で更新する。

---

## 6. 移動AI

### 6.1 基本方針

高度なゲームAIではなく、交流ワールド上で「生活しているように見えるMOB」を目的とする。

そのため完全ランダムAIではなく、少数の決定論的パターンをNPCごとにずらして使用する。

NPCごとに以下を変更可能とする。

- StartIndex
- Step
- Direction
- WaitPattern
- MoveSpeed
- RadiusPattern

例：

```text
NPC A
Start = 0
Step = +1
Wait = 3 sec

NPC B
Start = 7
Step = +2
Wait = 5 sec

NPC C
Start = 15
Step = -1
Wait = 2 sec
```

### 6.2 更新頻度

不要な毎フレーム処理を避ける。

基本：

```text
Moving
 ↓
目的地到着判定
 ↓
Waiting
 ↓
待機終了
 ↓
次目的地選択
 ↓
Moving
```

次目的地決定と `NavMeshAgent.SetDestination()` はイベント発生時のみ実行する。

近接判定などは必要に応じて低頻度Pollingとする。

目安：

- Player距離判定：0.1～0.5秒程度
- NPC距離判定：0.2～0.5秒程度
- Path更新：目的地到着時のみ
- Dialogue：イベント駆動
- GlobalFlag：変更時のみ同期

---

## 7. VNPC_Character

### 7.1 目標UX

Humanoidモデルに `VNPC_Character` を追加するだけでNPCとして利用可能な状態を構築する。

```text
Humanoid Model
└ VNPC_Character
```

追加後、内部的には必要に応じて以下を自動構築する。

```text
Humanoid Model
├ Animator
├ NavMeshAgent
├ VRCObjectSync
└ VNPC_Character
```

### 7.2 自動チェック

`VNPC_Character` はEditor上で以下を確認する。

- Animator存在確認
- Animator.avatar存在確認
- Humanoid Avatar判定
- NavMeshAgent存在確認
- VRCObjectSync存在確認
- VNPC_Manager存在確認

不足時は可能なものを自動追加する。

Humanoidではない場合は警告表示する。

### 7.3 Humanoid Bone利用

必要に応じてHumanoid Boneを自動取得する。

例：

- Head
- LeftEye
- RightEye

これらを視線追従等へ使用する。

---

## 8. Character Inspector

機能数が増えても利用者側の複雑さを抑えるため、Custom Inspectorでカテゴリ別にFoldout表示する。

想定UI：

```text
VNPC Character

[General]
 Manager
 Character ID

[Movement]
 Move Style
 Path
 Speed
 Wait Time

[Animations]
 Idle Animations[]
 Move Animations[]
 Reaction Animations[]

[Player Interaction]
 Look At Player
 Reaction Distance
 Follow Player

[NPC Interaction]
 Avoid NPC
 Greeting
 Reaction Distance

[Dialogue]
 Messages[]
 Choices[]
 Commands[]
```

MoveStyleによって不要な項目は非表示にする。

---

## 9. Animation

### 9.1 基本モーション

最低限以下を設定可能とする。

```text
IdleAni[]
MoveAni[]
```

追加候補：

```text
GreetingAni[]
ReactionAni[]
TalkAni[]
```

### 9.2 Animator制御

NPC移動速度等を利用してIdle / Moveを切り替える。

NavMeshAgentを移動主体とし、Animatorは原則として表示モーションを担当する。

Root MotionによるTransform移動とNavMeshAgent移動を同時に行わない設計を基本とする。

### 9.3 Animator Controller

既存AnimatorControllerが設定されている場合に無条件上書きしない。

候補仕様：

```text
Animator Mode
- VNPC Default
- Existing Controller
```

VNPC標準Controllerを使用する場合は、必要Parameterを定義する。

例：

- Speed
- IsMoving
- Reaction
- ActionID

---

## 10. プレイヤー近接制御

### 10.1 LookAt

プレイヤーが一定距離以内に存在する場合、NPCがプレイヤー方向を見る。

視線・Head制御はローカル処理とし、ネットワーク同期しない。

### 10.2 Reaction

一定距離以内にプレイヤーが入った場合、リアクションを実施可能とする。

例：

- 会釈
- 挨拶
- 振り向き
- 専用Animation
- 会話開始可能状態

設定候補：

```text
Reaction Distance
Reaction Animation
Cooldown
```

---

## 11. NPC近接制御

### 11.1 衝突回避

基本的なNPC間の移動回避は `NavMeshAgent` のObstacle Avoidanceを利用する。

必要に応じて以下を調整する。

- Radius
- Avoidance Priority
- Obstacle Avoidance Quality

### 11.2 NPC間リアクション

NPC同士が一定距離以内に入った際、会釈等のリアクションを実行可能とする。

例：

```text
NPC A
  ↓
NPC B検出
  ↓
Greeting
  ↓
Cooldown
```

二重発火防止ルールが必要。

候補：

- Character IDの小さい側のみイベント開始
- Manager側でペアを一時ロック
- Cooldownで再発火抑制

NPC同士の会話・リアクションStateは原則ローカル処理とする。

---

## 12. 会話システム

### 12.1 基本仕様

選択肢ベースの会話を実装する。

想定データ：

```text
Message
├ Text
└ Choices[]
    ├ Label
    ├ NextMessage
    └ Command
```

### 12.2 Character Inspector案

```text
Messages
[0]
  Message Field

  Choices
  [0]
    Label
    Command
    Parameter

  [1]
    Label
    Command
    Parameter
```

### 12.3 会話State

以下はプレイヤーごとのローカルStateとして扱い、同期しない。

- 現在表示中Message
- 現在Choice
- 会話中フラグ
- 一時会話変数

GlobalFlagに影響する選択肢のみManagerへ操作要求する。

---

## 13. Command

会話選択肢等からNPCまたはワールドへ処理を実行する。

初期候補：

- SetGlobalFlag
- ClearGlobalFlag
- ToggleGlobalFlag
- PlayAnimation
- ChangeMessage
- EnableObject
- DisableObject
- MoveTo
- ChangeMoveStyle

例：

```text
Parameter == N
 ↓
Animation
 ↓
Message
```

Commandは拡張可能な構造を目標とする。

---

## 14. State管理

単一の巨大Stateで全挙動を管理しない。

カテゴリ別にStateを分離する。

例：

```text
Movement State
- Idle
- Moving
- Waiting

Interaction State
- None
- Looking
- Greeting

Dialogue State
- None
- Talking
```

これにより以下のような同時状態を許容する。

- 歩きながらプレイヤーを見る
- 待機しながら会話する
- 移動終了後にGreetingへ遷移する

Stateは原則ローカル管理とし、同期しない。

---

## 15. Udon構成方針

### 15.1 同期Udonの集約

同期UdonBehaviourを必要以上に分散させない。

基本構成：

```text
VNPC_Manager
└ GlobalFlag Sync

NPC_A
├ VNPC_Character
└ VRCObjectSync

NPC_B
├ VNPC_Character
└ VRCObjectSync
```

`VNPC_Character` は原則としてSynced Variableを持たない。

### 15.2 Path

Path定義のためだけに同期UdonBehaviourを追加しない。

PathはManager内の共有設定データ、またはTransform参照として保持する。

---

## 16. 想定利用手順

### 最小手順

1. Sceneへ `VNPC_Manager` を配置
2. NavMeshを設定
3. Humanoidモデルを配置
4. Humanoidモデルへ `VNPC_Character` を追加
5. 自動検出されたAnimator / VRCObjectSync / NavMeshAgentを確認
6. MoveStyleを選択
7. PathまたはAreaを選択
8. Idle / Move Animationを設定
9. 必要に応じて会話・リアクションを設定

目標として、NPC追加時に利用者がUnity / VRC内部同期構造を意識しなくても使用できるものとする。

---

## 17. MVP候補

初期実装では以下を優先する。

### 必須
- VNPC_Manager
- VNPC_Character
- Humanoid自動検出
- NavMeshAgent自動追加
- VRCObjectSync自動追加
- Character自動登録
- PathLoop
- PointArea
- PlayerFollow
- Idle Animation
- Move Animation
- プレイヤーLookAt
- 選択肢会話
- GlobalFlag
- GlobalFlag同期

### 後続候補
- LinkageArea
- NPC同士のGreeting
- 高度なCommand
- 複数Idle Pattern
- NPC同士の会話
- Editor上でのArea候補地点自動生成
- Path可視化
- 設定Validation
- デバッグ表示

---

## 18. 設計上の原則

1. **NPC位置同期はVRCObjectSyncへ任せる**
2. **UdonでTransform同期を再実装しない**
3. **Globalに共有する必要のある状態だけ同期する**
4. **会話・リアクション・表示Stateは原則ローカル**
5. **Pathは複数NPCで共有する**
6. **移動AIは完全ランダムより決定論的パターンを優先する**
7. **Runtimeでの不要なNavMesh再探索を減らす**
8. **毎フレームの重い判断処理を避ける**
9. **利用者が追加する主コンポーネントはVNPC_Characterのみとする**
10. **内部実装の複雑さより、利用者側の設定簡略化を優先する**
11. **同期UdonBehaviourを必要以上に増やさない**
12. **Managerは共有情報、CharacterはNPC固有挙動を担当する**

---

## 19. 現時点で未確定の事項

以下は実装時に検証が必要。

- `VNPC_Character` 追加時のVRCObjectSync自動付与方法
- VRCSDK / UdonSharp環境におけるEditor自動設定の最適な実装方法
- ManagerへのCharacter自動登録方式
- PointArea候補地点のEditor事前生成方式
- LinkageAreaの最終アルゴリズム
- LookAtの実装方式
- AnimatorController統合方式
- NPC Owner移行時のAI再開処理
- GlobalFlagの具体的なデータ構造
- Dialogue / CommandデータのInspector表現
- NPC数増加時のCPU負荷測定
- VRCObjectSyncを使用した複数NPC同時移動時のネットワーク負荷測定

---

## 20. 参考となる既存実装

設計検討時に確認した類似系統：

- NPC狐Akyo【VRChatワールドギミック】
- 自動歩行会話システム【VRChat】
- AvatarNPC
- AvatarNPC TalkAction
- Narazaka Sync NPC
- VRChat公式 AI Navigation Example

既存実装と重複する機能はあるが、本仕様では以下を統合した汎用NPCフレームワークを目標とする。

- 共有Path管理
- NPC単体コンポーネントによる簡易導入
- 決定論的な軽量移動AI
- Local State主体の低同期設計
- 選択肢Dialogue
- GlobalFlag連携
- 複数NPCの一元管理
