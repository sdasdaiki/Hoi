# 江戸時代・ターン制ヘックスストラテジー 詳細開発設計書

## 1. プロジェクト概要・開発環境
* **コンセプト**: 江戸時代を舞台にしたターン制ヘックス（正六角形）ストラテジーゲーム。本陣（指令部隊）からの距離や地形に応じた「命令・報告の遅延（情報戦）」を核とし、将来的にHearts of Iron IV（HoI4）のような師団設計・将来的に補給線・人的資源・偵察といった高度な戦略要素を拡張できる設計とする。ゲームバランス調整のための攻撃力や防御力等の数値の調整、地形の追加などが容易になるようにする。
* **エンジン・バージョン**: Unity 2022.3.62f3 LTS (2D Universal Render Pipeline)
* **使用言語**: C# (.NET Standard 2.1)
* **進行形式**: ターン制（Turn-Based）

---

## 2. アーキテクチャ原則（GitHub Copilot向けコーディング規約）
コードを生成・修正する際は、必ず以下の原則を遵守すること。

1. **データと描画（View）の完全分離**
   * マップ情報、ユニットのステータス、命令キュー、視界判定などのゲームロジックはすべて純粋なC#クラス（MonoBehaviourを継承しないデータクラス・マネージャークラス）で完結させる。
   * `Tilemap` や `SpriteRenderer` などのUnityコンポーネントは、データ層の変更イベント（`Action` / `event`）を受け取って画面表示を更新する「描画専用（View）」として実装する。
2. **将来のHoI4ライクな拡張を見据えたデータ構造**
   * 初期実装（MVP）ではシンプルな数値で動作させるが、後から「師団設計」「補給線」「兵士人口（Manpower）」「偵察」を破壊的変更なしで追加できるよう、あらかじめクラス構造や配列・パラメータ枠を定義しておく。
3. **座標系の統一**
   * Unityの `Tilemap`（Odd-R / Even-R などの Offset座標）と、ヘックスの距離計算・隣接マス探索に便利な **Axial座標（`q, r`）** または **Cube座標（`x, y, z`）** を相互変換できるユーティリティクラス（`HexUtility`）を必ず用意する。

---

## 3. フォルダ・ファイル構成 (`Assets/` 配下)
* `Assets/`
  * `Maps/` : エディタ開発時に作成・保存されるJSONマップデータ
  * `Prefabs/`
    * `Units/` : 各兵科のユニット表示用プレハブ（Headquarters, Infantry等）
    * `UI/` : 地形ボタン、部隊アイコン、ステータスウィンドウ等のUIプレハブ
  * `Scenes/`
    * `TitleScene.unity` : タイトル・マップ選択画面
    * `MapEditorScene.unity` : タッチ＆なぞり操作によるマップ作成・JSON保存シーン
    * `BattleScene.unity` : メインの戦闘・戦略シミュレーションシーン
  * `Scripts/`
    * `Core/`
      * `GameManager.cs` : シーン間のマップデータ受け渡し、ゲーム全体管理
      * `TurnManager.cs` : バトルのフェーズ管理（配置→命令→解決→勝敗判定）
    * `Map/`
      * `HexCoordinates.cs` : Offset座標とAxial/Cube座標の変換、距離計算
      * `HexCell.cs` : 1マスごとのデータ（地形、移動コスト、将来の補給・視界枠）
      * `HexGrid.cs` : マップ全体のセル配列管理、隣接マス探索、パスファインディング
      * `HexTilemapView.cs` : HexGridのデータをTilemap（単色タイル）へ描画するView
      * `MapSerializer.cs` : マップデータのJSON書き出し・読み込み処理
    * `Editor/`（※インゲーム用のマップエディタ機能。UnityEditor拡張ではない）
      * `MapEditorController.cs` : タッチ/ドラッグ判定、選択中の地形ブラシ適用、保存UI連携
    * `Units/`
      * `UnitEnums.cs` : 兵科タイプ（UnitType）、陣営（Faction）の定義
      * `BattalionData.cs` : 大隊データ（将来の師団設計パーツ用）
      * `UnitData.cs` : 師団（部隊）データ（兵員数、組織率、ステータス、命令キュー）
      * `UnitView.cs` : 部隊のマップ上での表示・移動アニメーション・ゴースト表示
    * `Command/`
      * `Order.cs` : 命令データ（移動先座標、攻撃対象ID、残り遅延ターン数）
      * `CommandDelaySystem.cs` : 本陣からの距離・地形に基づく遅延ターン計算とキュー処理
      * `FogOfWarSystem.cs` : 視界計算と、報告遅延による敵情報（ゴースト）の更新
    * `UI/`
      * `MapEditorUI.cs` : 地形パレット（平地・森・山・川）、マップ名入力、保存ボタン
      * `BattleUI.cs` : 部隊配置パネル、ターン終了ボタン、部隊ステータス表示
  * `Tiles/` : 単色の正六角形Tileアセット（Plain, Forest, Mountain, River）

