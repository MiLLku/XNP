using UnityEngine;

/// <summary>
/// 직원이 액체에 닿았을 때의 영향 — 축축함, 익사, 용암 화상.
/// (이동 감속과 길찾기 비용은 EmployeeMovement·TilePathfinder가 FluidManager에 직접 묻습니다.)
///
///   • <b>축축함</b> — 발이 물에 잠기거나 비를 맞으면(하늘이 뚫린 곳) 붙는 정신력 페널티. 시간형이지만 쌓이지 않고,
///     젖어 있는 동안에는 지속 시간만 계속 다시 채워집니다.
///   • <b>익사</b> — 머리 칸까지 물이 차면 숨을 참습니다. 숨이 다하면 체력이 깎이고, 물 밖으로 나오면 금방 회복합니다.
///   • <b>용암</b> — 닿아 있는 동안 버티는 시간 없이 바로 체력이 깎입니다.
///
/// 수치는 FluidManager의 '직원 영향' 설정에 있습니다. 숨은 저장하지 않습니다(불러오면 가득 찬 상태).
/// </summary>
[RequireComponent(typeof(Employee))]
public class EmployeeFluidExposure : MonoBehaviour
{
    private const float TICK_INTERVAL = 0.25f;
    private const float BREATH_RECOVERY = 5f; // 물 밖에서는 참은 시간의 5배 속도로 숨을 되찾는다

    private Employee employee;
    private EmployeeStatsController statsController;
    private float tickTimer;
    private float heldBreath; // 숨을 참은 시간(초)

    /// <summary>머리까지 물에 잠겨 있는지</summary>
    public bool IsSubmerged { get; private set; }

    /// <summary>남은 숨 (1 = 가득, 0 = 다함 — 이때부터 체력이 깎입니다)</summary>
    public float Breath01
    {
        get
        {
            var fluids = FluidManager.instance;
            float max = fluids != null ? fluids.BreathSeconds : 0f;
            return max > 0f ? Mathf.Clamp01(1f - heldBreath / max) : 1f;
        }
    }

    private void Awake()
    {
        employee = GetComponent<Employee>();
        statsController = GetComponent<EmployeeStatsController>();
    }

    private void Update()
    {
        if (employee == null || employee.State == EmployeeState.Dead) return;

        tickTimer += Time.deltaTime;
        if (tickTimer < TICK_INTERVAL) return;

        float delta = tickTimer;
        tickTimer = 0f;
        Evaluate(delta);
    }

    /// <summary>축축함을 붙입니다 (물에 닿았거나 비를 맞았을 때). 이미 젖어 있으면 지속 시간만 다시 채웁니다.</summary>
    public void MakeDamp()
    {
        var fluids = FluidManager.instance;
        if (fluids == null || statsController == null) return;
        statsController.RefreshTimedMental(MentalReason.DAMP, "축축함", fluids.DampMental, fluids.DampSeconds);
    }

    private void Evaluate(float deltaTime)
    {
        var fluids = FluidManager.instance;
        if (fluids == null || statsController == null) return;

        // 직원은 두 칸 높이 — 발 칸과 그 위 머리 칸
        Vector3Int foot = employee.GetFootTile();
        FluidType atFoot = fluids.GetFluid(foot.x, foot.y);
        FluidType atHead = fluids.GetFluid(foot.x, foot.y + 1);

        if (atFoot == FluidType.Lava || atHead == FluidType.Lava)
            statsController.ModifyHealth(-fluids.LavaDamagePerSecond * deltaTime);

        bool footWet = atFoot == FluidType.Water && fluids.GetAmount(foot.x, foot.y) >= fluids.DampMinDepth;
        // 비는 머리 위로 막힌 것이 없는 곳에서만 맞는다
        bool rainedOn = fluids.IsRaining && RoomManager.instance != null && RoomManager.instance.IsOpenToSky(foot.x, foot.y + 1);
        if (footWet || atHead == FluidType.Water || rainedOn) MakeDamp();

        IsSubmerged = fluids.IsSubmerged(foot.x, foot.y);
        if (IsSubmerged)
        {
            heldBreath += deltaTime;
            if (heldBreath >= fluids.BreathSeconds)
                statsController.ModifyHealth(-fluids.DrownDamagePerSecond * deltaTime);
        }
        else heldBreath = Mathf.Max(0f, heldBreath - deltaTime * BREATH_RECOVERY);
    }
}
