# 江戸時代・ターン制ヘックスストラテジー 詳細開発設計書 (v3)

## 1. プロジェクト概要・開発環境
- **コンセプト:** 江戸時代を舞台にしたターン制ヘックス（正六角形）ストラテジーゲーム。本陣（指令部隊）からの距離や地形に応じた「命令・報告の遅延（情報戦）」を核とし、プレイヤーと敵AIの双方が不完全情報下で戦う。将来的に Hearts of Iron IV (HoI4) のような師団設計・補給線・人的資源・偵察といった高度な戦略要素を拡張できる設計とする。
- **エンジン・バージョン:** Unity 2022.3.62f3 LTS (2D Universal Render Pipeline)
- **使用言語:** C# (.NET Standard 2.1)
- **進行形式:** ターン制 (Turn-Based)
- **名前空間（Namespace）:** 機能ごとに `EdoStrategy.Core` / `EdoStrategy.Map` / `EdoStrategy.MapEditor` / `EdoStrategy.Units` / `EdoStrategy.Combat` / `EdoStrategy.Command` / `EdoStrategy.AI` / `EdoStrategy.UI` に所属させる（既存のPhase 1〜2スクリプトの名前空間・クラス名がある場合は互換性を維持すること）。

---

## 2. アーキテクチャ原則（GitHub Copilot向け厳密規約）
コードを生成・修正する際は、必ず以下の原則を遵守すること。

1. **データ駆動（文字列ID）と型安全（`enum`・定数）の使い分け:**
   - **拡張が発生するマスターデータ（地形・兵科）:** ロジック側の `switch` / `if` 分岐を排除するため、`string` 型IDと `ScriptableObject`（`TerrainDefinition` / `UnitDefinition`）で管理する。ただし、コード内でのタイプミスを防ぐため、基本IDは定数クラス（`TerrainIds` / `UnitTypeIds`）に定義し、マジックストリングの直書きを禁止する。
   - **ルール上固定される概念（陣営・命令種別・バトルフェーズ）:** コンパイル時の型安全性を確保するため、明確な **`enum`**（`Faction`, `OrderType`, `BattleState`）を使用する。
2. **非破壊的なデータ構造の拡張:**
   - 将来の「師団設計」「補給線」「兵士人口（Manpower）」「偵察」を既存クラスの破壊的変更なしで追加できるよう、あらかじめ配列・リスト・パラメータ枠を定義しておく。
3. **データ（Model/Logic）と描画（View）の完全分離:**
   - マップ情報、ユニット状態、命令キュー、視界判定、AI思考、戦闘計算はすべて純粋なC#クラス（`MonoBehaviour` 非継承）で実装する。
   - `Tilemap` や `SpriteRenderer` を操作する `MonoBehaviour` は、データ層の C# イベント（`event Action<...>`）を購読して画面を更新する「描画専用（View）」に徹する。
4. **フェアな不完全情報設計（AIの対等制約）:**
   - 本作の核心である「視界制限（Fog of War）」と「命令・報告の遅延」は、**プレイヤーだけでなく敵AIにも完全に同一のシステム（`CommandDelaySystem` / `FogOfWarSystem`）を適用する**。敵AIが未発見のプレイヤー座標や命令予定（`pendingOrders`）を直接参照することを禁止する。
5. **Unity `JsonUtility` シリアライズ制約の遵守:**
   - JSON保存対象のデータクラスでは、`Dictionary`、`Queue`、`Stack`、2次元配列 `[,]` を使用しない。グリッドは 1次元配列 `HexCell[]`（`y * width + x`）、命令キューは `List<Order>` の先頭（インデックス `0`）を処理する方式で実装する。
6. **整数千分率（Permille）による高解像度数値演算:**
   - ステータス補正・地形補正・包囲ペナルティ・組織率・補給率はすべて **千分率整数（`int`, `1000` = `100.0%` = `1.0倍`）** で定義・演算し、端数切り捨てによる数値潰れを防ぐ。

---

## 3. 共通Enum・定数定義 (`EdoStrategy.Core` / `EdoStrategy.Units`)

