using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 리팩터링 이전에 코드에 하드코딩되어 있던 타일·개체·바닥 타일 값을
/// 정의 에셋으로 한 번에 옮겨 주는 마이그레이션 도구.
///
/// 아래 표는 리팩터링 시점의 TileHardness / TileConductivity / TileHeatOutput /
/// MiningSkillGate / FloorTileType / MapGenerator 값을 그대로 옮겨 적은 것입니다.
/// 실행하면 에셋을 만든 뒤 기존 ResourceManager에서 타일 에셋·프리팹·드롭 아이템
/// 참조까지 끌어와 연결하므로, 수작업 재연결이 필요 없습니다.
///
/// 한 번 실행하고 나면 다시 쓸 일이 없지만, 값의 출처를 남겨 두는 문서 역할도 하므로
/// 지우지 말고 두세요.
/// </summary>
public static class DefinitionBootstrapper
{
    #region 경로

    private const string TileDir = "Assets/02.Data/ScriptableObject/Tile";
    private const string EntityDir = "Assets/02.Data/ScriptableObject/Entity";
    private const string FloorDir = "Assets/02.Data/ScriptableObject/FloorTile";
    private const string ResourcesDir = "Assets/Resources";
    private const string DatabasePath = ResourcesDir + "/DefinitionDatabase.asset";
    private const string ResourceManagerPath = "Assets/02.Data/Config/ResourceManager.asset";

    #endregion

    #region 시드 테이블 — 타일

    /// <summary>리팩터링 이전 코드에서 옮겨 온 타일 값.</summary>
    private readonly struct TileSeed
    {
        public readonly int Id;
        public readonly string Code;
        public readonly string Label;
        public readonly bool Solid;
        public readonly bool Climbable;
        public readonly bool SpawnableSurface;
        public readonly bool Mineable;
        public readonly float Hardness;
        public readonly int SkillId;
        public readonly float Conductivity;
        public readonly float Heat;

        // 광맥 (MapGenerator.PlaceMineralClusters에서 옮겨 옴)
        public readonly bool Vein;
        public readonly int Shallow;
        public readonly int Deep;
        public readonly float NoiseScale;
        public readonly float Threshold;
        public readonly float SeedOffset;

        public TileSeed(int id, string code, string label, bool solid, bool climbable,
            bool spawnableSurface, bool mineable, float hardness, int skillId,
            float conductivity, float heat,
            bool vein = false, int shallow = 0, int deep = 0,
            float noiseScale = 0.12f, float threshold = 0.65f, float seedOffset = 0f)
        {
            Id = id; Code = code; Label = label;
            Solid = solid; Climbable = climbable; SpawnableSurface = spawnableSurface;
            Mineable = mineable; Hardness = hardness; SkillId = skillId;
            Conductivity = conductivity; Heat = heat;
            Vein = vein; Shallow = shallow; Deep = deep;
            NoiseScale = noiseScale; Threshold = threshold; SeedOffset = seedOffset;
        }
    }

