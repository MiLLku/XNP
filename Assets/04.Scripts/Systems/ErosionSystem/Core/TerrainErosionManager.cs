using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 고정 침식 발원지 관리자 — 발원지가 <b>자기 방의 침식 수치</b>를 올립니다.
///
/// <b>예전 구조와 다른 점</b>
/// 타일 단위 BFS/원형 전파와 워터마크(한 번 밟으면 그만) 방식을 버렸습니다.
/// 이제 침식은 방에 <b>고이고</b>, 직원은 머무는 동안 <b>계속</b> 침식됩니다.
/// 그래야 "오래 두면 방 전체가 죽는" 시한폭탄형 발원지가 성립합니다.
///
/// <b>규칙</b>
///   · 발원지는 매 틱 <c>ErosionPerSecond × Δt</c>만큼 자기 방의 침식을 올립니다.
///   · 방 침식이 발원지의 <c>SaturationLevel</c>에 닿으면 그 발원지는 <b>멈춥니다</b>
///     → 일반 발원지는 평형에서 안정됩니다.
///   · <c>SaturationLevel</c>이 0 이하면 한계가 없습니다 → 특수 발원지(시한폭탄).
///   · <b>실외에서는 아무 일도 일어나지 않습니다</b>. 바깥은 부피가 무한해 희석됩니다.
///     따라서 벽을 뚫어 환기하는 것이 오염을 지우는 수단이 됩니다.
///
/// 움직이는 개체(제놉스 오라 등)는 여기 등록하지 않습니다 — 타일 단위 레이어가 담당합니다.
///
/// 세이브: 방 침식은 방에서 재계산되는 파생 값이 아니라 상태이지만,
/// 방 번호가 실행마다 달라지므로 이 매니저가 아니라 방 저장 경로에서 다룹니다.
/// </summary>
public class TerrainErosionManager : DestroySingleton<TerrainErosionManager>
{
    #region 인스펙터

    [Header("설정")]
    [Tooltip("방 침식 갱신 주기(초)")]
    [SerializeField] private float tickInterval = 1f;

    [Tooltip("방 침식 상한")]
    [SerializeField] private float roomErosionMax = 200f;

    [SerializeField] private bool showDebugLogs = false;

    #endregion

    #region 상태

    private readonly HashSet<ITerrainErosionSource> sources = new HashSet<ITerrainErosionSource>();

    /// <summary>열원 해석과 같은 방식으로 쓰는 주변 방 투표 버퍼</summary>
    private readonly Dictionary<int, int> neighborVotes = new Dictionary<int, int>();

    /// <summary>실외 기본 침식에 더해지는 모디파이어 (이벤트로만 바뀝니다)</summary>
    private readonly Dictionary<string, OutdoorErosionModifier> outdoorModifiers
        = new Dictionary<string, OutdoorErosionModifier>();

    private readonly List<string> expiredModifiers = new List<string>();

    private float tickTimer;

    #endregion

    #region 프로퍼티

    /// <summary>등록된 고정 발원지 수</summary>
    public int SourceCount => sources.Count;

    /// <summary>방 침식 상한</summary>
    public float RoomErosionMax => roomErosionMax;

    /// <summary>
    /// 지금의 실외 침식 = 기본값 + 이벤트 모디파이어 합.
    ///
    /// 평상시에는 <b>고정</b>입니다. 실외 침식이 관여하는 곳:
    ///   · 실외에 서 있는 직원이 받는 노출
    ///   · 새로 밀폐된 공간의 시작값 (바깥 공기를 가둔 셈이므로)
    ///   · 벽을 통한 스며듦 — 벽 재질의 침식 투과율에 따라 방이 바깥 값 쪽으로 서서히 끌려간다
    ///   · 실외로 난 문을 여닫는 순간의 섞임
    /// </summary>
    public float OutdoorErosion
    {
        get
        {
            float value = BaseOutdoorErosion;
            foreach (var pair in outdoorModifiers)
                value += pair.Value.delta;
            return Mathf.Max(0f, value);
        }
    }

