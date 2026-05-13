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
    /// <remarks>기본 구현: 아무것도 하지 않음. 필요 시 오버라이드.</remarks>
    public virtual void OnEnd(Employee employee) { }
}
