using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 절차적 맵 생성기.
/// Perlin Noise를 사용하여 200x200 타일 맵을 생성합니다.
/// 지형(언덕, 동굴, 광맥), 자연물(나무, 식물), 스탬프 배치를 담당합니다.
/// ISaveModule을 구현하여 맵 타일 + 자연물 상태를 저장/복원합니다.
/// </summary>
[RequireComponent(typeof(MapRenderer))]
public class MapGenerator : DestroySingleton<MapGenerator>, ISaveModule
{
    [Header("필수 연결")]
    [SerializeField] private StampLibrary stampLibrary;
    [SerializeField] private ResourceManager resourceManager;
    [Header("맵 생성 시드")]
    [Tooltip("0이면 새 게임마다 무작위로 정합니다. 특정 지형을 재현하려면 값을 넣으세요.")]
    [SerializeField] private int mapSeed = 0;
    [Header("언덕 지형")]
    [SerializeField] private int baseGroundLevel = 232;
    [SerializeField] [Range(0f, 50f)] private float hillAmplitude = 14f;
    [SerializeField] [Range(0.01f, 0.1f)] private float hillScale = 0.05f;
    [SerializeField] [Range(1, 20)] private int surfaceDirtDepth = 5;
    [Header("흙 덩어리 (돌 속)")]
    [SerializeField] [Range(0.01f, 0.2f)] private float dirtNoiseScale = 0.08f;
    [SerializeField] [Range(0f, 1f)] private float dirtThreshold = 0.5f;
    [Header("동굴")]
    [SerializeField] [Range(0.01f, 0.2f)] private float caveNoiseScale = 0.07f;
    [SerializeField] [Range(0f, 1f)] private float caveThreshold = 0.7f;
    [Header("지층 경계")]
    [Tooltip("층 경계가 위아래로 흔들리는 폭(칸). 0이면 자로 그은 듯 일직선이 됩니다.")]
    [SerializeField] [Range(0f, 40f)] private float strataBoundaryAmplitude = 12f;
    [Tooltip("경계 흔들림의 스케일. 작을수록 완만하게 굽이치고, 크면 잘게 들쭉날쭉해집니다.")]
    [SerializeField] [Range(0.005f, 0.1f)] private float strataBoundaryScale = 0.022f;
    // ── 광맥·식생물 배치 값은 정의 에셋으로 옮겼습니다 ─────────────────────────
    //   광물 지층(깊이·노이즈·희귀도) → TileDefinition의 "광맥 생성" 항목
    //   나무·베리 덤불·침식 식물     → EntityDefinition의 "맵 생성 배치" 항목
    // 새 광물이나 식생물을 추가할 때 이 스크립트를 고칠 필요가 없습니다.
    [Header("자연물 공통")]
    [Tooltip("시작 지점 좌우로 자연물을 배치하지 않을 여유 칸 수")]
    [SerializeField] [Range(5, 50)] private int spawnAreaPadding = 15;
    [Header("광물 군집 크기")]
    [Tooltip("군집당 최소 광물 타일 수")]
    [SerializeField] [Range(1, 6)] private int clusterMinSize = 2;
    [Tooltip("군집당 최대 광물 타일 수")]
    [SerializeField] [Range(1, 10)] private int clusterMaxSize = 6;
    [Header("스폰 지점")]
    [SerializeField] private string spawnChestKey = "SPAWN_CHEST_3X2"; 

    // --- 내부 시스템 변수 ---
    private GameMap _gameMap;
    private MapStamper _stamper;
    private MapRenderer _mapRenderer;

    // ── 시드 ────────────────────────────────────────────────────────────────
    // 예전 noiseSeed는 Perlin 좌표에 그대로 더하는 오프셋이라 해시가 아니었고,
    // 언덕 호출은 두 번째 인자로도 같은 값을 넘겨 시드 0에서 퇴화했습니다.
    // 그래서 값을 바꿔도 "같은 지형이 옆으로 밀리는" 결과만 나왔습니다.
    // 이제 시드에서 기능별 오프셋을 각각 해시로 파생합니다.
    private float _hillOffsetX, _hillOffsetY;
    private float _caveOffsetX, _caveOffsetY;
    private float _dirtOffsetX, _dirtOffsetY;
    private float _veinOffsetBase;
    private float _boundaryOffsetX, _boundaryOffsetY;
    private float _filamentOffsetX, _filamentOffsetY;
    private float _warpOffsetX, _warpOffsetY;

    /// <summary>
    /// 군집 모양·식생물 배치용 난수.
    /// 전역 <c>UnityEngine.Random</c>을 쓰면 다른 시스템의 난수까지 흔들리므로 로컬 스트림을 씁니다.
    /// </summary>
    private System.Random _rng;

    /// <summary>이번 생성에 실제로 쓴 시드. mapSeed가 0이면 무작위로 채운 값이 들어갑니다.</summary>
    public int ActiveSeed { get; private set; }

    /// <summary>이번 생성에 쓴 지층 경계. 지층을 등록하지 않으면 비어 있습니다.</summary>
    private StrataLayout _strata;

    /// <summary>이번 생성에서 시작방이 놓인 X. 자연물 제외 구역도 이 값을 기준으로 잡습니다.</summary>
    private int _startingRoomX = GameMap.MAP_WIDTH / 2;

    /// <summary>
    /// 시작방(기지)의 X 좌표. 실외 침식의 좌우 그라디언트가 이 지점을 중심으로 계산됩니다 —
    /// 기지에서 멀어질수록 위험해진다는 축이 여기서 나옵니다.
    /// </summary>
    public int StartingRoomX => _startingRoomX;
    
    // 지형 생성이 직접 쓰는 타일 ID. 값은 TileDefinition 에셋에서 생성된 TileType이 정합니다.
    // 광물은 여기 없습니다 — 광맥은 정의 에셋을 훑어 배치합니다(PlaceMineralClusters).
    private const int AIR_ID   = (int)TileType.Air;
    private const int DIRT_ID  = (int)TileType.Dirt;
    private const int STONE_ID = (int)TileType.Stone;
    private const int GRASS_ID = (int)TileType.GrassDirt;
    // 개체(식물·건물) 종류는 EntityType enum으로 관리합니다. MapEntity.id에는 (int) 캐스트로 대입합니다.


    /// <summary>언덕 기복을 뺀 기준 지표 높이(Y). 온도 시스템의 깊이 0 기준이 여기서 나옵니다.</summary>
    public int BaseGroundLevel => baseGroundLevel;

    /// <summary>
    /// 지표의 평균 높이(Y) — 기준 높이에 언덕 진폭의 절반을 더한 값입니다.
    /// 지열 깊이는 이 값에서 얼마나 내려왔는지로 잽니다.
    /// </summary>
    public float SurfaceReferenceY => baseGroundLevel + hillAmplitude * 0.5f;

