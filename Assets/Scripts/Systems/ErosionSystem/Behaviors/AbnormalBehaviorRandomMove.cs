using UnityEngine;

/// <summary>
/// 이상 행동 — 무작위 이동.
/// 현재 작업을 취소하고 랜덤 방향으로 짧게 이동합니다.
///
/// 실제 이동 로직은 EmployeeMovement API가 구현되는 시점에 채워 넣으세요.
/// 현재는 작업 취소만 수행하는 스텁 구현입니다.
/// </summary>
public class AbnormalBehaviorRandomMove : AbnormalBehaviorBase
{
    public override AbnormalBehaviorType BehaviorType => AbnormalBehaviorType.RandomMove;

    public override float Execute(Employee employee)
    {
        employee.CancelWork();

        // TODO: EmployeeMovement에 랜덤 목적지 이동 API 구현 후 연결
        // 예시: employee.Movement.MoveToRandomNearbyTile(maxDistance: 3);
        Debug.Log($"[AbnormalBehavior] {employee.DisplayName}: 무작위 이동 발동");
        return 5f;
    }
}