---

## 4. データ構造・クラス詳細仕様

### 4.1 マップ・地形システム (`Scripts/Map/`)

#### 地形種別 (`TerrainType`)
川は「マス自体を川とする（パターンA）」を採用し、初期段階ではRule Tileを使わず単色の六角形タイルで描画する。

| Enum値 | 日本語名 | タイル色（仮） | 基本移動コスト | 防御補正 | 視界遮蔽 | 備考・将来拡張 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `Plain` | 平地 | 黄緑 (`#A8E6CF`) | `1` | `0%` | なし | 標準地形 |
| `Forest` | 森 | 深緑 (`#2D6A4F`) | `2` | `+20%` | あり | 騎兵は追加コスト(+1) |
| `Mountain` | 山 | 茶/灰 (`#8D6E63`) | `3` | `+40%` | あり | 砲兵の射程+1、騎兵進入不可(または高コスト) |
| `River` | 川 | 水色 (`#4FC3F7`) | `3` | `-20%` | なし | 渡河ペナルティ（防御低下） |

#### `HexCell` クラス設計
    [System.Serializable]
    public class HexCell
    {
        public int x; // Offset X座標
        public int y; // Offset Y座標
        public TerrainType terrainType;
        public int deployZoneFaction; // 0: なし, 1: 自軍初期配置エリア, 2: 敵軍初期配置エリア

        // --- 将来のHoI4拡張用パラメータ枠 ---
        public int infrastructureLevel = 1; // 街道・インフラレベル（補給効率に影響）
        public float currentSupply = 100f;  // 現在の補給物資量
        public bool isVisibleToPlayer = false; // 現在の視界内フラグ
    }

#### マップJSON保存・読込仕様 (`MapSerializer.cs`)
* **保存タイミング**: `MapEditorScene` でプレイヤーが地形を塗った後、UIの「保存（Save）」ボタンを押した瞬間に実行する。
* **保存先パス**:
  * Unityエディタ実行時: `Application.dataPath + "/Maps/" + mapName + ".json"`（開発中に確認しやすいようプロジェクト内に保存）
  * ビルド後（PC / WebGL等）: `Application.persistentDataPath + "/Maps/" + mapName + ".json"`
* **JSONフォーマット例**:
      {
        "mapName": "Kawanakajima_01",
        "width": 20,
        "height": 15,
        "cells": [
          { "x": 0, "y": 0, "terrainType": 0, "deployZoneFaction": 1, "infrastructureLevel": 1 },
          { "x": 1, "y": 0, "terrainType": 3, "deployZoneFaction": 0, "infrastructureLevel": 1 }
        ]
      }

---

### 4.2 マップエディタ操作仕様 (`MapEditorController.cs`)
* **入力方式**: マウスクリック／ドラッグ、およびタッチパネルのタップ／スワイプに対応（Unityの `Input.GetMouseButton(0)` と `EventSystem.current.IsPointerOverGameObject()` を併用し、UIボタン操作時の誤爆を防ぐ）。
* **操作フロー**:
  1. 画面下部（または側面）の地形パレットボタン（「平地」「森」「山」「川」「自軍エリア」「敵軍エリア」）をタップし、`selectedBrush` を切り替える。
  2. キャンバス（マップ）上の任意のマスをタップすると、その1マスの `HexCell` データを書き換わり、`HexTilemapView` が即座にタイルを更新する。
  3. 指やマウスボタンを押したままマップ上を**なぞる（ドラッグする）**と、ポインタが通過したすべてのマスが連続して塗り替えられる。