    public GameMap GameMapInstance { get; private set; }
    public MapStamper StamperInstance { get; private set; }
    public MapRenderer MapRendererInstance { get; private set; }
    public ResourceManager ResourceManagerInstance { get; private set; }
    
    protected override void Awake()
    {
        base.Awake(); // 'instance = this' 실행

        Debug.Log("[MapGenerator] Awake: 모든 시스템 초기화 시작...");

        // 4. 필수 애셋이 연결되었는지 '즉시' 확인
        if (stampLibrary == null)
        {
            Debug.LogError("MapGenerator: StampLibrary가 인스펙터에 연결되지 않았습니다!", this.gameObject);
            return;
        }
        if (resourceManager == null)
        {
            Debug.LogError("MapGenerator: ResourceManager가 인스펙터에 연결되지 않았습니다!", this.gameObject);
            return;
        }

        // 5. 핵심 데이터 객체 생성 및 할당
        GameMapInstance = new GameMap();
        StamperInstance = new MapStamper(GameMapInstance, stampLibrary);
        ResourceManagerInstance = resourceManager;
        
        _gameMap = GameMapInstance;
        _stamper = StamperInstance;

        // 6. MapRenderer 컴포넌트 찾기 및 초기화
        MapRendererInstance = GetComponent<MapRenderer>();
        if (MapRendererInstance == null)
        {
            Debug.LogError("MapGenerator: MapRenderer 컴포넌트가 같은 오브젝트에 없습니다!", this.gameObject);
            return;
        }
        
        // 7. MapRenderer에게 필요한 데이터 주입
        MapRendererInstance.Initialize(GameMapInstance, resourceManager);
        
        Debug.Log("[MapGenerator] Awake: 모든 시스템 준비 완료.");
    }
    // --- Unity 생명주기 ---
    void Start()
    {
        if (GameMapInstance == null)
        {
            Debug.LogWarning("MapGenerator.Awake()에서 오류가 발생하여 맵 생성을 시작할 수 없습니다.");
            return;
        }

        GenerateWorld(); // 맵 생성 실행

        Debug.Log("--- 맵 데이터 생성 완료 ---");
        MapRendererInstance.RenderMap(GameMapInstance);

        // 타일 렌더링 완료 후 안개 초기화
        // (로드 시에도 항상 전체 안개로 채움 → FogOfWarManager.Restore()가 이후에 덮어씀)
        FogOfWarManager.instance?.InitializeFog();
        
        Debug.Log("--- 스폰 지점 근처 맵 출력 ---");
        GameMapInstance.PrintDebugMap(100, 140, 10);
        
        Debug.Log($"--- 배치된 개체: {GameMapInstance.Entities.Count}개 ---");
        foreach (var entity in GameMapInstance.Entities)
        {
            Debug.Log($"> {entity.type} (ID: {entity.id}) @ {entity.position}");
        }
    }
    
    // --- 맵 생성 메인 함수 ---
    /// <summary>
    /// 시드를 확정하고 기능별 노이즈 오프셋을 파생합니다.
    ///
    /// 오프셋을 기능마다 따로 두는 것이 핵심입니다 — 같은 값을 여기저기 더하면
    /// 언덕과 동굴이 같은 방향으로 함께 밀려서 "다른 맵"이 되지 않습니다.
    /// </summary>
    private void InitializeSeed()
    {
        ActiveSeed = mapSeed != 0 ? mapSeed : UnityEngine.Random.Range(1, int.MaxValue);

        _hillOffsetX = SeedOffset(ActiveSeed, 1);
        _hillOffsetY = SeedOffset(ActiveSeed, 2);
        _caveOffsetX = SeedOffset(ActiveSeed, 3);
        _caveOffsetY = SeedOffset(ActiveSeed, 4);
        _dirtOffsetX = SeedOffset(ActiveSeed, 5);
        _dirtOffsetY = SeedOffset(ActiveSeed, 6);
        _veinOffsetBase = SeedOffset(ActiveSeed, 7);
        _boundaryOffsetX = SeedOffset(ActiveSeed, 8);
        _boundaryOffsetY = SeedOffset(ActiveSeed, 9);
        _filamentOffsetX = SeedOffset(ActiveSeed, 10);
        _filamentOffsetY = SeedOffset(ActiveSeed, 11);
        _warpOffsetX = SeedOffset(ActiveSeed, 12);
        _warpOffsetY = SeedOffset(ActiveSeed, 13);

        _rng = new System.Random(ActiveSeed);

        Debug.Log($"[MapGenerator] 시드 {ActiveSeed} (mapSeed={mapSeed}, 0이면 무작위)");
    }

    /// <summary>
    /// 시드와 용도(salt)를 섞어 Perlin 좌표 오프셋을 만듭니다.
    /// Perlin은 정수 격자에서 반복·대칭이므로 <b>소수부가 있는</b> 큰 값이어야 패턴이 실제로 달라집니다.
    /// </summary>
    private static float SeedOffset(int seed, int salt)
    {
        unchecked
        {
            int h = seed * 73856093 ^ salt * 19349663;
            h ^= h >> 13;
            h *= 1274126177;
            h ^= h >> 16;
            return ((h & 0x7FFFFFFF) % 1000000) * 0.01f;   // 0 ~ 10000, 0.01 단위
        }
    }

    private void GenerateWorld()
    {
        Debug.Log("맵 데이터 생성을 시작합니다...");

        InitializeSeed();
        _strata = DefinitionDatabase.Instance != null ? DefinitionDatabase.Instance.StrataLayout : null;
        if (_strata != null && !_strata.IsEmpty)
            Debug.Log($"[MapGenerator] 지층: {_strata.Describe()}");
        else
            Debug.LogWarning("[MapGenerator] 지층이 등록되지 않아 예전 단일 파라미터로 생성합니다.");

        // 9만 칸을 한 번에 쓰므로 칸 단위 통지를 멈추고, 끝난 뒤 OnBulkChanged 한 번으로 알린다
        _gameMap.BeginBulkChange();
        try
        {
            int[] groundHeightMap = GenerateBaseTerrainAndOres();
            ConvertSurfaceDirtToGrass(groundHeightMap);
            PlaceMineralClusters(groundHeightMap);          // 광물 군집화 (2~6타일)
            PlaceStartingRoom(groundHeightMap);              // 스타팅 룸 배치
            PlaceNaturalEntities(groundHeightMap);           // 식생물 배치 (정의 에셋 기반)
        }
        finally
        {
            _gameMap.EndBulkChange();
        }
    }

    #region 지형 및 광물 (Terrain & Ores)
    
