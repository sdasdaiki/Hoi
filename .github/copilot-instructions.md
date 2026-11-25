# 江戸時代・ターン制ヘックスストラテジー 詳細開発設計書 (v4.1)

## 1. プロジェクト概要・開発環境
- **コンセプト:** 江戸時代を舞台にしたターン制ヘックス（正六角形）ストラテジーゲーム。本陣（指令部隊）からの距離や地形に応じた「命令・報告の遅延（情報戦）」を核とし、プレイヤーと敵AIの双方が不完全情報下で戦う。将来的に Hearts of Iron IV (HoI4) のような師団設計・補給線・人的資源・偵察・構築物（城・要塞・塹壕）・伏兵といった高度な戦略要素を拡張できる設計とする。
- **エンジン・バージョン:** Unity 2022.3.62f3 LTS (2D Universal Render Pipeline)
- **使用言語:** C# (.NET Standard 2.1)
- **進行形式:** ターン制 (Turn-Based / WeGo同時解決方式)
- **名前空間（Namespace）:** 機能ごとに `EdoStrategy.Core` / `EdoStrategy.Map` / `EdoStrategy.MapEditor` / `EdoStrategy.Units` / `EdoStrategy.Combat` / `EdoStrategy.Command` / `EdoStrategy.AI` / `EdoStrategy.UI` に所属させる（既存のPhase 1〜3.5スクリプトの名前空間・クラス名がある場合は互換性を維持すること）。

---

## 2. アーキテクチャ原則（GitHub Copilot向け厳密規約）
コードを生成・修正する際は、必ず以下の原則を遵守すること。

1. **データ駆動（文字列ID）と型安全（`enum`・定数）の使い分け:**
   - **拡張が発生するマスターデータ（地形・兵科）:** ロジック側の `switch` / `if` 分岐を排除するため、`string` 型IDと `ScriptableObject`（`TerrainDefinition` / `UnitDefinition`）で管理する。ただし、コード内でのタイプミスを防ぐため、基本IDは定数クラス（`TerrainIds` / `UnitTypeIds`）に定義し、マジックストリングの直書きを禁止する。
   - **ルール上固定される概念（陣営・命令種別・バトルフェーズ）:** コンパイル時の型安全性を確保するため、明確な **`enum`**（`Faction`, `OrderType`, `BattleState`）を使用する。
2. **非破壊的なデータ構造の拡張:**
   - 将来の「師団設計」「補給線」「兵士人口（Manpower）」「偵察」「構築物（塹壕・要塞・城）」「装備充足率」「伏兵」を既存クラスの破壊的変更なしで追加できるよう、あらかじめ配列・リスト・パラメータ枠を定義しておく。
3. **データ（Model/Logic）・描画（View）・入力（Input）の分離:**
   - マップ情報、ユニット状態、命令キュー、視界判定、AI思考、戦闘計算はすべて純粋なC#クラス（`MonoBehaviour` 非継承）で実装する。
   - `Tilemap` や `SpriteRenderer`、UIを操作する `MonoBehaviour` は、データ層の C# イベント（`event Action<...>`）を購読して画面を更新する「描画専用（View）」に徹する。
   - 将来的にPC向けのキーボードショートカットや操作カスタムを柔軟に導入できるよう、入力検知（マウスクリック・キー入力）の内部にロジックを直書きせず、公開メソッド（`SelectUnit`, `IssueMoveOrder`, `CancelOrder` 等）を呼び出す構成にする（具体的なキー配置は全コア機能実装後に決定する）。
4. **フェアな不完全情報設計（AIの対等制約）:**
   - 本作の核心である「視界制限（Fog of War）」と「命令・報告の遅延」は、**プレイヤーだけでなく敵AIにも完全に同一のシステム（`CommandDelaySystem` / `FogOfWarSystem`）を適用する**。敵AIが未発見のプレイヤー座標や命令予定（`pendingOrders`）を直接参照することを禁止する。
5. **Unity `JsonUtility` シリアライズ制約の遵守:**
   - JSON保存対象のデータクラスでは、`Dictionary`、`Queue`、`Stack`、2次元配列 `[,]` を使用しない。グリッドは 1次元配列 `HexCell[]`（`y * width + x`）、命令キューは `List<Order>` の先頭（インデックス `0`）を処理する方式で実装する。
