using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방 단위 온도 시뮬레이션.
///
/// <b>모델</b> — 방마다 온도 값 하나. 매 틱 평형 온도로 지수 접근합니다.
/// <code>
///   평형 = 주변온도 + 열원출력 / 누출계수
///   T += (평형 - T) × (1 - exp(-누출계수 / 열용량 × Δt))
/// </code>
/// 지수형이라 틱 간격을 바꿔도 장기 결과가 같고 평형을 넘어 튀지 않습니다.
/// 누출계수가 0인 완전 밀폐 방은 평형이 없으므로 출력만큼 계속 오릅니다.
///
/// <b>방과 방 사이</b>는 벽으로 새지 않습니다. 오직 <b>문을 여닫는 순간</b>에만 공기가 섞입니다.
/// 벽을 통한 손실은 언제나 주변 온도(실외·지열) 쪽으로만 갑니다.
///
/// 비용은 <b>방 개수 × 상수</b>입니다. 타일 수와 무관합니다.
/// </summary>
public class TemperatureManager : DestroySingleton<TemperatureManager>
{
    #region 인스펙터

    [Header("설정")]
    [SerializeField] private TemperatureConfig config;

    [Tooltip("틱마다 방 개수와 대표 온도를 로그로 남깁니다.")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    #region 상태

    private readonly HashSet<IHeatSource> sources = new HashSet<IHeatSource>();

    /// <summary>이번 틱에 방별로 합산한 열 출력</summary>
    private readonly Dictionary<int, float> heatByRoom = new Dictionary<int, float>();

    /// <summary>열원 주변 방 투표용 버퍼</summary>
    private readonly Dictionary<int, int> neighborVotes = new Dictionary<int, int>();

    /// <summary>실외 온도에 더해지는 모디파이어 (한파·폭염 등)</summary>
    private readonly Dictionary<string, OutdoorModifier> outdoorModifiers
        = new Dictionary<string, OutdoorModifier>();

    /// <summary>만료된 모디파이어를 지울 때 쓰는 임시 목록</summary>
    private readonly List<string> expiredModifiers = new List<string>();

    private float tickTimer;
    private RoomManager subscribedRooms;

    #endregion

    #region 프로퍼티

    public TemperatureConfig Config => config;

    /// <summary>
    /// 지금의 실외 온도 = 기준값 + 모든 모디파이어 합.
    ///
    /// 한파·폭염은 <b>실외 온도만</b> 바꿉니다. 방은 벽을 통해 실외 쪽으로 새고 있으므로
    /// 실내는 자동으로 서서히 끌려갑니다 — 잘 막고 난방한 방일수록 덜 흔들립니다.
    /// </summary>
    public float OutdoorTemperature
    {
        get
        {
            float value = BaseOutdoorTemperature;
            foreach (var pair in outdoorModifiers)
                value += pair.Value.delta;
            return value;
        }
    }

    /// <summary>
    /// 모디파이어를 뺀 실외 기준 온도 = <b>계절 기준 온도 + 그 시각의 일교차</b>.
    ///
    /// 계절을 끄면(useSeasons=false) 설정의 고정값을 씁니다.
    /// DayCycle이 없으면 하루 주기를 계산할 수 없으므로 계절 기준값만 씁니다.
    /// </summary>
    public float BaseOutdoorTemperature
    {
        get
        {
            if (config == null) return 20f;
            if (!config.useSeasons) return config.outdoorTemperature;

            return config.GetSeasonTemperature(CurrentSeason) + DailyTemperatureOffset;
        }
    }

    /// <summary>지금 계절. 경과 일수에서 파생되므로 저장하지 않습니다.</summary>
    public Season CurrentSeason
    {
        get
        {
            int day = DayCycle.instance != null ? DayCycle.instance.Day : 1;
            int perSeason = config != null ? config.daysPerSeason : 15;
            return SeasonCalendar.GetSeason(day, perSeason);
        }
    }

    /// <summary>이번 계절이 며칠째인지</summary>
    public int DayInSeason
    {
        get
        {
            int day = DayCycle.instance != null ? DayCycle.instance.Day : 1;
            int perSeason = config != null ? config.daysPerSeason : 15;
            return SeasonCalendar.GetDayInSeason(day, perSeason);
        }
    }

    /// <summary>
    /// 하루 안에서의 기온 변동(℃).
    /// 새벽 3시가 최저, 오후 3시가 최고가 되도록 코사인 한 주기를 씁니다.
    /// </summary>
    public float DailyTemperatureOffset
    {
        get
        {
            if (config == null || DayCycle.instance == null) return 0f;
            if (config.dailyTemperatureAmplitude <= 0f) return 0f;

            // 15시(0.625)에서 +최대, 반 바퀴 떨어진 3시에서 -최대
            float phase = DayCycle.instance.TimeNormalized - 0.625f;
            return config.dailyTemperatureAmplitude * Mathf.Cos(phase * Mathf.PI * 2f);
        }
    }

    /// <summary>현재 계절·시각·온도 요약 (UI·디버그용)</summary>
    public string DescribeSeason()
    {
        if (config == null) return "설정 없음";
        if (!config.useSeasons) return $"계절 미사용 (고정 {config.outdoorTemperature:F1}도)";

        string season = SeasonCalendar.GetDisplayName(CurrentSeason);
        float seasonBase = config.GetSeasonTemperature(CurrentSeason);
        return $"{season} {DayInSeason}일차 · 계절 기준 {seasonBase:F1}도 · 일교차 {DailyTemperatureOffset:+0.0;-0.0}도";
    }

    /// <summary>현재 걸려 있는 실외 모디파이어 개수</summary>
    public int OutdoorModifierCount => outdoorModifiers.Count;

    /// <summary>등록된 열원 개수</summary>
    public int SourceCount => sources.Count;

    /// <summary>접촉면 전도율 합에 곱하는 전역 배율</summary>
    public float ConductanceScale => config != null ? Mathf.Max(0.0001f, config.conductanceScale) : 0.05f;

    #endregion

    #region 생명주기

    private void Start()
    {
        if (config == null)
            Debug.LogWarning("[TemperatureManager] TemperatureConfig가 연결되지 않았습니다. 기본값으로 동작합니다.");

        SubscribeToRooms();
        RefreshRoomThermalData();
    }

    private void OnDestroy()
    {
        if (subscribedRooms != null)
            subscribedRooms.OnRoomsRebuilt -= HandleRoomsRebuilt;
    }

    private void SubscribeToRooms()
    {
        if (RoomManager.instance == null || subscribedRooms == RoomManager.instance) return;

        if (subscribedRooms != null)
            subscribedRooms.OnRoomsRebuilt -= HandleRoomsRebuilt;

        subscribedRooms = RoomManager.instance;
        subscribedRooms.OnRoomsRebuilt += HandleRoomsRebuilt;
    }

    private void HandleRoomsRebuilt() => RefreshRoomThermalData();

    private void Update()
    {
        if (RoomManager.instance == null) return;
        if (subscribedRooms == null) { SubscribeToRooms(); RefreshRoomThermalData(); }

        float interval = config != null ? Mathf.Max(0.05f, config.tickInterval) : 1f;

        tickTimer += Time.deltaTime;
        if (tickTimer < interval) return;

        float delta = tickTimer;
        tickTimer = 0f;
        Tick(delta);
    }

    #endregion

    #region 열원 등록

    /// <summary>열원을 등록합니다. 건물이 완공되거나 다시 가동될 때 호출합니다.</summary>
    public void RegisterSource(IHeatSource source)
    {
        if (source == null) return;
        sources.Add(source);
    }

    /// <summary>열원 등록을 해제합니다.</summary>
    public void UnregisterSource(IHeatSource source)
    {
        if (source == null) return;
        sources.Remove(source);
    }

    #endregion

    #region 실외 온도 모디파이어

    /// <summary>
    /// 실외 온도 모디파이어. 한파는 음수, 폭염은 양수 delta를 씁니다.
    /// 정신력 모디파이어와 같은 방식으로 키 하나당 하나만 걸립니다(같은 키면 덮어씀).
    /// </summary>
    private class OutdoorModifier
    {
        public string displayName;
        public float delta;

        /// <summary>남은 시간(초). 0 이하면 무기한 — RemoveOutdoorModifier로만 사라집니다.</summary>
        public float remaining;
        public bool timed;
    }

    /// <summary>
    /// 실외 온도 모디파이어를 걸거나 갱신합니다.
    /// </summary>
    /// <param name="key">중복 방지 키 (같은 키면 덮어씁니다)</param>
    /// <param name="displayName">UI 표시용 이름 (예: 한파)</param>
    /// <param name="delta">실외 온도 변화량(℃). 한파는 음수.</param>
    /// <param name="duration">지속 시간(초). 0 이하면 무기한.</param>
    public void SetOutdoorModifier(string key, string displayName, float delta, float duration = 0f)
    {
        if (string.IsNullOrEmpty(key)) return;

        outdoorModifiers[key] = new OutdoorModifier
        {
            displayName = displayName,
            delta = delta,
            remaining = duration,
            timed = duration > 0f
        };

        if (showDebugLogs)
            Debug.Log($"[TemperatureManager] 실외 모디파이어 '{displayName}' {delta:+0.0;-0.0}도 → 실외 {OutdoorTemperature:F1}도");
    }

    /// <summary>실외 온도 모디파이어를 제거합니다.</summary>
    public void RemoveOutdoorModifier(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        outdoorModifiers.Remove(key);
    }

    /// <summary>걸려 있는 모디파이어를 모두 제거합니다.</summary>
    public void ClearOutdoorModifiers() => outdoorModifiers.Clear();

    /// <summary>모디파이어 요약 (UI·디버그용)</summary>
    public string DescribeOutdoorModifiers()
    {
        if (outdoorModifiers.Count == 0) return "없음";

        var parts = new List<string>();
        foreach (var pair in outdoorModifiers)
        {
            OutdoorModifier m = pair.Value;
            string time = m.timed ? $" ({m.remaining:F0}초 남음)" : "";
            parts.Add($"{m.displayName} {m.delta:+0.0;-0.0}도{time}");
        }
        return string.Join(", ", parts);
    }

    /// <summary>시간제 모디파이어의 남은 시간을 줄이고 만료된 것을 지웁니다.</summary>
    private void UpdateOutdoorModifiers(float deltaTime)
    {
        if (outdoorModifiers.Count == 0) return;

        expiredModifiers.Clear();

        foreach (var pair in outdoorModifiers)
        {
            OutdoorModifier m = pair.Value;
            if (!m.timed) continue;

            m.remaining -= deltaTime;
            if (m.remaining <= 0f) expiredModifiers.Add(pair.Key);
        }

        foreach (string key in expiredModifiers)
        {
            if (showDebugLogs)
                Debug.Log($"[TemperatureManager] 실외 모디파이어 '{outdoorModifiers[key].displayName}' 종료");
            outdoorModifiers.Remove(key);
        }
    }

    #endregion

    #region 방 열 특성

    /// <summary>
    /// 방마다 누출계수를 다시 구합니다. 방이 재계산될 때마다 호출됩니다.
    /// 접촉면 하나하나의 전도율을 더하므로, 벽이 넓게 맞닿을수록 빨리 식습니다.
    /// </summary>
    public void RefreshRoomThermalData()
    {
        var manager = RoomManager.instance;
        if (manager == null) return;

        float ambient = OutdoorTemperature;

        foreach (var pair in manager.Rooms)
        {
            Room room = pair.Value;

            float conductance = 0f;
            float environmentHeat = 0f;

            // 접촉면 한 번의 순회로 '얼마나 새는가'와 '벽이 얼마나 뜨거운가'를 함께 구한다
            foreach (var face in room.BoundaryFaces)
            {
                conductance    += GetCellConductivity(face);
                environmentHeat += GetCellHeatOutput(face);
            }

            room.LeakConductance = conductance * ConductanceScale;
            room.EnvironmentHeat = environmentHeat;

            // 새로 생긴 방(이어받을 값이 없던 방)은 주변 온도에서 시작한다
            if (!room.TemperatureInitialized)
            {
                room.Temperature = ambient;
                room.TemperatureInitialized = true;
            }
        }
    }

    /// <summary>
    /// 경계 칸 하나의 열 전도율.
    /// 차단 건물이 있으면 건물 값을, 없으면 지형 타일 값을 씁니다.
    /// </summary>
    private float GetCellConductivity(Vector2Int cell)
    {
        Building building = Building.GetBuildingAt(cell);
        if (building != null && building.buildingData != null)
            return Mathf.Max(0f, building.buildingData.heatConductivity);

        GameMap map = MapGenerator.instance != null ? MapGenerator.instance.GameMapInstance : null;
        if (map == null) return TileConductivity.DEFAULT;

        return TileConductivity.Get(map.TileGrid[cell.x, cell.y]);
    }

    /// <summary>
    /// 경계 칸 하나가 스스로 내는 열.
    /// 건물이 덮고 있으면 지형이 가려진 것으로 보고 0을 반환합니다 — 단열 벽으로 뜨거운 광맥을 덮는 대응이 성립합니다.
    /// </summary>
    private float GetCellHeatOutput(Vector2Int cell)
    {
        if (Building.GetBuildingAt(cell) != null) return TileHeatOutput.NONE;

        GameMap map = MapGenerator.instance != null ? MapGenerator.instance.GameMapInstance : null;
        if (map == null) return TileHeatOutput.NONE;

        return TileHeatOutput.Get(map.TileGrid[cell.x, cell.y]);
    }

    #endregion

    #region 틱

    private void Tick(float deltaTime)
    {
        var manager = RoomManager.instance;
        if (manager == null) return;

        UpdateOutdoorModifiers(deltaTime);

        float ambient = OutdoorTemperature;
        float capacityPerCell = config != null ? Mathf.Max(0.01f, config.heatCapacityPerCell) : 1f;
        float minT = config != null ? config.minTemperature : -60f;
        float maxT = config != null ? config.maxTemperature : 300f;

        // 1) 열원을 방별로 합산 — 실외에 있는 열원은 버린다(바깥은 데워지지 않는다)
        heatByRoom.Clear();
        foreach (var source in sources)
        {
            if (source == null || !source.IsHeatActive) continue;

            int roomId = ResolveHeatRoom(manager, source.HeatTilePosition, source.HeatFootprint);
            if (roomId == RoomManager.OUTDOOR_ID) continue;

            heatByRoom.TryGetValue(roomId, out float current);
            heatByRoom[roomId] = current + source.HeatOutput;
        }

        // 2) 방마다 평형으로 접근
        foreach (var pair in manager.Rooms)
        {
            Room room = pair.Value;

            heatByRoom.TryGetValue(room.Id, out float heat);
            heat += room.EnvironmentHeat;   // 뜨거운 벽이 내는 열

            float capacity = Mathf.Max(0.01f, room.CellCount * capacityPerCell);
            float leak = room.LeakConductance;

            if (leak <= 0f)
            {
                // 완전 밀폐 — 평형이 없다. 열원 출력만큼 계속 오르거나 내린다.
                room.Temperature += heat / capacity * deltaTime;
            }
            else
            {
                float equilibrium = ambient + heat / leak;
                float k = 1f - Mathf.Exp(-leak / capacity * deltaTime);
                room.Temperature += (equilibrium - room.Temperature) * k;
            }

            room.Temperature = Mathf.Clamp(room.Temperature, minT, maxT);
        }

        if (showDebugLogs && manager.RoomCount > 0)
            Debug.Log($"[TemperatureManager] 방 {manager.RoomCount}개 갱신 (열원 {sources.Count}개)");
    }

    /// <summary>
    /// 열원이 데울 방을 찾습니다.
    ///
    /// 난로처럼 <b>이동을 막는 건물은 자기가 선 칸이 벽</b>이 되어 어느 방에도 속하지 않습니다.
    /// 게다가 2×2 이상 건물은 <b>자기 풋프린트가 주변 8칸을 다 가려버리므로</b>,
    /// 한 칸만 보고 판단하면 아무 방도 못 찾습니다(다중 타일 냉난방기가 작동하지 않던 원인).
    /// 그래서 풋프린트 <b>바깥을 두르는 띠</b>를 훑어 가장 많이 맞닿은 방을 고릅니다.
    /// </summary>
    private int ResolveHeatRoom(RoomManager manager, Vector2Int origin, Vector2Int size)
    {
        int width = Mathf.Max(1, size.x);
        int height = Mathf.Max(1, size.y);

        // 통과형 건물이라 자기 칸이 그대로 방에 속하는 경우
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                int id = manager.GetRoomId(origin.x + x, origin.y + y);
                if (id != RoomManager.OUTDOOR_ID) return id;
            }

        // 풋프린트를 두르는 한 칸 띠를 투표
        neighborVotes.Clear();
        int bestRoom = RoomManager.OUTDOOR_ID;
        int bestVotes = 0;

        for (int x = -1; x <= width; x++)
        {
            for (int y = -1; y <= height; y++)
            {
                bool insideFootprint = x >= 0 && x < width && y >= 0 && y < height;
                if (insideFootprint) continue;

                int id = manager.GetRoomId(origin.x + x, origin.y + y);
                if (id == RoomManager.OUTDOOR_ID) continue;

                neighborVotes.TryGetValue(id, out int votes);
                votes++;
                neighborVotes[id] = votes;

                if (votes > bestVotes) { bestVotes = votes; bestRoom = id; }
            }
        }

        return bestRoom;
    }

