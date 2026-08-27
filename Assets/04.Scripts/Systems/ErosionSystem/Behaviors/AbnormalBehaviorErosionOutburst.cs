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

    /// <summary>반경 안에 있는 동료에게 붙는 고정 침식량 (벗어나면 돌아간다)</summary>
    private const float EROSION_AMOUNT = 20f;

    #endregion

    #region 직원별 상태

    /// <summary>
    /// 구현체는 전 직원이 공유하는 단일 인스턴스이므로 상태를 직원별로 보관한다.
    ///
    /// 이 상태 객체 자체가 <see cref="IEntityErosionSource"/>다 — 폭주가 시작되면
    /// 타일 레이어에 등록되어 <b>그 자리를 오염시키고</b>, 끝나면 등록이 풀린다.
    /// 주변 동료를 직접 찾아 침식을 꽂던 방식에서 바뀐 것이며, 제놉스 오라와 같은 처리다.
    /// "그 자리가 오염원이 된다"는 설계 의도가 오히려 더 분명해진다.
    /// </summary>
    private class OutburstState : IEntityErosionSource
    {
        /// <summary>이상 행동이 시작된 위치 — 침식이 퍼지는 중심점</summary>
        public Vector3 anchor;

        /// <summary>폭주를 일으킨 직원 (내역 표기용)</summary>
        public Employee owner;

        /// <summary>아직 폭주 중인지</summary>
        public bool active = true;

        public Vector2 EmitPosition => anchor;
        public float EmitRadius => RADIUS;

        /// <summary>범위 안 동료에게 붙는 고정량. 폭주가 끝나거나 벗어나면 돌아갑니다.</summary>
        public float FixedErosionAmount => EROSION_AMOUNT;

        public bool HorizontalOnly => false;

        public bool Covers(Vector2 worldPosition)
            => Vector2.Distance(EmitPosition, worldPosition) <= RADIUS;

        public bool IsEmitting => active && owner != null && owner.State != EmployeeState.Dead;

        public string ErosionSourceKey
            => ErosionSource.OutburstKey(owner != null ? owner.InstanceId : 0);

        public string ErosionSourceName
            => $"{(owner != null ? owner.DisplayName : "직원")} 침식 폭주";
    }

    private readonly Dictionary<Employee, OutburstState> states = new Dictionary<Employee, OutburstState>();

    #endregion

    public override AbnormalBehaviorType BehaviorType => AbnormalBehaviorType.ErosionOutburst;

    #region 실행

    public override float Execute(Employee employee)
    {
        SeizeControl(employee);

        RegisterState(employee);

        Debug.Log($"[AbnormalBehavior] {employee.DisplayName}: 침식 폭주 — 제자리에서 반경 {RADIUS}타일 침식 ({DURATION:F0}초)");
        return DURATION;
    }

    public override void Tick(Employee employee, float deltaTime)
    {
        if (employee == null || employee.State == EmployeeState.Dead) return;

        // 침식은 타일 레이어가 알아서 뿌린다. 여기서는 자리에 붙들어 두기만 하면 된다.
        GetOrCreateState(employee);
        HoldPosition(employee);
    }

    public override void OnEnd(Employee employee)
    {
        if (states.TryGetValue(employee, out var state))
        {
            state.active = false;
            EntityErosionField.instance?.UnregisterSource(state);
        }

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
        return RegisterState(employee);
    }

    /// <summary>상태를 만들고 타일 레이어에 등록합니다.</summary>
    private OutburstState RegisterState(Employee employee)
    {
        var state = new OutburstState
        {
            anchor = employee.transform.position,
            owner  = employee
        };

        states[employee] = state;
        EntityErosionField.instance?.RegisterSource(state);
        return state;
    }

    /// <summary>제자리에 붙들어 둡니다. 다른 경로에서 이동이 걸렸어도 즉시 멈춘다.</summary>
    private static void HoldPosition(Employee employee)
    {
        var movement = employee.GetComponent<EmployeeMovement>();
        if (movement != null && movement.IsMoving)
            movement.StopMoving();
    }

    #endregion
}