    private int[] GenerateBaseTerrainAndOres()
    {
        // ... (변경 사항 없음) ...
        int[] groundHeightMap = new int[GameMap.MAP_WIDTH];
        for (int x = 0; x < GameMap.MAP_WIDTH; x++)
        {
            // 두 인자에 서로 다른 오프셋을 준다 — 같은 값을 넘기면 시드가 바뀌어도 곡선이 퇴화한다
            float hillNoise = Mathf.PerlinNoise((x * hillScale) + _hillOffsetX, _hillOffsetY);
            int currentHeight = baseGroundLevel + (int)(hillNoise * hillAmplitude);
            groundHeightMap[x] = currentHeight; 
            for (int y = 0; y < GameMap.MAP_HEIGHT; y++)
            {
                int tileID = GetTileIDForCoordinate(x, y, currentHeight);
                _gameMap.SetTile(x, y, tileID);
            }
        }
        return groundHeightMap;
    }
    
    /// <summary>
    /// 칸 하나의 지형 재료를 정합니다 — <b>지형 재료를 정하는 유일한 함수</b>라 지층 조회도 여기 한 곳에 들어갑니다.
    ///
    /// 지층이 등록되어 있으면 동굴·흙·기반 암석 파라미터를 그 층 값으로 바꿔 씁니다.
    /// 등록이 없으면 인스펙터의 단일 파라미터로 예전처럼 동작합니다.
    /// </summary>
    private int GetTileIDForCoordinate(int x, int y, int currentHeight)
    {
        if (y > currentHeight) return AIR_ID;

        StrataDefinition strata = StrataAt(x, y);

        if (strata != null && strata.terrainMode == StrataTerrainMode.FilamentWeb)
            return GetFilamentTile(x, y, strata);

        float caveScale = strata != null ? strata.caveNoiseScale : caveNoiseScale;
        float caveLimit = strata != null ? strata.caveThreshold  : caveThreshold;
        float dirtScale = strata != null ? strata.dirtNoiseScale : dirtNoiseScale;
        float dirtLimit = strata != null ? strata.dirtThreshold  : dirtThreshold;
        int   baseRock  = strata != null ? strata.BaseRockId     : STONE_ID;

        // 가로로 늘이기 — x를 느리게 훑으면 무늬가 그만큼 옆으로 길어진다
        float stretch = strata != null ? Mathf.Max(1f, strata.caveStretch) : 1f;
        float lens    = strata != null ? strata.caveLensSplit : 0f;

        if (IsCaveOpen(x, y, caveScale, stretch, caveLimit, lens)) return AIR_ID;

        // 지표 바로 아래는 지층과 무관하게 흙 — 잔디가 덮이는 층이다
        if (y >= currentHeight - surfaceDirtDepth) return DIRT_ID;

        float dirtNoise = Mathf.PerlinNoise((x * dirtScale) + _dirtOffsetX,
                                             (y * dirtScale) + _dirtOffsetY);
        if (dirtNoise > dirtLimit) return DIRT_ID;

        // 광물은 PlaceMineralClusters()에서 군집(2~6타일)으로 배치
        return baseRock;
    }
    
    private void ConvertSurfaceDirtToGrass(int[] groundHeightMap)
    {
        // ... (IsSky 헬퍼 함수를 사용하는 버전으로 되돌림)
        Debug.Log("[MapGenerator] 흙 지표면을 잔디로 변환 중...");
        for (int x = 0; x < GameMap.MAP_WIDTH; x++)
        {
            for (int y = 0; y < GameMap.MAP_HEIGHT - 1; y++)
            {
                if (_gameMap.TileGrid[x, y] == DIRT_ID && _gameMap.TileGrid[x, y + 1] == AIR_ID)
                {
                    if (IsSky(x, y + 2)) 
                    {
                        _gameMap.SetTile(x, y, GRASS_ID);
                    }
                }
            }
        }
    }
    
    private bool IsSky(int x, int startY)
    {
        // ... (변경 사항 없음) ...
        for (int y = startY; y < GameMap.MAP_HEIGHT; y++)
        {
            if (_gameMap.TileGrid[x, y] != AIR_ID) { return false; }
        }
        return true;
    }

    #endregion