    #endregion

    #region 문 혼합

    /// <summary>
    /// 문이 여닫히는 순간 좌우 공간의 공기를 섞습니다.
    ///
    /// 칸 수 가중 평균으로 목표를 구하고 그 쪽으로 <paramref name="exchangeRate"/>만큼만 당기므로,
    /// <b>작은 방일수록 크게 흔들리고 온도차가 클수록 변화가 큽니다.</b>
    /// 한쪽이 실외면 부피가 무한한 셈이라 방만 바깥 온도로 끌려가고 실외는 변하지 않습니다.
    /// </summary>
    public void MixThroughDoor(int roomIdA, int roomIdB, float exchangeRate)
    {
        var manager = RoomManager.instance;
        if (manager == null) return;
        if (roomIdA == roomIdB) return;

        exchangeRate = Mathf.Clamp01(exchangeRate);
        if (exchangeRate <= 0f) return;

        Room a = manager.GetRoomById(roomIdA);
        Room b = manager.GetRoomById(roomIdB);

        if (a == null && b == null) return;

        // 한쪽이 실외 — 무한 부피로 취급한다
        if (a == null) { PullToward(b, OutdoorTemperature, exchangeRate); return; }
        if (b == null) { PullToward(a, OutdoorTemperature, exchangeRate); return; }

        float totalVolume = a.CellCount + b.CellCount;
        if (totalVolume <= 0f) return;

        float mixed = (a.Temperature * a.CellCount + b.Temperature * b.CellCount) / totalVolume;

        PullToward(a, mixed, exchangeRate);
        PullToward(b, mixed, exchangeRate);
    }

    private void PullToward(Room room, float target, float rate)
    {
        if (room == null) return;
        room.Temperature += (target - room.Temperature) * rate;
    }

    #endregion

    #region 조회

    /// <summary>해당 칸의 온도. 실외이거나 고체면 주변 온도를 반환합니다.</summary>
    public float GetTemperatureAt(int x, int y)
    {
        Room room = RoomManager.instance != null ? RoomManager.instance.GetRoom(x, y) : null;
        return room != null ? room.Temperature : OutdoorTemperature;
    }

    /// <inheritdoc cref="GetTemperatureAt(int,int)"/>
    public float GetTemperatureAt(Vector2Int cell) => GetTemperatureAt(cell.x, cell.y);

    #endregion
}