6. **整数千分率（Permille）による高解像度数値演算:**
   - ステータス補正・地形補正・包囲ペナルティ・過密ペナルティ・組織率・補給率はすべて **千分率整数（`int`, `1000` = `100.0%` = `1.0倍`）** で定義・演算し、端数切り捨てによる数値潰れを防ぐ。

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
        CheckVictoryPhase,   // 勝敗判定フェーズ
        BattleEnded          // 決着済み（ターン進行停止）
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

## 4. 座標系・空間ルール (`HexCoordinates.cs` / `HexGrid.cs` / `UnitManager.cs`)
- **グリッド仕様:** Unity `Grid` コンポーネントは **Hexagonal Pointy Top（尖頂六角形）**、座標系は **Odd-R Offset座標**（奇数行 `y % 2 != 0` が右へ `+0.5` シフト）に統一する（標準マップサイズ例: `16 x 10`）。
- **Offset `(x, y)` → Axial `(q, r)`:** `q = x - (y - (y & 1)) / 2`, `r = y`
- **Axial `(q, r)` → Offset `(x, y)`:** `x = q + (r - (r & 1)) / 2`, `y = r`
- **最短ヘックス距離 $D$:** $D = \frac{|q_1 - q_2| + |q_1 + r_1 - q_2 - r_2| + |r_1 - r_2|}{2}$
- **隣接6方向ベクトル（時計回り `0`〜`5` / Axial基準）:**
  - `0`(右): `(+1, 0)`, `1`(右下): `(0, +1)`, `2`(左下): `(-1, +1)`, `3`(左): `(-1, 0)`, `4`(左上): `(0, -1)`, `5`(右上): `(+1, -1)`
- **ユニット配置・移動の空間ルール（HoI4式無制限スタック制）:**
  - **同一陣営の無制限スタック許可:** 同一のヘックスマスに、**同じ陣営（`Faction`）の部隊は何部隊でも同時に配置・進入・通過・停止することができる**。複雑な退避予約や相互入れ替え（玉突き）処理は廃止する。
  - **敵部隊の進入・素通り禁止:** 敵対陣営（`Faction` が異なる）の部隊が1部隊でも存在するマス、および進入不可地形（移動コスト `-1`）は、**通過・停止ともにいかなる理由でも不可**（素通りできない）。敵がいるマスへは `OrderType.Attack`（攻撃命令）のみ指定できる。
  - **同時移動の先着判定ルール:** 同一マスへの味方同士の移動制限はないため全員進入可能。ただし、敵撃破後の空きマス占領や特殊条件で競合が発生した場合は、**① 必要移動コストが少ないほう → ② 機動力（`movePoints`）が高いほう → ③ 運（乱数）** の優先順位で決定する。

---