    #region 구조물 (Structures)
    
 
    /// <summary>
    /// 맵 정중앙에 13×9 스타팅 룸을 배치합니다.
    ///
    /// 레이아웃 (local y 기준, 0=바닥):
    ///   y=8 : 상단 벽 (흙)
    ///   y=5~7: 상층 내부 — 채용사무소(2×2) lx=5,6 / ly=5,6
    ///   y=3~4: 층 구분 벽 — lx=8에만 AIR (사다리 통로)
    ///   y=1~2: 하층 내부 — 상자 lx=5,6,7 / 사다리 lx=8
    ///   y=0 : 하단 벽 (흙)
    ///   lx=0, lx=12: 좌우 벽 (흙)
    ///
    /// 사다리(lx=8)는 ly=1~4에 배치하여 하층-상층 간 수직 이동을 지원합니다.
    ///
    /// <b>위치는 언제나 맵 정중앙</b>입니다. 지형이 돌이든 흙언덕이든 동굴이든 상관하지 않습니다 —
    /// 아래 1~2단계가 파내고·메우고·걷어내서 자리를 <b>만들어</b> 쓰기 때문입니다.
    ///
    /// 한때 "가장 평탄한 열"을 찾게 해봤지만 잘못된 방향이었습니다. 어차피 지형을 갈아엎으므로
    /// 평탄한 자리를 고를 이유가 없는데, 탐색 때문에 기지가 시드마다 수십 칸씩 밀렸습니다.
    /// <b>좌우 침식축이 기지 중심을 기준으로 도는 설계</b>라 기지가 밀리면 그 축이 통째로 망가집니다.
    /// </summary>
    private void PlaceStartingRoom(int[] groundHeightMap)
    {
        const int roomWidth  = 13;
        const int roomHeight = 9;

        int spawnX = GameMap.MAP_WIDTH / 2;
        _startingRoomX = spawnX;   // 자연물 제외 구역이 이 값을 공유한다
        const int ladderLX   = 8;   // 사다리 열 (local x)
        const int dividerY   = 4;   // 층 구분 (1줄, local y) — 기존 y=3 구분선 제거
        const int ladderMinY = 1;   // 사다리 시작 (하층 바닥, local y)
        const int ladderMaxY = dividerY; // 사다리 끝 (층 구분, local y)

        int groundY  = groundHeightMap[spawnX];
        int roomLeft = spawnX - roomWidth / 2;

        // 방 좌우로 이만큼 더 정리한다 — 문 앞과 직원 스폰 지점이 묻히지 않도록
        const int CLEAR_MARGIN = 3;
        // 방 바닥 아래를 이 깊이까지 메운다 — 동굴 공동 위에 방이 뜨는 것을 막는다
        const int FOUNDATION_DEPTH = 8;

        int clearLeft  = Mathf.Max(0, roomLeft - CLEAR_MARGIN);
        int clearRight = Mathf.Min(GameMap.MAP_WIDTH - 1, roomLeft + roomWidth - 1 + CLEAR_MARGIN);

        // ── 1. 방 아래 지형 평탄화 (기존 terrain이 더 낮으면 DIRT로 채움) ──────
        for (int lx = 0; lx < roomWidth; lx++)
        {
            int wx = roomLeft + lx;
            int localGround = groundHeightMap[wx];
            for (int wy = localGround + 1; wy <= groundY; wy++)
                _gameMap.SetTile(wx, wy, DIRT_ID);
        }

        // ── 1-b. 기초 다지기 — 방 바닥 아래의 동굴 공동을 메운다 ────────────────
        //    1번은 지표가 더 '낮은' 열만 채운다. 바로 아래에 동굴이 뚫려 있으면
        //    방이 허공에 뜨고, 직원이 바닥을 뚫고 떨어지거나 아예 진입하지 못한다.
        for (int wx = clearLeft; wx <= clearRight; wx++)
        {
            for (int d = 0; d < FOUNDATION_DEPTH; d++)
            {
                int wy = groundY - d;
                if (wy < 0) break;
                if (_gameMap.TileGrid[wx, wy] == AIR_ID)
                    _gameMap.SetTile(wx, wy, DIRT_ID);
            }
        }

        // ── 1-c. 방 위 지형 걷어내기 — 언덕에 묻히지 않도록 ──────────────────────
        for (int wx = clearLeft; wx <= clearRight; wx++)
        {
            int top = groundHeightMap[wx];
            for (int wy = groundY + roomHeight; wy <= top; wy++)
                _gameMap.SetTile(wx, wy, AIR_ID);
        }

        // ── 2. 방 구조 배치 (벽/층구분=흙, 내부=공기) ──────────────────────────
        for (int lx = 0; lx < roomWidth; lx++)
        {
            for (int ly = 0; ly < roomHeight; ly++)
            {
                int wx = roomLeft + lx;
                int wy = groundY  + ly;

                // 좌우 벽의 ly=1~2 두 칸은 나무 문(1×2) 자리 → AIR로 비워둠
                bool isDoorOpening = (lx == 0 || lx == roomWidth - 1) && (ly == 1 || ly == 2);
                bool isOuterWall   = (lx == 0 || lx == roomWidth - 1
                                   || ly == 0 || ly == roomHeight - 1) && !isDoorOpening;
                bool isDivider     = (ly == dividerY) && lx != ladderLX;   // 사다리 열은 뚫어둠

                _gameMap.SetTile(wx, wy, AIR_ID);
                if (isOuterWall || isDivider)
                {
                    _gameMap.AddEntity(new MapEntity
                    {
                        position = new Vector2Int(wx, wy),
                        type     = TypeObjectTile.Building,
                        id       = (int)EntityType.StoneFloor
                    });
                    _gameMap.MarkTileOccupied(wx, wy, blocksMovement: true);
                }
            }
        }

        // ── 3. 사다리 엔티티 배치 (lx=8, ly=1~4) ────────────────────────────────
        //    - ly=1,2,3: 하층 내부 사다리 (하층 바닥에서 층 구분까지 연결)
        //    - ly=4   : 층 구분(단일) 안의 사다리 → IsFloorSupport(8,4)=true 설정
        //    ly=4 FloorSupport가 지지대가 되어 상층(ly=5) 진입이 가능해집니다.
        int ladderWorldX = roomLeft + ladderLX;
        for (int ly = ladderMinY; ly <= ladderMaxY; ly++)
        {
            _gameMap.AddEntity(new MapEntity
            {
                position = new Vector2Int(ladderWorldX, groundY + ly),
                type     = TypeObjectTile.Building,
                id       = (int)EntityType.WoodLadder
            });
        }

        // ── 4. 나무 문 (좌우 벽 하단, 1×2) ─────────────────────────────────────
        //    ly=1 이 하단 타일, ly=2 가 상단 타일 (타일은 2단계 위에서 AIR로 비워둠)
        //    왼쪽 문: lx=0 / 오른쪽 문: lx=roomWidth-1(=12)
        _gameMap.AddEntity(new MapEntity
        {
            position = new Vector2Int(roomLeft,                  groundY + 1),
            type     = TypeObjectTile.Building,
            id       = (int)EntityType.WoodDoor
        });
        _gameMap.AddEntity(new MapEntity
        {
            position = new Vector2Int(roomLeft + roomWidth - 1,  groundY + 1),
            type     = TypeObjectTile.Building,
            id       = (int)EntityType.WoodDoor
        });

        // ── 5. 채용 사무소 (상층 중앙, BuildingData size=3×3) ─────────────────
        //    상층 내부: lx=1~11, ly=5~7
        //    배치: lx=5,6,7 (중앙 기준) / ly=5,6,7 (상층 전체 높이)
        int officeWX = roomLeft + 5;
        int officeWY = groundY  + 5;
        _gameMap.AddEntity(new MapEntity
        {
            position = new Vector2Int(officeWX, officeWY),
            type     = TypeObjectTile.Building,
            id       = (int)EntityType.HiringOffice
        });
        // 건물 풋프린트 점유 마킹 (3×3)
        for (int dx = 0; dx < 3; dx++)
        for (int dy = 0; dy < 3; dy++)
            _gameMap.MarkTileOccupied(officeWX + dx, officeWY + dy);

        // ── 5. 상자 (하층 중앙, 3×2 시각 오브젝트) ──────────────────────────────
        //    배치: lx=5,6,7 / ly=1 (하층 바닥)
        _gameMap.AddEntity(new MapEntity
        {
            position = new Vector2Int(roomLeft + 5, groundY + 1),
            type     = TypeObjectTile.Building,
            id       = (int)EntityType.Chest
        });

        // ── 6. 직원 스폰 포인트 (방 오른쪽 바깥) ─────────────────────────────────
        SetupEmployeeSpawn(roomLeft + roomWidth + 1, groundY + 1);

        // ── 7. 스타팅 룸 + 주변 1칸을 영구 안개 공개 ────────────────────────────
        //    벽/상자/사다리는 Building 컴포넌트 없이도 방 전체가 보이도록 영구 등록.
        //    스타팅 룸의 벽(StoneFloor)도 일반 건물처럼 자기 너머 1칸까지 비추도록
        //    공개 영역을 사방으로 1칸씩 확장합니다 (벽이 "시야를 밝히는" 효과).
        //    FogOfWarManager.instance는 Awake()에서 이미 생성되므로 접근 가능.
        const int REVEAL_MARGIN = 1; // 벽 바깥 1칸까지 공개 (취향에 따라 조정)
        FogOfWarManager.instance?.PermanentlyRevealArea(
            new Vector2Int(roomLeft - REVEAL_MARGIN, groundY - REVEAL_MARGIN),
            new Vector2Int(roomWidth + REVEAL_MARGIN * 2, roomHeight + REVEAL_MARGIN * 2)
        );

        Debug.Log($"[PlaceStartingRoom] 스타팅 룸 배치 완료. " +
                  $"범위: ({roomLeft},{groundY}) ~ ({roomLeft + roomWidth - 1},{groundY + roomHeight - 1})");
    }

