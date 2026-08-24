using UnityEngine;

/// <summary>
/// 이상 행동 — 명령 무시.
/// 현재 작업을 취소하고, EmployeeErosionController가 지속 시간 동안
/// 새 작업 배정을 차단합니다.
///
/// 확장 예시:
///   AbnormalBehaviorIgnoreCommandEnhanced를 만들어 ignoreCount = 3으로 설정하면
///   4단계용 '명령 무시(강화)'를 구현할 수 있습니다.
/// </summary>
public class AbnormalBehaviorIgnoreCommand : AbnormalBehaviorBase
{
    /// <summary>명령 무시 지속 시간 (초)</summary>
    protected virtual float Duration => 15f;

    public override AbnormalBehaviorType BehaviorType => AbnormalBehaviorType.IgnoreCommand;

    public override float Execute(Employee employee)
    {
        employee.CancelWork();
        Debug.Log($"[AbnormalBehavior] {employee.DisplayName}: 명령 무시 발동 ({Duration}초)");
        return Duration;
    }
}
