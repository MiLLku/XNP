using UnityEngine;

/// <summary>
/// 전쟁의 안개(Fog of War) 시스템 — 참조 카운터 기반 동적 reveal + LOS 차단.
///
/// 아키텍처:
///   - int[,] _empSightCount  : 각 타일을 시야에 두고 있는 직원 수
///   - int[,] _bldSightCount  : 각 타일을 비추는 건물 수
///   - bool[,] _permanentReveal : 스탬프로 영구 공개된 타일
///   - 세 상태 모두 0/false → 안개로 가려짐; 하나라도 true/≥1 → 공개
///
/// 직원 시야:
///   - 머리(발 + HEAD_OFFSET_Y) 기준 원형 범위
///   - Bresenham LOS: 중간 타일이 solid(비-AIR)면 그 너머 타일은 차단
///   - 이동 시 UpdateEmployeeSight(from, to) — diff 영역만 재계산 (효율적)
///
/// 건물 시야:
///   - 풋프린트 + buildingRadius 정사각 확장, LOS 미적용 (건물은 벽을 투과)
///
/// 인스펙터:
///   - employeeRadius : 직원 시야 반경 (기본 5)
///   - buildingRadius : 건물 시야 반경 (기본 2)
/// </summary>
public class FogOfWarManager : DestroySingleton<FogOfWarManager>, ISaveModule
{
    #region 설정

    [Header("시야 반경 (타일 단위)")]
    [SerializeField] private int employeeRadius = 5;
    [SerializeField] private int buildingRadius = 2;

    [Header("직원 머리 오프셋 (발 기준 타일 단위)")]
    [Tooltip("직원 스프라이트 높이에 맞게 조정. 2타일 키라면 1.")]
    [SerializeField] private int headOffsetY = 1;

    #endregion

    #region 내부 상태

    /// <summary>각 타일을 시야에 두고 있는 직원 수</summary>
    private int[,] _empSightCount;

    /// <summary>각 타일을 비추는 건물 수</summary>
    private int[,] _bldSightCount;

    /// <summary>스탬프/구조물로 영구 공개된 타일 (세이브/로드 유지)</summary>
    private bool[,] _permanentReveal;

    #endregion

    #region 초기화

    protected override void Awake()
    {
        base.Awake();
        _empSightCount   = new int[GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT];
        _bldSightCount   = new int[GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT];
        _permanentReveal = new bool[GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT];
    }

    /// <summary>
    /// 안개 시각 상태를 현재 카운터 상태로부터 동기화합니다.
    /// 카운터를 리셋하지 않으므로 이미 등록된 시야는 그대로 유지됩니다.
    /// 새 게임 시작 시 MapGenerator.Start()에서 RenderMap 이후 호출됩니다.
    /// </summary>
    public void InitializeFog()
    {
        MapRenderer renderer = MapGenerator.instance?.MapRendererInstance;
        if (renderer == null) return;

        bool[,] revealed = new bool[GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT];
        for (int x = 0; x < GameMap.MAP_WIDTH; x++)
        for (int y = 0; y < GameMap.MAP_HEIGHT; y++)
            revealed[x, y] = IsRevealed(x, y);

        renderer.RestoreFog(revealed);
        Debug.Log("[FogOfWarManager] 안개 초기화 완료 (카운터 상태 기반)");
    }

    #endregion

    #region 영구 공개 API

    /// <summary>
    /// 지정 직사각형 영역을 영구적으로 공개합니다.
    /// 스탬프 배치(PlaceStartingRoom 등) 시 호출되며, 세이브/로드 후에도 유지됩니다.
    /// 맵 생성 중 호출될 때는 fog 타일맵이 아직 없으므로 시각 반영은 InitializeFog()에 위임됩니다.
    /// </summary>
    public void PermanentlyRevealArea(Vector2Int origin, Vector2Int size)
    {
        if (_permanentReveal == null) return;
        for (int x = origin.x; x < origin.x + size.x; x++)
        for (int y = origin.y; y < origin.y + size.y; y++)
        {
            if (!InBounds(x, y)) continue;
            _permanentReveal[x, y] = true;
        }
        Debug.Log($"[FogOfWarManager] 영구 공개 영역 등록: origin={origin}, size={size}");
    }