### 3.1 固定Enum (`GameEnums.cs`)
    public enum Faction
    {
        None = 0,   // 中立・未設定
        Player = 1, // 自軍
        Enemy = 2   // 敵軍
    }

    public enum OrderType
    {
        Move = 0,   // 移動
        Attack = 1  // 攻撃
    }

    public enum BattleState
    {
        DeploymentPhase,     // 初期配置フェーズ
        PlayerCommandPhase,  // プレイヤー命令フェーズ
        ExecutionPhase,      // 解決フェーズ
        ReconAndReportPhase, // 視界・伝令更新フェーズ
        CheckVictoryPhase    // 勝敗判定フェーズ
    }

### 3.2 マスターデータ基本ID定数 (`GameIds.cs`)
    public static class TerrainIds
    {
        public const string Plain = "plain";
        public const string Forest = "forest";
        public const string Mountain = "mountain";
        public const string River = "river";
    }

    public static class UnitTypeIds
    {
        public const string Headquarters = "headquarters";
        public const string Infantry = "infantry";
        public const string Musketeer = "musketeer";
        public const string Cavalry = "cavalry";
        public const string Artillery = "artillery";
    }

---

## 4. 座標系・空間ルール (`HexCoordinates.cs` / `HexGrid.cs`)
- **グリッド仕様:** Unity `Grid` コンポーネントは **Hexagonal Pointy Top（尖頂六角形）**、座標系は **Odd-R Offset座標**（奇数行 `y % 2 != 0` が右へ `+0.5` シフト）に統一する（標準マップサイズ例: `16 x 10`）。
- **Offset `(x, y)` → Axial `(q, r)`:** `q = x - (y - (y & 1)) / 2`, `r = y`
- **Axial `(q, r)` → Offset `(x, y)`:** `x = q + (r - (r & 1)) / 2`, `y = r`
- **最短ヘックス距離 $D$:** $D = \frac{|q_1 - q_2| + |q_1 + r_1 - q_2 - r_2| + |r_1 - r_2|}{2}$
- **隣接6方向ベクトル（時計回り `0`〜`5` / Axial基準）:**
  - `0`(右): `(+1, 0)`, `1`(右下): `(0, +1)`, `2`(左下): `(-1, +1)`, `3`(左): `(-1, 0)`, `4`(左上): `(0, -1)`, `5`(右上): `(+1, -1)`
- **ユニット配置・移動の空間ルール（重要）:**
  - **スタック不可（1マス1部隊）:** 同一のヘックスマスに複数の部隊が同時に存在することはできない。
  - **通過と停止:** 味方部隊がいるマスは移動力があれば「通過」可能だが、移動終了時に「停止」はできない。敵部隊がいるマスは通過不可（進入しようとした場合は接触・攻撃判定となる）。

---