    /// <summary>모디파이어를 뺀 실외 기본 침식</summary>
    public float BaseOutdoorErosion
    {
        get
        {
            var config = ErosionManager.instance != null ? ErosionManager.instance.RecoveryConfig : null;
            return config != null ? config.outdoorErosionBase : 10f;
        }
    }

    #endregion

    #region 실외 침식 — 좌우 그라디언트

    /// <summary>
    /// 기지 중심에서 좌우로 멀어질수록 오르는 실외 침식.
    ///
    /// <b>깊이가 온도 축이라면 좌우는 침식 축입니다.</b> 지열(<c>TemperatureManager.GetAmbientAtDepth</c>)과
    /// 정확히 대칭인 구조로, 안전지대 밖으로 나갈수록 위험하고 보상이 큰 숲이 됩니다.
    ///
    /// 이벤트 모디파이어는 <b>거리와 무관하게 전체에 더해집니다</b> — 오염 폭풍은 기지 안팎을 가리지 않습니다.
    /// </summary>
    public float GetOutdoorErosionAt(float x)
    {
        var config = ErosionManager.instance != null ? ErosionManager.instance.RecoveryConfig : null;
        if (config == null || !config.useHorizontalGradient) return OutdoorErosion;

        float center = MapGenerator.instance != null
            ? MapGenerator.instance.StartingRoomX
            : GameMap.MAP_WIDTH * 0.5f;

        float distance = Mathf.Abs(x - center);
        float beyond = Mathf.Max(0f, distance - config.safeZoneRadius);

        float value = config.outdoorErosionBase + beyond * config.erosionPerTileFromBase;
        value = Mathf.Min(value, config.maxOutdoorErosion);

        // 이벤트 모디파이어는 거리와 무관하게 전체에 얹힌다
        foreach (var pair in outdoorModifiers)
            value += pair.Value.delta;

        return Mathf.Max(0f, value);
    }

    /// <summary>디버그용 — 기지에서 거리별 실외 침식 프로파일</summary>
    public string DescribeHorizontalGradient()
    {
        var config = ErosionManager.instance != null ? ErosionManager.instance.RecoveryConfig : null;
        if (config == null || !config.useHorizontalGradient) return "좌우 그라디언트 꺼짐";

        float center = MapGenerator.instance != null
            ? MapGenerator.instance.StartingRoomX
            : GameMap.MAP_WIDTH * 0.5f;

        var sb = new System.Text.StringBuilder($"기지 X {center:F0} · 안전지대 {config.safeZoneRadius:F0}칸 · ");
        foreach (int d in new[] { 0, 40, 80, 120, 150 })
            sb.Append($"{d}칸 {GetOutdoorErosionAt(center + d):F1} / ");
        return sb.ToString().TrimEnd('/', ' ');
    }

    #endregion

    #region 실외 침식 모디파이어

    /// <summary>실외 침식 모디파이어. 이벤트(오염 폭풍·정화 등)만 이 값을 건드립니다.</summary>
    private class OutdoorErosionModifier
    {
        public string displayName;
        public float delta;
        public float remaining;
        public bool timed;
    }

    /// <summary>실외 침식 모디파이어를 걸거나 갱신합니다.</summary>
    /// <param name="key">중복 방지 키 (같은 키면 덮어씁니다)</param>
    /// <param name="displayName">UI 표시용 이름</param>
    /// <param name="delta">실외 침식 변화량. 정화 이벤트는 음수.</param>
    /// <param name="duration">지속 시간(초). 0 이하면 무기한.</param>
    public void SetOutdoorErosionModifier(string key, string displayName, float delta, float duration = 0f)
    {
        if (string.IsNullOrEmpty(key)) return;

        outdoorModifiers[key] = new OutdoorErosionModifier
        {
            displayName = displayName,
            delta = delta,
            remaining = duration,
            timed = duration > 0f
        };

        if (showDebugLogs)
            Debug.Log($"[TerrainErosionManager] 실외 침식 '{displayName}' {delta:+0.0;-0.0} → {OutdoorErosion:F1}");
    }