    /// <summary>
    /// EmployeeManager에 스폰 지점을 설정합니다.
    /// 초기 자동 스폰은 수행하지 않습니다 — 직원 채용은 HiringOffice(채용 건물)를 통해 이루어집니다.
    /// </summary>
    private void SetupEmployeeSpawn(int x, int y)
    {
        if (EmployeeManager.instance == null)
        {
            Debug.LogError("[MapGenerator] EmployeeManager를 찾을 수 없습니다!");
            return;
        }

        // 직원은 바닥 위에 스폰되어야 함 (y+1이 발 위치)
        Vector3 spawnPoint = new Vector3(x + 0.5f, y + 3, 0);
        EmployeeManager.instance.SetSpawnPoint(spawnPoint);

        Debug.Log($"[MapGenerator] 직원 스폰 지점 설정 완료: {spawnPoint} (초기 스폰 없음 — 채용 건물 사용)");
    }
    #endregion
    
    #region 광물 군집 (Mineral Clusters)

    /// <summary>
    /// generateAsVein이 켜진 모든 타일 정의를 깊이별로 군집(2~clusterMaxSize 타일) 형태로 배치합니다.
    /// GetTileIDForCoordinate에서는 STONE_ID만 반환하고, 이 함수가 광물을 덮어씁니다.
    ///
    /// 깊이·노이즈·희귀도는 각 TileDefinition의 "광맥 생성" 항목이 들고 있습니다.
    /// <b>얕은 광맥부터 순서대로</b> 놓습니다 — 씨앗은 돌 타일에만 심기므로 순서가 결과를 바꾸고,
    /// 얕은 것부터 놓아야 리팩터링 이전(석탄→구리→철→은→금→수정)과 같은 지층이 나옵니다.
    /// </summary>
    private void PlaceMineralClusters(int[] groundHeightMap)
    {
        var db = DefinitionDatabase.Instance;
        if (db == null)
        {
            Debug.LogError("[PlaceMineralClusters] 정의 데이터베이스가 없어 광맥을 배치하지 못했습니다. " +
                           "메뉴 [XNP/정의/현재 코드에서 정의 에셋 생성]을 실행하세요.");
            return;
        }

        var veins = new List<TileDefinition>();
        foreach (var def in db.tiles)
        {
            if (def != null && def.generateAsVein) veins.Add(def);
        }

        // 얕은 한계가 지표에 가까운(=값이 큰) 것부터
        veins.Sort((a, b) => b.veinShallowOffset.CompareTo(a.veinShallowOffset));

        foreach (var def in veins)
        {
            PlaceClustersForMineral(def.id, groundHeightMap,
                def.veinShallowOffset, def.veinDeepOffset,
                def.veinNoiseScale, def.veinThreshold, def.veinSeedOffset);
        }

        Debug.Log($"[PlaceMineralClusters] 광물 군집 배치 완료. (광맥 {veins.Count}종)");
    }

    /// <summary>
    /// 지정 광물을 해당 깊이 범위에 군집으로 배치합니다.
    /// Perlin noise로 씨앗 위치를 선정 후, 무작위 확장으로 2~clusterMaxSize 타일 군집을 형성합니다.
    /// </summary>
    private void PlaceClustersForMineral(int mineralId, int[] groundHeightMap,
        int minDepthOffset, int maxDepthOffset, float noiseScale, float threshold, float seedOffset)
    {
        for (int x = 0; x < GameMap.MAP_WIDTH; x++)
        {
            int groundY = groundHeightMap[x];
            int yTop    = Mathf.Max(0, groundY + maxDepthOffset); // 더 깊은 한계
            int yBottom = Mathf.Min(GameMap.MAP_HEIGHT - 1, groundY + minDepthOffset); // 얕은 한계

            for (int y = yTop; y <= yBottom; y++)
            {
                // 이미 광물이 채워졌거나 그 층의 기반 암석이 아니면 씨앗 불가
                if (!IsVeinHost(x, y)) continue;

                float noise = Mathf.PerlinNoise(
                    x * noiseScale + _veinOffsetBase + seedOffset,
                    y * noiseScale + _veinOffsetBase + seedOffset);

                if (noise < threshold) continue;

                // 씨앗 선정 → 군집 확장
                int targetSize = _rng.Next(clusterMinSize, clusterMaxSize + 1);
                ExpandMineralCluster(x, y, mineralId, targetSize, yBottom);
            }
        }
    }

    /// <summary>
    /// 이 칸이 속한 지층 — 경계를 노이즈로 <b>흔들어서</b> 찾습니다.
    ///
    /// 층 경계를 Y로 딱 자르면 자로 그은 듯한 일직선이 나와 지형이 인공적으로 보입니다.
    /// 조회에 쓰는 Y를 노이즈만큼 밀어 주면 같은 경계가 굽이치고, 층 사이에 서로 파고든
    /// 주머니도 생겨 훨씬 자연스러워집니다.
    /// </summary>
    private StrataDefinition StrataAt(int x, int y)
    {
        if (_strata == null) return null;
        if (strataBoundaryAmplitude <= 0f) return _strata.At(y);

        // ⚠️ 진폭을 그 층 두께에 맞춰 조인다.
        //    경계층은 15칸뿐이라 ±12칸을 흔들면 위아래 층이 그대로 배어들어
        //    "뚫리지 않는 장벽"이라는 설계가 통째로 무너진다.
        //    두께의 1/4로 제한하면 층 한가운데 절반은 항상 자기 층으로 남는다.
        int thickness = _strata.ThicknessAt(y);
        float amplitude = thickness > 0
            ? Mathf.Min(strataBoundaryAmplitude, thickness * 0.25f)
            : strataBoundaryAmplitude;

        // y도 섞되 비중을 낮춘다 — 순수 x 함수면 모든 경계가 똑같은 모양으로 평행하게 굽이친다
        float n = Mathf.PerlinNoise(x * strataBoundaryScale + _boundaryOffsetX,
                                    y * strataBoundaryScale * 0.35f + _boundaryOffsetY);
        int warped = y + Mathf.RoundToInt((n - 0.5f) * 2f * amplitude);
        return _strata.At(warped);
    }

    /// <summary>
    /// 이 칸이 동굴(빈 공간)인지.
    ///
    /// <paramref name="lens"/>가 0이면 평범한 노이즈 임계 판정입니다.
    /// 0보다 크면 같은 노이즈를 <b>위아래로 어긋나게 두 번</b> 뽑아 둘 다 열린 곳만 파냅니다 —
    /// 두 타원의 교집합이라 위아래가 눌리고 <b>좌우 끝이 뾰족한 눈동자</b>가 됩니다.
    ///
    /// 임계값만 올리는 방법으로는 이 모양이 안 나옵니다. Perlin은 봉우리 근처가 매끄러워
    /// 등고선이 타원이라, 높게 자를수록 <b>작아질 뿐 여전히 둥급니다</b>(실측 충전율 0.71~0.76 고정).
    /// </summary>
    private bool IsCaveOpen(int x, float y, float scale, float stretch, float limit, float lens)
    {
        if (lens <= 0f) return CaveNoise(x, y, scale, stretch) > limit;

        return CaveNoise(x, y - lens, scale, stretch) > limit
            && CaveNoise(x, y + lens, scale, stretch) > limit;
    }