## 5. フォルダ・ファイル構成 (`Assets/` 配下)
- `Assets/`
  - `Data/`
    - `Terrains/` : `TerrainDefinition` アセット（PlainTerrain, ForestTerrain, MountainTerrain, RiverTerrain ※作成済み）
    - `Units/` : `UnitDefinition` アセット（HeadquartersUnit, InfantryUnit, MusketeerUnit, CavalryUnit, ArtilleryUnit ※作成済み）
  - `Maps/` : マップエディタで保存されるJSONマップデータ
  - `Prefabs/`
    - `Units/` : HoI4式四角形カウンタープレハブ（兵科アイコン・スタック数・縦棒2本ゲージ・ゴースト）
    - `UI/` : 地形パレットボタン、スタック内ユニット一覧カードプレハブ
  - `Scenes/`
    - `TitleScene.unity` : タイトル・マップ選択画面
    - `MapEditorScene.unity` : タッチ＆なぞり操作によるマップ作成・JSON保存シーン（実装済み）
    - `BattleScene.unity` : メインの戦闘・戦略シミュレーションシーン
  - `Scripts/`
    - `Core/`
      - `GameEnums.cs` : `Faction`, `OrderType`, `BattleState` の定義（実装済み）
      - `GameIds.cs` : `TerrainIds`, `UnitTypeIds` の定数定義（実装済み）
      - `GameManager.cs` : マスターデータレジストリ保持、シーン間のマップJSON受け渡し（実装済み）
      - `TurnManager.cs` : バトルのステートマシン管理・操作コマンド受付（実装済み・拡張）
    - `Map/`
      - `HexCoordinates.cs` : Odd-R ⇔ Axial変換、距離計算、隣接探索、方向インデックス判定（実装済み）
      - `TerrainDefinition.cs` : 地形マスターデータ（ScriptableObject・実装済み）
      - `HexCell.cs` : 1マスごとのデータ（実装済み・拡張）
      - `HexGrid.cs` : マップ全体の1次元セル配列管理、移動コスト計算、A*パスファインディング（実装済み）
      - `HexTilemapView.cs` : `HexGrid` の変更イベントを受け取り `Tilemap` を更新するView（実装済み）
      - `MapSerializer.cs` : `MapSaveData` のJSONシリアライズ・ファイル保存/読込（実装済み）
    - `MapEditor/`（※Unity予約語 `Editor` を避けるため `MapEditor` フォルダを使用・実装済み）
      - `MapEditorController.cs` : タッチ/ドラッグ判定、選択ブラシ適用、保存UI連携
    - `Units/`
      - `UnitDefinition.cs` : 兵科マスターデータ（ScriptableObject・実装済み・`antiStructureAttack` 追加）
      - `BattalionData.cs` : 大隊データ（師団設計の最小構成単位・実装済み）
      - `UnitData.cs` : 師団（部隊）データ（兵員数、組織率、補給、残り移動力、命令リスト・実装済み）
      - `UnitManager.cs` : 全生存部隊のリスト管理、座標からの全スタック部隊検索（`GetUnitsAt`）、移動・敗走・消滅処理（実装済み）
      - `HexStackCounterView.cs` : **HoI4式四角形マップカウンター1枠の描画**（兵科アイコン、同種スタック数表示、組織率＆兵員数の縦棒2本ゲージ、選択枠・実装済み）
      - `UnitBoardView.cs` : マスごとの部隊を兵科別にグルーピングして `HexStackCounterView` を配置・更新し、命令矢印・ハイライト・移動アニメーションを描画するView（実装済み）
    - `Combat/`
      - `UnitModifier.cs` : 戦闘・移動・視界・遅延などあらゆる補正を動的リストで扱う汎用修飾子
      - `CombatResolver.cs` : 包囲判定、過密スタック混乱判定、攻撃中防御低下判定、修飾子集約、突破力・対物攻撃力・兵員・組織率ダメージ計算、敗走退避・占領前進処理エンジン
    - `Command/`
      - `Order.cs` : 命令データ（種別、目標座標、対象ユニットID、経由パス、残り遅延ターン数・実装済み）
      - `CommandDelaySystem.cs` : 最も近い司令部からの経路コストに基づく遅延計算と命令キュー進行
      - `IntelReport.cs` : 偵察報告データ（発見敵ID、目撃座標、目撃ターン、到着までの残りターン数）
      - `FactionIntelState.cs` : 陣営ごとの視界マップと既知の敵情報（ゴースト・陳腐化管理）
      - `FogOfWarSystem.cs` : 各陣営の視界判定、報告遅延タイマー進行、ゴースト更新
    - `AI/`
      - `EnemyAIController.cs` : 敵陣営の `FactionIntelState` のみを参照し、`CommandDelaySystem` 経由で命令を発行するAIロジック
    - `UI/`
      - `BattleCameraController.cs` : マウスホイールのズームおよびパン操作（実装済み）
      - `MapEditorUI.cs` : 地形パレット、陣営エリアブラシ、マップ名入力、保存ボタン（実装済み）
      - `BattleUI.cs` : **マス目左クリック時の全スタック部隊一覧パネル**、個別・複数選択、命令操作UI、ターン終了ボタン、勝敗リザルトパネル（実装済み・拡張）
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
        public int fortificationLevel = 0;  // 構築物・要塞・城レベル (0〜5)
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
| `unitTypeId` | 表示名 | 司令レベル (`commandLevel`) | 最大兵力 (`maxManpower`) | 基礎攻撃 (`baseAttack`) | 対物攻撃 (`antiStructureAttack`) | 基礎防御 (`baseDefense`) | 突破力 (`breakthrough`) | 移動力 (`movePoints`) | 射程 (`attackRange`) | 偵察力 (`recon`) | 視界 (`visionRange`) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `"headquarters"` | 本陣 | `2` | `2000` | `120` | `80` | `250` | `100` | `2` | `1` | `2` | `3` |
| `"infantry"` | 歩兵（槍足軽） | `0` | `5000` | `300` | `150` | `450` | `250` | `3` | `1` | `1` | `2` |
| `"musketeer"` | 銃兵（鉄砲隊） | `0` | `3000` | `550` | `120` | `180` | `300` | `3` | `2` | `1` | `2` |
| `"cavalry"` | 騎兵 | `0` | `2500` | `480` | `100` | `260` | `600` | `5` | `1` | `3` | `4` |
| `"artillery"` | 砲兵（大筒） | `0` | `1500` | `750` | `600` | `120` | `400` | `1` | `3` | `0` | `2` |