    // 경도: TileHardness.Get / 스킬: MiningSkillGate.RequiredSkillId
    // 전도율: TileConductivity.Get / 발열: TileHeatOutput.Get
    // 통행: GameMap의 AIR_ID·LADDER_ID / 스폰면: GameMap.IsTileSpawnable(DIRT·GRASS)
    private static readonly TileSeed[] TileSeeds =
    {
        //           id  code             label       solid climb spawn  mine  hard skill cond  heat   광맥  얕음  깊음  scale  thr    seed
        new TileSeed( 0, "Air",           "빈 공간",    false, false, false, false, 1.0f, 0, 1.0f, 0f),
        new TileSeed( 1, "Dirt",          "흙",         true,  false, true,  true,  0.6f, 0, 0.5f, 0f),
        new TileSeed( 2, "Stone",         "돌",         true,  false, false, true,  1.0f, 0, 1.0f, 0f),
        new TileSeed( 3, "CopperOre",     "구리 광석",  true,  false, false, true,  1.5f, 5, 2.2f, 0f,   true, -10, -30, 0.15f, 0.70f, 2000f),
        new TileSeed( 4, "IronOre",       "철 광석",    true,  false, false, true,  2.2f, 6, 1.9f, 0f,   true, -20, -45, 0.18f, 0.75f, 3000f),
        new TileSeed( 5, "GoldOre",       "금 광석",    true,  false, false, true,  4.0f, 7, 2.8f, 0f,   true, -40, -70, 0.20f, 0.80f, 4000f),
        new TileSeed( 6, "GrassDirt",     "잔디흙",     true,  false, true,  true,  0.6f, 0, 0.5f, 0f),
        new TileSeed( 7, "ProcessedDirt", "가공된 흙",  true,  false, false, true,  0.6f, 0, 0.4f, 0f),
        new TileSeed( 8, "Ladder",        "사다리",     false, true,  false, false, 1.0f, 0, 1.0f, 0f),
        new TileSeed( 9, "Coal",          "석탄",       true,  false, false, true,  1.2f, 5, 0.6f, 0f,   true,  -3, -20, 0.12f, 0.65f, 5000f),
        new TileSeed(10, "SilverOre",     "은 광석",    true,  false, false, true,  3.0f, 6, 2.6f, 0f,   true, -25, -55, 0.14f, 0.73f, 6000f),
        new TileSeed(11, "Crystal",       "수정",       true,  false, false, true,  6.0f, 7, 1.5f, 3.0f, true, -55, -90, 0.22f, 0.82f, 7000f),
        new TileSeed(99, "Special",       "특수",       true,  false, false, false, 1.0f, 0, 1.0f, 0f),
    };

    #endregion

    #region 시드 테이블 — 개체

    /// <summary>리팩터링 이전 EntityType + MapGenerator 배치 값.</summary>
    private readonly struct EntitySeed
    {
        public readonly int Id;
        public readonly string Code;
        public readonly string Label;
        public readonly EntityKind Kind;
        public readonly bool AutoSpawn;
        public readonly int Order;
        public readonly PlacementMode Mode;
        public readonly string StampKey;
        public readonly SurfaceScanMode Scan;
        public readonly int Width;
        public readonly float Chance;
        public readonly int Spacing;
        public readonly TileType[] Ground;
        public readonly bool Persist;
        public readonly bool Growth;

        public EntitySeed(int id, string code, string label, EntityKind kind,
            bool autoSpawn = false, int order = 0,
            PlacementMode mode = PlacementMode.Entity, string stampKey = "",
            SurfaceScanMode scan = SurfaceScanMode.GroundLevel, int width = 1,
            float chance = 0f, int spacing = 0, TileType[] ground = null,
            bool persist = false, bool growth = false)
        {
            Id = id; Code = code; Label = label; Kind = kind;
            AutoSpawn = autoSpawn; Order = order; Mode = mode; StampKey = stampKey;
            Scan = scan; Width = width; Chance = chance; Spacing = spacing;
            Ground = ground ?? new TileType[0];
            Persist = persist; Growth = growth;
        }
    }

