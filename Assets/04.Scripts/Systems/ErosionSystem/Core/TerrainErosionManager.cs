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
    /// 평상시에는 <b>고정</b>입니다. 온도와 달리 방으로 새어 들어가지 않습니다 —
    /// 침식에는 전도라는 개념이 없어서 벽으로 막으면 완전히 차단됩니다.
    /// 실외 침식이 관여하는 곳은 두 군데뿐입니다:
    ///   · 실외에 서 있는 직원이 받는 노출
    ///   · 새로 밀폐된 공간의 시작값 (바깥 공기를 가둔 셈이므로)
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

        if (showDebugLogs && sources.Count > 0)
            Debug.Log($"[TerrainErosionManager] 발원지 {sources.Count}개 갱신");
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
    /// 방 안이면 그 방의 침식, <b>실외면 실외 기본 침식</b>입니다.
    /// </summary>
    public float GetRoomErosionAt(int x, int y)
    {
        Room room = RoomManager.instance != null ? RoomManager.instance.GetRoom(x, y) : null;
        return room != null ? room.Erosion : OutdoorErosion;
    }

    /// <summary>
    /// 방의 침식을 줄입니다 (세척 작업·정화 아이템).
    /// 실외 기본 침식보다 아래로도 내려갈 수 있습니다 — 밀폐해서 관리하면 바깥보다 깨끗한 공간을 만들 수 있습니다.
    /// </summary>
    public void ReduceRoomErosion(Room room, float amount)
    {
        if (room == null || amount <= 0f) return;

        room.Erosion = Mathf.Max(0f, room.Erosion - amount);
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