- **司令レベル (`commandLevel`):** `0 = 一般部隊`, `1 = 前線司令部（軍団長等）`, `2 = 総大将本陣`。命令・報告の起点は同一陣営内で通信コストが最小の `commandLevel >= 1` 部隊となり、勝敗判定の主目標は `commandLevel >= 2` とする。
- **対物攻撃力 (`antiStructureAttack`):** 将来実装する「塹壕」「要塞」「城」などの構築物による防御減衰を受けにくくし、構築物自体を破壊しやすくする攻撃パラメータ（砲兵が最も高い）。

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
        public Sprite unitIcon;
        public GameObject unitPrefab;

        public int maxManpower = 3000;
        public int baseAttack = 300;
        public int antiStructureAttack = 100;
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
        public int equipmentRatio = 1000; // 千分率 (1000 = 100%) ※Phase 4時点では1000固定で扱い、管理機能はPhase 6以降で実装
    }

    [System.Serializable]
    public class UnitData
    {
        public string unitId;
        public string unitName;
        public Faction faction = Faction.Player;
        public int x;
        public int y;
        public int remainingMovePoints;

        // 師団を構成する大隊リスト（MVPでは要素1つ、将来は師団設計で複数編成）
        public List<BattalionData> battalions = new List<BattalionData>();

        // 戦闘・戦略ステータス（千分率）
        public int organization = 1000;    // 組織率（0で敗走）
        public int maxOrganization = 1000;
        public int supplyStatus = 1000;    // 補給状態（将来用）

        // 命令キュー（JsonUtility対応のためListで実装し、index 0 を先頭として扱う）
        public List<Order> pendingOrders = new List<Order>();
    }

---

### 6.3 汎用修飾子と戦闘・包囲・敗走・回復システム (`EdoStrategy.Combat`)

#### `UnitModifier` クラス（汎用補正）
戦闘・移動・視界・命令遅延などへのあらゆる補正を統一的に扱う。演算時は**同種の加算ボーナス（`+Bonus`）を合計したうえで、乗算倍率（`Multiplier / 1000`）を順次適用**する。

    [System.Serializable]
    public class UnitModifier
    {
        public string sourceId;                     // 例: "terrain_mountain", "flank_pincer", "overstacking"
        public string displayName;                  // UI表示用名称
        public int attackMultiplier = 1000;         // 攻撃倍率 (千分率)
        public int antiStructureMultiplier = 1000;  // 対物攻撃倍率 (千分率)
        public int defenseMultiplier = 1000;        // 防御倍率 (千分率)
        public int breakthroughMultiplier = 1000;   // 突破力倍率 (千分率)
        public int orgDamageMultiplier = 1000;      // 被組織率ダメージ倍率 (千分率)
        public int movePointBonus = 0;              // 移動力増減
        public int visionRangeBonus = 0;            // 視界増減
        public int commandDelayBonus = 0;           // 命令遅延ターン増減
    }