    // 배치 값 출처: MapGenerator의 PlaceTrees / PlaceBerryBushes / PlaceErosionPlants
    //   나무      — treeStampKey="TREE_2x3" (씬에 직렬화된 실제 값. 코드 기본값 "TREE_2X3"는 오타였다), minTreeDistance=5, treePlacementChance=0.5, 2칸 폭, 잔디 위
    //   베리 덤불 — berryBushStampKey="BERRY_BUSH", minBerryBushDistance=11, chance=0.25, 잔디 위
    //   침식 식물 — 지상 노출면 전체, 독성고사리 0.03 / 부패버섯 0.02
    // placementOrder는 리팩터링 이전 호출 순서(침식 식물 → 나무 → 베리 덤불)를 그대로 옮긴 것입니다.
    // 먼저 놓인 개체가 칸을 점유하므로 순서가 결과를 바꿉니다.
    private static readonly EntitySeed[] EntitySeeds =
    {
        // '개체 없음'을 나타내는 예약 정의. ResourceManager 등이 EntityType.None을 씁니다.
        new EntitySeed(0, "None", "없음", EntityKind.None),

        new EntitySeed(20, "ToxicFern", "독성 고사리", EntityKind.Plant,
            autoSpawn: true, order: 0, mode: PlacementMode.Entity,
            scan: SurfaceScanMode.AnyExposedSurface, chance: 0.03f, persist: true),

        new EntitySeed(21, "CorruptedMushroom", "부패한 버섯", EntityKind.Plant,
            autoSpawn: true, order: 1, mode: PlacementMode.Entity,
            scan: SurfaceScanMode.AnyExposedSurface, chance: 0.02f, persist: true),

        new EntitySeed(2001, "Tree2x3", "나무", EntityKind.Plant,
            autoSpawn: true, order: 10, mode: PlacementMode.Stamp, stampKey: "TREE_2x3",
            scan: SurfaceScanMode.GroundLevel, width: 2, chance: 0.5f, spacing: 5,
            ground: new[] { TileType.GrassDirt }, persist: true, growth: true),

        new EntitySeed(2002, "HiringOffice", "채용 사무소", EntityKind.Building),

        new EntitySeed(2003, "BerryBush", "베리 덤불", EntityKind.Plant,
            autoSpawn: true, order: 20, mode: PlacementMode.Stamp, stampKey: "BERRY_BUSH",
            scan: SurfaceScanMode.GroundLevel, width: 1, chance: 0.25f, spacing: 11,
            ground: new[] { TileType.GrassDirt }, persist: true),

        new EntitySeed(2004, "WoodLadder",  "나무 사다리", EntityKind.Building),
        new EntitySeed(2005, "Chest",       "상자",        EntityKind.Building),
        new EntitySeed(2006, "WoodDoor",    "나무 문",     EntityKind.Building),

        new EntitySeed(2010, "WoodFloor",   "나무 바닥",   EntityKind.FloorPrefab),
        new EntitySeed(2011, "StoneFloor",  "돌 바닥",     EntityKind.FloorPrefab),
        new EntitySeed(2012, "MetalFloor",  "금속 바닥",   EntityKind.FloorPrefab),
        new EntitySeed(2013, "DirtFloor",   "흙 바닥",     EntityKind.FloorPrefab),
        new EntitySeed(2014, "CopperFloor", "구리 바닥",   EntityKind.FloorPrefab),
        new EntitySeed(2015, "GoldFloor",   "금 바닥",     EntityKind.FloorPrefab),
        new EntitySeed(2016, "SilverFloor", "은 바닥",     EntityKind.FloorPrefab),
    };

    #endregion

    #region 시드 테이블 — 바닥 타일

    private readonly struct FloorSeed
    {
        public readonly int Id;
        public readonly string Code;
        public readonly string Label;
        public readonly float Speed;
        public readonly bool Passable;
        public readonly bool Vertical;

        public FloorSeed(int id, string code, string label, float speed, bool passable, bool vertical)
        {
            Id = id; Code = code; Label = label;
            Speed = speed; Passable = passable; Vertical = vertical;
        }
    }

    // 값 출처: Build Prefab/Tile 아래 FloorTile 프리팹들의 직렬화 값 (문서 주석과 일치 확인 완료)
    private static readonly FloorSeed[] FloorSeeds =
    {
        new FloorSeed(0, "WoodFloor",   "나무 바닥",     1.0f, false, false),
        new FloorSeed(1, "StoneFloor",  "돌 바닥",       1.0f, false, false),
        new FloorSeed(2, "MetalFloor",  "금속 바닥",     1.2f, false, false),
        new FloorSeed(3, "WoodLadder",  "나무 사다리",   0.8f, true,  true),
        new FloorSeed(4, "MetalLadder", "금속 사다리",   0.9f, true,  true),
        new FloorSeed(5, "Catwalk",     "좁은 통로",     0.9f, false, false),
        new FloorSeed(6, "Bridge",      "다리",          1.0f, false, false),
        new FloorSeed(7, "DirtFloor",   "가공된 흙 바닥", 0.9f, false, false),
    };

    #endregion

    #region 메뉴