## 5. フォルダ・ファイル構成 (`Assets/` 配下)
- `Assets/`
  - `Data/`
    - `Terrains/` : `TerrainDefinition` アセット（PlainTerrain, ForestTerrain, MountainTerrain, RiverTerrain ※作成済み）
    - `Units/` : `UnitDefinition` アセット（Headquarters, Infantry, Musketeer, Cavalry, Artillery）
  - `Maps/` : マップエディタで保存されるJSONマップデータ
  - `Prefabs/`
    - `Units/` : 部隊表示用プレハブ（実体アイコン・ゴーストアイコン・ステータスバー）
    - `UI/` : 地形パレットボタン、部隊配置ボタン、命令アイコンプレハブ
  - `Scenes/`
    - `TitleScene.unity` : タイトル・マップ選択画面
    - `MapEditorScene.unity` : タッチ＆なぞり操作によるマップ作成・JSON保存シーン（実装済み）
    - `BattleScene.unity` : メインの戦闘・戦略シミュレーションシーン
  - `Scripts/`
    - `Core/`
      - `GameEnums.cs` : `Faction`, `OrderType`, `BattleState` の定義
      - `GameIds.cs` : `TerrainIds`, `UnitTypeIds` の定数定義
      - `GameManager.cs` : マスターデータレジストリ保持、シーン間のマップJSON受け渡し
      - `TurnManager.cs` : バトルのステートマシン管理
    - `Map/`
      - `HexCoordinates.cs` : Odd-R ⇔ Axial変換、距離計算、隣接探索、方向インデックス判定（実装済み・拡張）
      - `TerrainDefinition.cs` : 地形マスターデータ（ScriptableObject・実装済み）
      - `HexCell.cs` : 1マスごとのデータ（実装済み・拡張）
      - `HexGrid.cs` : マップ全体の1次元セル配列管理、移動コスト計算、A*パスファインディング（実装済み・拡張）
      - `HexTilemapView.cs` : `HexGrid` の変更イベントを受け取り `Tilemap` を更新するView（実装済み）
      - `MapSerializer.cs` : `MapSaveData` のJSONシリアライズ・ファイル保存/読込（実装済み）
    - `MapEditor/`（※Unity予約語 `Editor` を避けるため `MapEditor` フォルダを使用・実装済み）
      - `MapEditorController.cs` : タッチ/ドラッグ判定、選択ブラシ適用、保存UI連携
    - `Units/`
      - `UnitDefinition.cs` : 兵科マスターデータ（ScriptableObject）
      - `BattalionData.cs` : 大隊データ（師団設計の最小構成単位）
      - `UnitData.cs` : 師団（部隊）データ（兵員数、組織率、補給、命令リスト）
      - `UnitManager.cs` : 全生存部隊のリスト管理、座標からの部隊検索、移動・消滅処理
      - `UnitView.cs` : 部隊・ゴーストのマップ描画、移動補間、命令ルート描画
    - `Combat/`
      - `UnitModifier.cs` : 戦闘・移動・視界・遅延などあらゆる補正を動的リストで扱う汎用修飾子
      - `CombatResolver.cs` : 包囲判定、修飾子集約、兵員・組織率ダメージ計算エンジン
    - `Command/`
      - `Order.cs` : 命令データ（種別、目標座標、対象ユニットID、残り遅延ターン数）
      - `CommandDelaySystem.cs` : 最も近い司令部からの経路コストに基づく遅延計算と命令キュー進行
      - `IntelReport.cs` : 偵察報告データ（発見敵ID、目撃座標、目撃ターン、到着までの残りターン数）
      - `FactionIntelState.cs` : 陣営ごとの視界マップと既知の敵情報（ゴースト・陳腐化管理）
      - `FogOfWarSystem.cs` : 各陣営の視界判定、報告遅延タイマー進行、ゴースト更新
    - `AI/`
      - `EnemyAIController.cs` : 敵陣営の `FactionIntelState` のみを参照し、`CommandDelaySystem` 経由で命令を発行するAIロジック
    - `UI/`
      - `MapEditorUI.cs` : 地形パレット、陣営エリアブラシ、マップ名入力、保存ボタン（実装済み）
      - `BattleUI.cs` : 部隊配置パネル、ターン終了ボタン、選択部隊・敵ゴースト情報パネル
  - `Tiles/` : 単色の正六角形Tileアセット（Plain, Forest, Mountain, River ※作成済み）

---

## 6. データ構造・クラス詳細仕様

### 6.1 マップ・地形システム (`EdoStrategy.Map`)

#### `TerrainDefinition` (ScriptableObject)
| `terrainId` | 表示名 | 基本移動コスト (`baseMoveCost`) | 防御倍率 (`defenseMultiplier`) | 視界消費 (`visionCost`) | 命令遅延加算 (`commandDelayPenalty`) |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `"plain"` | 平地 | `1` | `1000` (0%) | `1` | `0` |
| `"forest"` | 森 | `2` | `1200` (+20%) | `2` | `0` |
| `"mountain"` | 山 | `3` | `1400` (+40%) | `3` | `1` |
| `"river"` | 川 | `3` | `800` (-20%) | `1` | `1` |

#### `HexCell` と `MapSaveData`（既存JSONとの後方互換性を維持）
    [System.Serializable]
    public class HexCell
    {
        public int x;
        public int y;
        public string terrainId = "plain";
        public int deployZoneFaction = 0; // 0: Faction.None, 1: Faction.Player, 2: Faction.Enemy

        // --- 将来のHoI4拡張用パラメータ枠 ---
        public int infrastructureLevel = 1; // 街道・インフラレベル (1〜5)
        public int currentSupply = 1000;    // 現在の補給物資量 (千分率)
    }

    [System.Serializable]
    public class MapSaveData
    {
        public string mapName;
        public int width;
        public int height;
        public HexCell[] cells; // 長さ = width * height (index = y * width + x)
    }

---

### 6.2 ユニット・師団システム (`EdoStrategy.Units`)