#### 包囲・挟み撃ち（Flanking / Encirclement）判定ルール (`CombatResolver.cs`)
防御側マスの隣接6マス（方向インデックス `0`〜`5`）を走査し、敵対ユニットが占拠している方向集合 $E$ を取得する（**最もペナルティが重い1つのみ適用**し、重複加算はしない）：
- **完全包囲（Encircled）:** 退路（敵ユニットがおらず進入可能な隣接マス）が `0`、かつ $|E| \ge 2$ → `defenseMultiplier = 500`, `orgDamageMultiplier = 1600`
- **背面挟撃（Pincer）:** 方向インデックス差 `3`（180度対角）のペアあり → `defenseMultiplier = 650`, `orgDamageMultiplier = 1500`
- **挟撃（Crossfire）:** 方向インデックス差 `2`（120度）のペアあり → `defenseMultiplier = 750`, `orgDamageMultiplier = 1300`
- **側面攻撃（Flanked）:** 上記以外で $|E| \ge 2$ → `defenseMultiplier = 850`, `orgDamageMultiplier = 1150`

#### 連携防衛・過密スタックペナルティ（Overstacking Penalty）
1マスに大量の部隊を詰め込む（デススタック）戦術を防ぐため、同一マスにいる味方部隊数に応じて以下の修飾子を適用する：
- **基本許容上限:** 1マスあたり **`2` 部隊** までペナルティなし。
- **司令部による統制緩和:** 同一マスに自陣営の司令部（`commandLevel >= 1`）が存在する場合、許容上限が **`+1` 緩和（`3` 部隊までペナルティなし）** される。
- **上限超過時のペナルティ（超過 1 部隊につき加算）:**
  - **防御側として戦闘時:** `defenseMultiplier` を `-100`（-10%・下限 `500`）、`orgDamageMultiplier` を `+100`（+10%）する。
  - **攻撃側として戦闘時:** `attackMultiplier` を `-100`（-10%・下限 `500`）する（狭い戦線で展開しきれない表現）。

#### 攻撃中ユニットの無防備ペナルティ（Attacking Exposure / Counter-Attack Vulnerability）
同時解決フェーズにおいて「攻撃中の部隊が別の敵から横槍攻撃を受けた場合」の挙動を以下のように定義する：
- **攻撃命令自体はキャンセルされない:** 攻撃中に別方向から攻撃を受けても、自らの攻撃命令は取り消されず続行される。
- **防御低下修飾子の適用:** そのターンに有効な攻撃命令（`OrderType.Attack`）を実行予定の部隊は、前傾姿勢で攻撃に集中しているため、他部隊から攻撃を受けた際に **`defenseMultiplier = 800`（防御 -20%）、`orgDamageMultiplier = 1200`（被組織率ダメージ +20%）** の修飾子を受ける。
- **解決順による事前脱落:** 解決順序（射程の長い順等）において、自分の攻撃を実行する**前**に他からの攻撃ダメージで兵員 `<= 0` または `organization <= 0`（敗走）となった場合は、その時点で戦線離脱となり自身の攻撃は不発となる。

#### ダメージ計算式（千分率演算）
- 有効攻撃力 $A_{eff}$ = $\lfloor \sum (\text{baseAttack} \times \frac{\text{currentManpower}}{\text{maxManpower}} \times \frac{\text{equipmentRatio}}{1000}) \times \frac{\text{supplyStatus}}{1000} \times \frac{\text{organization}}{1000} \times \prod \frac{\text{attackMultiplier}}{1000} \rfloor$ *(※`equipmentRatio` は Phase 4 時点では `1000` 固定)*
- 有効対物攻撃力 $AS_{eff}$ = $\lfloor \sum (\text{antiStructureAttack} \times \frac{\text{currentManpower}}{\text{maxManpower}} \times \frac{\text{equipmentRatio}}{1000}) \times \prod \frac{\text{antiStructureMultiplier}}{1000} \rfloor$ *(※将来の塹壕・要塞・城の防御ボーナス減衰・耐久削りに適用)*
- 有効突破力 $B_{eff}$ = $\lfloor \sum (\text{breakthrough} \times \frac{\text{currentManpower}}{\text{maxManpower}} \times \frac{\text{equipmentRatio}}{1000}) \times \prod \frac{\text{breakthroughMultiplier}}{1000} \rfloor$
- 有効防御力 $D_{eff}$ = $\lfloor \sum (\text{baseDefense} \times \frac{\text{currentManpower}}{\text{maxManpower}}) \times \frac{\text{terrainDefenseMultiplier}}{1000} \times \prod \frac{\text{defenseMultiplier}}{1000} \rfloor$
- 実質防御力 $D_{net}$ = $\max\left(50,\ D_{eff} - \lfloor B_{eff} \times 0.3 \rfloor\right)$
- **兵員ダメージ（Manpower Loss）:** $\max\left(10,\ \lfloor \frac{A_{eff} \times A_{eff}}{A_{eff} + D_{net}} \times 0.5 \rfloor\right)$
  *(複数大隊がいる場合は現在兵員数比率で按分し、端数は先頭大隊へ寄せる)*
