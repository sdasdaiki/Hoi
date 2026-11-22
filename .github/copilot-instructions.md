# 江戸時代・ターン制ヘックスストラテジー 詳細開発設計書

## 1. プロジェクト概要・開発環境
- **コンセプト:** 江戸時代を舞台にしたターン制ヘックス（正六角形）ストラテジーゲーム。本陣（指令部隊）からの距離や地形に応じた「命令・報告の遅延（情報戦）」を核とし、将来的に Hearts of Iron IV (HoI4) のような師団設計・補給線・人的資源・偵察といった高度な戦略要素を拡張できる設計とする。
- **エンジン・バージョン:** Unity 2022.3.62f3 LTS (2D Universal Render Pipeline)
- **使用言語:** C# (.NET Standard 2.1)
- **進行形式:** ターン制 (Turn-Based)
- **名前空間（Namespace）:** すべてのスクリプトは機能ごとに `EdoStrategy.Core` / `EdoStrategy.Map` / `EdoStrategy.Units` / `EdoStrategy.Combat` / `EdoStrategy.Command` / `EdoStrategy.UI` のいずれかに所属させる。

---

## 2. アーキテクチャ原則（GitHub Copilot向け厳密規約）
コードを生成・修正する際は、必ず以下の原則を遵守すること。

1. **データ駆動設計（OCP：開放閉鎖の原則）の徹底:**
   - 地形種別・兵科種別の識別子は `enum` のハードコード分岐（`switch` / `if`）に依存せず、すべて **`string` 型のID**（例: `"plain"`, `"forest"`, `"mountain"`, `"river"` / `"headquarters"`, `"infantry"`, `"musketeer"`, `"cavalry"`, `"artillery"`）で管理する。
   - 数値調整や新規地形・新規兵科の追加は、ScriptableObject（`TerrainDefinition` / `UnitDefinition`）の新規作成・編集のみで完結させ、既存のコアロジック（移動計算・戦闘計算・視界計算）は一切修正しない。
2. **非破壊的なデータ構造の拡張:**
   - 将来の「師団設計」「補給線」「兵士人口（Manpower）」「偵察」を既存クラスの破壊的変更なしで追加できるよう、あらかじめ配列・リスト・パラメータ枠を定義しておく。
3. **データ（Model/Logic）と描画（View）の完全分離:**
   - マップ情報、ユニット状態、命令キュー、視界判定、戦闘計算はすべて純粋なC#クラス（`MonoBehaviour` 非継承）で実装する。
   - `Tilemap` や `SpriteRenderer` を操作する `MonoBehaviour` は、データ層が発火する C# イベント（`event Action<...>`）を購読して画面を更新する「描画専用（View）」に徹する。
4. **Unity `JsonUtility` シリアライズ制約の遵守:**
   - JSON保存対象のデータクラスでは、Unity標準の `JsonUtility` がシリアライズできない型（`Dictionary`、`Queue`、`Stack`、2次元配列 `[,]`）を使用しない。
   - グリッド配列は 1次元配列 `HexCell[]`（インデックス計算: `y * width + x`）を使用し、命令キューは `List<Order>` の先頭（インデックス `0`）を処理する方式で実装する。
5. **整数千分率（Permille）による高解像度数値演算:**
   - ステータス補正・地形補正・包囲ペナルティ・組織率・補給率はすべて **千分率整数（`int`, `1000` = `100.0%` = `1.0倍`）** で定義・演算する。途中の計算で小数丸め誤差による数値潰れを防ぎ、最終ダメージ算出時のみ整数化する。

---

## 3. 座標系・ヘックス数学仕様 (`HexUtility.cs`)
Unityの `Grid` コンポーネントは **Hexagonal Pointy Top（尖頂六角形）**、座標系は **Odd-R Offset座標**（奇数行 `y % 2 != 0` が右へ `+0.5` シフト）に統一する。

### 3.1 Offset座標 `(x, y)` と Axial座標 `(q, r)` の相互変換式
- **Offset `(x, y)` → Axial `(q, r)`:**
  - `q = x - (y - (y & 1)) / 2`
  - `r = y`