#### `UnitDefinition` (ScriptableObject)
| `unitTypeId` | 表示名 | 司令レベル (`commandLevel`) | 最大兵力 (`maxManpower`) | 基礎攻撃 (`baseAttack`) | 基礎防御 (`baseDefense`) | 突破力 (`breakthrough`) | 移動力 (`movePoints`) | 射程 (`attackRange`) | 偵察力 (`recon`) | 視界 (`visionRange`) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `"headquarters"` | 本陣 | `2` | `2000` | `120` | `250` | `100` | `2` | `1` | `2` | `3` |
| `"infantry"` | 歩兵（槍足軽） | `0` | `5000` | `300` | `450` | `250` | `3` | `1` | `1` | `2` |
| `"musketeer"` | 銃兵（鉄砲隊） | `0` | `3000` | `550` | `180` | `300` | `3` | `2` | `1` | `2` |
| `"cavalry"` | 騎兵 | `0` | `2500` | `480` | `260` | `600` | `5` | `1` | `3` | `4` |
| `"artillery"` | 砲兵（大筒） | `0` | `1500` | `750` | `120` | `400` | `1` | `3` | `0` | `2` |

- **司令レベル (`commandLevel`):** `0 = 一般部隊`, `1 = 前線司令部（軍団長等）`, `2 = 総大将本陣`。命令・報告の起点は同一陣営内で通信コストが最小の `commandLevel >= 1` 部隊となり、勝敗判定の主目標は `commandLevel >= 2` とする。

    [System.Serializable]
    public struct TerrainMoveCostOverride
    {
        public string terrainId;
        public int moveCost; // -1 の場合は進入不可
    }

    [CreateAssetMenu(fileName = "NewUnit", menuName = "EdoStrategy/UnitDefinition")]
    public class UnitDefinition : ScriptableObject
    {
        public string unitTypeId;
        public string displayName;
        public int commandLevel = 0;
        public GameObject unitPrefab;

        public int maxManpower = 3000;
        public int baseAttack = 300;
        public int baseDefense = 300;
        public int breakthrough = 250;
        public int movePoints = 3;
        public int attackRange = 1;
        public int recon = 1;
        public int visionRange = 2;

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
        public Faction faction = Faction.Player;
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

### 6.3 汎用修飾子と戦闘・包囲システム (`EdoStrategy.Combat`)

#### `UnitModifier` クラス（汎用補正）
戦闘・移動・視界・命令遅延などへのあらゆる補正を統一的に扱う。演算時は**同種の加算ボーナス（`+Bonus`）を合計したうえで、乗算倍率（`Multiplier / 1000`）を順次適用**する。

    [System.Serializable]
    public class UnitModifier
    {
        public string sourceId;                 // 例: "terrain_mountain", "flank_pincer", "low_supply"
        public string displayName;              // UI表示用名称
        public int attackMultiplier = 1000;     // 攻撃倍率 (千分率)
        public int defenseMultiplier = 1000;    // 防御倍率 (千分率)
        public int orgDamageMultiplier = 1000;  // 被組織率ダメージ倍率 (千分率)
        public int movePointBonus = 0;          // 移動力増減
        public int visionRangeBonus = 0;        // 視界増減
        public int commandDelayBonus = 0;       // 命令遅延ターン増減
    }

#### 包囲・挟み撃ち（Flanking / Encirclement）判定ルール (`CombatResolver.cs`)
防御側ユニットの隣接6マス（方向インデックス `0`〜`5`）を走査し、敵対ユニットが占拠している方向集合 $E$ を取得する（$|E| \ge 2$ で発動）。
- **完全包囲（Encircled）:** 隣接6マスすべてが敵ユニットまたは進入不可地形（退路0） → `defenseMultiplier = 500`, `orgDamageMultiplier = 1600`
- **背面挟撃（Pincer）:** 方向インデックス差 `3`（180度対角）のペア存在 → `defenseMultiplier = 650`, `orgDamageMultiplier = 1500`
- **挟撃（Crossfire）:** 方向インデックス差 `2`（120度）のペア存在 → `defenseMultiplier = 750`, `orgDamageMultiplier = 1300`
- **側面攻撃（Flanked）:** 上記以外で隣接敵が2部隊以上 → `defenseMultiplier = 850`, `orgDamageMultiplier = 1150`

#### ダメージ計算式（千分率演算）
- 有効攻撃力 $A_{eff}$ = $\sum (\text{大隊基礎攻撃} \times \frac{\text{現在兵員}}{\text{最大兵員}} \times \frac{\text{装備充足率}}{1000}) \times \frac{\text{補給状態}}{1000} \times \prod \frac{\text{攻撃修飾子}}{1000}$
- 有効防御力 $D_{eff}$ = $\sum (\text{大隊基礎防御} \times \frac{\text{現在兵員}}{\text{最大兵員}}) \times \frac{\text{地形防御補正}}{1000} \times \prod \frac{\text{防御修飾子}}{1000}$
- **兵員ダメージ（Manpower Loss）:** $\max\left(10,\ \lfloor \frac{A_{eff} \times A_{eff}}{A_{eff} + D_{eff}} \times 0.5 \rfloor\right)$
- **組織率ダメージ（Org Loss）:** $\max\left(15,\ \lfloor \frac{A_{eff} \times 1000}{D_{eff} + 200} \times \frac{\text{組織率ダメージ修飾子}}{1000} \rfloor\right)$

---

### 6.4 命令・報告の遅延システム (`EdoStrategy.Command`)

#### 命令データ (`Order.cs`) と遅延計算 (`CommandDelaySystem.cs`)
    [System.Serializable]
    public class Order
    {
        public OrderType orderType;
        public int targetX;
        public int targetY;
        public string targetUnitId;
        public int remainingDelayTurns;
    }

- **遅延ターン算出式（経路ベース）:**
  - 対象部隊と同一陣営（`Faction`）に属する司令部（`commandLevel >= 1`）のうち、最も通信コストが低い司令部 $P_{HQ}$ を選択する。
  - $P_{HQ}$ から対象部隊 $P_{unit}$ までの**伝令用A*最小コスト経路**上のセル群を $Path$ とし、経路ステップ数を $D$ とする（大規模マップ化時は経路キャッシュを利用する）。
  - $\text{DelayTurns} = \max\left(0,\ \lfloor \frac{D}{3} \rfloor + \sum_{c \in Path} \text{TerrainDelayPenalty}(c) - \text{ReconBonus}\right)$

#### 陣営別情報管理 (`FactionIntelState.cs` / `IntelReport.cs` / `FogOfWarSystem.cs`)
    [System.Serializable]
    public class IntelReport
    {
        public string targetUnitId;
        public string reportedUnitTypeId;
        public int reportedX;
        public int reportedY;
        public int remainingReportTurns; // 0になった瞬間にその陣営の認識情報へ反映
        public int spottedTurn;          // 目撃されたターン数
    }

- **視界・報告・情報陳腐化フロー（両陣営共通）:**
  1. `ReconAndReportPhase` で各陣営の生存部隊から `visionRange` 以内を走査し、`TerrainDefinition.visionCost` を累積してその陣営の現在視界（`visibleCellIndices`）を確定する。
  2. 視界内の敵対ユニットを発見した際、発見部隊と最寄り自陣営司令部との遅延ターン数 $\text{DelayTurns}$ を計算し、`IntelReport` を報告キューへ追加する。
  3. 既存の全 `IntelReport` の `remainingReportTurns` を `-1` し、`0` 以下になった報告をその陣営の `KnownEnemyIntel`（既知の敵位置・目撃ターン `spottedTurn`）へ反映する。
  4. **情報の陳腐化（ゴースト消滅）:** 現在のターン数から `spottedTurn` を引いた経過ターン数が `MaxIntelAge`（デフォルト `3` ターン）を超えた古いゴースト情報は「所在不明」としてリストから削除する。
  5. **描画（プレイヤー視点）:** `DelayTurns == 0` の直接視界内の敵は実体として描画し、遅延報告された有効期限内の敵は**「`reportedX, reportedY` に半透明のゴーストアイコン（＋何ターン前の目撃か）」**として描画する。

---

### 6.5 敵AI設計方針 (`EdoStrategy.AI` / `EnemyAIController.cs`)
- **対等な情報・遅延制約:** `EnemyAIController` は `Faction.Enemy` の `FactionIntelState`（敵陣営の視界および到着済みの `IntelReport`）**のみ**を参照し、発行した命令もすべて `CommandDelaySystem.IssueOrder()` を通じて遅延実行させる。
- **行動決定アルゴリズム（優先度順）:**
  1. **本陣防衛（最優先）:** 敵の総大将本陣（`commandLevel >= 2`）から一定距離（例: 4マス以内）の認識情報にプレイヤー部隊が存在する場合、最寄りの遊撃部隊に迎撃・攻撃命令を発行し、本陣自身も危険なら後方へ退避させる。
  2. **好機での攻撃・挟撃:** `KnownEnemyIntel` 内の目標に対し、自軍戦力が優位または複数方向から挟撃できる場合、攻撃・包囲移動命令を発行する。
  3. **偵察前進:** 有効な敵情報がない場合、偵察力の高い部隊（騎兵など）をマップ中央や未探索エリアへ前進させ、本陣は後方の防御有利地形（森・山）で待機する。

---

## 7. バトル進行ステートマシン (`TurnManager.cs`)
1. **`DeploymentPhase`（初期配置フェーズ）:**
   - `MapSerializer` でJSONマップを読み込み、`HexGrid` と `HexTilemapView` を初期化。
   - プレイヤーは `deployZoneFaction == 1`（マップに未設定の場合は左端3列）のセルに総大将本陣1部隊＋任意部隊を配置。敵AIは `deployZoneFaction == 2`（未設定の場合は右端3列）のセルに自動配置。
   - 「開戦」ボタンで `PlayerCommandPhase` へ遷移。
2. **`PlayerCommandPhase`（プレイヤー命令フェーズ）:**
   - プレイヤーが自軍部隊を選択し、移動先または攻撃対象を指定して `Order` を `pendingOrders` に登録（待機中命令のキャンセル・上書きも可能）。
   - 「ターン終了」ボタンで `ExecutionPhase` へ遷移。
3. **`ExecutionPhase`（解決フェーズ）:**
   - 敵AI（`EnemyAIController`）が敵陣営の認識情報に基づき命令を発行（`CommandDelaySystem` 経由で遅延付与）。
   - 両陣営の全生存部隊の `pendingOrders[0]` の `remainingDelayTurns` を `-1` し、`0` になった命令を順次実行（移動・戦闘解決）。
4. **`ReconAndReportPhase`（視界・伝令更新フェーズ）:**
   - `FogOfWarSystem` によりプレイヤー・敵AI双方の視界再計算、偵察報告タイマー進行、陳腐化したゴーストの削除・更新イベントを発火。
5. **`CheckVictoryPhase`（勝敗判定フェーズ）:**
   - 敵の総大将本陣（`commandLevel >= 2`）の兵員数 `<= 0` または敵全滅 → **プレイヤー勝利**。
   - 自軍の総大将本陣（`commandLevel >= 2`）の兵員数 `<= 0` または自軍全滅 → **プレイヤー敗北**。
   - 未決着の場合、ターン数を `+1` して `PlayerCommandPhase` へ戻る。

---

## 8. 段階的実装ステップ（Copilotへの指示順序）
1. **Phase 1（マップ基盤と描画・完了済み）:** `HexCoordinates.cs` / `TerrainDefinition.cs` / `HexCell.cs` / `HexGrid.cs` / `HexTilemapView.cs`
2. **Phase 2（マップエディタとJSON保存・完了済み）:** `MapSerializer.cs` / `MapEditorController.cs`（`Scripts/MapEditor/`）/ `MapEditorUI.cs`
3. **Phase 3（ユニット配置と基本移動）:** `GameEnums.cs` / `GameIds.cs` / `UnitDefinition.cs` / `BattalionData.cs` / `UnitData.cs` / `UnitManager.cs` / `UnitView.cs` / `TurnManager.cs` を実装し、配置フェーズからスタック不可・移動コストを考慮した移動ができることを確認する。
4. **Phase 4（戦闘・包囲計算と勝敗判定）:** `UnitModifier.cs` / `CombatResolver.cs` を実装し、包囲ペナルティを含めた戦闘ダメージ計算と本陣撃破による勝敗判定を実装する。
5. **Phase 5（命令・報告遅延システムとフェアな敵AI）:** `Order.cs` / `CommandDelaySystem.cs` / `IntelReport.cs` / `FactionIntelState.cs` / `FogOfWarSystem.cs` / `EnemyAIController.cs` を実装し、両陣営に適用される命令遅延とゴースト表示（および陳腐化消滅）を実装する。
6. **Phase 6（HoI4ライクな戦略拡張）:** `HexCell.infrastructureLevel` を用いた補給線（兵站ルートと補給切れペナルティ、A*経路キャッシュ）、師団編成UI（複数 `BattalionData` の編集）、兵士人口（Manpower）プールからの補充システムを追加する。