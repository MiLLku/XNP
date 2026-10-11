using UnityEngine;

/// <summary>
/// 물 흡수 시설 — 맞닿은 칸의 물을 빨아들여 '물' 아이템으로 바꿉니다. 직원 작업 없이 저절로 돕니다.
///
/// 받는 자리의 침식이 기준 이상이면 '침식된 물'이 나옵니다(판정은 FluidManager).
/// 보관 칸은 종류별 하나씩 — '물' 한 스택, '침식된 물' 한 스택까지만 만들고 멈춥니다. 직원이 창고로 옮겨 가야 다시 돕니다.
/// 빨아들인 만큼 맵의 물이 실제로 줄어듭니다. 맞닿은 칸에 물이 없으면(수위가 내려갔으면) 섭니다.
///
/// 정전이나 기반 파괴 시 진행도 누적만 멈추고 리셋하지 않습니다(VentExtractor와 같은 방식).
/// </summary>
[RequireComponent(typeof(Building), typeof(BuildingOutputBuffer))]
public class WaterPump : MonoBehaviour, IBuildingFunction
{
    [Header("흡수 설정")]
    [Tooltip("한 번 빨아들이는 간격(초)")]
    [Min(0.1f)] [SerializeField] private float cycleSeconds = 10f;

    [Tooltip("한 번에 빨아들이는 물의 양 (1 = 한 칸)")]
    [Range(0.01f, 1f)] [SerializeField] private float cellsPerCycle = 0.2f;

    [Tooltip("종류별 보관 한도 (한 스택). '물'과 '침식된 물'을 각각 이만큼까지만 만듭니다. 보관함 capacity는 이 값의 두 배 이상이어야 합니다.")]
    [Min(1)] [SerializeField] private int stackSize = 50;

    private static readonly Vector2Int[] Sides = { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up };

    private Building _building;
    private PowerConsumer _power;
    private BuildingOutputBuffer _buffer;
    private float _progress;

    /// <summary>지금 실제로 돌고 있는지 (전력·기반이 있고, 빨아들일 물과 담을 자리가 있어야 한다)</summary>
    public bool IsOperating => HasPowerAndBase && TryFindIntake(out _);

    private bool HasPowerAndBase =>
        (_building == null || _building.IsFunctional) && (_power == null || _power.IsPowered);

    void Awake()
    {
        _building = GetComponent<Building>();
        _power = GetComponent<PowerConsumer>();
        _buffer = GetComponent<BuildingOutputBuffer>();
    }

    void Update()
    {
        if (!HasPowerAndBase) return;

        _progress += Time.deltaTime / cycleSeconds;
        if (_progress < 1f) return;
        _progress = 0f;

        var fluids = FluidManager.instance;
        if (fluids == null || !TryFindIntake(out Vector2Int intake)) return;

        int count = fluids.TakeWaterAsItems(intake.x, intake.y, cellsPerCycle, out ItemData item, fromSurroundings: true);
        if (count > 0 && item != null) _buffer.TryStore(item, count);
    }

    /// <summary>맞닿은 칸 중 물이 있고, 거기서 나올 아이템을 담을 자리가 남은 칸을 찾습니다.</summary>
    private bool TryFindIntake(out Vector2Int intake)
    {
        intake = default;
        var fluids = FluidManager.instance;
        if (fluids == null) return false;

        var origin = new Vector2Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y));
        foreach (var side in Sides)
        {
            Vector2Int cell = origin + side;
            ItemData item = fluids.GetWaterItemAt(cell.x, cell.y);
            if (item == null || _buffer.GetStoredAmount(item) >= stackSize) continue;

            intake = cell;
            return true;
        }
        return false;
    }

    // ── IBuildingFunction ──────────────────────────────────────
    // 기반 파괴·복구 시 Building이 이 컴포넌트의 enabled를 토글하므로 별도 처리는 없습니다.
    public void OnBuildingDisabled() { }
    public void OnBuildingEnabled() { }
}