- **Axial `(q, r)` → Offset `(x, y)`:**
  - `x = q + (r - (r & 1)) / 2`
  - `y = r`

### 3.2 ヘックス距離と隣接6方向ベクトル（Axial座標基準）
- **2点 $(q_1, r_1)$ と $(q_2, r_2)$ の最短ヘックス距離 $D$:**
  - $D = \frac{|q_1 - q_2| + |q_1 + r_1 - q_2 - r_2| + |r_1 - r_2|}{2}$
- **隣接6方向ベクトル（時計回りインデックス `0`〜`5`）:**
  - `0`（右）: `(+1, 0)`
  - `1`（右下）: `(0, +1)`
  - `2`（左下）: `(-1, +1)`
  - `3`（左）: `(-1, 0)`
  - `4`（左上）: `(0, -1)`
  - `5`（右上）: `(+1, -1)`

---

## 4. フォルダ・ファイル構成 (`Assets/` 配下)
- `Assets/`
  - `Data/`
    - `Terrains/` : `TerrainDefinition` アセット（Plain, Forest, Mountain, River）
    - `Units/` : `UnitDefinition` アセット（Headquarters, Infantry, Musketeer, Cavalry, Artillery）
  - `Maps/` : エディタ開発時に作成・保存されるJSONマップデータ
  - `Prefabs/`
    - `Units/` : 部隊表示用プレハブ（実体アイコン・ゴーストアイコン・ステータスバー）
    - `UI/` : 地形パレットボタン、部隊配置ボタン、命令アイコンプレハブ
  - `Scenes/`
    - `TitleScene.unity` : タイトル・マップ選択画面
    - `MapEditorScene.unity` : タッチ＆なぞり操作によるマップ作成・JSON保存シーン
    - `BattleScene.unity` : メインの戦闘・戦略シミュレーションシーン
  - `Scripts/`
    - `Core/`
      - `GameManager.cs` : マスターデータレジストリ保持、シーン間のマップJSON受け渡し
      - `TurnManager.cs` : バトルのステートマシン管理（配置→命令→解決→偵察報告→勝敗判定）
    - `Map/`
      - `HexUtility.cs` : Odd-R ⇔ Axial変換、距離計算、隣接探索、方向インデックス判定
      - `TerrainDefinition.cs` : 地形マスターデータ（ScriptableObject）
      - `HexCell.cs` : 1マスごとのランタイムデータ
      - `HexGrid.cs` : マップ全体の1次元セル配列管理、移動コスト計算、A*パスファインディング
      - `HexTilemapView.cs` : `HexGrid` の変更イベントを受け取り `Tilemap` を更新するView
      - `MapSerializer.cs` : `MapSaveData` のJSONシリアライズ・ファイル保存/読込
    - `Editor/`（※インゲーム用のマップエディタ機能。`UnityEditor` 名前空間は使用しない）
      - `MapEditorController.cs` : タッチ/ドラッグ判定、選択ブラシ適用、保存UI連携
    - `Units/`
      - `UnitDefinition.cs` : 兵科マスターデータ（ScriptableObject）
      - `BattalionData.cs` : 大隊データ（師団設計の最小構成単位）
      - `UnitData.cs` : 師団（部隊）データ（兵員数、組織率、補給、命令リスト）
      - `UnitView.cs` : 部隊・ゴーストのマップ描画、移動補間、命令ライン描画
    - `Combat/`
      - `CombatModifier.cs` : 地形・包囲・兵科相性・補給などの補正を動的リストで扱う修飾子
      - `CombatResolver.cs` : 包囲判定、修飾子集約、兵員・組織率ダメージ計算エンジン
    - `Command/`
      - `Order.cs` : 命令データ（種別、目標座標、対象ユニットID、残り遅延ターン数）
      - `CommandDelaySystem.cs` : 本陣からの距離・経路地形に基づく遅延計算と命令キュー進行
      - `IntelReport.cs` : 偵察報告データ（発見敵ID、目撃座標、本陣到達までの残りターン数）
      - `FogOfWarSystem.cs` : 視界判定、報告遅延タイマー進行、ゴースト座標の更新
    - `UI/`
      - `MapEditorUI.cs` : 地形パレット、陣営エリアブラシ、マップ名入力、保存ボタン
      - `BattleUI.cs` : 部隊配置パネル、ターン終了ボタン、選択部隊・敵情報パネル
  - `Tiles/` : 単色の正六角形Tileアセット（Plain, Forest, Mountain, River）

