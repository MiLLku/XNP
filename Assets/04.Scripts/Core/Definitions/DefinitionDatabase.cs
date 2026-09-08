using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 타일·개체·바닥 타일 정의 에셋을 모아 두고 ID로 조회해 주는 데이터베이스.
///
/// <b>Resources 폴더에 두는 이유</b>: TileHardness·GameMap처럼 인스펙터 참조를 받을 수 없는
/// 정적 코드에서도 정의를 읽어야 하므로, 씬 배선 없이 지연 로드할 수 있어야 합니다.
/// 에셋 경로는 <see cref="ResourcePath"/>로 고정됩니다.
///
/// 정의 에셋이 아직 없어도 게임이 죽지 않도록, 조회 실패 시에는 각 소비처가
/// 예전 하드코딩 기본값으로 되돌아갑니다(<see cref="TileRules"/> 참고).
/// </summary>
[CreateAssetMenu(fileName = "DefinitionDatabase", menuName = "XNP/정의/정의 데이터베이스", order = -1)]
public class DefinitionDatabase : ScriptableObject
{
    #region 상수 · 인스턴스

    /// <summary>Resources 기준 경로 (확장자 없음).</summary>
    public const string ResourcePath = "DefinitionDatabase";

    private static DefinitionDatabase _instance;

    /// <summary>
    /// 데이터베이스 인스턴스. 처음 접근할 때 Resources에서 지연 로드합니다.
    /// 에셋이 없으면 null이며, 이 경우 소비처는 하드코딩 기본값을 씁니다.
    /// </summary>
    public static DefinitionDatabase Instance
    {
        get
        {
            if (_instance == null)
                _instance = Resources.Load<DefinitionDatabase>(ResourcePath);
            return _instance;
        }
    }

    /// <summary>테스트·에디터 도구에서 인스턴스를 직접 주입합니다.</summary>
    public static void SetInstance(DefinitionDatabase db)
    {
        _instance = db;
        if (db != null) db.RebuildCaches();
        TileRules.Invalidate();
    }

    #endregion

    #region 필드

    [Header("타일 (블록)")]
    [Tooltip("모든 TileDefinition. TileType enum이 이 목록에서 생성됩니다.")]
    public List<TileDefinition> tiles = new List<TileDefinition>();

    [Header("개체 (식생물·건물·바닥 프리팹)")]
    [Tooltip("모든 EntityDefinition. EntityType enum이 이 목록에서 생성됩니다.")]
    public List<EntityDefinition> entities = new List<EntityDefinition>();

    [Header("바닥 타일")]
    [Tooltip("모든 FloorTileDefinition. FloorTileType enum이 이 목록에서 생성됩니다.")]
    public List<FloorTileDefinition> floorTiles = new List<FloorTileDefinition>();

    [Tooltip("지층 목록 — 순서가 곧 위에서 아래입니다(첫 항목이 하늘, 마지막이 최하층). " +
             "두께는 thicknessWeight의 비율로 정해지므로 맵 높이를 바꿔도 구성이 따라옵니다.")]
    public List<StrataDefinition> strata = new List<StrataDefinition>();

    private Dictionary<int, TileDefinition> _tileById;
    private Dictionary<int, EntityDefinition> _entityById;
    private Dictionary<int, FloorTileDefinition> _floorById;
    private List<EntityDefinition> _naturalSpawns;
    private StrataLayout _strataLayout;

    #endregion

    #region 생명주기

    private void OnEnable() => RebuildCaches();

    /// <summary>ID 조회 캐시를 다시 만듭니다. 목록을 편집한 뒤 호출하세요.</summary>
    public void RebuildCaches()
    {
        _tileById = new Dictionary<int, TileDefinition>();
        if (tiles != null)
        {
            foreach (var def in tiles)
            {
                if (def == null) continue;
                if (_tileById.ContainsKey(def.id))
                {
                    Debug.LogWarning($"[DefinitionDatabase] 타일 ID {def.id} 중복 — '{def.codeName}'은(는) 무시됩니다.");
                    continue;
                }
                _tileById[def.id] = def;
            }
        }

        _entityById = new Dictionary<int, EntityDefinition>();
        _naturalSpawns = new List<EntityDefinition>();
        if (entities != null)
        {
            foreach (var def in entities)
            {
                if (def == null) continue;
                if (_entityById.ContainsKey(def.id))
                {
                    Debug.LogWarning($"[DefinitionDatabase] 개체 ID {def.id} 중복 — '{def.codeName}'은(는) 무시됩니다.");
                    continue;
                }
                _entityById[def.id] = def;
                if (def.spawnOnMapGeneration) _naturalSpawns.Add(def);
            }
        }
        _naturalSpawns.Sort((a, b) => a.placementOrder.CompareTo(b.placementOrder));

        _floorById = new Dictionary<int, FloorTileDefinition>();
        if (floorTiles != null)
        {
            foreach (var def in floorTiles)
            {
                if (def == null) continue;
                if (_floorById.ContainsKey(def.id))
                {
                    Debug.LogWarning($"[DefinitionDatabase] 바닥 타일 ID {def.id} 중복 — '{def.codeName}'은(는) 무시됩니다.");
                    continue;
                }
                _floorById[def.id] = def;
            }
        }

        // 지층 경계는 맵 높이 상수에서 파생되므로 여기서 한 번만 풀어 둔다
        _strataLayout = new StrataLayout(strata, GameMap.MAP_HEIGHT);

        TileRules.Invalidate();
    }

