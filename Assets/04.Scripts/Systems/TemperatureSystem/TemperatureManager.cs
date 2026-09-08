using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방 단위 온도 시뮬레이션.
///
/// <b>모델</b> — 방마다 온도 값 하나. 매 틱 평형 온도로 지수 접근합니다.
/// <code>
///   G   = 누출계수 + 비례 구간 열원의 전도율 합
///   평형 = (누출계수 × 주변온도 + Σ(g × 목표온도) + Σ전출력) / G
///   T  += (평형 - T) × (1 - exp(-G / 열용량 × Δt))
/// </code>
/// 지수형이라 틱 간격을 바꿔도 장기 결과가 같고 평형을 넘어 튀지 않습니다.
///
/// <b>열원의 목표 온도는 해법 안으로 접어 넣습니다.</b> 출력만 미리 깎아서 넣으면
/// 뻣뻣한 항을 명시적으로 푸는 꼴이라 목표 근처에서 톱니처럼 진동합니다
/// (2×2 밀폐 방 + 40W 열원이면 한 틱에 10도씩 넘나든다). 목표 근처의 열원을
/// "목표 온도의 저장소에 전도율 g로 붙은 것"으로 보고 G와 평형에 함께 넣으면 무조건 안정합니다.
/// 모든 열원이 목표에서 멀면(전출력) G = 누출계수가 되어 예전 공식과 정확히 같아집니다.
///
/// <b>주변 온도는 깊이의 함수</b>입니다 — 깊을수록 덥고, 깊을수록 계절·한파·폭염을 덜 탑니다.
/// 누출계수가 0인 완전 밀폐 방이라도 목표 온도를 가진 열원이 있으면 그 목표에서 멈춥니다.
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

    /// <summary>이번 틱에 방별로 합산한 열원 기여 (목표 온도 상태별로 분류된 것)</summary>
    private readonly Dictionary<int, HeatAccum> heatByRoom = new Dictionary<int, HeatAccum>();

    /// <summary>이번 틱에 각 열원이 데우기로 결정된 방. 냉난방기가 자기 방을 되찾을 때 씁니다.</summary>
    private readonly Dictionary<IHeatSource, int> resolvedRoomBySource = new Dictionary<IHeatSource, int>();

    /// <summary>지표 기준 Y 캐시. MapGenerator가 있으면 그쪽 값을 우선합니다.</summary>
    private float? surfaceYCache;

    /// <summary>열원 주변 방 투표용 버퍼</summary>
    private readonly Dictionary<int, int> neighborVotes = new Dictionary<int, int>();

    /// <summary>실외 온도에 더해지는 모디파이어 (한파·폭염 등)</summary>
    private readonly Dictionary<string, OutdoorModifier> outdoorModifiers
        = new Dictionary<string, OutdoorModifier>();

    /// <summary>만료된 모디파이어를 지울 때 쓰는 임시 목록</summary>
    private readonly List<string> expiredModifiers = new List<string>();

    private float tickTimer;
    /// <summary>방 재계산 메시지 구독 핸들</summary>
    private IDisposable roomsRebuiltSubscription;

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

    /// <summary>등록된 열원들 (디버그 표시용)</summary>
    public IEnumerable<IHeatSource> Sources => sources;

    /// <summary>접촉면 전도율 합에 곱하는 전역 배율</summary>
    public float ConductanceScale => config != null ? Mathf.Max(0.0001f, config.conductanceScale) : 0.05f;

    #endregion

    #region 깊이 · 지열

    /// <summary>
    /// 깊이 0으로 삼는 지표 Y. MapGenerator가 있으면 그 값을, 없으면 설정값을 씁니다.
    /// 지형 파라미터를 바꿔도 지열 곡선이 따라오도록 코드 쪽을 우선합니다.
    /// </summary>
    public float SurfaceReferenceY
    {
        get
        {
            if (surfaceYCache.HasValue) return surfaceYCache.Value;

            float value = MapGenerator.instance != null
                ? MapGenerator.instance.SurfaceReferenceY
                : (config != null ? config.surfaceReferenceY : 145f);

            surfaceYCache = value;
            return value;
        }
    }

    /// <summary>지표면 기준 깊이(칸). 지표보다 위면 0입니다.</summary>
    public float GetDepth(float y) => Mathf.Max(0f, SurfaceReferenceY - y);

    /// <summary>
    /// 깊이에 따른 주변 온도(℃).
    ///
    /// <code>
    ///   주변온도 = 연평균 + 기울기 × 깊이 + (지표 실외온도 - 연평균) × exp(-깊이 / 감쇠깊이)
    /// </code>
    ///
    /// 두 번째 항이 <b>깊을수록 덥다</b>를, 세 번째 항이 <b>깊을수록 계절·날씨를 안 탄다</b>를 만듭니다.
    /// 깊이 0에서는 감쇠가 1이라 결과가 실외 온도와 <b>정확히 같습니다</b> — 지표는 예전 그대로 동작합니다.
    /// 한파·폭염 모디파이어도 실외 온도에 들어 있으므로 자동으로 같이 감쇠합니다.
    /// </summary>
    public float GetAmbientAtDepth(float depth) => GetAmbientAtDepth(depth, OutdoorTemperature);

    /// <summary>실외 온도를 미리 구해 둔 경우용 (틱 루프에서 모디파이어 순회를 반복하지 않기 위함)</summary>
    private float GetAmbientAtDepth(float depth, float outdoor)
    {
        if (config == null || !config.useGeothermal) return outdoor;

        float mean = config.annualMeanTemperature;
        float damp = Mathf.Exp(-depth / Mathf.Max(1f, config.seasonDampDepth));

        return mean + config.geothermalGradient * depth + (outdoor - mean) * damp;
    }

    /// <summary>해당 높이의 주변 온도. 방에 속하지 않은 칸을 조회할 때 씁니다.</summary>
    public float GetAmbientAt(float y) => GetAmbientAtDepth(GetDepth(y));

    /// <summary>방의 평균 깊이에 해당하는 주변 온도. 열원이 없다면 이 방이 수렴할 온도입니다.</summary>
    public float GetAmbientForRoom(Room room)
        => room == null ? OutdoorTemperature : GetAmbientForRoom(room, OutdoorTemperature);

    private float GetAmbientForRoom(Room room, float outdoor)
    {
        if (room == null || room.CellCount == 0) return outdoor;
        return GetAmbientAtDepth(GetDepth(room.AverageY), outdoor);
    }

    #endregion

    #region 생명주기

    private void Start()
    {
        if (config == null)
            Debug.LogWarning("[TemperatureManager] TemperatureConfig가 연결되지 않았습니다. 기본값으로 동작합니다.");

        roomsRebuiltSubscription = GameMessageBus.Subscribe<RoomsRebuiltMessage>(_ => HandleRoomsRebuilt());
        RefreshRoomThermalData();
    }

    private void OnDestroy()
    {
        roomsRebuiltSubscription?.Dispose();
        roomsRebuiltSubscription = null;
    }

    private void HandleRoomsRebuilt() => RefreshRoomThermalData();

    private void Update()
    {
        if (RoomManager.instance == null) return;

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

        surfaceYCache = null;   // 지형이 바뀌었을 수 있으니 지표 기준을 다시 잡는다
        float outdoor = OutdoorTemperature;

        foreach (var pair in manager.Rooms)
        {
            Room room = pair.Value;

            float conductance = 0f;
            room.EnvironmentHeat.Clear();

            // 접촉면 한 번의 순회로 '얼마나 새는가'와 '벽이 얼마나 뜨거운가'를 함께 구한다
            foreach (var face in room.BoundaryFaces)
            {
                ReadFace(face, out float conductivity, out float heat, out bool limited, out float target);

                conductance += conductivity;
                if (heat != 0f) AddEnvironmentHeat(room, heat, limited, target);
            }

            room.LeakConductance = conductance * ConductanceScale;
            room.AmbientTemperature = GetAmbientForRoom(room, outdoor);

            // 새로 생긴 방(이어받을 값이 없던 방)은 주변 온도에서 시작한다.
            // 실제로는 RoomManager.InheritState가 먼저 값을 채우므로 여기까지 오는 경우는 드물다.
            if (!room.TemperatureInitialized)
            {
                room.Temperature = room.AmbientTemperature;
                room.TemperatureInitialized = true;
            }
        }
    }

    /// <summary>
    /// 경계 칸 하나의 열 특성을 <b>한 번의 조회</b>로 모두 읽습니다.
    ///
    /// 차단 건물이 있으면 건물 값을 쓰고 지형 발열은 가려진 것으로 봅니다 —
    /// 단열 벽으로 뜨거운 광맥을 덮는 대응이 여기서 성립합니다.
    /// </summary>
    private void ReadFace(Vector2Int cell, out float conductivity, out float heat, out bool limited, out float target)
    {
        heat = 0f;
        limited = false;
        target = 0f;

        Building building = Building.GetBuildingAt(cell);
        if (building != null && building.buildingData != null)
        {
            conductivity = Mathf.Max(0f, building.buildingData.heatConductivity);
            return;
        }

        GameMap map = MapGenerator.instance != null ? MapGenerator.instance.GameMapInstance : null;
        if (map == null)
        {
            conductivity = TileConductivity.DEFAULT;
            return;
        }

        TileDefinition def = TileDefinitionLookup.Find(map.TileGrid[cell.x, cell.y]);
        if (def == null)
        {
            conductivity = TileConductivity.DEFAULT;
            return;
        }

        conductivity = def.thermalConductivity;
        heat = def.heatOutput;
        limited = def.useHeatTarget;
        target = def.heatTargetTemperature;
    }

    /// <summary>
    /// 접촉면의 발열을 방의 목표 온도별 묶음에 더합니다.
    /// 목표가 거의 같으면(0.01도 이내) 같은 묶음으로 합칩니다 — 부동소수 동등 비교를 피하기 위함입니다.
    /// </summary>
    private static void AddEnvironmentHeat(Room room, float heat, bool limited, float target)
    {
        var buckets = room.EnvironmentHeat;

        for (int i = 0; i < buckets.Count; i++)
        {
            if (buckets[i].limited != limited) continue;
            if (limited && Mathf.Abs(buckets[i].target - target) > 0.01f) continue;

            var merged = buckets[i];
            merged.output += heat;
            buckets[i] = merged;
            return;
        }

        buckets.Add(new EnvironmentHeatBucket { output = heat, limited = limited, target = target });
    }

    #endregion

    #region 틱

    private void Tick(float deltaTime)
    {
        var manager = RoomManager.instance;
        if (manager == null) return;

        UpdateOutdoorModifiers(deltaTime);

        float outdoor = OutdoorTemperature;
        float capacityPerCell = config != null ? Mathf.Max(0.01f, config.heatCapacityPerCell) : 1f;
        float minT = config != null ? config.minTemperature : -60f;
        float maxT = config != null ? config.maxTemperature : 300f;
        float band = config != null ? Mathf.Max(0.1f, config.heatTargetBand) : 3f;
        bool buildingTargets = config == null || config.buildingsUseHeatTarget;

        // 1) 열원을 방별로 합산 — 실외에 있는 열원은 버린다(바깥은 데워지지 않는다)
        heatByRoom.Clear();
        resolvedRoomBySource.Clear();

        foreach (var source in sources)
        {
            if (source == null) continue;

            int roomId = ResolveHeatRoom(manager, source.HeatTilePosition, source.HeatFootprint);
            resolvedRoomBySource[source] = roomId;   // 꺼져 있어도 기록한다 — 냉난방기 UI가 자기 방을 찾는 데 쓴다

            if (!source.IsHeatActive) continue;
            if (roomId == RoomManager.OUTDOOR_ID) continue;

            Room room = manager.GetRoomById(roomId);
            if (room == null) continue;

            // 목표 온도를 쓰지 않으면 무한대를 목표로 삼는다 → 항상 전출력 = 예전 동작과 완전히 동일
            bool limited = source.HasHeatTarget && (buildingTargets || !(source is Building));
            float target = limited
                ? source.HeatTargetTemperature
                : (source.HeatOutput > 0f ? float.PositiveInfinity : float.NegativeInfinity);

            HeatAccum acc = heatByRoom.TryGetValue(roomId, out var existing) ? existing : HeatAccum.Empty;
            Accumulate(ref acc, source.HeatOutput, target, band, room.Temperature, source.IsClimateControl);
            heatByRoom[roomId] = acc;
        }

        // 2) 방마다 평형으로 접근
        foreach (var pair in manager.Rooms)
        {
            Room room = pair.Value;

            HeatAccum acc = heatByRoom.TryGetValue(room.Id, out var found) ? found : HeatAccum.Empty;

            // 뜨거운 벽이 내는 열 — 이건 능동 공조기가 아니므로 자연 평형에도 들어간다
            foreach (var bucket in room.EnvironmentHeat)
            {
                float bucketTarget = bucket.limited
                    ? bucket.target
                    : (bucket.output > 0f ? float.PositiveInfinity : float.NegativeInfinity);

                Accumulate(ref acc, bucket.output, bucketTarget, band, room.Temperature, false);
            }

            float ambient = GetAmbientForRoom(room, outdoor);
            room.AmbientTemperature = ambient;

            float capacity = Mathf.Max(0.01f, room.CellCount * capacityPerCell);
            float leak = room.LeakConductance;
            float before = room.Temperature;

            // 냉난방기를 뺀 평형 — 공조기의 부하이자 전력 소모의 기준
            room.NaturalEquilibrium = leak + acc.passiveConductance > 0f
                ? (leak * ambient + acc.passiveDrive + acc.passiveSaturated) / (leak + acc.passiveConductance)
                : before;

            float totalConductance = leak + acc.conductance;

            if (totalConductance <= 0f)
            {
                // 완전 밀폐 + 목표 온도를 쓰지 않는 열원뿐 — 평형이 없다. 출력만큼 계속 오르거나 내린다.
                room.Temperature += acc.saturated / capacity * deltaTime;
            }
            else
            {
                float equilibrium = (leak * ambient + acc.drive + acc.saturated) / totalConductance;
                float k = 1f - Mathf.Exp(-totalConductance / capacity * deltaTime);
                room.Temperature += (equilibrium - room.Temperature) * k;
            }

            ApplyTargetGuard(room, acc, ambient, before);

            room.Temperature = Mathf.Clamp(room.Temperature, minT, maxT);
        }

        if (showDebugLogs && manager.RoomCount > 0)
            Debug.Log($"[TemperatureManager] 방 {manager.RoomCount}개 갱신 (열원 {sources.Count}개)");
    }

    /// <summary>
    /// 열원 하나를 세 상태 중 하나로 분류해 누적합니다.
    ///
    /// <list type="bullet">
    /// <item><b>정지</b> — 이미 목표를 넘었다. 아무것도 하지 않는다.</item>
    /// <item><b>비례</b> — 목표에서 band 이내. "목표 온도의 저장소에 전도율 g = |출력|/band로 붙은 것"으로 다룬다.</item>
    /// <item><b>전출력</b> — 목표에서 멀다. 정격 출력을 그대로 넣는다(예전 동작과 동일).</item>
    /// </list>
    ///
    /// 비례 구간을 전도율로 다루는 것이 핵심입니다. 출력만 깎아서 넣으면 명시적 적분이 되어
    /// 목표 근처에서 톱니처럼 진동하지만, G와 평형에 함께 넣으면 무조건 안정합니다.
    /// </summary>
    private static void Accumulate(ref HeatAccum acc, float output, float target, float band,
                                   float currentTemperature, bool isClimateControl)
    {
        if (output == 0f) return;

        // 목표까지 남은 거리 — 난방은 위로, 냉방은 아래로 잰다
        float headroom = output > 0f ? target - currentTemperature : currentTemperature - target;

        if (headroom <= 0f) return;                        // 정지

        if (headroom >= band)
        {
            acc.saturated += output;                       // 전출력
            if (!isClimateControl) acc.passiveSaturated += output;
        }
        else
        {
            float g = Mathf.Abs(output) / band;            // 비례
            acc.conductance += g;
            acc.drive += g * target;

            if (!isClimateControl)
            {
                acc.passiveConductance += g;
                acc.passiveDrive += g * target;
            }
        }

        // 하드 천장/바닥 — 전출력 구간의 단일 틱 오버슛까지 막는다
        if (output > 0f) acc.ceiling = Mathf.Max(acc.ceiling, target);
        else             acc.floor   = Mathf.Min(acc.floor, target);
    }

    /// <summary>
    /// 열원이 자기 목표를 넘겨버리는 것을 막습니다.
    ///
    /// 지수 해법은 평형을 넘지 않지만, 전출력 구간에서는 평형 자체가 목표보다 위일 수 있습니다.
    /// <paramref name="before"/>로 게이트하므로 <b>지열이 정당하게 올려놓은 방은 건드리지 않습니다</b> —
    /// 깊이 100의 52도 방에 목표 24도 온열기를 놓아도 방이 24도로 끌려 내려가지 않습니다.
    /// </summary>
    private static void ApplyTargetGuard(Room room, HeatAccum acc, float ambient, float before)
    {
        float roof = Mathf.Max(ambient, acc.ceiling);
        if (before <= roof && room.Temperature > roof) room.Temperature = roof;

        float basin = Mathf.Min(ambient, acc.floor);
        if (before >= basin && room.Temperature < basin) room.Temperature = basin;
    }

    /// <summary>
    /// 한 방에 모인 열원 기여. passive* 는 능동 공조기를 뺀 값으로,
    /// <see cref="Room.NaturalEquilibrium"/>(= 공조기의 부하 기준)을 구하는 데 씁니다.
    /// </summary>
    private struct HeatAccum
    {
        public float conductance;        // Σ g   (비례 구간)
        public float drive;              // Σ g × 목표온도
        public float saturated;          // Σ 출력 (전출력 구간)

        public float passiveConductance;
        public float passiveDrive;
        public float passiveSaturated;

        public float ceiling;            // 난방 열원이 넘길 수 없는 상한
        public float floor;              // 냉방 열원이 내릴 수 없는 하한

        public static HeatAccum Empty => new HeatAccum
        {
            ceiling = float.NegativeInfinity,
            floor = float.PositiveInfinity
        };
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

        // 한쪽이 실외 — 무한 부피로 취급한다.
        // 실외 온도는 살아있는 쪽 방의 깊이를 따른다(문은 그 방에 붙어 있으므로 깊이가 같다).
        if (a == null) { PullToward(b, GetAmbientForRoom(b), exchangeRate); return; }
        if (b == null) { PullToward(a, GetAmbientForRoom(a), exchangeRate); return; }

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

    /// <summary>
    /// 해당 칸의 온도. 실외이거나 고체면 그 <b>높이의 주변 온도</b>를 반환합니다.
    ///
    /// 즉 하늘까지 뚫은 수직 갱도라도 깊은 곳은 여전히 덥습니다 — 갱도 하나로 지열을 무력화할 수 없습니다.
    /// 설정의 <c>outdoorFollowsDepth</c>를 끄면 예전처럼 지표 공기가 그대로 내려옵니다.
    /// </summary>
    public float GetTemperatureAt(int x, int y)
    {
        Room room = RoomManager.instance != null ? RoomManager.instance.GetRoom(x, y) : null;
        if (room != null) return room.Temperature;

        return (config != null && config.outdoorFollowsDepth) ? GetAmbientAt(y) : OutdoorTemperature;
    }

    /// <inheritdoc cref="GetTemperatureAt(int,int)"/>
    public float GetTemperatureAt(Vector2Int cell) => GetTemperatureAt(cell.x, cell.y);

    /// <summary>
    /// 이번 틱에 이 열원이 데우기로 결정된 방. 없으면 <see cref="RoomManager.OUTDOOR_ID"/>.
    ///
    /// 다중 타일 건물은 자기 풋프린트가 벽이라 어느 방에도 속하지 않으므로,
    /// 좌표로 방을 찾으면 실외가 나옵니다. 냉난방기는 반드시 이 캐시를 통해 자기 방을 찾아야 합니다.
    /// </summary>
    public int GetResolvedRoomId(IHeatSource source)
    {
        if (source == null) return RoomManager.OUTDOOR_ID;
        return resolvedRoomBySource.TryGetValue(source, out int roomId) ? roomId : RoomManager.OUTDOOR_ID;
    }

    /// <summary>
    /// 이 열원이 선 방에서 <b>능동 공조기를 뺐을 때</b> 수렴할 온도.
    /// 공조기의 목표와 이 값의 차이가 곧 부하이고, 부하가 전력 소모를 정합니다.
    /// 방을 못 찾으면 그 높이의 주변 온도를 돌려줍니다.
    /// </summary>
    public float GetNaturalEquilibriumFor(IHeatSource source)
    {
        int roomId = GetResolvedRoomId(source);
        Room room = RoomManager.instance != null ? RoomManager.instance.GetRoomById(roomId) : null;

        if (room != null) return room.NaturalEquilibrium;
        return source != null ? GetAmbientAt(source.HeatTilePosition.y) : OutdoorTemperature;
    }

    #endregion
}