    #endregion

    #region 직원 시야 API

    /// <summary>
    /// 지정 발 위치 기준 머리에서 employeeRadius 반경 + LOS를 적용해 카운터를 +1 합니다.
    /// </summary>
    public void AddEmployeeSight(Vector2Int feet)
    {
        if (_empSightCount == null) return;
        var head = new Vector2Int(feet.x, feet.y + headOffsetY);
        IterateCircleWithLOS(head, feet, employeeRadius, (x, y) => Increment(x, y, isEmp: true));
    }

    /// <summary>
    /// 지정 발 위치 기준 머리에서 employeeRadius 반경 + LOS를 적용해 카운터를 -1 합니다.
    /// </summary>
    public void RemoveEmployeeSight(Vector2Int feet)
    {
        if (_empSightCount == null) return;
        var head = new Vector2Int(feet.x, feet.y + headOffsetY);
        IterateCircleWithLOS(head, feet, employeeRadius, (x, y) => Decrement(x, y, isEmp: true));
    }

    /// <summary>
    /// 현재 위치의 직원 시야를 완전히 재계산합니다.
    /// 채광 등으로 지형이 바뀐 뒤 호출하면 새로 열린 LOS 경로의 타일을 즉시 공개합니다.
    /// </summary>
    public void RefreshEmployeeSight(Vector2Int feet)
    {
        if (_empSightCount == null) return;
        RemoveEmployeeSight(feet);
        AddEmployeeSight(feet);
    }

    /// <summary>
    /// 직원이 from → to 로 이동했을 때 시야를 효율적으로 갱신합니다.
    /// 머리 기준 LOS를 포함한 diff 계산: 두 원+LOS 집합의 차집합만 갱신합니다.
    /// </summary>
    public void UpdateEmployeeSight(Vector2Int from, Vector2Int to)
    {
        if (_empSightCount == null) return;
        if (from == to) return;

        var headFrom = new Vector2Int(from.x, from.y + headOffsetY);
        var headTo   = new Vector2Int(to.x,   to.y   + headOffsetY);

        int r  = employeeRadius;
        int r2 = r * r;

        int minX = Mathf.Min(from.x, to.x) - r;
        int maxX = Mathf.Max(from.x, to.x) + r;
        int minY = Mathf.Min(from.y, to.y) - r;
        int maxY = Mathf.Max(from.y, to.y) + r;

        for (int x = minX; x <= maxX; x++)
        for (int y = minY; y <= maxY; y++)
        {
            if (!InBounds(x, y)) continue;

            var tile = new Vector2Int(x, y);

            int dxf = x - from.x, dyf = y - from.y;
            int dxt = x - to.x,   dyt = y - to.y;

            // 원 범위 안에 있고 LOS가 통과되는 경우만 시야 포함
            bool inFrom = (dxf * dxf + dyf * dyf <= r2) && HasLineOfSight(headFrom, tile);
            bool inTo   = (dxt * dxt + dyt * dyt <= r2) && HasLineOfSight(headTo,   tile);

            if (inFrom == inTo) continue;

            if (inFrom) Decrement(x, y, isEmp: true);
            else        Increment(x, y, isEmp: true);
        }
    }

    #endregion

    #region 건물 시야 API

    /// <summary>
    /// 건물 풋프린트 + 주변 buildingRadius 영역의 카운터를 +1 합니다.
    /// (정사각형 확장 — 측면도 모두 비춤)
    /// </summary>
    public void AddBuildingSight(Vector2Int origin, Vector2Int size)
    {
        if (_bldSightCount == null) return;
        IterateBuildingArea(origin, size, (x, y) => Increment(x, y, isEmp: false));
    }

