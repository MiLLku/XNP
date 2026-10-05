using UnityEngine;

/// <summary>
/// 환풍 시설 — 한쪽 공간의 침식을 반대쪽으로 옮깁니다.
///
/// 벽 한 칸에 박아 넣는 건물이며, 화살표 방향으로 <b>뒤쪽 칸의 공간 → 앞쪽 칸의 공간</b>으로 공기를 밀어냅니다.
///   · 앞쪽이 실외면 침식이 사라집니다 (방을 정화하는 기본 용도).
///   · 앞쪽이 밀폐된 방이면 그 방에 침식이 쌓입니다 — 좁은 방으로 몰아넣으면 급격히 치솟습니다(의도된 동작).
///   · 뒤쪽이 실외면 바깥 침식을 방 안으로 끌어들입니다.
/// 앞뒤 칸 중 하나라도 막혀 있으면(지형·건물) 돌지 않습니다.
///
/// 같은 컴포넌트로 두 건물을 만듭니다:
///   · 단방향 환풍구 — 나무를 태워 돈다. 연료 보급은 <see cref="PowerProducer"/>의 흐름을 그대로 빌린다(출력 0W).
///   · 환풍기       — 전기로 돈다 (<see cref="PowerConsumer"/>).
///
/// 방향은 건물을 클릭할 때마다 → ↓ ← ↑ 순으로 바뀌며 세이브에 남습니다.
/// </summary>
[RequireComponent(typeof(Building))]
public class ErosionVent : MonoBehaviour, IBuildingExtraSerializable, IBuildingFunction
{
    [Header("환풍")]
    [Tooltip("초당 옮기는 공기량(칸). 방 침식은 '이 값 ÷ 방 칸 수'의 비율로 빠집니다 — 넓은 방일수록 오래 걸립니다.")]
    [Min(0f)] [SerializeField] private float flowCellsPerSecond = 0.3f;

    [Tooltip("방향 표시 화살표 (선택). 방향에 맞춰 회전합니다.")]
    [SerializeField] private Transform arrow;

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.right, Vector2Int.down, Vector2Int.left, Vector2Int.up
    };

    private const float TICK_INTERVAL = 1f;

    private Building _building;
    private PowerConsumer _power;
    private PowerProducer _fuel;
    private int _direction;
    private float _timer;

    /// <summary>공기를 밀어내는 방향</summary>
    public Vector2Int Direction => Directions[_direction];

    /// <summary>동력이 들어와 있는지 (전기 또는 연료). 앞뒤가 막혔는지는 보지 않습니다.</summary>
    public bool IsOperating =>
        (_building == null || _building.IsFunctional) &&
        (_power == null || _power.IsPowered) &&
        (_fuel == null || !_fuel.RequiresFuel || _fuel.FuelBuffer > 0f);

    private void Awake()
    {
        _building = GetComponent<Building>();
        _power = GetComponent<PowerConsumer>();
        _fuel = GetComponent<PowerProducer>();
        ApplyArrow();
    }

    private void Update()
    {
        _timer += Time.deltaTime;
        if (_timer < TICK_INTERVAL) return;

        float elapsed = _timer;
        _timer = 0f;

        if (!IsOperating || flowCellsPerSecond <= 0f) return;

        var rooms = RoomManager.instance;
        var erosion = TerrainErosionManager.instance;
        if (rooms == null || erosion == null) return;

        Vector2Int cell = Vector2Int.FloorToInt(transform.position);
        Vector2Int from = cell - Direction;
        Vector2Int to = cell + Direction;
        if (!rooms.IsAirCell(from.x, from.y) || !rooms.IsAirCell(to.x, to.y)) return;

        erosion.PumpErosion(from, to, flowCellsPerSecond * elapsed);
    }

    private void OnMouseDown()
    {
        // UI 위 클릭은 월드로 흘리지 않는다 (OnMouseDown은 EventSystem을 거치지 않는다)
        if (UIManager.PointerOverUI) return;
        if (_building != null && !_building.IsFunctional) return;

        _direction = (_direction + 1) % Directions.Length;
        ApplyArrow();
    }

    private void ApplyArrow()
    {
        // 화살표 그림은 오른쪽(→)을 향해 그려져 있다
        if (arrow != null) arrow.localRotation = Quaternion.Euler(0f, 0f, -90f * _direction);
    }

    // ── IBuildingFunction ──────────────────────────────────────
    // 기반 파괴·복구 시 Building이 이 컴포넌트의 enabled를 토글하므로 별도 처리는 없습니다.
    public void OnBuildingDisabled() { }
    public void OnBuildingEnabled() { }

    // ── IBuildingExtraSerializable ─────────────────────────────
    // 건물 하나당 저장되는 컴포넌트는 첫 번째 하나뿐이라, 연료 잔량(PowerProducer)도 여기서 함께 품어 저장한다.
    // 프리팹에서 이 컴포넌트가 PowerProducer보다 위에 있어야 한다.
    [System.Serializable]
    private class SaveState
    {
        public int direction;
        public string fuel;
    }

    public string SerializeExtra()
        => JsonUtility.ToJson(new SaveState { direction = _direction, fuel = _fuel != null ? _fuel.SerializeExtra() : "" });

    public void DeserializeExtra(string json)
    {
        if (string.IsNullOrEmpty(json)) return;

        var state = JsonUtility.FromJson<SaveState>(json);
        if (state == null) return;

        _direction = Mathf.Clamp(state.direction, 0, Directions.Length - 1);
        ApplyArrow();
        if (_fuel != null) _fuel.DeserializeExtra(state.fuel);
    }
}