---

## 5. データ構造・クラス詳細仕様

### 5.1 マップ・地形システム (`EdoStrategy.Map`)

#### `TerrainDefinition` (ScriptableObject)
初期マスターデータとして以下の4種を作成する（川はマス自体を川とするパターンAを採用し、単色タイルで描画する）。

| `terrainId` | 表示名 (`displayName`) | タイル色（仮） | 基本移動コスト (`baseMoveCost`) | 防御倍率 (`defenseMultiplier`) | 視界遮蔽コスト (`visionCost`) | 命令遅延加算 (`commandDelayPenalty`) | 備考 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `"plain"` | 平地 | 黄緑 (`#A8E6CF`) | `1` | `1000` (0%) | `1` | `0` | 標準地形 |
| `"forest"` | 森 | 深緑 (`#2D6A4F`) | `2` | `1200` (+20%) | `2` | `0` | 視界を遮りやすい |
| `"mountain"` | 山 | 茶/灰 (`#8D6E63`) | `3` | `1400` (+40%) | `3` | `1` | 伝令遅延+1、防御有利 |
| `"river"` | 川 | 水色 (`#4FC3F7`) | `3` | `800` (-20%) | `1` | `1` | 渡河ペナルティ（防御低下） |

    [CreateAssetMenu(fileName = "NewTerrain", menuName = "EdoStrategy/TerrainDefinition")]
    public class TerrainDefinition : ScriptableObject
    {
        public string terrainId;
        public string displayName;
        public TileBase tileAsset;
        public int baseMoveCost = 1;
        public int defenseMultiplier = 1000; // 千分率 (1000 = 100%)
        public int attackMultiplier = 1000;  // 千分率 (地形からの攻撃補正)
        public int visionCost = 1;           // 視界消費コスト
        public int commandDelayPenalty = 0;  // 伝令経路通過時の追加遅延ターン
    }

#### `HexCell` と `MapSaveData`
    [System.Serializable]
    public class HexCell
    {
        public int x;
        public int y;
        public string terrainId = "plain";
        public int deployZoneFaction = 0; // 0: なし, 1: 自軍初期配置エリア, 2: 敵軍初期配置エリア

        // --- 将来のHoI4拡張用パラメータ枠 ---
        public int infrastructureLevel = 1; // 街道・インフラレベル (1〜5)
        public int currentSupply = 1000;    // 現在の補給物資量 (千分率)
        [System.NonSerialized] public bool isVisibleToPlayer = false;
    }

    [System.Serializable]
    public class MapSaveData
    {
        public string mapName;
        public int width;
        public int height;
        public HexCell[] cells; // 長さ = width * height (index = y * width + x)
    }

#### マップJSON保存・読込仕様 (`MapSerializer.cs`)
- **保存タイミング:** `MapEditorScene` でUIの「保存（Save）」ボタンを押した瞬間に実行する。
- **保存先パス:**
  - Unityエディタ実行時（`#if UNITY_EDITOR`）: `Path.Combine(Application.dataPath, "Maps", mapName + ".json")`
  - ビルド後（PC / WebGL等）: `Path.Combine(Application.persistentDataPath, "Maps", mapName + ".json")`
- **インゲームマップエディタ操作 (`MapEditorController.cs`):**
  - 画面上のパレットボタンで選択中のブラシ（`string currentTerrainId` または `int currentDeployZone`）を切り替える。
  - `Input.GetMouseButton(0)` 中に `EventSystem.current.IsPointerOverGameObject()` が `false` の場合、スクリーン座標を `Camera.main.ScreenToWorldPoint` → `Tilemap.WorldToCell` で Offset座標 `(x, y)` に変換し、`HexGrid.SetCellTerrain(x, y, currentTerrainId)` を呼ぶ。タップとドラッグ（なぞり）の双方で連続塗り替えを行う。

---

### 5.2 ユニット・師団システム (`EdoStrategy.Units`)

