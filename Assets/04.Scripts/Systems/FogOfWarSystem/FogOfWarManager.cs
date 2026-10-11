using UnityEngine;

/// <summary>
/// 전쟁의 안개(Fog of War) 시스템 — 참조 카운터 기반 동적 reveal + LOS 차단.
///
/// 아키텍처:
///   - int[,] _empSightCount  : 각 타일을 시야에 두고 있는 직원 수
///   - int[,] _bldSightCount  : 각 타일을 비추는 건물 수
///   - bool[,] _permanentReveal : 스탬프로 영구 공개된 타일
///   - 세 상태 모두 0/false → 시야 밖; 하나라도 true/≥1 → 지금 보임 (IsRevealed)
///   - bool[,] _explored      : 한 번이라도 보인 적 있는 타일 (IsExplored, 세이브 유지)
///
/// 그리기 (XNP/Fog 셰이더, 맵 크기 쿼드 1장):
///   - 미탐색 = 완전 검정 / 탐색했지만 시야 밖 = 어둡게만 / 시야 안 = 그대로
///   - 침식이 기준 이상인 탐색한 공기 칸 = 침식 안개 (ErosionFilterLevel 1단계)
///   - 탐색한 공기 칸마다 침식 수치를 숫자로 (ErosionFilterLevel 2단계, 안개 위에 함께)
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

    [Header("그리기")]
    [Tooltip("XNP/Fog 머티리얼. 어둡기·침식 안개 기준·텍스처는 이 머티리얼에서 조절합니다.")]
    [SerializeField] private Material fogMaterial;

    #endregion

    #region 내부 상태

    /// <summary>각 타일을 시야에 두고 있는 직원 수</summary>
    private int[,] _empSightCount;

    /// <summary>각 타일을 비추는 건물 수</summary>
    private int[,] _bldSightCount;

    /// <summary>스탬프/구조물로 영구 공개된 타일 (세이브/로드 유지)</summary>
    private bool[,] _permanentReveal;

    /// <summary>한 번이라도 시야에 들어온 타일 (세이브/로드 유지)</summary>
    private bool[,] _explored;

    // 칸마다 (탐색함, 지금 시야 안, 침식/100, 숫자로 적을 침식 + 1) — 앞의 셋은 셰이더가 보간해 읽고, 넷째는 칸 값 그대로 읽는다
    private Texture2D _fogTexture;
    private Color32[] _fogData;
    private bool _fogDirty;
    private float _nextErosionRefresh;
    private byte[] _openErosion; // RefreshErosion 작업 버퍼
    private MeshRenderer _fogRenderer;
    private MaterialPropertyBlock _fogBlock;

    // 비 — 열마다 하늘이 시작되는 높이(가로 한 줄 텍스처). 셰이더가 그 위쪽 칸에만 빗줄기를 그린다.
    private Texture2D _skyTexture;
    private float[] _skyData;
    private bool _raining;
    private int _erosionFilterLevel = 1;

    private const int FogSortingOrder = 20; // 지형·개체·하이라이트 위, 방/구역 오버레이 아래
    private const float ErosionScale = 100f; // 셰이더와 같아야 한다

    /// <summary>침식 필터 단계 수 (0 끔 / 1 안개 / 2 수치)</summary>
    public const int ErosionFilterLevels = 3;

    /// <summary>
    /// 침식 필터 단계 (좌상단 보기 토글 바가 바꿉니다).
    /// 0 = 끔, 1 = 침식 안개, 2 = 침식 안개 + 칸마다 침식 수치를 칸 한가운데에 숫자로.
    /// </summary>
    public int ErosionFilterLevel
    {
        get => _erosionFilterLevel;
        set
        {
            _erosionFilterLevel = Mathf.Clamp(value, 0, ErosionFilterLevels - 1);
            if (_fogRenderer == null) return;
            ApplyErosionFilter();
            _fogRenderer.SetPropertyBlock(_fogBlock);
        }
    }

    private void ApplyErosionFilter()
    {
        _fogBlock.SetFloat("_ErosionVisible", _erosionFilterLevel >= 1 ? 1f : 0f);
        _fogBlock.SetFloat("_ErosionNumbers", _erosionFilterLevel == 2 ? 1f : 0f);
    }

    #endregion

    #region 초기화

    protected override void Awake()
    {
        base.Awake();
        _empSightCount   = new int[GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT];
        _bldSightCount   = new int[GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT];
        _permanentReveal = new bool[GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT];
        _explored        = new bool[GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT];
    }

    /// <summary>
    /// 안개 시각 상태를 현재 카운터·탐색 상태로부터 동기화합니다.
    /// 카운터를 리셋하지 않으므로 이미 등록된 시야는 그대로 유지됩니다.
    /// 새 게임 시작 시 MapGenerator.Start()에서 RenderMap 이후 호출됩니다.
    /// </summary>
    public void InitializeFog()
    {
        SetupFogRenderer();
        if (_fogData == null) return;

        for (int x = 0; x < GameMap.MAP_WIDTH; x++)
        for (int y = 0; y < GameMap.MAP_HEIGHT; y++)
            WriteFogCell(x, y);

        _nextErosionRefresh = 0f;
        Debug.Log("[FogOfWarManager] 안개 초기화 완료 (카운터 상태 기반)");
    }

    #endregion

    #region 그리기

    private void SetupFogRenderer()
    {
        if (_fogTexture != null) return;
        if (fogMaterial == null)
        {
            Debug.LogWarning("[FogOfWarManager] fogMaterial이 연결되지 않아 안개를 그리지 않습니다.");
            return;
        }

        int w = GameMap.MAP_WIDTH, h = GameMap.MAP_HEIGHT;
        _fogTexture = new Texture2D(w, h, TextureFormat.RGBA32, false, true)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        _fogData = new Color32[w * h];

        var mesh = new Mesh { name = "FogQuad" };
        mesh.vertices = new[] { new Vector3(0, 0), new Vector3(w, 0), new Vector3(0, h), new Vector3(w, h) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();

        var go = new GameObject("FogOverlay");
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(0, 0, -0.02f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        _fogRenderer = go.AddComponent<MeshRenderer>();
        _fogRenderer.sharedMaterial = fogMaterial;
        _fogRenderer.sortingOrder = FogSortingOrder;

        // 셰이더 Properties에 없는 값은 머티리얼에 남지 않아 프로퍼티 블록으로 넘긴다
        _fogBlock = new MaterialPropertyBlock();
        _fogBlock.SetTexture("_FogData", _fogTexture);
        _fogBlock.SetVector("_MapSize", new Vector4(w, h, 0, 0));
        ApplyErosionFilter();

        _skyTexture = new Texture2D(w, 1, TextureFormat.RFloat, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        _skyData = new float[w];
        _fogBlock.SetTexture("_SkyBottom", _skyTexture);
        _fogBlock.SetFloat("_Rain", 0f);
        _fogRenderer.SetPropertyBlock(_fogBlock);
    }

    private void WriteFogCell(int x, int y)
    {
        if (_fogData == null) return;
        int i = y * GameMap.MAP_WIDTH + x;
        _fogData[i].r = _explored[x, y] ? (byte)255 : (byte)0;
        _fogData[i].g = IsRevealed(x, y) ? (byte)255 : (byte)0;
        _fogDirty = true;
    }

    /// <summary>
    /// 탐색한 빈 칸의 침식 수치를 다시 읽는다. 미탐색 칸은 0(안개 없음).
    /// 막힌 칸(지형·건물)은 맞닿은 빈 칸의 값을 물려받는다 — 0으로 두면 셰이더의 보간 때문에
    /// 벽에 가까워질수록 안개가 옅어져 타일 옆에 안개 없는 띠가 생긴다.
    ///
    /// 숫자 표시용 값(a)은 물려받지 않는다 — 공기가 통하는 칸(가구가 놓인 칸 포함, 지형·차단 건물·문 제외)에만
    /// 그 칸의 수치를 정수로 적고, 나머지는 0(안 적음).
    /// </summary>
    private void RefreshErosion()
    {
        var erosion = TerrainErosionManager.instance;
        var gameMap = MapGenerator.instance?.GameMapInstance;
        var rooms = RoomManager.instance;
        if (erosion == null || gameMap == null) return;

        int w = GameMap.MAP_WIDTH, h = GameMap.MAP_HEIGHT;
        _openErosion ??= new byte[w * h];

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            byte value = 0, number = 0;
            if (_explored[x, y] && gameMap.TileGrid[x, y] == 0)
            {
                bool open = !gameMap.IsTileOccupied(x, y);
                if (open || (rooms != null && rooms.IsAirCell(x, y)))
                {
                    float amount = erosion.GetRoomErosionAt(x, y);
                    if (open) value = (byte)Mathf.Clamp(amount / ErosionScale * 255f, 1f, 255f);
                    number = (byte)(1 + Mathf.Clamp(Mathf.RoundToInt(amount), 0, 254));
                }
            }
            _openErosion[y * w + x] = value; // 0 = 막혔거나 미탐색

            if (_fogData[y * w + x].a != number)
            {
                _fogData[y * w + x].a = number;
                _fogDirty = true;
            }
        }

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            byte value = _openErosion[i];
            if (value == 0)
            {
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    byte n = _openErosion[ny * w + nx];
                    if (n > value) value = n;
                }
            }

            if (_fogData[i].b == value) continue;
            _fogData[i].b = value;
            _fogDirty = true;
        }
    }

    /// <summary>비가 내리는 동안 하늘 높이를 갱신하고, 비가 시작·끝날 때 셰이더에 알린다 (1초 폴링).</summary>
    private void RefreshRain()
    {
        bool raining = FluidManager.instance != null && FluidManager.instance.IsRaining;
        if (raining)
        {
            var rooms = RoomManager.instance;
            for (int x = 0; x < _skyData.Length; x++)
                _skyData[x] = rooms != null ? rooms.GetSkyBottom(x) : GameMap.MAP_HEIGHT;
            _skyTexture.SetPixelData(_skyData, 0);
            _skyTexture.Apply(false);
        }

        if (raining == _raining) return;
        _raining = raining;
        _fogBlock.SetFloat("_Rain", raining ? 1f : 0f);
        _fogRenderer.SetPropertyBlock(_fogBlock);
    }

    private void LateUpdate()
    {
        if (_fogData == null) return;

        // ponytail: 1초마다 전체 훑기. 방 재계산·침식 변화를 이벤트 없이 따라간다.
        if (Time.unscaledTime >= _nextErosionRefresh)
        {
            _nextErosionRefresh = Time.unscaledTime + 1f;
            RefreshErosion();
            RefreshRain();
        }

        if (!_fogDirty) return;
        _fogDirty = false;
        _fogTexture.SetPixelData(_fogData, 0);
        _fogTexture.Apply(false);
    }

    #endregion

    #region 영구 공개 API

    /// <summary>
    /// 지정 직사각형 영역을 영구적으로 공개합니다.
    /// 스탬프 배치(PlaceStartingRoom 등) 시 호출되며, 세이브/로드 후에도 유지됩니다.
    /// 맵 생성 중 호출될 때는 안개 텍스처가 아직 없으므로 시각 반영은 InitializeFog()에 위임됩니다.
    /// </summary>
    public void PermanentlyRevealArea(Vector2Int origin, Vector2Int size)
    {
        if (_permanentReveal == null) return;
        for (int x = origin.x; x < origin.x + size.x; x++)
        for (int y = origin.y; y < origin.y + size.y; y++)
        {
            if (!InBounds(x, y)) continue;
            _permanentReveal[x, y] = true;
            _explored[x, y] = true;
            WriteFogCell(x, y);
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

    /// <summary>지금 시야 안에 있는지 (직원·건물 시야 또는 영구 공개).</summary>
    public bool IsRevealed(int x, int y)
    {
        if (!InBounds(x, y)) return false;
        return _permanentReveal[x, y]
            || _empSightCount[x, y] > 0
            || _bldSightCount[x, y] > 0;
    }

    /// <summary>한 번이라도 시야에 들어온 적이 있는지. 시야를 벗어나도 유지됩니다.</summary>
    public bool IsExplored(int x, int y) => InBounds(x, y) && _explored[x, y];

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
        {
            _explored[x, y] = true;
            WriteFogCell(x, y);
        }
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
            WriteFogCell(x, y);
    }

    #endregion

    #region ISaveModule

    /// <summary>DayCycle(5), MapGenerator(10) 이후 / ZoneManager(25) 이전</summary>
    public int SaveOrder => 15;

    /// <summary>
    /// 영구 공개 영역과 탐색한 영역 비트마스크를 저장합니다.
    /// 카운터(직원/건물 시야)는 파생 상태이므로 저장하지 않습니다.
    /// </summary>
    public void Capture(SaveData data)
    {
        int total = GameMap.MAP_WIDTH * GameMap.MAP_HEIGHT;
        byte[] bitmask = new byte[(total + 7) / 8];
        byte[] explored = new byte[(total + 7) / 8];

        for (int i = 0; i < total; i++)
        {
            int x = i % GameMap.MAP_WIDTH;
            int y = i / GameMap.MAP_WIDTH;
            if (_permanentReveal != null && _permanentReveal[x, y])
                bitmask[i / 8] |= (byte)(1 << (i % 8));
            if (_explored != null && _explored[x, y])
                explored[i / 8] |= (byte)(1 << (i % 8));
        }

        data.fog = new FogSaveData { permanentRevealBitmask = bitmask, revealedBitmask = explored };
    }

    /// <summary>
    /// 영구 공개 영역을 복원합니다.
    /// 카운터(직원/건물 시야)는 건물·직원 Awake/Start에서 자동 누적됩니다.
    /// </summary>
    public void Restore(SaveData data)
    {
        _permanentReveal = new bool[GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT];
        _explored        = new bool[GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT];

        // 구 세이브에는 탐색 기록이 없다(null) — 영구 공개 영역만 탐색한 것으로 시작하고, 직원 시야가 다시 채운다
        ReadBitmask(data.fog?.permanentRevealBitmask, _permanentReveal);
        ReadBitmask(data.fog?.permanentRevealBitmask, _explored);
        ReadBitmask(data.fog?.revealedBitmask, _explored);

        Debug.Log("[FogOfWarManager] 영구 공개 영역 복원 완료");
    }

    private static void ReadBitmask(byte[] bitmask, bool[,] target)
    {
        if (bitmask == null) return;

        int total = GameMap.MAP_WIDTH * GameMap.MAP_HEIGHT;
        for (int i = 0; i < total; i++)
        {
            int byteIdx = i / 8;
            if (byteIdx >= bitmask.Length) break;
            if ((bitmask[byteIdx] & (1 << (i % 8))) != 0)
                target[i % GameMap.MAP_WIDTH, i / GameMap.MAP_WIDTH] = true;
        }
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