- **組織率ダメージ（Org Loss）:** $\max\left(15,\ \lfloor \frac{A_{eff} \times 1000}{D_{net} + 200} \times \prod \frac{\text{orgDamageMultiplier}}{1000} \rfloor\right)$
- **反撃（Counter-Attack）の発生条件と計算:**
  - 防御側がダメージを受けた後でも生存（`CurrentTotalManpower > 0` かつ `organization > 0`）しており、かつ**防御側の `attackRange >= 攻撃側までのヘックス距離`** の場合のみ反撃が発生する。
  - 反撃ダメージは防御側の `baseDefense`（または攻撃力の `700` 倍率）をベースに算出し、攻撃側へ兵員・組織率ダメージを与える。

#### 複数スタック戦闘・移動ロック・敗走・占領・自然回復・解決順序ルール
1. **同時攻撃の解決順序:**
   - 同一ターンに複数部隊が攻撃を行う場合、**射程（`attackRange`）が長い順**（砲兵 → 銃兵 → 歩兵・騎兵）に解決する。
2. **複数スタック戦闘:**
   - 攻撃対象マスに複数の敵ユニットが存在する場合、防衛側の主力ユニット（組織率・防御力が最も高いユニット）と交戦し、防衛側スタック全体でダメージを分担・適用する。
3. **攻撃による移動ロック（釘付け／Pinning）:**
   - そのターンに有効な攻撃命令（`OrderType.Attack`）の攻撃対象マスに指定されている部隊は、戦闘が解決されるまで移動命令（`OrderType.Move`）を実行できずその場に釘付けとなる。
4. **敗走（Rout）と退避:**
   - 戦闘ダメージにより `CurrentTotalManpower <= 0` となった部隊は**消滅（壊滅）**する。
   - 兵員は生存しているが `organization <= 0` となった部隊（総大将本陣 `commandLevel >= 2` も含む）は**敗走（Rout）**となり、攻撃元の**反対側（後方3マス：真後ろの方向を最優先、なければ他方向）にある「敵がいない進入可能マス（味方スタックへの合流可）」**へ1マス退避する。
   - 敵ユニットと進入不可地形に完全に囲まれて退避可能なマスが1つもない場合は、退路断絶により**消滅（壊滅）**する。
   - 敗走退避に成功して生き残った部隊は、組織率が **`250`（25.0%）** まで復帰する。
5. **空きマスへの前進・占領ルール:**
   - 戦闘によって防衛側マスの**全敵部隊**が消滅または敗走退避してマスが空いた場合、以下の優先順位で攻撃側部隊がそのマスへ進入する：
     1. そのマスへ今ターン実行予定の移動命令（`OrderType.Move`）を出していた攻撃側部隊。
     2. 上記がなければ、トドメを刺した攻撃側が**近接兵科（`attackRange == 1`）**の場合のみ前進して占領する（銃兵・砲兵などの遠距離兵科は、移動命令がない限り前進しない）。
6. **ターン終了時の自然回復（非戦闘部隊のみ）:**
   - そのターンに攻撃も被弾（防御）もしていない部隊は、ターン終了時（`ReconAndReportPhase`）に以下が回復する：
     - **組織率：** `+150`（15.0%）回復（上限 `maxOrganization`）。
     - **兵員数（`currentManpower`）：** 隣接6マスにいる敵部隊数に応じて回復率が決まる。
       - 隣接敵 `0` 部隊（安全圏）：最大兵員数の **`5%`** 回復
       - 隣接敵 `1` 部隊（前線対峙）：最大兵員数の **`2%`** 回復
       - 隣接敵 `2` 部隊以上（包囲下）：**`0%`**（回復なし）

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
        public List<Vector2Int> path = new List<Vector2Int>();
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

### 6.6 HoI4式マップ表示・UI仕様 (`EdoStrategy.UI` / `EdoStrategy.Units`)