---

### 4.3 ユニット・師団システム (`Scripts/Units/`)

#### 初期兵科 (`UnitType`)
| Enum値 | 兵科名 | 特徴・役割 | 攻撃力(目安) | 防御力(目安) | 移動力 | 射程 | 偵察力 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `Headquarters` | 本陣（指令部隊） | プレイヤーの分身。命令・報告の起点。撃破されると敗北。 | 5 | 10 | 2 | 1 | 2 |
| `Infantry` | 歩兵（槍足軽） | 前線維持・壁役。コストが低く安定。 | 15 | 20 | 3 | 1 | 1 |
| `Musketeer` | 銃兵（鉄砲隊） | 遠距離火力。近接防御が低い。 | 25 | 8 | 3 | 2 | 1 |
| `Cavalry` | 騎兵 | 高機動力・突撃力。森・山が苦手。 | 22 | 12 | 5 | 1 | 3 |
| `Artillery` | 砲兵（大筒） | 超長射程・高火力だが移動が極端に遅い。 | 35 | 5 | 1 | 3 | 0 |

#### 将来のHoI4拡張を見据えた師団データ構造 (`UnitData.cs`)
最初から「1部隊＝1兵科」ではなく、**「1つの師団（`UnitData`）の中に、複数の大隊（`BattalionData`）を持つリスト構造」**にしておく。初期MVPではリストに1つだけ大隊を入れて動かす。
    [System.Serializable]
    public class BattalionData
    {
        public UnitType unitType;
        public int maxManpower;     // 最大兵員数（例: 1000人）
        public int currentManpower; // 現在兵員数
        public float equipmentRatio = 1.0f; // 装備充足率（0.0〜1.0）
    }

    [System.Serializable]
    public class UnitData
    {
        public string unitId;
        public string unitName;
        public int faction; // 1: 自軍, 2: 敵軍
        public int x;
        public int y;

        // 師団を構成する大隊リスト（将来、師団設計画面でここを編集する）
        public List<BattalionData> battalions = new List<BattalionData>();

        // 戦闘・戦略ステータス（大隊リストの合計・平均から算出）
        public float organization = 100f;    // 組織率（士気・指揮統制。0になると撤退・壊滅）
        public float maxOrganization = 100f;
        public float supplyStatus = 1.0f;    // 補給状態（将来用: 1.0で完全補給）

        // 命令キュー（遅延実行用）
        public Queue<Order> orderQueue = new Queue<Order>();
    }

---

### 4.4 コアシステム：命令・報告の遅延 (`Scripts/Command/`)

#### 1. 遅延ターン数の計算式 (`CommandDelaySystem.cs`)
本陣（`Headquarters`）の座標 P_HQ と、対象部隊の座標 P_unit の**ヘックス距離（最短マス数）**を D とする。
* **基本遅延ターン数**: `DelayTurns = Floor(D / 3)`
  （例：距離0〜2マスなら `0ターン（即時）`、3〜5マスなら `1ターン遅れ`、6〜8マスなら `2ターン遅れ`）
* **地形・偵察補正（将来拡張）**:
  * 本陣と部隊の間に「山」や「川」が多い場合は `+1ターン` 遅延。
  * 部隊の「偵察力（`recon`）」が高い場合や、「狼煙（のろし）」スキル使用時は `-1ターン` 短縮。

#### 2. 命令の遅延（Command Delay）
* プレイヤーが部隊に「移動（座標X, Yへ）」または「攻撃（敵IDへ）」を指示すると即座には動かず、部隊の「命令キュー（`Queue<Order>`）」に格納される。
* `Order` は対象部隊の `orderQueue` に追加される。
* 毎ターンの「解決フェーズ」で `remainingDelayTurns` が `1` 減少し、`0` になった瞬間にその命令が現地で実行される。
* **UI表現**: 命令を出した直後は、マップ上に「伝令の移動ルート（点線）」と「あと◯ターンで命令到達」のアイコンが表示される。