    [MenuItem("XNP/정의/현재 코드에서 정의 에셋 생성", priority = 20)]
    public static void BootstrapFromMenu()
    {
        bool proceed = EditorUtility.DisplayDialog(
            "정의 에셋 생성",
            "리팩터링 이전 코드에 하드코딩되어 있던 값으로\n" +
            $"타일 {TileSeeds.Length}종 · 개체 {EntitySeeds.Length}종 · 바닥 타일 {FloorSeeds.Length}종의\n" +
            "정의 에셋과 DefinitionDatabase를 만듭니다.\n\n" +
            "이미 있는 에셋은 덮어쓰지 않고 그대로 둡니다.\n" +
            "기존 ResourceManager에서 타일 에셋·프리팹·드롭 아이템 참조를 끌어옵니다.",
            "생성", "취소");
        if (!proceed) return;

        EditorUtility.DisplayDialog("생성 완료", Bootstrap(), "확인");
    }

    /// <summary>
    /// 정의 에셋과 데이터베이스를 만듭니다. 대화상자를 띄우지 않으므로 자동화에서도 호출할 수 있습니다.
    /// 이미 있는 에셋은 건드리지 않습니다.
    /// </summary>
    /// <returns>사람이 읽을 결과 문장</returns>
    public static string Bootstrap()
    {
        EnsureFolder(TileDir);
        EnsureFolder(EntityDir);
        EnsureFolder(FloorDir);
        EnsureFolder(ResourcesDir);

        var links = ReadResourceManagerLinks();

        var tiles = new List<TileDefinition>();
        foreach (var seed in TileSeeds)
        {
            var def = LoadOrCreate<TileDefinition>($"{TileDir}/Tile_{seed.Code}.asset", out bool created);
            if (created)
            {
                def.codeName = seed.Code;
                def.id = seed.Id;
                def.displayName = seed.Label;
                def.isSolid = seed.Solid;
                def.isClimbable = seed.Climbable;
                def.isSpawnableSurface = seed.SpawnableSurface;
                def.isMineable = seed.Mineable;
                def.hardness = seed.Hardness;
                def.requiredMiningSkillId = seed.SkillId;
                def.thermalConductivity = seed.Conductivity;
                def.heatOutput = seed.Heat;
                def.generateAsVein = seed.Vein;
                def.veinShallowOffset = seed.Shallow;
                def.veinDeepOffset = seed.Deep;
                def.veinNoiseScale = seed.NoiseScale;
                def.veinThreshold = seed.Threshold;
                def.veinSeedOffset = seed.SeedOffset;

                links.TileAssets.TryGetValue(seed.Id, out var tileAsset);
                def.tileAsset = tileAsset;
                links.DropItems.TryGetValue(seed.Id, out var drop);
                def.dropItem = drop;

                EditorUtility.SetDirty(def);
            }
            tiles.Add(def);
        }

        var entities = new List<EntityDefinition>();
        foreach (var seed in EntitySeeds)
        {
            var def = LoadOrCreate<EntityDefinition>($"{EntityDir}/Entity_{seed.Code}.asset", out bool created);
            if (created)
            {
                def.codeName = seed.Code;
                def.id = seed.Id;
                def.displayName = seed.Label;
                def.kind = seed.Kind;
                def.spawnOnMapGeneration = seed.AutoSpawn;
                def.placementOrder = seed.Order;
                def.placementMode = seed.Mode;
                def.stampKey = seed.StampKey;
                def.scanMode = seed.Scan;
                def.footprintWidth = seed.Width;
                def.spawnChance = seed.Chance;
                def.minSpacing = seed.Spacing;
                def.allowedGroundTiles = seed.Ground;
                def.avoidSpawnArea = true;
                def.persistInSave = seed.Persist;
                def.hasGrowthState = seed.Growth;

                links.Prefabs.TryGetValue(seed.Id, out var prefab);
                def.prefab = prefab;

                EditorUtility.SetDirty(def);
            }
            entities.Add(def);
        }

        var floors = new List<FloorTileDefinition>();
        foreach (var seed in FloorSeeds)
        {
            var def = LoadOrCreate<FloorTileDefinition>($"{FloorDir}/Floor_{seed.Code}.asset", out bool created);
            if (created)
            {
                def.codeName = seed.Code;
                def.id = seed.Id;
                def.displayName = seed.Label;
                def.movementSpeedMultiplier = seed.Speed;
                def.isPassable = seed.Passable;
                def.allowsVerticalMovement = seed.Vertical;
                EditorUtility.SetDirty(def);
            }
            floors.Add(def);
        }

        var db = LoadOrCreate<DefinitionDatabase>(DatabasePath, out _);
        db.tiles = tiles;
        db.entities = entities;
        db.floorTiles = floors;
        db.RebuildCaches();
        EditorUtility.SetDirty(db);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        DefinitionDatabase.SetInstance(db);
        Selection.activeObject = db;

        string message =
            $"정의 에셋 생성 완료\n\n" +
            $"타일 {tiles.Count}종 → {TileDir}\n" +
            $"개체 {entities.Count}종 → {EntityDir}\n" +
            $"바닥 타일 {floors.Count}종 → {FloorDir}\n" +
            $"데이터베이스 → {DatabasePath}\n\n" +
            $"타일 에셋 {links.TileAssets.Count}건 · 프리팹 {links.Prefabs.Count}건 · " +
            $"드롭 아이템 {links.DropItems.Count}건을 ResourceManager에서 가져왔습니다.\n\n" +
            "이어서 메뉴 [XNP/정의/enum 재생성]을 실행하세요.";

        Debug.Log("[DefinitionBootstrapper] " + message.Replace("\n\n", " / ").Replace("\n", " "));
        return message;
    }

