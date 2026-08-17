using UnityEngine;

/// <summary>
/// 이상 행동 추상 베이스 클래스.
/// IAbnormalBehavior의 공통 구현을 제공합니다.
/// 새 이상 행동을 만들 때 이 클래스를 상속하세요.
/// </summary>
public abstract class AbnormalBehaviorBase : IAbnormalBehavior
{
    /// <inheritdoc/>
    public abstract AbnormalBehaviorType BehaviorType { get; }

    /// <inheritdoc/>
    /// <remarks>기본 구현: 직원이 null이 아니고 Dead 상태가 아니면 실행 가능.</remarks>
    public virtual bool CanExecute(Employee employee)
    {
        return employee != null && employee.State != EmployeeState.Dead;
    }

    /// <inheritdoc/>
    public abstract float Execute(Employee employee);

    /// <inheritdoc/>
    /// <remarks>기본 구현: 아무것도 하지 않음. 지속형 행동만 오버라이드.</remarks>
    public virtual void Tick(Employee employee, float deltaTime) { }

    /// <inheritdoc/>
    /// <remarks>기본 구현: 아무것도 하지 않음. 필요 시 오버라이드.</remarks>
    public virtual void OnEnd(Employee employee) { }

    #region 통제권 헬퍼 (지속형 행동 공용)

    /// <summary>
    /// 직원의 통제권을 이상 행동이 가져옵니다.
    ///
    /// 작업·이동을 끊고 소집을 강제 해제한 뒤 MentalBreak 상태로 전환합니다.
    /// MentalBreak 상태에서는 EmployeeAI와 EmployeeWork가 개입하지 않고
    /// EmployeeDraft가 재소집을 거부하므로, 이상 행동이 끝날 때까지
    /// <b>플레이어가 이 직원을 소집해 직접 조작할 수 없습니다.</b>
    /// </summary>
    protected static void SeizeControl(Employee employee)
    {
        if (employee == null) return;

        employee.CancelWork();
        employee.GetComponent<EmployeeMovement>()?.StopMoving();

        // 소집 중이었다면 먼저 풀어야 한다 (해제가 상태를 Idle로 되돌리므로 순서가 중요)
        employee.Draft?.SetDrafted(false);

        employee.SetState(EmployeeState.MentalBreak);
    }

    /// <summary>
    /// 이상 행동이 끝나 통제권을 직원에게 돌려줍니다.
    /// 정신력이 여전히 0이라면 EmployeeStatsController가 곧바로 다시 MentalBreak로 되돌립니다.
    /// </summary>
    protected static void ReleaseControl(Employee employee)
    {
        if (employee == null) return;

        employee.GetComponent<EmployeeMovement>()?.StopMoving();

        if (employee.State == EmployeeState.MentalBreak)
            employee.SetState(EmployeeState.Idle);
    }

    /// <summary>
    /// 직원의 현재 공격력·공격 간격·사거리를 가져옵니다.
    /// 값은 전적으로 <b>장착 무기</b>가 정하고 숙련·특성·연구가 조정합니다(무기가 없으면 맨손 기본값).
    /// 이상 행동의 공격도 평상시 전투와 같은 수치를 쓰도록 EmployeeCombat의 계산을 그대로 재사용합니다.
    /// </summary>
    protected static void GetAttackProfile(Employee employee, out float damage, out float interval, out float range)
    {
        var combat = employee != null ? employee.Combat : null;

        damage   = combat != null ? combat.GetAttackDamage()   : 3f;
        interval = combat != null ? combat.GetAttackInterval() : 1.2f;
        range    = combat != null ? combat.GetAttackRange()    : 1.5f;
    }

    #endregion
}