    private float CaveNoise(float x, float y, float scale, float stretch)
        => Mathf.PerlinNoise((x * scale / stretch) + _caveOffsetX, (y * scale) + _caveOffsetY);

    /// <summary>
    /// 덩굴망 지형 — 빈 공간에 굵은 줄기가 얽힌 구조.
    ///
    /// 노이즈의 <b>능선(ridge)</b>만 남기는 방식입니다. Perlin 값이 0.5에 가까운 곳은
    /// 등고선처럼 이어진 곡선을 이루는데, 그 주변만 타일로 채우면 굵기가 일정한 줄기가 됩니다.
    /// 샘플 좌표를 다른 노이즈로 미리 비틀어(도메인 워프) 곡선을 뒤엉키게 만듭니다.
    /// 두 겹을 겹치면 서로 다른 방향의 망이 교차해 뿌리처럼 보입니다.
    /// </summary>
    private int GetFilamentTile(int x, int y, StrataDefinition strata)
    {
        float scale = strata.filamentScale;
        float band  = strata.filamentThickness;

        // 도메인 워프 — 좌표 자체를 흔들어 곡선을 비튼다
        float warpScale = scale * 0.5f;
        float wx = Mathf.PerlinNoise(x * warpScale + _warpOffsetX, y * warpScale + _warpOffsetY) - 0.5f;
        float wy = Mathf.PerlinNoise(x * warpScale + _warpOffsetY, y * warpScale + _warpOffsetX) - 0.5f;

        float sx = x + wx * 2f * strata.filamentWarp;
        float sy = y + wy * 2f * strata.filamentWarp;

        if (IsOnRidge(sx * scale + _filamentOffsetX, sy * scale + _filamentOffsetY, band))
            return strata.FilamentTileId;

        // 두 번째 겹은 스케일과 오프셋을 달리해 다른 방향으로 흐르게 한다
        if (strata.filamentSecondLayer &&
            IsOnRidge(sy * scale * 1.37f + _filamentOffsetY, sx * scale * 1.37f + _filamentOffsetX, band * 0.8f))
            return strata.FilamentTileId;

        return AIR_ID;
    }

    /// <summary>노이즈 값이 0.5 근처(=능선)인지. band가 클수록 두꺼운 띠가 됩니다.</summary>
    private static bool IsOnRidge(float nx, float ny, float band)
    {
        float n = Mathf.PerlinNoise(nx, ny);
        return Mathf.Abs(n - 0.5f) < band * 0.5f;
    }

    /// <summary>
    /// 이 칸에 광맥 씨앗을 심을 수 있는지 — <b>그 층의 기반 암석</b>이어야 합니다.
    ///
    /// 예전에는 STONE_ID로 못박혀 있었습니다. 지층마다 기반 암석이 달라질 수 있게 되면서
    /// 그대로 두면 <b>돌이 아닌 층의 광맥이 통째로 조용히 사라집니다</b>(에러도 안 납니다).
    /// </summary>
    private bool IsVeinHost(int x, int y)
    {
        StrataDefinition strata = StrataAt(x, y);
        if (strata != null && !strata.HostsVeins) return false;   // 덩굴망 층엔 기반 암석이 없다

        int host = strata != null ? strata.BaseRockId : STONE_ID;
        return _gameMap.TileGrid[x, y] == host;
    }

    /// <summary>
    /// 씨앗 위치에서 무작위 확장(Random Walk)으로 광물 군집을 형성합니다.
    /// 그 층의 기반 암석 칸으로만 확장하며, maxY(얕은 한계)를 넘지 않습니다.
    /// </summary>
    private void ExpandMineralCluster(int startX, int startY, int mineralId, int targetSize, int maxY)
    {
        var placed   = new System.Collections.Generic.HashSet<Vector2Int>();
        var frontier = new System.Collections.Generic.List<Vector2Int>();

        var origin = new Vector2Int(startX, startY);
        placed.Add(origin);
        frontier.Add(origin);
        _gameMap.SetTile(startX, startY, mineralId);

        // 4방향 배열 (셔플용)
        var dirs = new Vector2Int[]
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
        };