#### 1. HoI4式マップカウンター表示 (`HexStackCounterView.cs` / `UnitBoardView.cs`)
1つのヘックスマスに複数の部隊が存在する場合、既存の丸型アイコンを廃止し、HoI4と同一の**四角形（矩形プレート）カウンター**で描画する。
- **四角形カウンター枠と陣営色分け:**
  - カウンターはすべて**四角形（横長の矩形プレート＋外枠線）**で描画する（丸型スプライトは使用しない）。
  - 自軍は青系ベース、敵軍は赤系ベースの背景色および枠線で陣営を識別する。
- **同種ユニットのグルーピング（スタックまとめ）:**
  - 同一マス・同一陣営（`Faction`）内で、**同じ兵科（`unitTypeId`）のユニットは1つの四角形カウンター枠にまとめて表示**する。
  - カウンター枠内の右側に、**その兵科のスタック数（例：`1`, `2`, `3`）を数値で表示**する。
- **複数兵科の共存表示:**
  - 同一マスに異なる兵科（例：歩兵2部隊＋銃兵1部隊＋砲兵1部隊）が混在している場合、**兵科ごとの四角形カウンター枠を同じマス目内に縦に並べて（または少しずらして重ねて）すべて同時に表示**し、どの兵科が何部隊いるか一目で判別できるようにする。
- **縦棒2本ゲージ（組織率 ＆ 兵員充足率）:**
  - 各四角形カウンター枠の左端の内側に、**2本の縦型プログレスバー（縦棒ゲージ）**を並べて描画する。
    - **1本目（組織率 Organization）：** グループ内の組織率割合を**緑色**（低下時は黄〜赤）の縦棒で表示。
    - **2本目（兵員数 Manpower）：** グループ内の兵員充足率（`currentManpower / maxManpower`）を**茶色／オレンジ色**の縦棒で表示。
- **選択強調ハイライト:**
  - 現在選択中のユニットが含まれるカウンター枠には、明るい黄色（またはシアン）の**四角い選択外枠ハイライト**を表示する。

#### 2. マス目左クリックによるスタック全ユニット一覧表示 (`BattleUI.cs`)
- プレイヤーがマップ上の**マス目を左クリック**した際、そのマス目に部隊が存在する場合（1部隊でも複数部隊でも）、UI上に**「選択マス部隊一覧パネル」**を開き、**そのマス目にいるすべてのユニットを一覧表示**する。
- 各リスト項目には「兵科アイコン・部隊名・縦棒2本ゲージ（組織率・兵員数）・数値（兵員数・組織率%）・現在の予約命令ステータス」を表示する。
- 一覧リスト内の特定ユニットをクリックすると、その1部隊が個別選択状態（`selectedUnit`）となり、個別に移動・攻撃等の命令を出せる。また、パネル内の「同マス全選択」ボタンにより、そのマスの自軍全スタック部隊をまとめて選択・一括命令することも可能とする。

---

## 7. バトル進行ステートマシン (`TurnManager.cs`)
1. **`DeploymentPhase`（初期配置フェーズ）:**
   - `MapSerializer` でJSONマップを読み込み、`HexGrid` と `HexTilemapView` を初期化。
   - プレイヤーは `deployZoneFaction == 1`（マップに未設定の場合は左端3列）のセルに総大将本陣1部隊＋任意部隊を配置（同一マスへの複数スタック配置も可）。敵AIは `deployZoneFaction == 2`（未設定の場合は右端3列）のセルに自動配置。
   - 「開戦」ボタンで `PlayerCommandPhase` へ遷移。
2. **`PlayerCommandPhase`（プレイヤー命令フェーズ）:**
   - マス目左クリックでマス内の全ユニット一覧を表示し、個別選択またはスタック一括選択。
   - 移動先（`OrderType.Move`）または射程内の敵マスへの攻撃対象（`OrderType.Attack`）を指定して `Order` を `pendingOrders` に登録（待機中命令のキャンセル・上書きも可能）。
   - 「ターン終了」ボタンで選択状態を強制解除し、`ExecutionPhase` へ遷移。