    #endregion

    #region 조회 — 지층

    /// <summary>
    /// 지층 목록을 Y 경계로 푼 결과. 목록을 편집하면 <see cref="RebuildCaches"/> 후 갱신됩니다.
    /// 지층을 하나도 등록하지 않으면 비어 있는 레이아웃이 오고, 생성기는 예전 단일 파라미터로 동작합니다.
    /// </summary>
    public StrataLayout StrataLayout
    {
        get
        {
            if (_strataLayout == null) RebuildCaches();
            return _strataLayout;
        }
    }

    #endregion

    #region 조회 — 타일

    /// <summary>타일 ID로 정의를 조회합니다. 없으면 null.</summary>
    public TileDefinition GetTile(int id)
    {
        if (_tileById == null) RebuildCaches();
        _tileById.TryGetValue(id, out var def);
        return def;
    }

    /// <summary>타일 종류로 정의를 조회합니다. 없으면 null.</summary>
    public TileDefinition GetTile(TileType type) => GetTile((int)type);

    #endregion

    #region 조회 — 개체

    /// <summary>개체 ID로 정의를 조회합니다. 없으면 null.</summary>
    public EntityDefinition GetEntity(int id)
    {
        if (_entityById == null) RebuildCaches();
        _entityById.TryGetValue(id, out var def);
        return def;
    }

    /// <summary>개체 종류로 정의를 조회합니다. 없으면 null.</summary>
    public EntityDefinition GetEntity(EntityType type) => GetEntity((int)type);

    /// <summary>맵 생성 시 자동 배치될 개체 목록 (placementOrder 오름차순).</summary>
    public IReadOnlyList<EntityDefinition> NaturalSpawns
    {
        get
        {
            if (_naturalSpawns == null) RebuildCaches();
            return _naturalSpawns;
        }
    }

    #endregion

    #region 조회 — 바닥 타일

    /// <summary>바닥 타일 ID로 정의를 조회합니다. 없으면 null.</summary>
    public FloorTileDefinition GetFloor(int id)
    {
        if (_floorById == null) RebuildCaches();
        _floorById.TryGetValue(id, out var def);
        return def;
    }

    /// <summary>바닥 타일 종류로 정의를 조회합니다. 없으면 null.</summary>
    public FloorTileDefinition GetFloor(FloorTileType type) => GetFloor((int)type);

    #endregion

    #region 검증

    /// <summary>
    /// 정의 목록의 문제를 찾아 사람이 읽을 수 있는 문장 목록으로 돌려줍니다.
    /// 에디터 인스펙터와 enum 생성기가 공유합니다.
    /// </summary>
    public List<string> Validate()
    {
        var problems = new List<string>();

        ValidateIds(problems, "타일", CollectMeta(tiles, d => d.codeName, d => d.id, d => d.name),
            GameIDRegistry.Tiles.RAW_MIN, GameIDRegistry.Tiles.RAW_MAX);
        ValidateIds(problems, "개체", CollectMeta(entities, d => d.codeName, d => d.id, d => d.name),
            GameIDRegistry.Entities.MIN, GameIDRegistry.Entities.MAX);
        ValidateIds(problems, "바닥 타일", CollectMeta(floorTiles, d => d.codeName, d => d.id, d => d.name),
            GameIDRegistry.FloorTiles.MIN, GameIDRegistry.FloorTiles.MAX);

        // 자동 배치 개체는 배치 수단이 갖춰져 있어야 한다
        if (entities != null)
        {
            foreach (var e in entities)
            {
                if (e == null || !e.spawnOnMapGeneration) continue;
                if (e.placementMode == PlacementMode.Stamp && string.IsNullOrWhiteSpace(e.stampKey))
                    problems.Add($"개체 '{e.codeName}': 배치 방식이 Stamp인데 stampKey가 비어 있습니다.");
                if (e.placementMode == PlacementMode.Entity && e.prefab == null)
                    problems.Add($"개체 '{e.codeName}': 자동 배치 대상인데 프리팹이 비어 있습니다.");
                if (e.spawnChance <= 0f)
                    problems.Add($"개체 '{e.codeName}': 자동 배치 대상인데 spawnChance가 0입니다 — 하나도 생기지 않습니다.");
            }
        }

        return problems;
    }