        while (placed.Count < targetSize && frontier.Count > 0)
        {
            // 무작위 프론티어 선택
            int fi = _rng.Next(frontier.Count);
            var pos = frontier[fi];

            // 방향 셔플 (Fisher-Yates)
            for (int i = dirs.Length - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                var tmp = dirs[i]; dirs[i] = dirs[j]; dirs[j] = tmp;
            }

            bool expanded = false;
            foreach (var dir in dirs)
            {
                var nb = pos + dir;
                if (placed.Contains(nb)) continue;
                if (nb.x < 0 || nb.x >= GameMap.MAP_WIDTH)  continue;
                if (nb.y < 0 || nb.y >= GameMap.MAP_HEIGHT) continue;
                if (nb.y > maxY) continue; // 얕은 한계 초과 금지
                if (!IsVeinHost(nb.x, nb.y)) continue;

                placed.Add(nb);
                frontier.Add(nb);
                _gameMap.SetTile(nb.x, nb.y, mineralId);
                expanded = true;
                break;
            }

            if (!expanded)
                frontier.RemoveAt(fi);
        }
    }

    #endregion

    #region 식물 (Vegetation)

    /// <summary>
    /// spawnOnMapGeneration이 켜진 모든 개체 정의를 placementOrder 순서대로 배치합니다.
    ///
    /// 예전에는 PlaceTrees / PlaceBerryBushes / PlaceErosionPlants 세 함수가
    /// 거의 같은 일을 각자 하드코딩된 확률·간격·스탬프 키로 하고 있었습니다.
    /// 이제 그 값들은 EntityDefinition의 "맵 생성 배치" 항목에 있으므로,
    /// 새 식생물을 추가할 때 이 스크립트를 고칠 필요가 없습니다.
    /// </summary>
    private void PlaceNaturalEntities(int[] groundHeightMap)
    {
        var db = DefinitionDatabase.Instance;
        if (db == null)
        {
            Debug.LogError("[PlaceNaturalEntities] 정의 데이터베이스가 없어 식생물을 배치하지 못했습니다. " +
                           "메뉴 [XNP/정의/현재 코드에서 정의 에셋 생성]을 실행하세요.");
            return;
        }

        foreach (var def in db.NaturalSpawns)
            PlaceNaturalEntity(def, groundHeightMap);
    }

    /// <summary>
    /// 개체 정의 하나를 규칙대로 맵 전체에 뿌립니다.
    ///
    /// 지표면만 훑는 모드(GroundLevel)는 열마다 후보를 한 곳만 보고,
    /// 노출면 전체 모드(AnyExposedSurface)는 동굴 천장·단차까지 훑습니다.
    /// </summary>
    private void PlaceNaturalEntity(EntityDefinition def, int[] groundHeightMap)
    {
        if (def == null || def.spawnChance <= 0f) return;

        // 예전에는 여기에 100이 또 한 번 못박혀 있어서, 시작방을 옮기면 제외 구역만 엉뚱한 곳에 남았다.
        // 이제 실제 시작방 X를 공유한다.
        int spawnX = _startingRoomX;
        int width = Mathf.Max(1, def.footprintWidth);
        int lastPlacedX = int.MinValue;
        int placed = 0;

        for (int x = 0; x + width <= GameMap.MAP_WIDTH; x++)
        {
            if (def.avoidSpawnArea &&
                x >= spawnX - spawnAreaPadding && x <= spawnX + spawnAreaPadding) continue;

            // 기지로부터의 거리 띠 — 위험 지대에만 나는 식생을 만든다
            int distance = Mathf.Abs(x - spawnX);
            if (def.minDistanceFromBase > 0 && distance < def.minDistanceFromBase) continue;
            if (def.maxDistanceFromBase > 0 && distance > def.maxDistanceFromBase) continue;

            if (def.minSpacing > 0 && lastPlacedX != int.MinValue &&
                x < lastPlacedX + def.minSpacing) continue;

            if (def.scanMode == SurfaceScanMode.GroundLevel)
            {
                int y = groundHeightMap[x];
                if (!IsFootprintValid(def, x, y, width, groundHeightMap)) continue;
                if (_rng.NextDouble() >= def.spawnChance) continue;

                PlaceNaturalEntityAt(def, x, y, width);
                lastPlacedX = x;
                placed++;
            }
            else
            {
                // 한 열에 여러 개가 붙을 수 있다 (동굴 안 여러 층)
                for (int y = 1; y < GameMap.MAP_HEIGHT - 1; y++)
                {
                    if (!IsFootprintValid(def, x, y, width, null)) continue;
                    if (_rng.NextDouble() >= def.spawnChance) continue;

                    PlaceNaturalEntityAt(def, x, y, width);
                    lastPlacedX = x;
                    placed++;
                }
            }
        }

        Debug.Log($"[PlaceNaturalEntities] {def.Label} 배치 완료: {placed}개");
    }

    /// <summary>
    /// (x, y)에서 시작해 width칸이 이 개체를 받을 수 있는 자리인지 확인합니다.
    /// 바닥이 스폰 가능한 지표면이고, 점유되어 있지 않고, 위가 비어 있어야 합니다.
    /// </summary>
    private bool IsFootprintValid(EntityDefinition def, int x, int y, int width, int[] groundHeightMap)
    {
        if (y + 1 >= GameMap.MAP_HEIGHT) return false;

        for (int i = 0; i < width; i++)
        {
            int cx = x + i;

            // 여러 칸짜리는 평지여야 한다
            if (groundHeightMap != null && groundHeightMap[cx] != y) return false;

            // 스폰 가능한 지표면 + 미점유 (IsTileSpawnable이 둘 다 본다)
            if (!_gameMap.IsTileSpawnable(cx, y)) return false;

            // 위가 비어 있어야 개체가 설 수 있다
            if (_gameMap.TileGrid[cx, y + 1] != AIR_ID) return false;

            // 특정 타일 위에만 두는 개체 (나무·베리 덤불은 잔디만)
            if (def.allowedGroundTiles != null && def.allowedGroundTiles.Length > 0)
            {
                bool allowed = false;
                foreach (var t in def.allowedGroundTiles)
                {
                    if (_gameMap.TileGrid[cx, y] == (int)t) { allowed = true; break; }
                }
                if (!allowed) return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 개체를 실제로 놓고 바닥 칸을 점유 표시합니다.
    /// 개체는 바닥 타일 <b>위</b>(y+1)에 서고, 점유되는 것은 바닥 칸(y)입니다.
    /// </summary>
    private void PlaceNaturalEntityAt(EntityDefinition def, int x, int y, int width)
    {
        var position = new Vector2Int(x, y + 1);

        if (def.placementMode == PlacementMode.Stamp)
        {
            _stamper.PlaceStamp(def.stampKey, position);
        }
        else
        {
            _gameMap.AddEntity(new MapEntity
            {
                position = position,
                type     = ToObjectType(def.kind),
                id       = def.id
            });
        }

        for (int i = 0; i < width; i++)
            _gameMap.MarkTileOccupied(x + i, y);
    }

    /// <summary>개체 분류를 맵 오브젝트 타입으로 옮깁니다.</summary>
    private static TypeObjectTile ToObjectType(EntityKind kind)
    {
        return kind == EntityKind.Plant ? TypeObjectTile.Plant : TypeObjectTile.Building;
    }

    #endregion

    #region ISaveModule 구현

    /// <summary>맵은 가장 먼저 복원</summary>
    public int SaveOrder => 10;

    /// <summary>
    /// 맵 타일/벽 그리드와 자연물 상태를 캡처합니다.
    /// </summary>
    public void Capture(SaveData data)
    {
        if (_gameMap == null) return;

        int width = GameMap.MAP_WIDTH;
        int height = GameMap.MAP_HEIGHT;

        int[] tiles = new int[width * height];
        int[] walls = new int[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                tiles[index] = _gameMap.TileGrid[x, y];
                walls[index] = _gameMap.WallGrid[x, y];
            }
        }

        data.map = new MapSaveData
        {
            width = width,
            height = height,
            tileGrid = tiles,
            wallGrid = walls,
            entities = CaptureMapEntities()
        };
    }

    /// <summary>
    /// 맵 타일/벽 그리드를 복원하고 비주얼을 갱신합니다.
    /// </summary>
    public void Restore(SaveData data)
    {
        if (data.map == null || _gameMap == null) return;

        var mapData = data.map;

        // 맵 크기가 다르면 인덱싱이 그대로 깨진다(저장이 더 크면 IndexOutOfRange,
        // 작으면 절반만 덮인 채 조용히 이상한 맵이 된다). 조용히 깨지느니 명확히 거부한다.
        if (mapData.width != GameMap.MAP_WIDTH || mapData.height != GameMap.MAP_HEIGHT)
        {
            Debug.LogError(
                $"[MapGenerator] 세이브의 맵 크기({mapData.width}×{mapData.height})가 " +
                $"현재 맵 크기({GameMap.MAP_WIDTH}×{GameMap.MAP_HEIGHT})와 달라 불러올 수 없습니다. " +
                "맵 크기를 바꾼 뒤에는 이전 세이브를 쓸 수 없습니다 — 새로 시작하세요.");
            return;
        }

        int expected = mapData.width * mapData.height;
        if (mapData.tileGrid == null || mapData.tileGrid.Length < expected ||
            mapData.wallGrid == null || mapData.wallGrid.Length < expected)
        {
            Debug.LogError("[MapGenerator] 세이브의 타일/벽 배열이 손상되었습니다. 불러오기를 중단합니다.");
            return;
        }

        // 복원도 대량 변경 — 끝난 뒤 OnBulkChanged로 파생 데이터를 전체 재계산시킨다
        _gameMap.BeginBulkChange();
        try
        {
            for (int y = 0; y < mapData.height; y++)
            {
                for (int x = 0; x < mapData.width; x++)
                {
                    int index = y * mapData.width + x;
                    _gameMap.TileGrid[x, y] = mapData.tileGrid[index];
                    _gameMap.WallGrid[x, y] = mapData.wallGrid[index];
                }
            }

            MapRendererInstance?.RefreshAllTiles();

            // 자연물 복원
            RestoreMapEntities(mapData.entities);
        }
        finally
        {
            _gameMap.EndBulkChange();
        }
    }

    public void PostRestore(SaveData data) { }

    /// <summary>
    /// 저장 대상으로 표시된(EntityDefinition.persistInSave) 모든 개체의 위치와 성장 상태를 담습니다.
    ///
    /// 예전에는 ChoppableTree·ErosionPlantEntity를 타입별로 찾아 <c>variantId</c>에
    /// 0·2·3 같은 임시 번호를 넣었고, 베리 덤불은 아예 빠져 있어 로드하면 사라졌습니다.
    /// 이제 <see cref="MapEntityIdentity"/> 꼬리표 하나만 훑고 variantId에는 EntityType 값을 넣습니다.
    /// </summary>
    private List<MapEntitySaveData> CaptureMapEntities()
    {
        var entities = new List<MapEntitySaveData>();
        var db = DefinitionDatabase.Instance;

        foreach (var identity in FindObjectsByType<MapEntityIdentity>(FindObjectsSortMode.None))
        {
            var def = db?.GetEntity(identity.EntityId);
            if (def == null || !def.persistInSave) continue;

            float growth = 0f;
            if (def.hasGrowthState)
            {
                var tree = identity.GetComponentInChildren<ChoppableTree>();
                if (tree != null) growth = tree.IsFullyGrown ? 1f : tree.GrowthProgress;
            }

            entities.Add(new MapEntitySaveData
            {
                x = Mathf.FloorToInt(identity.transform.position.x),
                y = Mathf.FloorToInt(identity.transform.position.y),
                entityType        = (int)ToObjectType(def.kind),
                variantId         = def.id,
                remainingResource = growth
            });
        }

        return entities;
    }

    /// <summary>
    /// 구 세이브의 variantId(0·1·2·3)를 EntityType 값으로 옮깁니다.
    ///
    /// 신규 ID는 20 이상이라 구 번호와 겹치지 않으므로 세이브 버전을 올리지 않고도 구분됩니다.
    /// </summary>
    private static int ResolveEntityId(int variantId)
    {
        switch (variantId)
        {
            case 0: return (int)EntityType.Tree2x3;
            case 1: return (int)EntityType.BerryBush;
            case 2: return (int)EntityType.ToxicFern;
            case 3: return (int)EntityType.CorruptedMushroom;
            default: return variantId;
        }
    }

    private void RestoreMapEntities(List<MapEntitySaveData> entities)
    {
        if (entities == null || _stamper == null) return;

        var db = DefinitionDatabase.Instance;

        // 1. 기존 자연물 제거 — 꼬리표가 붙은 저장 대상만
        foreach (var identity in FindObjectsByType<MapEntityIdentity>(FindObjectsSortMode.None))
        {
            var def = db?.GetEntity(identity.EntityId);
            if (def != null && def.persistInSave) Destroy(identity.gameObject);
        }

        // 1-1. 꼬리표 없이 씬에 남아 있던 구 자연물도 정리 (리팩터링 이전 경로 대비)
        foreach (var tree in FindObjectsByType<ChoppableTree>(FindObjectsSortMode.None))
        {
            if (tree.GetComponent<MapEntityIdentity>() == null) Destroy(tree.gameObject);
        }
        foreach (var plant in FindObjectsByType<ErosionPlantEntity>(FindObjectsSortMode.None))
        {
            if (plant.GetComponent<MapEntityIdentity>() == null) Destroy(plant.gameObject);
        }

        // 2. _gameMap.Entities 클리어 — GenerateWorld()에서 쌓인 항목 제거
        _gameMap.ClearEntities();

        // 3. 데이터 등록 (_gameMap.Entities에 추가, 아직 GameObject 없음)
        foreach (var entity in entities)
        {
            int id = ResolveEntityId(entity.variantId);
            var def = db?.GetEntity(id);
            if (def == null)
            {
                Debug.LogWarning($"[MapGenerator] 세이브의 개체 ID {id}에 해당하는 정의가 없어 건너뜁니다.");
                continue;
            }

            var pos = new Vector2Int(entity.x, entity.y);

            if (def.placementMode == PlacementMode.Stamp)
            {
                _stamper.PlaceStamp(def.stampKey, pos);
            }
            else
            {
                _gameMap.AddEntity(new MapEntity
                {
                    position = pos,
                    type     = ToObjectType(def.kind),
                    id       = def.id
                });
            }
        }

        // 4. 실제 GameObject 인스턴스화
        MapRendererInstance.RenderRestoredEntities();

        // 5. 성장 상태 복원 (hasGrowthState가 켜진 개체만)
        ApplyGrowthStates(entities);
    }

    /// <summary>
    /// 복원된 GameObject에 저장된 성장 진행도를 적용합니다.
    /// 성장 상태가 없는 개체(침식 식물 등)는 건너뜁니다.
    /// </summary>
    private void ApplyGrowthStates(List<MapEntitySaveData> entities)
    {
        var db = DefinitionDatabase.Instance;
        var spawnedTrees = FindObjectsByType<ChoppableTree>(FindObjectsSortMode.None);
        int restored = 0;

        foreach (var saved in entities)
        {
            var def = db?.GetEntity(ResolveEntityId(saved.variantId));
            if (def == null || !def.hasGrowthState) continue;

            var tree = System.Array.Find(spawnedTrees, t =>
                Mathf.FloorToInt(t.transform.position.x) == saved.x &&
                Mathf.FloorToInt(t.transform.position.y) == saved.y);

            if (tree != null)
            {
                tree.RestoreGrowthState(saved.remainingResource);
                restored++;
            }
        }

        Debug.Log($"[MapGenerator] 성장 상태 복원 완료 (저장 항목: {entities.Count}개, 적용: {restored}개)");
    }

    #endregion

}