#### `UnitDefinition` (ScriptableObject)
高解像度スケーリングに基づき、初期兵科5種を以下の基準値で定義する。

| `unitTypeId` | 表示名 | 最大兵力 (`maxManpower`) | 基礎攻撃 (`baseAttack`) | 基礎防御 (`baseDefense`) | 突破力 (`breakthrough`) | 移動力 (`movePoints`) | 射程 (`attackRange`) | 偵察力 (`recon`) | 視界 (`visionRange`) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `"headquarters"` | 本陣 | `2000` | `120` | `250` | `100` | `2` | `1` | `2` | `3` |
| `"infantry"` | 歩兵（槍足軽） | `5000` | `300` | `450` | `250` | `3` | `1` | `1` | `2` |
| `"musketeer"` | 銃兵（鉄砲隊） | `3000` | `550` | `180` | `300` | `3` | `2` | `1` | `2` |
| `"cavalry"` | 騎兵 | `2500` | `480` | `260` | `600` | `5` | `1` | `3` | `4` |
| `"artillery"` | 砲兵（大筒） | `1500` | `750` | `120` | `400` | `1` | `3` | `0` | `2` |

    [System.Serializable]
    public struct TerrainMoveCostOverride
    {
        public string terrainId;
        public int moveCost; // -1 の場合は進入不可（例: 騎兵の山岳進入制限など）
    }

    [CreateAssetMenu(fileName = "NewUnit", menuName = "EdoStrategy/UnitDefinition")]
    public class UnitDefinition : ScriptableObject
    {
        public string unitTypeId;
        public string displayName;
        public bool isHeadquarters = false;
        public GameObject unitPrefab;

        public int maxManpower = 3000;
        public int baseAttack = 300;
        public int baseDefense = 300;
        public int breakthrough = 250;
        public int movePoints = 3;
        public int attackRange = 1;
        public int recon = 1;
        public int visionRange = 2;

        // 地形別の移動コスト上書き（OCP準拠: ロジック側で兵科分岐を書かない）
        public List<TerrainMoveCostOverride> terrainCostOverrides = new List<TerrainMoveCostOverride>();
    }

#### 師団・大隊データ構造 (`BattalionData.cs` / `UnitData.cs`)
    [System.Serializable]
    public class BattalionData
    {
        public string unitTypeId;
        public int maxManpower;
        public int currentManpower;
        public int equipmentRatio = 1000; // 千分率 (1000 = 100%)
    }

    [System.Serializable]
    public class UnitData
    {
        public string unitId;
        public string unitName;
        public int faction; // 1: 自軍, 2: 敵軍
        public int x;
        public int y;

        // 師団を構成する大隊リスト（MVPでは要素1つ、将来は師団設計で複数編成）
        public List<BattalionData> battalions = new List<BattalionData>();

        // 戦闘・戦略ステータス（千分率）
        public int organization = 1000;    // 組織率（0で敗走・壊滅）
        public int maxOrganization = 1000;
        public int supplyStatus = 1000;    // 補給状態（将来用）

        // 命令キュー（JsonUtility対応のためListで実装し、index 0 を先頭として扱う）
        public List<Order> pendingOrders = new List<Order>();
    }

---

### 5.3 戦闘・包囲修飾子システム (`EdoStrategy.Combat`)

#### `CombatModifier` クラス
戦闘計算時に適用されるすべての補正（地形、包囲、兵科相性、補給不足、指揮統制）は、`CombatModifier` の動的リストに集約して計算する。

    [System.Serializable]
    public class CombatModifier
    {
        public string sourceId;          // 例: "terrain_mountain", "flank_pincer", "low_supply"
        public string displayName;       // UI表示用名称
        public int attackMultiplier = 1000;     // 千分率 (1000 = 1.0倍)
        public int defenseMultiplier = 1000;    // 千分率 (1000 = 1.0倍)
        public int orgDamageMultiplier = 1000;  // 被組織率ダメージ倍率 (1000 = 1.0倍)
    }