    /// <summary>실외 침식 모디파이어를 제거합니다.</summary>
    public void RemoveOutdoorErosionModifier(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        outdoorModifiers.Remove(key);
    }

    /// <summary>걸려 있는 실외 침식 모디파이어 요약</summary>
    public string DescribeOutdoorModifiers()
    {
        if (outdoorModifiers.Count == 0) return "없음";

        var parts = new List<string>();
        foreach (var pair in outdoorModifiers)
        {
            var m = pair.Value;
            string time = m.timed ? $" ({m.remaining:F0}초 남음)" : "";
            parts.Add($"{m.displayName} {m.delta:+0.0;-0.0}{time}");
        }
        return string.Join(", ", parts);
    }

    private void UpdateOutdoorModifiers(float deltaTime)
    {
        if (outdoorModifiers.Count == 0) return;

        expiredModifiers.Clear();
        foreach (var pair in outdoorModifiers)
        {
            var m = pair.Value;
            if (!m.timed) continue;

            m.remaining -= deltaTime;
            if (m.remaining <= 0f) expiredModifiers.Add(pair.Key);
        }

        foreach (string key in expiredModifiers)
            outdoorModifiers.Remove(key);
    }

    #endregion

    #region 등록

    /// <summary>발원지를 등록합니다. TerrainErosionEmitter.OnEnable에서 호출됩니다.</summary>
    public void RegisterSource(ITerrainErosionSource source)
    {
        if (source == null) return;
        sources.Add(source);
    }

    /// <summary>발원지 등록을 해제합니다. 제거되면 방 침식은 더 이상 오르지 않습니다.</summary>
    public void UnregisterSource(ITerrainErosionSource source)
    {
        if (source == null) return;
        sources.Remove(source);
    }

    #endregion

    #region 지형 타일 침식

    /// <summary>방 재계산 메시지 구독 핸들</summary>
    private System.IDisposable roomsRebuiltSubscription;

    private void Start()
    {
        roomsRebuiltSubscription = GameMessageBus.Subscribe<RoomsRebuiltMessage>(_ => RefreshTerrainErosion());
        RefreshTerrainErosion();
    }

    private void OnDestroy()
    {
        roomsRebuiltSubscription?.Dispose();
        roomsRebuiltSubscription = null;
    }