#### 3. 報告の遅延と視界（Information Delay & Fog of War）
* 自軍ユニットの周囲（視界範囲内）にいる敵ユニットのみ「発見状態」となる。
* 敵を発見した自軍部隊と「本陣」との距離が D の場合、その敵の位置情報がプレイヤー画面に反映されるまでに `DelayTurns` ターンかかる。
* 遅延が発生している間、プレイヤーの画面には**「`DelayTurns` ターン前に目撃された古い位置」に半透明の敵アイコン（ゴースト）**が表示される（「敵はそこにいると報告を受けたが、現在はすでに移動しているかもしれない」という緊張感を生む）。

---

### 4.5 バトル進行フロー（ステートマシン）
`TurnManager.cs` は以下の状態（`BattleState` enum）を順番に遷移させる。

1. **`DeploymentPhase`（初期配置フェーズ）**
   * マップJSONを読み込み生成。
   * プレイヤーは `deployZoneFaction == 1` のマスに、手持ちの部隊（本陣1＋任意の兵科）を配置する。
   * 敵軍は `deployZoneFaction == 2` のマスに自動配置される。
   * 「開戦（Start Battle）」ボタン押下で次へ。
2. **`PlayerCommandPhase`（プレイヤー命令フェーズ）**
   * プレイヤーは自軍部隊を選択し、移動や攻撃の指示を出す（命令キューへ登録）。
   * すでにキューに入っている待機中の命令のキャンセル（伝令の呼び戻し／上書き）も可能。
   * 「ターン終了（End Turn）」ボタン押下で次へ。
3. **`ExecutionPhase`（解決フェーズ）**
   * 全自軍部隊の `orderQueue` の遅延ターン数を `-1` し、`0` になった命令を実行（移動・戦闘）。
   * 敵AIの思考と行動実行。
   * 戦闘ダメージ計算（兵員数 `currentManpower` と組織率 `organization` の減少）。
4. **`ReconAndReportPhase`（視界・伝令更新フェーズ）**
   * 視界の再計算と、本陣への報告遅延タイマーの進行、ゴースト表示の更新。
5. **`CheckVictoryPhase`（勝敗判定フェーズ）**
   * **勝利条件**: 敵の `Headquarters`（本陣）の兵員数が0になる、または敵の全滅。
   * **敗北条件**: 自軍の `Headquarters`（本陣）の兵員数が0になる、または自軍の全滅。
   * 勝敗が決まっていなければターン数を `+1` し、`PlayerCommandPhase` へ戻る。

---

## 5. 段階的実装ステップ（Copilotへの指示順序）

開発は以下の順番で1ファイル・1機能ずつ実装し、動作確認を行いながら進める。

* **Phase 1: マップ基盤と描画**
  * `HexCoordinates.cs` / `HexCell.cs` / `HexGrid.cs` / `HexTilemapView.cs` の作成。
  * Unityシーン上に `Grid` と `Tilemap` を配置し、スクリプトから指定サイズの平地マップが描画されることを確認する。
* **Phase 2: タッチ＆なぞりマップエディタとJSON保存**
  * `MapSerializer.cs` / `MapEditorController.cs` / `MapEditorUI.cs` の作成。
  * 地形ボタンを押してタップ＆なぞりで「平地・森・山・川」を塗り、JSONファイルとして保存・再読込できることを確認する。
* **Phase 3: ユニット配置と基本移動**
  * `UnitData.cs` / `UnitView.cs` / `TurnManager.cs` の作成。
  * 保存したJSONマップを読み込み、配置フェーズで自軍ユニットを置き、地形の移動コストを考慮して移動できることを確認する。
* **Phase 4: 戦闘と勝敗判定**
  * 射程判定、攻撃・反撃のダメージ計算、本陣撃破時のリザルト画面実装。
* **Phase 5: 命令・報告の遅延システム**
  * `CommandDelaySystem.cs` / `FogOfWarSystem.cs` の実装。本陣からの距離に応じた命令キュー遅延と、敵のゴースト表示を実装する。
* **Phase 6: HoI4ライクな戦略拡張**
  * 補給線（街道からの物資輸送と補給切れペナルティ）、師団設計画面（大隊スロットのカスタマイズ）、兵士人口（Manpower）プールからの補充システムの実装など。