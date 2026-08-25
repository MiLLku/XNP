using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 이상 행동 — 침식 폭주.
///
/// 발동한 자리에 그대로 멈춰 서서 주변으로 침식을 흩뿌린다.
/// 지속되는 동안 <b>플레이어는 이 직원을 소집해 직접 조작할 수 없다</b>
/// (SeizeControl이 MentalBreak 상태로 바꾸고, EmployeeDraft가 그 상태의 소집을 거부한다).
///
/// 설계 의도:
///   움직이지 않는다는 점이 핵심이다. 도망치거나 쫓아오는 행동과 달리
///   "그 자리가 오염원이 된다"는 형태라, 플레이어는 직원을 통제하는 대신
///   <b>주변 동료를 물리는 쪽</b>으로 대응해야 한다. 창고·식당처럼 사람이 몰리는
///   곳에서 터지면 피해가 커지므로, 침식 직원을 어디에 두느냐가 의미를 갖는다.
/// </summary>
public class AbnormalBehaviorErosionOutburst : AbnormalBehaviorBase
{
    #region 수치 (밸런스 조정 지점)

    /// <summary>지속 시간 (초)</summary>
    private const float DURATION = 35f;

    /// <summary>침식을 퍼뜨리는 반경 (타일)</summary>
    private const float RADIUS = 4f;

    /// <summary>반경 내 동료가 초당 받는 침식량</summary>
    private const float EROSION_PER_SECOND = 6f;

    /// <summary>침식 적용 주기 (초). 매 프레임 계산하지 않기 위한 간격.</summary>
    private const float TICK_INTERVAL = 0.5f;

    #endregion

    #region 직원별 상태

    /// <summary>구현체는 전 직원이 공유하는 단일 인스턴스이므로 상태를 직원별로 보관한다.</summary>
    private class OutburstState
    {
        /// <summary>이상 행동이 시작된 위치 — 침식이 퍼지는 중심점</summary>
        public Vector3 anchor;

        /// <summary>다음 침식 적용까지 남은 시간</summary>
        public float tickTimer;
    }

    private readonly Dictionary<Employee, OutburstState> states = new Dictionary<Employee, OutburstState>();

    #endregion

    public override AbnormalBehaviorType BehaviorType => AbnormalBehaviorType.ErosionOutburst;

    #region 실행

    public override float Execute(Employee employee)
    {
        SeizeControl(employee);

        states[employee] = new OutburstState
        {
            anchor    = employee.transform.position,
            tickTimer = TICK_INTERVAL
        };

        Debug.Log($"[AbnormalBehavior] {employee.DisplayName}: 침식 폭주 — 제자리에서 반경 {RADIUS}타일 침식 ({DURATION:F0}초)");
        return DURATION;
    }

    public override void Tick(Employee employee, float deltaTime)
    {
        if (employee == null || employee.State == EmployeeState.Dead) return;

        var state = GetOrCreateState(employee);

        state.tickTimer -= deltaTime;
        if (state.tickTimer > 0f) return;
        state.tickTimer = TICK_INTERVAL;

        HoldPosition(employee);
        SpreadErosion(employee, state);
    }

    public override void OnEnd(Employee employee)
    {
        states.Remove(employee);
        ReleaseControl(employee);
    }

    #endregion

    #region 내부 로직

    /// <summary>
    /// 직원별 상태를 가져오거나 없으면 만듭니다.
    /// 세이브 로드 직후에는 Execute를 거치지 않고 Tick부터 재개되므로,
    /// 그 경로에서도 통제권을 다시 확보하고 현재 위치를 중심점으로 삼습니다.
    /// </summary>
    private OutburstState GetOrCreateState(Employee employee)
    {
        if (states.TryGetValue(employee, out var existing)) return existing;

        SeizeControl(employee);

        var state = new OutburstState
        {
            anchor    = employee.transform.position,
            tickTimer = TICK_INTERVAL
        };
        states[employee] = state;
        return state;
    }

    /// <summary>제자리에 붙들어 둡니다. 다른 경로에서 이동이 걸렸어도 즉시 멈춘다.</summary>
    private static void HoldPosition(Employee employee)
    {
        var movement = employee.GetComponent<EmployeeMovement>();
        if (movement != null && movement.IsMoving)
            movement.StopMoving();
    }

    /// <summary>중심점 반경 내의 동료에게 침식을 누적시킵니다.</summary>
    private static void SpreadErosion(Employee employee, OutburstState state)
    {
        if (EmployeeManager.instance == null) return;

        float erosionThisTick = EROSION_PER_SECOND * TICK_INTERVAL;
        string sourceKey  = ErosionSource.OutburstKey(employee.InstanceId);
        string sourceName = $"{employee.DisplayName} 침식 폭주";

        foreach (var other in EmployeeManager.instance.AllEmployees)
        {
            if (other == null || other == employee) continue;
            if (other.State == EmployeeState.Dead) continue;

            float dist = Vector2.Distance(state.anchor, other.transform.position);
            if (dist > RADIUS) continue;

            // 방어구·특성의 침식 무시 합산값이 초당 침식량 이상이면 완전 차단 (제놉스 오라와 동일 규칙)
            float erosionIgnore = (other.Equipment?.GetTotalErosionIgnore() ?? 0f)
                                + (other.StatsController?.CachedErosionIgnoreBonus ?? 0f);
            if (erosionIgnore >= EROSION_PER_SECOND) continue;

            other.ErosionController?.AddErosion(erosionThisTick, sourceKey, sourceName);
            other.ErosionController?.MarkAuraExposure();
        }
    }

    #endregion
}