    /// <summary>
    /// 방마다 경계 타일이 뿜는 침식을 다시 구합니다.
    ///
    /// 온도의 <c>TemperatureManager.RefreshRoomThermalData</c>와 같은 방식입니다 —
    /// 접촉면을 훑어 합산하므로 <b>등록·해제가 필요 없고</b>, 캐내거나 벽으로 덮으면
    /// 다음 재계산에 자동 반영됩니다. 한계치는 접촉한 타일들 중 가장 높은 값을 씁니다.
    /// </summary>
    public void RefreshTerrainErosion()
    {
        var manager = RoomManager.instance;
        if (manager == null) return;

        GameMap map = MapGenerator.instance != null ? MapGenerator.instance.GameMapInstance : null;
        if (map == null) return;

        foreach (var pair in manager.Rooms)
        {
            Room room = pair.Value;

            float rate = 0f;
            float saturation = 0f;
            bool unbounded = false;
            float permeability = 0f;

            foreach (var face in room.BoundaryFaces)
            {
                // 건물이 덮은 칸은 지형이 가려진 것으로 본다 — 벽으로 오염원을 봉하는 대응이 성립한다.
                // 투과율도 건물 값을 쓴다(좋은 벽 재료로 방을 지킨다).
                Building building = Building.GetBuildingAt(face);
                if (building != null)
                {
                    if (building.buildingData != null) permeability += Mathf.Max(0f, building.buildingData.erosionPermeability);
                    continue;
                }

                // 스탬프로 놓인 바닥 타일은 건물 목록에 없다 — 바닥 타일 쪽에서 읽는다
                FloorTile floor = FloorTile.GetFloorTileAt(face);
                if (floor != null)
                {
                    permeability += floor.ErosionPermeability;
                    continue;
                }

                int tileId = map.TileGrid[face.x, face.y];
                var tileDef = TileDefinitionLookup.Find((TileType)tileId);
                permeability += tileDef != null ? Mathf.Max(0f, tileDef.erosionPermeability) : 1f;
                float output = TileErosionOutput.Get(tileId);
                if (output <= 0f) continue;

                rate += output;

                float sat = TileErosionOutput.GetSaturation(tileId);
                if (sat <= 0f) unbounded = true;
                else if (sat > saturation) saturation = sat;
            }

            // 부피로 나눈다 — 침식은 총량이 아니라 농도다.
            // 안 나누면 접촉면이 많은 거대 공동이 그냥 제일 위험해진다(초당 77까지 나왔다).
            // 발열이 열용량으로 나누는 것과 같은 자리다.
            room.TerrainErosionRate = room.CellCount > 0 ? rate / room.CellCount : 0f;
            room.TerrainErosionSaturation = unbounded ? 0f : saturation;
            room.ErosionLeak = room.CellCount > 0 ? permeability / room.CellCount : 0f;
        }
    }

    #endregion

    #region 틱

    private void Update()
    {
        if (RoomManager.instance == null) return;

        tickTimer += Time.deltaTime;
        if (tickTimer < Mathf.Max(0.1f, tickInterval)) return;

        float delta = tickTimer;
        tickTimer = 0f;
        Tick(delta);
    }

    private void Tick(float deltaTime)
    {
        UpdateOutdoorModifiers(deltaTime);

        RoomManager manager = RoomManager.instance;
        if (manager == null) return;

        foreach (var source in sources)
        {
            if (source == null || !source.IsActive) continue;
            if (source.ErosionPerSecond <= 0f) continue;

            Room room = ResolveRoom(manager, source.TilePosition);
            if (room == null) continue;   // 실외 — 고이지 않는다

            // 포화에 닿은 일반 발원지는 멈춘다. 0 이하는 한계 없음(특수 발원지).
            bool unbounded = source.SaturationLevel <= 0f;
            if (!unbounded && room.Erosion >= source.SaturationLevel) continue;

            float added = source.ErosionPerSecond * deltaTime;
            float cap = unbounded ? roomErosionMax : Mathf.Min(source.SaturationLevel, roomErosionMax);

            room.Erosion = Mathf.Min(cap, room.Erosion + added);
        }

        // 지형 타일이 뿜는 침식 — 발원지 개체와 같은 규칙(포화에서 멈춤, 0이면 무한)
        foreach (var pair in manager.Rooms)
        {
            Room room = pair.Value;
            if (room.TerrainErosionRate <= 0f) continue;

            bool unbounded = room.TerrainErosionSaturation <= 0f;
            if (!unbounded && room.Erosion >= room.TerrainErosionSaturation) continue;

            float cap = unbounded ? roomErosionMax : Mathf.Min(room.TerrainErosionSaturation, roomErosionMax);
            room.Erosion = Mathf.Min(cap, room.Erosion + room.TerrainErosionRate * deltaTime);
        }

        // 벽을 통한 스며듦 — 방이 그 자리의 바깥 침식 쪽으로 서서히 끌려간다 (오를 수도, 빠질 수도 있다)
        var recovery = ErosionManager.instance != null ? ErosionManager.instance.RecoveryConfig : null;
        float seepRate = recovery != null ? recovery.wallSeepRate : 0f;
        if (seepRate > 0f)
        {
            foreach (var pair in manager.Rooms)
            {
                Room room = pair.Value;
                if (room.ErosionLeak <= 0f) continue;

                float pull = 1f - Mathf.Exp(-room.ErosionLeak * seepRate * deltaTime);
                room.Erosion = Mathf.Clamp(room.Erosion + (GetAmbientErosionFor(room) - room.Erosion) * pull, 0f, roomErosionMax);
            }
        }

        if (showDebugLogs && sources.Count > 0)
            Debug.Log($"[TerrainErosionManager] 발원지 {sources.Count}개 갱신");
    }

