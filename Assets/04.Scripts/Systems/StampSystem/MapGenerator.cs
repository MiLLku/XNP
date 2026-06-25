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
    // ... (모든 인스펙터 변수는 변경 없음) ...
    [Header("필수 연결")]
    [SerializeField] private StampLibrary stampLibrary;
    [SerializeField] private ResourceManager resourceManager;
    [Header("맵 생성 시드")]
    [SerializeField] private float noiseSeed = 0f;
    [Header("언덕 지형")]
    [SerializeField] private int baseGroundLevel = 140;
    [SerializeField] [Range(0f, 50f)] private float hillAmplitude = 10f;
    [SerializeField] [Range(0.01f, 0.1f)] private float hillScale = 0.05f;
    [SerializeField] [Range(1, 20)] private int surfaceDirtDepth = 5;
    [Header("흙 덩어리 (돌 속)")]
    [SerializeField] [Range(0.01f, 0.2f)] private float dirtNoiseScale = 0.08f;
    [SerializeField] [Range(0f, 1f)] private float dirtThreshold = 0.5f;
    [Header("동굴")]
    [SerializeField] [Range(0.01f, 0.2f)] private float caveNoiseScale = 0.07f;
    [SerializeField] [Range(0f, 1f)] private float caveThreshold = 0.7f;
    [Header("광물 지층 (지표면 기준)")]
    [SerializeField] [Range(0.01f, 0.3f)] private float copperNoiseScale = 0.15f;
    [SerializeField] [Range(0f, 1f)]      private float copperThreshold  = 0.70f;
    [SerializeField] [Range(0.01f, 0.3f)] private float ironNoiseScale   = 0.18f;
    [SerializeField] [Range(0f, 1f)]      private float ironThreshold    = 0.75f;
    [SerializeField] [Range(0.01f, 0.3f)] private float goldNoiseScale   = 0.20f;
    [SerializeField] [Range(0f, 1f)]      private float goldThreshold    = 0.80f;
    [Header("석탄 (지표 -3 ~ -15, 흔함)")]
    [SerializeField] [Range(0.01f, 0.3f)] private float coalNoiseScale   = 0.12f;
    [SerializeField] [Range(0f, 1f)]      private float coalThreshold    = 0.65f;
    [Header("은 (지표 -25 이하, 보통)")]
    [SerializeField] [Range(0.01f, 0.3f)] private float silverNoiseScale = 0.14f;
    [SerializeField] [Range(0f, 1f)]      private float silverThreshold  = 0.73f;
    [Header("수정 (지표 -55 이하, 희귀)")]
    [SerializeField] [Range(0.01f, 0.3f)] private float crystalNoiseScale = 0.22f;
    [SerializeField] [Range(0f, 1f)]      private float crystalThreshold  = 0.82f;
    [Header("나무 배치 (지표면 잔디)")]
    [SerializeField] private string treeStampKey = "TREE_2X3";
    [SerializeField] [Range(2, 20)] private int minTreeDistance = 5;
    [SerializeField] [Range(5, 50)] private int spawnAreaPadding = 15;
    [SerializeField] [Range(0f, 1f)] private float treePlacementChance = 0.5f;
    [Header("베리 덤불 배치 (지표면 잔디, 식량원)")]
    [Tooltip("StampLibrary의 베리 덤불 스탬프 키 (대소문자 정확히 일치해야 함)")]
    [SerializeField] private string berryBushStampKey = "BERRY_BUSH";
    [SerializeField] [Range(2, 20)] private int minBerryBushDistance = 7;
    [SerializeField] [Range(0f, 1f)] private float berryBushPlacementChance = 0.3f;
    [Header("침식 식물 (지상 잔디/흙)")]
    [Tooltip("독성 고사리 타일당 생성 확률")]
    [SerializeField] [Range(0f, 0.1f)] private float toxicFernSpawnChance = 0.03f;
    [Tooltip("부패한 버섯 타일당 생성 확률")]
    [SerializeField] [Range(0f, 0.1f)] private float corruptedMushroomSpawnChance = 0.02f;
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
    
    // 사용할 타일 ID
    private const int AIR_ID           = 0;
    private const int DIRT_ID          = 1;  // 흙
    private const int STONE_ID         = 2;  // 돌
    private const int COPPER_ID        = 3;  // 구리
    private const int IRON_ID          = 4;  // 철
    private const int GOLD_ID          = 5;  // 금
    private const int GRASS_ID         = 6;  // 잔디
    // ── 신규 광물 ────────────────────────────────────────────────────────────
    private const int COAL_ID          = 9;  // 석탄  (표층 근처, 흔함)
    private const int SILVER_ID        = 10; // 은    (중간 깊이)
    private const int CRYSTAL_ID       = 11; // 수정  (심층, 희귀)
    // 개체(식물·건물) 종류는 EntityType enum으로 관리합니다. MapEntity.id에는 (int) 캐스트로 대입합니다.


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
    private void GenerateWorld()
    {
        Debug.Log("맵 데이터 생성을 시작합니다...");
        int[] groundHeightMap = GenerateBaseTerrainAndOres();
        ConvertSurfaceDirtToGrass(groundHeightMap);
        PlaceMineralClusters(groundHeightMap);          // 광물 군집화 (2~6타일)
        PlaceStartingRoom(groundHeightMap);              // 스타팅 룸 배치
        PlaceErosionPlants();                            // 침식 식물 배치
        PlaceTrees(groundHeightMap);
        PlaceBerryBushes(groundHeightMap);               // 베리 덤불(식량원) 배치
    }

    #region 지형 및 광물 (Terrain & Ores)
    
    private int[] GenerateBaseTerrainAndOres()
    {
        // ... (변경 사항 없음) ...
        int[] groundHeightMap = new int[GameMap.MAP_WIDTH];
        for (int x = 0; x < GameMap.MAP_WIDTH; x++)
        {
            float hillNoise = Mathf.PerlinNoise((x * hillScale) + noiseSeed, noiseSeed);
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
    
    private int GetTileIDForCoordinate(int x, int y, int currentHeight)
    {
        if (y > currentHeight) return AIR_ID;

        float caveNoise = Mathf.PerlinNoise((x * caveNoiseScale) + noiseSeed + 1000f,
                                             (y * caveNoiseScale) + noiseSeed + 1000f);
        float dirtNoise = Mathf.PerlinNoise((x * dirtNoiseScale) + noiseSeed - 1000f,
                                             (y * dirtNoiseScale) + noiseSeed - 1000f);

        if (caveNoise > caveThreshold) return AIR_ID;
        if (y >= currentHeight - surfaceDirtDepth || dirtNoise > dirtThreshold) return DIRT_ID;

        // 광물은 PlaceMineralClusters()에서 군집(2~6타일)으로 배치
        return STONE_ID;
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
    /// 맵 중앙(X=100)에 13×9 스타팅 룸을 배치합니다.
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
    /// </summary>
    private void PlaceStartingRoom(int[] groundHeightMap)
    {
        const int spawnX     = 100;
        const int roomWidth  = 13;
        const int roomHeight = 9;
        const int ladderLX   = 8;   // 사다리 열 (local x)
        const int dividerY   = 4;   // 층 구분 (1줄, local y) — 기존 y=3 구분선 제거
        const int ladderMinY = 1;   // 사다리 시작 (하층 바닥, local y)
        const int ladderMaxY = dividerY; // 사다리 끝 (층 구분, local y)

        int groundY  = groundHeightMap[spawnX];
        int roomLeft = spawnX - roomWidth / 2;   // = 94

        // ── 1. 방 아래 지형 평탄화 (기존 terrain이 더 낮으면 DIRT로 채움) ──────
        for (int lx = 0; lx < roomWidth; lx++)
        {
            int wx = roomLeft + lx;
            int localGround = groundHeightMap[wx];
            for (int wy = localGround + 1; wy <= groundY; wy++)
                _gameMap.SetTile(wx, wy, DIRT_ID);
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
    /// 모든 광물 타입을 깊이별로 군집(2~clusterMaxSize 타일) 형태로 배치합니다.
    /// GetTileIDForCoordinate에서는 STONE_ID만 반환하고, 이 함수가 광물을 덮어씁니다.
    ///
    /// 깊이 기준: currentHeight(지표면) 기준 음수 오프셋
    ///   석탄:  -3 ~ -20  (흔함)
    ///   구리: -10 ~ -30  (보통)
    ///   철:   -20 ~ -45  (보통)
    ///   은:   -25 ~ -55  (희귀)
    ///   금:   -40 ~ -70  (희귀)
    ///   수정: -55 ~ -90  (매우 희귀)
    /// </summary>
    private void PlaceMineralClusters(int[] groundHeightMap)
    {
        // (mineralId, minDepth, maxDepth, noiseScale, seedThreshold, seedOffset)
        PlaceClustersForMineral(COAL_ID,    groundHeightMap,  -3, -20, coalNoiseScale,    coalThreshold,    5000f);
        PlaceClustersForMineral(COPPER_ID,  groundHeightMap, -10, -30, copperNoiseScale,  copperThreshold,  2000f);
        PlaceClustersForMineral(IRON_ID,    groundHeightMap, -20, -45, ironNoiseScale,    ironThreshold,    3000f);
        PlaceClustersForMineral(SILVER_ID,  groundHeightMap, -25, -55, silverNoiseScale,  silverThreshold,  6000f);
        PlaceClustersForMineral(GOLD_ID,    groundHeightMap, -40, -70, goldNoiseScale,    goldThreshold,    4000f);
        PlaceClustersForMineral(CRYSTAL_ID, groundHeightMap, -55, -90, crystalNoiseScale, crystalThreshold, 7000f);

        Debug.Log("[PlaceMineralClusters] 광물 군집 배치 완료.");
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
                // 이미 광물이 채워졌거나 돌이 아니면 씨앗 불가
                if (_gameMap.TileGrid[x, y] != STONE_ID) continue;

                float noise = Mathf.PerlinNoise(
                    x * noiseScale + noiseSeed + seedOffset,
                    y * noiseScale + noiseSeed + seedOffset);

                if (noise < threshold) continue;

                // 씨앗 선정 → 군집 확장
                int targetSize = Random.Range(clusterMinSize, clusterMaxSize + 1);
                ExpandMineralCluster(x, y, mineralId, targetSize, yBottom);
            }
        }
    }

    /// <summary>
    /// 씨앗 위치에서 무작위 확장(Random Walk)으로 광물 군집을 형성합니다.
    /// 인접한 STONE 타일에만 확장하며, maxY(얕은 한계)를 넘지 않습니다.
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
            int fi = Random.Range(0, frontier.Count);
            var pos = frontier[fi];

            // 방향 셔플 (Fisher-Yates)
            for (int i = dirs.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
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
                if (_gameMap.TileGrid[nb.x, nb.y] != STONE_ID) continue;

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
    /// 침식 식물(독성 고사리 / 부패한 버섯)을 지상 잔디/흙 타일 위에 배치합니다.
    /// 스폰 지점 주변(spawnAreaPadding)은 제외합니다.
    /// </summary>
    private void PlaceErosionPlants()
    {
        int spawnX       = 100;
        int toxicCount   = 0;
        int mushroomCount = 0;

        for (int x = 0; x < GameMap.MAP_WIDTH; x++)
        {
            // 스폰 지점 주변 제외
            if (x >= spawnX - spawnAreaPadding && x <= spawnX + spawnAreaPadding) continue;

            for (int y = 1; y < GameMap.MAP_HEIGHT - 1; y++)
            {
                // 스폰 가능한 잔디/흙 타일이고 윗 칸이 공기인 경우만
                if (!_gameMap.IsTileSpawnable(x, y)) continue;
                if (_gameMap.TileGrid[x, y + 1] != AIR_ID) continue;

                float roll = Random.value;

                if (roll < toxicFernSpawnChance)
                {
                    _gameMap.AddEntity(new MapEntity
                    {
                        position = new Vector2Int(x, y + 1),
                        type     = TypeObjectTile.Plant,
                        id       = (int)EntityType.ToxicFern
                    });
                    _gameMap.MarkTileOccupied(x, y);
                    toxicCount++;
                }
                else if (roll < toxicFernSpawnChance + corruptedMushroomSpawnChance)
                {
                    _gameMap.AddEntity(new MapEntity
                    {
                        position = new Vector2Int(x, y + 1),
                        type     = TypeObjectTile.Plant,
                        id       = (int)EntityType.CorruptedMushroom
                    });
                    _gameMap.MarkTileOccupied(x, y);
                    mushroomCount++;
                }
            }
        }

        Debug.Log($"[PlaceErosionPlants] 배치 완료. (독성고사리: {toxicCount}개, 부패버섯: {mushroomCount}개)");
    }

    private void PlaceTrees(int[] groundHeightMap)
    {
        if (treePlacementChance <= 0f) return;

        int spawnX = 100;
        int lastTreeX = -minTreeDistance; 
        const int treeWidth = 2; 
        int skippedByPadding = 0, skippedByDistance = 0, failedFlatGroundCheck = 0, failedChanceRoll = 0, treesPlaced = 0;

        for (int x = 0; x < GameMap.MAP_WIDTH - treeWidth; x++) 
        {
            if (x >= spawnX - spawnAreaPadding && x <= spawnX + spawnAreaPadding) { skippedByPadding++; continue; }
            if (x < lastTreeX + minTreeDistance) { skippedByDistance++; continue; }

            int y = groundHeightMap[x];
            
            // 1. (x,y) 타일이 잔디(GRASS_ID)이고 '스폰 가능'한지 확인
            // (IsTileSpawnable이 OccupiedGrid를 검사하므로 열매 나무와 겹치지 않음)
            bool isFlatGrassPatch = _gameMap.IsTileSpawnable(x, y) && 
                                    _gameMap.TileGrid[x, y] == GRASS_ID; 

            for (int i = 1; i < treeWidth; i++) 
            {
                // 2. 옆 타일(x+i, y)도 잔디이고, 높이가 같고, '스폰 가능'한지 확인
                if (groundHeightMap[x + i] != y || 
                    !_gameMap.IsTileSpawnable(x + i, y) || // ★점유 상태 확인
                    _gameMap.TileGrid[x + i, y] != GRASS_ID) 
                { 
                    isFlatGrassPatch = false; 
                    break; 
                }
            }
            
            if (isFlatGrassPatch)
            {
                if (Random.value < treePlacementChance) 
                {
                    _stamper.PlaceStamp(treeStampKey, new Vector2Int(x, y + 1)); 
                    lastTreeX = x;
                    
                    // 2. ★타일 2칸을 '점유됨'으로 마킹 (ID를 바꾸지 않음)★
                    _gameMap.MarkTileOccupied(x, y);
                    _gameMap.MarkTileOccupied(x + 1, y);
                    treesPlaced++;
                } else { failedChanceRoll++; }
            } else { failedFlatGroundCheck++; }
        }
        Debug.Log($"[PlaceTrees] 배치 완료. (배치: {treesPlaced}그루, 평지실패: {failedFlatGroundCheck}칸, 확률실패: {failedChanceRoll}칸)");
    }

    /// <summary>
    /// 지표면 잔디에 베리 덤불(식량원)을 배치합니다. 베리 덤불은 1칸이며,
    /// 나무·다른 베리 덤불과 겹치지 않도록 IsTileSpawnable(점유 그리드)을 확인합니다.
    /// 스폰 지점 주변(spawnAreaPadding)은 제외합니다.
    /// </summary>
    private void PlaceBerryBushes(int[] groundHeightMap)
    {
        if (berryBushPlacementChance <= 0f) return;

        int spawnX = 100;
        int lastX = -minBerryBushDistance;
        int placed = 0;

        for (int x = 0; x < GameMap.MAP_WIDTH; x++)
        {
            if (x >= spawnX - spawnAreaPadding && x <= spawnX + spawnAreaPadding) continue;
            if (x < lastX + minBerryBushDistance) continue;

            int y = groundHeightMap[x];

            // 잔디이고 점유되지 않은 칸에만 (나무가 이미 차지한 칸 회피)
            if (!_gameMap.IsTileSpawnable(x, y) || _gameMap.TileGrid[x, y] != GRASS_ID) continue;

            if (Random.value < berryBushPlacementChance)
            {
                _stamper.PlaceStamp(berryBushStampKey, new Vector2Int(x, y + 1));
                lastX = x;
                _gameMap.MarkTileOccupied(x, y);
                placed++;
            }
        }
        Debug.Log($"[PlaceBerryBushes] 배치 완료. (배치: {placed}개)");
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

    public void PostRestore(SaveData data) { }

    private List<MapEntitySaveData> CaptureMapEntities()
    {
        var entities = new List<MapEntitySaveData>();

        // 나무 (ChoppableTree) — variantId=0
        var trees = FindObjectsByType<ChoppableTree>(FindObjectsSortMode.None);
        foreach (var tree in trees)
        {
            entities.Add(new MapEntitySaveData
            {
                x = Mathf.FloorToInt(tree.transform.position.x),
                y = Mathf.FloorToInt(tree.transform.position.y),
                entityType        = (int)TypeObjectTile.Plant,
                variantId         = 0,
                remainingResource = tree.IsFullyGrown ? 1f : tree.GrowthProgress
            });
        }

        // 침식 식물 (ErosionPlantEntity) — variantId=2(ToxicFern), 3(CorruptedMushroom)
        var erosionPlants = FindObjectsByType<ErosionPlantEntity>(FindObjectsSortMode.None);
        foreach (var ep in erosionPlants)
        {
            int variant = (ep.entityId == (int)EntityType.ToxicFern) ? 2 : 3;
            entities.Add(new MapEntitySaveData
            {
                x = Mathf.FloorToInt(ep.transform.position.x),
                y = Mathf.FloorToInt(ep.transform.position.y),
                entityType        = (int)TypeObjectTile.Plant,
                variantId         = variant,
                remainingResource = 0f // 침식 식물은 성장 상태 없음
            });
        }

        return entities;
    }

    private void RestoreMapEntities(List<MapEntitySaveData> entities)
    {
        if (entities == null || _stamper == null) return;

        // 1. 기존 자연물 제거
        var existingTrees = FindObjectsByType<ChoppableTree>(FindObjectsSortMode.None);
        foreach (var tree in existingTrees) Destroy(tree.gameObject);

        var existingErosionPlants = FindObjectsByType<ErosionPlantEntity>(FindObjectsSortMode.None);
        foreach (var ep in existingErosionPlants) Destroy(ep.gameObject);

        // 2. _gameMap.Entities 클리어 — GenerateWorld()에서 쌓인 항목 제거
        _gameMap.ClearEntities();

        // 3. 데이터 등록 (_gameMap.Entities에 추가, 아직 GameObject 없음)
        foreach (var entity in entities)
        {
            Vector2Int pos = new Vector2Int(entity.x, entity.y);
            switch (entity.variantId)
            {
                case 0: // 나무
                    _stamper.PlaceStamp(treeStampKey, pos);
                    break;
                case 2: // 독성 고사리
                    _gameMap.AddEntity(new MapEntity
                    {
                        position = pos,
                        type     = TypeObjectTile.Plant,
                        id       = (int)EntityType.ToxicFern
                    });
                    break;
                case 3: // 부패한 버섯
                    _gameMap.AddEntity(new MapEntity
                    {
                        position = pos,
                        type     = TypeObjectTile.Plant,
                        id       = (int)EntityType.CorruptedMushroom
                    });
                    break;
            }
        }

        // 4. 실제 GameObject 인스턴스화
        MapRendererInstance.RenderRestoredEntities();

        // 5. 나무 성장 상태 복원 (침식 식물은 성장 상태 없음)
        ApplyTreeGrowthStates(entities);
    }

    /// <summary>
    /// 복원된 나무 GameObjects에 저장된 성장 진행도를 적용합니다.
    /// </summary>
    private void ApplyTreeGrowthStates(List<MapEntitySaveData> entities)
    {
        var spawnedTrees = FindObjectsByType<ChoppableTree>(FindObjectsSortMode.None);

        foreach (var saved in entities)
        {
            if (saved.variantId != 0) continue;

            var tree = System.Array.Find(spawnedTrees, t =>
                Mathf.FloorToInt(t.transform.position.x) == saved.x &&
                Mathf.FloorToInt(t.transform.position.y) == saved.y);
            tree?.RestoreGrowthState(saved.remainingResource);
        }

        Debug.Log($"[MapGenerator] 성장 상태 복원 완료 (저장 항목: {entities.Count}개)");
    }

    #endregion

}