    /// <summary>건물 영역의 카운터를 -1 합니다.</summary>
    public void RemoveBuildingSight(Vector2Int origin, Vector2Int size)
    {
        if (_bldSightCount == null) return;
        IterateBuildingArea(origin, size, (x, y) => Decrement(x, y, isEmp: false));
    }

    private void IterateBuildingArea(Vector2Int origin, Vector2Int size, System.Action<int, int> action)
    {
        int minX = origin.x - buildingRadius;
        int maxX = origin.x + size.x - 1 + buildingRadius;
        int minY = origin.y - buildingRadius;
        int maxY = origin.y + size.y - 1 + buildingRadius;

        for (int x = minX; x <= maxX; x++)
        for (int y = minY; y <= maxY; y++)
            action(x, y);
    }

    #endregion

    #region 조회

    public bool IsRevealed(int x, int y)
    {
        if (!InBounds(x, y)) return false;
        return _permanentReveal[x, y]
            || _empSightCount[x, y] > 0
            || _bldSightCount[x, y] > 0;
    }

    #endregion

    #region 내부 헬퍼

    private bool InBounds(int x, int y) =>
        x >= 0 && x < GameMap.MAP_WIDTH && y >= 0 && y < GameMap.MAP_HEIGHT;

    // ── LOS (Line of Sight) ────────────────────────────────────────────────────

    /// <summary>
    /// 타일 (x, y)가 불투명(solid)한지 반환합니다.
    /// 다음 중 하나라도 해당하면 차단:
    ///   1) 자연 지형 (TileGrid != AIR)
    ///   2) 점유된 타일 (건설된 Floor·벽·작업대 등 모든 건물)
    ///      Floor는 blocksMovement=false라도 벽 역할(LOS 차단)을 하도록 OccupiedGrid 기반으로 판정합니다.
    /// </summary>
    private bool IsSolid(int x, int y)
    {
        if (!InBounds(x, y)) return true; // 맵 밖은 차단으로 처리
        var gameMap = MapGenerator.instance?.GameMapInstance;
        if (gameMap == null) return false;
        if (gameMap.TileGrid[x, y] != 0) return true;        // 자연 지형
        if (gameMap.IsTileOccupied(x, y)) return true;       // 건설된 건물(Floor·벽·작업대 등)
        return false;
    }