    /// <summary>
    /// 그 방 자리의 '바깥' 침식 — 그 X 위치의 실외 침식과 그 자리 지층 침식 중 큰 쪽.
    /// 벽으로 스며드는 목표치이자, 실외로 난 문이 섞이는 상대값입니다.
    /// </summary>
    public float GetAmbientErosionFor(Room room)
    {
        if (room == null) return OutdoorErosion;

        float strata = MapGenerator.instance != null
            ? MapGenerator.instance.StrataErosionAt(Mathf.RoundToInt(room.AverageX), Mathf.RoundToInt(room.AverageY))
            : 0f;
        return Mathf.Max(GetOutdoorErosionAt(room.AverageX), strata);
    }

    /// <summary>
    /// 문이 여닫히는 순간 좌우 공간의 침식을 섞습니다 (온도의 <c>TemperatureManager.MixThroughDoor</c>와 같은 규칙).
    ///
    /// 칸 수 가중 평균 쪽으로 <paramref name="exchangeRate"/>만큼만 당기므로 작은 방일수록 크게 흔들립니다.
    /// 한쪽이 실외면 방만 바깥 값으로 끌려갑니다 — 이중문을 두면 중간 방이 완충합니다.
    /// </summary>
    public void MixThroughDoor(int roomIdA, int roomIdB, float exchangeRate)
    {
        var manager = RoomManager.instance;
        if (manager == null || roomIdA == roomIdB) return;

        exchangeRate = Mathf.Clamp01(exchangeRate);
        if (exchangeRate <= 0f) return;

        Room a = manager.GetRoomById(roomIdA);
        Room b = manager.GetRoomById(roomIdB);
        if (a == null && b == null) return;

        if (a == null) { b.Erosion += (GetAmbientErosionFor(b) - b.Erosion) * exchangeRate; return; }
        if (b == null) { a.Erosion += (GetAmbientErosionFor(a) - a.Erosion) * exchangeRate; return; }

        float mixed = (a.Erosion * a.CellCount + b.Erosion * b.CellCount) / (a.CellCount + b.CellCount);
        a.Erosion += (mixed - a.Erosion) * exchangeRate;
        b.Erosion += (mixed - b.Erosion) * exchangeRate;
    }