#### 包囲・挟み撃ち（Flanking / Encirclement）判定ルール (`CombatResolver.cs`)
防御側ユニットの隣接6マス（方向インデックス `0`〜`5`）を `HexUtility` で走査し、敵対ユニットが占拠している方向インデックス集合 $E$ を取得する。
1. **包囲レベル判定（$|E| \ge 2$ の場合）:**
   - **完全包囲（Encircled）:** 隣接6マスすべてが「敵ユニット」または「進入不可地形」で塞がれており退路が0マスの場合。
     - `defenseMultiplier = 500`（防御 -50%）、`orgDamageMultiplier = 1600`（組織率ダメージ +60%）。
   - **背面挟撃（Pincer）:** 方向インデックス差が `3`（対角線上・180度反対側）のペアが存在する場合。
     - `defenseMultiplier = 650`（防御 -35%）、`orgDamageMultiplier = 1500`（組織率ダメージ +50%）。
   - **挟撃（Crossfire）:** 方向インデックス差が `2`（120度）のペアが存在する場合。
     - `defenseMultiplier = 750`（防御 -25%）、`orgDamageMultiplier = 1300`（組織率ダメージ +30%）。
   - **側面攻撃（Flanked）:** 上記以外で隣接する敵が2部隊以上いる場合。
     - `defenseMultiplier = 850`（防御 -15%）、`orgDamageMultiplier = 1150`（組織率ダメージ +15%）。
2. **ダメージ計算式（千分率演算）:**
   - 師団の有効攻撃力 $A_{eff}$ = $\sum (\text{大隊基礎攻撃} \times \frac{\text{現在兵員}}{\text{最大兵員}} \times \frac{\text{装備充足率}}{1000}) \times \frac{\text{補給状態}}{1000} \times \prod \frac{\text{攻撃修飾子}}{1000}$
   - 師団の有効防御力 $D_{eff}$ = $\sum (\text{大隊基礎防御} \times \frac{\text{現在兵員}}{\text{最大兵員}}) \times \frac{\text{地形防御補正}}{1000} \times \prod \frac{\text{防御修飾子}}{1000}$
   - **兵員ダメージ（Manpower Loss）:** $\max\left(10,\ \lfloor \frac{A_{eff} \times A_{eff}}{A_{eff} + D_{eff}} \times 0.5 \rfloor\right)$
   - **組織率ダメージ（Org Loss）:** $\max\left(15,\ \lfloor \frac{A_{eff} \times 1000}{D_{eff} + 200} \times \frac{\text{組織率ダメージ修飾子}}{1000} \rfloor\right)$

---

### 5.4 命令・報告の遅延システム (`EdoStrategy.Command`)

#### 命令データ (`Order.cs`) と遅延計算 (`CommandDelaySystem.cs`)
    public enum OrderType { Move, Attack }

    [System.Serializable]
    public class Order
    {
        public OrderType orderType;
        public int targetX;
        public int targetY;
        public string targetUnitId;
        public int remainingDelayTurns;
    }

- **遅延ターン算出式:**
  - 本陣（`isHeadquarters == true`）の座標 $P_{HQ}$ と対象部隊の座標 $P_{unit}$ の最短ヘックス距離を $D$ とする。
  - $\text{DelayTurns} = \max\left(0,\ \lfloor \frac{D}{3} \rfloor + \text{TerrainDelayPenalty} - \text{ReconBonus}\right)$
  - 距離 `0〜2` マスは `0ターン（即時）`、`3〜5` マスは `1ターン遅延`、`6〜8` マスは `2ターン遅延`。

#### 偵察報告データ (`IntelReport.cs`) とゴースト表示 (`FogOfWarSystem.cs`)
    [System.Serializable]
    public class IntelReport
    {
        public string targetUnitId;
        public string reportedUnitTypeId;
        public int reportedX;
        public int reportedY;
        public int remainingReportTurns; // 0になった瞬間にプレイヤー画面へ反映
        public int spottedTurn;
    }