    #endregion

    #region 기존 참조 수집

    private sealed class ResourceLinks
    {
        public readonly Dictionary<int, TileBase> TileAssets = new Dictionary<int, TileBase>();
        public readonly Dictionary<int, GameObject> Prefabs = new Dictionary<int, GameObject>();
        public readonly Dictionary<int, ItemData> DropItems = new Dictionary<int, ItemData>();
    }

    /// <summary>
    /// 기존 ResourceManager 에셋에서 타일 에셋·프리팹·드롭 아이템 참조를 읽어 옵니다.
    /// 필드가 private이라 SerializedObject로 접근합니다.
    /// </summary>
    private static ResourceLinks ReadResourceManagerLinks()
    {
        var links = new ResourceLinks();

        var rm = AssetDatabase.LoadAssetAtPath<ResourceManager>(ResourceManagerPath);
        if (rm == null)
        {
            Debug.LogWarning($"[DefinitionBootstrapper] {ResourceManagerPath}를 찾지 못해 " +
                             "참조 자동 연결을 건너뜁니다. 정의 에셋에 직접 연결하세요.");
            return links;
        }

        var so = new SerializedObject(rm);

        var tileEntries = so.FindProperty("tileEntries");
        for (int i = 0; tileEntries != null && i < tileEntries.arraySize; i++)
        {
            var e = tileEntries.GetArrayElementAtIndex(i);
            int id = e.FindPropertyRelative("tile").intValue;
            var asset = e.FindPropertyRelative("tileAsset").objectReferenceValue as TileBase;
            if (asset != null) links.TileAssets[id] = asset;
        }

        var entityEntries = so.FindProperty("entityEntries");
        for (int i = 0; entityEntries != null && i < entityEntries.arraySize; i++)
        {
            var e = entityEntries.GetArrayElementAtIndex(i);
            int id = e.FindPropertyRelative("entity").intValue;
            var prefab = e.FindPropertyRelative("prefab").objectReferenceValue as GameObject;
            if (prefab != null) links.Prefabs[id] = prefab;
        }

        var dropEntries = so.FindProperty("dropEntries");
        for (int i = 0; dropEntries != null && i < dropEntries.arraySize; i++)
        {
            var e = dropEntries.GetArrayElementAtIndex(i);
            int id = e.FindPropertyRelative("tile").intValue;
            var item = e.FindPropertyRelative("itemData").objectReferenceValue as ItemData;
            if (item != null) links.DropItems[id] = item;
        }

        return links;
    }

    #endregion

    #region 유틸리티

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(path);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    private static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null)
        {
            created = false;
            return existing;
        }

        var asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        created = true;
        return asset;
    }

    #endregion
}