    /// <summary>
    /// from → to 타일 간 직선 시야가 통과되는지 Bresenham 알고리즘으로 검사합니다.
    /// from 타일 자체는 검사 제외 (시야 원점).
    /// to 타일은 solid여도 '벽 표면'으로 보이므로 차단하지 않습니다.
    /// </summary>
    private bool HasLineOfSight(Vector2Int from, Vector2Int to)
    {
        int x0 = from.x, y0 = from.y;
        int x1 = to.x,   y1 = to.y;

        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = (x0 < x1) ? 1 : -1;
        int sy = (y0 < y1) ? 1 : -1;
        int err = dx - dy;

        // 무한루프 방지용 최대 스텝
        int maxSteps = dx + dy + 2;
        int cx = x0, cy = y0;

        for (int step = 0; step < maxSteps; step++)
        {
            if (cx == x1 && cy == y1) return true;

            // 출발 타일이 아닌 중간 타일이 solid이면 차단
            if ((cx != x0 || cy != y0) && IsSolid(cx, cy))
                return false;

            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; cx += sx; }
            if (e2 <  dx) { err += dx; cy += sy; }
        }
        return (cx == x1 && cy == y1);
    }

    /// <summary>
    /// head 위치에서 LOS가 통과되는 타일만 action을 호출합니다.
    /// center + radius로 원형 범위를 정의하고, head 기준 LOS를 필터로 적용합니다.
    /// </summary>
    private void IterateCircleWithLOS(Vector2Int head, Vector2Int center, int radius, System.Action<int, int> action)
    {
        int r2 = radius * radius;
        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -radius; dy <= radius; dy++)
        {
            if (dx * dx + dy * dy > r2) continue;
            int nx = center.x + dx;
            int ny = center.y + dy;
            if (!InBounds(nx, ny)) continue;
            if (!HasLineOfSight(head, new Vector2Int(nx, ny))) continue;
            action(nx, ny);
        }
    }

    // ── 기존 원형 이터레이터 (건물 시야 등 LOS 불필요한 경우용) ─────────────────
    private void IterateCircle(Vector2Int center, int radius, System.Action<int, int> action)
    {
        int r2 = radius * radius;
        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -radius; dy <= radius; dy++)
        {
            if (dx * dx + dy * dy > r2) continue;
            int nx = center.x + dx;
            int ny = center.y + dy;
            if (!InBounds(nx, ny)) continue;
            action(nx, ny);
        }
    }

    private void Increment(int x, int y, bool isEmp)
    {
        if (!InBounds(x, y)) return;
        bool wasRevealed = IsRevealed(x, y);
        if (isEmp) _empSightCount[x, y]++;
        else       _bldSightCount[x, y]++;

        if (!wasRevealed)
            MapGenerator.instance?.MapRendererInstance?.ClearFogAt(x, y);
    }

    private void Decrement(int x, int y, bool isEmp)
    {
        if (!InBounds(x, y)) return;
        if (isEmp)
        {
            if (_empSightCount[x, y] <= 0) return;
            _empSightCount[x, y]--;
        }
        else
        {
            if (_bldSightCount[x, y] <= 0) return;
            _bldSightCount[x, y]--;
        }

        if (!IsRevealed(x, y))
            MapGenerator.instance?.MapRendererInstance?.ApplyFogAt(x, y);
    }

    #endregion

    #region ISaveModule

    /// <summary>DayCycle(5), MapGenerator(10) 이후 / ZoneManager(25) 이전</summary>
    public int SaveOrder => 15;

    /// <summary>
    /// 영구 공개 영역 비트마스크를 저장합니다.
    /// 카운터(직원/건물 시야)는 파생 상태이므로 저장하지 않습니다.
    /// </summary>
    public void Capture(SaveData data)
    {
        int total = GameMap.MAP_WIDTH * GameMap.MAP_HEIGHT;
        byte[] bitmask = new byte[(total + 7) / 8];

        for (int i = 0; i < total; i++)
        {
            int x = i % GameMap.MAP_WIDTH;
            int y = i / GameMap.MAP_WIDTH;
            if (_permanentReveal != null && _permanentReveal[x, y])
                bitmask[i / 8] |= (byte)(1 << (i % 8));
        }

        data.fog = new FogSaveData { permanentRevealBitmask = bitmask };
    }

    /// <summary>
    /// 영구 공개 영역을 복원합니다.
    /// 카운터(직원/건물 시야)는 건물·직원 Awake/Start에서 자동 누적됩니다.
    /// </summary>
    public void Restore(SaveData data)
    {
        _permanentReveal = new bool[GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT];

        if (data.fog?.permanentRevealBitmask == null) return;

        int total = GameMap.MAP_WIDTH * GameMap.MAP_HEIGHT;
        for (int i = 0; i < total; i++)
        {
            int byteIdx = i / 8;
            if (byteIdx >= data.fog.permanentRevealBitmask.Length) break;
            if ((data.fog.permanentRevealBitmask[byteIdx] & (1 << (i % 8))) != 0)
            {
                _permanentReveal[i % GameMap.MAP_WIDTH, i / GameMap.MAP_WIDTH] = true;
            }
        }

        Debug.Log("[FogOfWarManager] 영구 공개 영역 복원 완료");
    }

    /// <summary>
    /// 모든 엔티티 등록 완료 후, 현재 카운터 상태를 시각 안개에 반영합니다.
    /// </summary>
    public void PostRestore(SaveData data)
    {
        InitializeFog();
    }

    #endregion
}
