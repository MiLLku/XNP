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
    private void GenerateWorld()
    {
        Debug.Log("맵 데이터 생성을 시작합니다...");

        // 4만 칸을 한 번에 쓰므로 칸 단위 통지를 멈추고, 끝난 뒤 OnBulkChanged 한 번으로 알린다
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

        const int SPAWN_X = 100;
        int width = Mathf.Max(1, def.footprintWidth);
        int lastPlacedX = int.MinValue;
        int placed = 0;

        for (int x = 0; x + width <= GameMap.MAP_WIDTH; x++)
        {
            if (def.avoidSpawnArea &&
                x >= SPAWN_X - spawnAreaPadding && x <= SPAWN_X + spawnAreaPadding) continue;

            if (def.minSpacing > 0 && lastPlacedX != int.MinValue &&
                x < lastPlacedX + def.minSpacing) continue;

            if (def.scanMode == SurfaceScanMode.GroundLevel)
            {
                int y = groundHeightMap[x];
                if (!IsFootprintValid(def, x, y, width, groundHeightMap)) continue;
                if (Random.value >= def.spawnChance) continue;

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
                    if (Random.value >= def.spawnChance) continue;

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

