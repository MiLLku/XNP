using UnityEngine;

/// <summary>
/// ErosionShooter의 자식 오브젝트에 부착되는 탐지 트리거.
/// ErosionShooterBehavior.Start()에서 동적으로 생성됩니다.
///
/// 직원이 탐지 반경에 들어오면 ErosionShooterBehavior에 알려
/// "가장 먼저 범위에 들어온 직원"을 우선 타겟으로 삼습니다.
/// </summary>
[RequireComponent(typeof(CircleCollider2D))]
public class ErosionShooterDetectionZone : MonoBehaviour
{
    private ErosionShooterBehavior _owner;

    public void Initialize(ErosionShooterBehavior owner, float radius)
    {
        _owner = owner;
        var col = GetComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = radius;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_owner == null) return;
        var emp = other.GetComponent<Employee>();
        if (emp != null && emp.State != EmployeeState.Dead)
            _owner.OnEmployeeEnterRange(emp);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (_owner == null) return;
        var emp = other.GetComponent<Employee>();
        if (emp != null)
            _owner.OnEmployeeExitRange(emp);
    }
}
