# VNPC（VRChat NPC Framework）

VNPCは、VRChatの交流WorldへHumanoid NPCを配置するためのUdonSharp Frameworkです。

現在の仕様Versionは **v0.1.2** です。

## 主な機能

- PathLoop／PointArea／PlayerFollow／LinkageAreaによるDirect Movement
- VRCObjectSyncによるTransform同期
- Idle／Walk／Run Animatorの自動生成
- Playerへの視線追従と近接時の移動停止
- 単一話者の選択肢会話とGlobalFlag操作
- `.vnpc`によるCharacter設定のExport／Import

NavMeshによる経路探索や静的障害物回避は行いません。World制作者が安全なWaypointと移動領域を設定してください。

## 必要環境

- VRChatがサポートするUnity
- VRChat Worlds SDK
- UdonSharp

## 最小セットアップ

1. Sceneへ`VNPC_Manager`を追加します。
2. Humanoid Modelへ`VNPC_Character`を追加します。
3. Inspectorで`Validate and Auto Setup`を実行します。
4. Move Styleと移動設定を指定します。
5. Idle／Walk／Run AnimationClipを設定します。
6. `Generate / Rebuild Animator`を実行します。
7. VRChatのBuild & Testで動作を確認します。

ManagerがSceneに1個だけある場合は自動割り当てされます。複数ある場合は、使用するManagerをCharacterへ明示指定してください。

## ドキュメント

- 詳細な機能・設定・同期仕様：[Specification.md](Specification.md)
- Versionごとの変更内容：[UPDATELOG.md](UPDATELOG.md)