    /// <summary>정의 목록에서 검증에 필요한 (에셋명, codeName, id)만 뽑아냅니다.</summary>
    private static List<(string asset, string code, int id)> CollectMeta<T>(
        List<T> source,
        System.Func<T, string> codeOf,
        System.Func<T, int> idOf,
        System.Func<T, string> assetOf) where T : UnityEngine.Object
    {
        var result = new List<(string, string, int)>();
        if (source == null) return result;

        for (int i = 0; i < source.Count; i++)
        {
            var item = source[i];
            if (item == null)
            {
                result.Add(($"[{i}번 빈 항목]", null, int.MinValue));
                continue;
            }
            result.Add((assetOf(item), codeOf(item), idOf(item)));
        }
        return result;
    }

    private static void ValidateIds(
        List<string> problems, string label,
        List<(string asset, string code, int id)> items,
        int minId, int maxId)
    {
        var seenIds = new Dictionary<int, string>();
        var seenNames = new HashSet<string>();

        foreach (var (asset, code, id) in items)
        {
            if (code == null)
            {
                problems.Add($"{label} 목록에 빈 항목이 있습니다 {asset}.");
                continue;
            }

            if (!IsValidIdentifier(code))
                problems.Add($"{label} '{asset}': codeName '{code}'은(는) 유효한 C# 식별자가 아닙니다.");
            else if (!seenNames.Add(code))
                problems.Add($"{label}: codeName '{code}'이(가) 중복됩니다.");

            if (seenIds.TryGetValue(id, out string other))
                problems.Add($"{label}: ID {id}이(가) '{other}'와 '{code}'에서 중복됩니다.");
            else
                seenIds[id] = code;

            if (id < minId || id > maxId)
                problems.Add($"{label} '{code}': ID {id}이(가) 허용 범위({minId}~{maxId})를 벗어났습니다.");
        }
    }

    /// <summary>C# 식별자로 쓸 수 있는 문자열인지 확인합니다.</summary>
    public static bool IsValidIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!char.IsLetter(s[0]) && s[0] != '_') return false;
        foreach (char c in s)
        {
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        }
        return true;
    }

    #endregion
}

/// <summary>
/// 타일의 통행·스폰 규칙을 O(1)로 조회하는 정적 캐시.
///
/// GameMap의 이동 판정은 경로탐색에서 매 프레임 수천 번 호출되므로
/// 딕셔너리 대신 ID를 인덱스로 쓰는 bool 배열을 씁니다.
/// 정의 데이터베이스가 없으면 예전 하드코딩 규칙으로 되돌아갑니다.
/// </summary>
public static class TileRules
{
    private const int MAX_TILE_ID = 256;

    // 레거시 폴백 값 (정의 에셋이 아직 없을 때)
    private const int LEGACY_AIR = 0;
    private const int LEGACY_DIRT = 1;
    private const int LEGACY_GRASS = 6;
    private const int LEGACY_LADDER = 8;

    private static bool[] _solid;
    private static bool[] _climbable;
    private static bool[] _spawnableSurface;
    private static bool _built;

    /// <summary>다음 조회 때 캐시를 다시 만들도록 표시합니다.</summary>
    public static void Invalidate() => _built = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad() => _built = false;

    private static void EnsureBuilt()
    {
        if (_built) return;

        _solid = new bool[MAX_TILE_ID];
        _climbable = new bool[MAX_TILE_ID];
        _spawnableSurface = new bool[MAX_TILE_ID];

        var db = DefinitionDatabase.Instance;
        if (db != null && db.tiles != null && db.tiles.Count > 0)
        {
            foreach (var def in db.tiles)
            {
                if (def == null) continue;
                if (def.id < 0 || def.id >= MAX_TILE_ID) continue;
                _solid[def.id] = def.isSolid;
                _climbable[def.id] = def.isClimbable;
                _spawnableSurface[def.id] = def.isSpawnableSurface;
            }
        }
        else
        {
            // 폴백: 공기·사다리만 통과 가능, 흙·잔디흙만 스폰 가능
            for (int i = 0; i < MAX_TILE_ID; i++) _solid[i] = true;
            _solid[LEGACY_AIR] = false;
            _solid[LEGACY_LADDER] = false;
            _climbable[LEGACY_LADDER] = true;
            _spawnableSurface[LEGACY_DIRT] = true;
            _spawnableSurface[LEGACY_GRASS] = true;
        }

        _built = true;
    }

    /// <summary>이 타일이 이동을 막는지 (공기·사다리는 false).</summary>
    public static bool IsSolid(int tileId)
    {
        EnsureBuilt();
        if (tileId < 0 || tileId >= MAX_TILE_ID) return true;
        return _solid[tileId];
    }

    /// <summary>이 타일에서 수직 이동이 가능한지 (사다리).</summary>
    public static bool IsClimbable(int tileId)
    {
        EnsureBuilt();
        if (tileId < 0 || tileId >= MAX_TILE_ID) return false;
        return _climbable[tileId];
    }

    /// <summary>직원 스폰·식생물 배치가 가능한 지표면 타일인지.</summary>
    public static bool IsSpawnableSurface(int tileId)
    {
        EnsureBuilt();
        if (tileId < 0 || tileId >= MAX_TILE_ID) return false;
        return _spawnableSurface[tileId];
    }
}