3. **`ExecutionPhase`（解決フェーズ）:**
   - **ステップA（AI命令登録）:** 敵AI（`EnemyAIController`）が敵陣営の認識情報に基づき命令を発行（`CommandDelaySystem` 経由で遅延付与）。
   - **ステップB（遅延カウント進行）:** 両陣営の全生存部隊の `pendingOrders[0]` の `remainingDelayTurns` を `-1` し、`0` 以下になった命令を実行対象とする。
   - **ステップC（移動の一斉解決）:** 今ターン有効な `OrderType.Attack` の攻撃対象マスに指定されている部隊は「釘付け（移動ロック）」となり移動不可。それ以外の実行対象 `OrderType.Move` を解決する（途中で伏兵・未発見の敵と接触した場合はその1マス手前で停止）。
   - **ステップD（戦闘の一斉解決と敗走・占領処理）:** 移動完了後の配置を基準に、射程（`attackRange`）が長い順に実行対象の全 `OrderType.Attack` 命令を解決する。ダメージ計算・反撃処理の後、組織率0の敗走退避を実行し、空いたマスに対して「①移動予約済みの攻撃側部隊 ＞ ②近接攻撃（射程1）による占領前進」を適用する。
4. **`ReconAndReportPhase`（視界・伝令更新・自然回復フェーズ）:**
   - `FogOfWarSystem` によりプレイヤー・敵AI双方の視界再計算、偵察報告タイマー進行、陳腐化したゴーストの削除・更新イベントを発火。
   - そのターンに攻撃も被弾（防御）もしていない非戦闘部隊に対し、組織率（`+150`）および隣接敵数に応じた兵員数（`5%` / `2%` / `0%`）の自然回復を実行する。
5. **`CheckVictoryPhase`（勝敗判定フェーズ）:**
   - 敵の総大将本陣（`commandLevel >= 2`）の兵員数 `<= 0` または敵全滅 → **プレイヤー勝利**（`BattleEnded` へ遷移）。
   - 自軍の総大将本陣（`commandLevel >= 2`）の兵員数 `<= 0` または自軍全滅 → **プレイヤー敗北**（`BattleEnded` へ遷移）。
   - 未決着の場合、ターン数を `+1` して `PlayerCommandPhase` へ戻る。

---

## 8. 段階的実装ステップ（Copilotへの指示順序）
1. **Phase 1（マップ基盤と描画・完了済み）:** `HexCoordinates.cs` / `TerrainDefinition.cs` / `HexCell.cs` / `HexGrid.cs` / `HexTilemapView.cs`
2. **Phase 2（マップエディタとJSON保存・完了済み）:** `MapSerializer.cs` / `MapEditorController.cs`（`Scripts/MapEditor/`）/ `MapEditorUI.cs`
3. **Phase 3 & 3.5（ユニット配置・基本移動・HoI4式無制限スタック・四角形カウンター・複数選択UI・完了済み）:** `GameEnums.cs` / `GameIds.cs` / `UnitDefinition.cs` / `BattalionData.cs` / `UnitData.cs` / `UnitManager.cs` / `HexStackCounterView.cs` / `UnitBoardView.cs` / `BattleCameraController.cs` / `TurnManager.cs` / `BattleUI.cs`
4. **Phase 4（戦闘・包囲・過密混乱・敗走計算と勝敗判定・現在地）:** `UnitModifier.cs` / `CombatResolver.cs` を実装し、包囲ペナルティ・過密スタック混乱・攻撃中防御低下・突破力・対物攻撃力・反撃・敗走退避・自然回復・本陣撃破による勝敗リザルトを実装する。
5. **Phase 5（命令・報告遅延システムとフェアな敵AI）:** `Order.cs` / `CommandDelaySystem.cs` / `IntelReport.cs` / `FactionIntelState.cs` / `FogOfWarSystem.cs` / `EnemyAIController.cs` を実装し、両陣営に適用される命令遅延とゴースト表示（および陳腐化消滅）を実装する。
6. **Phase 6（HoI4ライクな戦略拡張・操作チューニング）:** `HexCell.infrastructureLevel` を用いた補給線（兵站ルートと補給切れペナルティ、A*経路キャッシュ）、師団編成UI（複数 `BattalionData` の編集）、兵士人口（Manpower）プールからの補充、構築物（城・要塞・塹壕）、およびPC向けキーボードショートカット・操作キー配置の最適化を行う。