- **視界と報告フロー:**
  1. 毎ターン `ReconAndReportPhase` で、全自軍部隊から `visionRange` 以内のセルを走査し、`TerrainDefinition.visionCost` を累積して視界内セル（`isVisibleToPlayer = true`）を確定する。
  2. 視界内の敵ユニットを発見した際、発見した自軍部隊と本陣の距離 $D$ から $\text{DelayTurns}$ を計算し、`IntelReport` を生成して報告キューへ追加する。
  3. 既存の全 `IntelReport` の `remainingReportTurns` を `-1` し、`0` 以下になった報告をプレイヤーの認識情報（`KnownEnemyIntel`）へ上書き反映する。
  4. 本陣の直接視界内（`DelayTurns == 0`）の敵は実体として描画し、遠方から遅延報告された敵は**「報告時の座標（`reportedX, reportedY`）に半透明のゴーストアイコン」**として `UnitView` に描画させる。

---

## 6. バトル進行ステートマシン (`TurnManager.cs`)
`BattleState` enum を定義し、以下の順序でフェーズを遷移させる。

1. **`DeploymentPhase`（初期配置フェーズ）:**
   - `MapSerializer` でJSONマップを読み込み、`HexGrid` と `HexTilemapView` を初期化。
   - プレイヤーは `deployZoneFaction == 1` のセルに本陣1部隊＋任意の部隊を配置。敵軍は `deployZoneFaction == 2` のセルに自動配置。
   - 「開戦」ボタンで `PlayerCommandPhase` へ遷移。
2. **`PlayerCommandPhase`（プレイヤー命令フェーズ）:**
   - プレイヤーが自軍部隊を選択し、移動先または攻撃対象を指定して `Order` を `pendingOrders` に登録（待機中命令のキャンセル・上書きも可能）。
   - 「ターン終了」ボタンで `ExecutionPhase` へ遷移。
3. **`ExecutionPhase`（解決フェーズ）:**
   - 全自軍部隊の `pendingOrders[0]` の `remainingDelayTurns` を `-1` し、`0` になった命令を順次実行（移動・戦闘解決）。
   - 敵AIの行動決定と実行。
4. **`ReconAndReportPhase`（視界・伝令更新フェーズ）:**
   - `FogOfWarSystem` により視界再計算、偵察報告タイマー進行、ゴースト表示更新イベントを発火。
5. **`CheckVictoryPhase`（勝敗判定フェーズ）:**
   - 敵本陣（`isHeadquarters == true`）の兵員数 `<= 0` または敵全滅 → **プレイヤー勝利**。
   - 自軍本陣の兵員数 `<= 0` または自軍全滅 → **プレイヤー敗北**。
   - 未決着の場合、ターン数を `+1` して `PlayerCommandPhase` へ戻る。

---

## 7. 段階的実装ステップ（Copilotへの指示順序）
1. **Phase 1（マップ基盤と描画）:** `HexUtility.cs` / `TerrainDefinition.cs` / `HexCell.cs` / `HexGrid.cs` / `HexTilemapView.cs` を実装し、Odd-R ヘックスグリッドに単色タイルが描画されることを確認する。
2. **Phase 2（マップエディタとJSON保存）:** `MapSerializer.cs` / `MapEditorController.cs` / `MapEditorUI.cs` を実装し、タップ＆なぞり操作で地形と初期配置エリアを塗り、JSON保存・再読込できることを確認する。
3. **Phase 3（ユニット配置と移動）:** `UnitDefinition.cs` / `BattalionData.cs` / `UnitData.cs` / `UnitView.cs` / `TurnManager.cs` を実装し、配置フェーズから移動コストを考慮した移動ができることを確認する。
4. **Phase 4（戦闘・包囲計算と勝敗判定）:** `CombatModifier.cs` / `CombatResolver.cs` を実装し、包囲ペナルティを含めた戦闘ダメージ計算と本陣撃破による勝敗判定を実装する。
5. **Phase 5（命令・報告遅延システム）:** `Order.cs` / `CommandDelaySystem.cs` / `IntelReport.cs` / `FogOfWarSystem.cs` を実装し、本陣からの距離に応じた命令キュー遅延と敵ゴースト表示を実装する。
6. **Phase 6（HoI4ライクな戦略拡張）:** `HexCell.infrastructureLevel` を用いた補給線（兵站ルートと補給切れペナルティ）、師団編成UI（複数 `BattalionData` の編集）、兵士人口（Manpower）プールからの補充システムなどを追加する。