    /// <summary>
    /// 발원지가 오염시킬 방을 찾습니다.
    ///
    /// 식물처럼 <b>이동을 막는 개체는 자기가 선 칸이 벽</b>이 되어 어느 방에도 속하지 않으므로,
    /// 자기 칸에 방이 없으면 둘러싼 칸을 투표해 가장 많이 맞닿은 방을 고릅니다.
    /// (온도의 열원 해석과 같은 규칙입니다)
    /// </summary>
    private Room ResolveRoom(RoomManager manager, Vector2Int tile)
    {
        Room direct = manager.GetRoom(tile);
        if (direct != null) return direct;

        neighborVotes.Clear();
        int bestRoom = RoomManager.OUTDOOR_ID;
        int bestVotes = 0;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;

                int id = manager.GetRoomId(tile.x + dx, tile.y + dy);
                if (id == RoomManager.OUTDOOR_ID) continue;

                neighborVotes.TryGetValue(id, out int votes);
                votes++;
                neighborVotes[id] = votes;

                if (votes > bestVotes) { bestVotes = votes; bestRoom = id; }
            }
        }

        return bestRoom == RoomManager.OUTDOOR_ID ? null : manager.GetRoomById(bestRoom);
    }

    #endregion

    #region 조회

    /// <summary>
    /// 해당 칸의 환경 침식 수치.
    /// 방 안이면 그 방의 침식, <b>실외면 그 X 위치의 실외 침식과 그 칸 지층 침식 중 큰 쪽</b>입니다.
    /// 지표와 뚫린 갱도라도 침식 동굴로 파고들면 그 층만큼 더러워집니다(경계에선 서서히).
    ///
    /// 환경 노출을 읽는 곳이 여기 하나뿐이라(<c>EmployeeErosionController.UpdateAmbientErosion</c>),
    /// 좌우 그라디언트는 이 한 줄만 바꾸면 전체에 적용됩니다.
    /// </summary>
    public float GetRoomErosionAt(int x, int y)
    {
        Room room = RoomManager.instance != null ? RoomManager.instance.GetRoom(x, y) : null;
        if (room != null) return room.Erosion;

        float strata = MapGenerator.instance != null ? MapGenerator.instance.StrataErosionAt(x, y) : 0f;
        return Mathf.Max(GetOutdoorErosionAt(x), strata);
    }

    /// <summary>
    /// 환풍 — <paramref name="from"/> 칸의 공간에서 <paramref name="to"/> 칸의 공간으로 공기 <paramref name="airCells"/>칸 분량을 옮깁니다.
    ///
    /// 옮겨지는 침식 총량은 <c>보내는 쪽 농도 × 공기량</c>이고, 각 방의 수치는 그 총량을 <b>자기 칸 수로 나눈 만큼</b> 변합니다.
    /// 그래서 넓은 방에서 좁은 방으로 밀어 넣으면 받는 쪽이 급격히 치솟습니다(의도된 동작 — 상한은 roomErosionMax뿐).
    ///   · 받는 쪽이 실외면 침식은 사라집니다. 실외보다 깨끗하게도 만들 수 있습니다.
    ///   · 보내는 쪽이 실외면 그 자리 실외 침식을 방 안으로 끌어들입니다(실외는 줄지 않습니다).
    /// </summary>
    public void PumpErosion(Vector2Int from, Vector2Int to, float airCells)
    {
        var manager = RoomManager.instance;
        if (manager == null || airCells <= 0f) return;

        Room source = manager.GetRoom(from);
        Room target = manager.GetRoom(to);
        if (source == target) return; // 같은 방이거나 둘 다 실외

        float concentration = source != null ? source.Erosion : GetRoomErosionAt(from.x, from.y);
        float amount = concentration * airCells; // 침식 × 칸

        if (source != null)
        {
            amount = Mathf.Min(amount, source.Erosion * source.CellCount);
            source.Erosion = Mathf.Max(0f, source.Erosion - amount / source.CellCount);
        }

        if (target != null)
            target.Erosion = Mathf.Min(roomErosionMax, target.Erosion + amount / target.CellCount);
    }

    /// <inheritdoc cref="GetRoomErosionAt(int,int)"/>
    public float GetRoomErosionAt(Vector2Int cell) => GetRoomErosionAt(cell.x, cell.y);

    /// <summary>해당 방에서 아직 활동 중인(포화에 닿지 않은) 발원지 수</summary>
    public int CountActiveSourcesIn(Room room)
    {
        if (room == null) return 0;

        RoomManager manager = RoomManager.instance;
        if (manager == null) return 0;

        int count = 0;
        foreach (var source in sources)
        {
            if (source == null || !source.IsActive) continue;
            if (ResolveRoom(manager, source.TilePosition) != room) continue;
            if (source.SaturationLevel > 0f && room.Erosion >= source.SaturationLevel) continue;

            count++;
        }
        return count;
    }

    #endregion
}
