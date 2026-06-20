using UnityEngine;

/// <summary>
/// 직원에게 임시로 부착되는 지속 피해(DoT) 컴포넌트.
/// 스피터 피격 시 Employee GO에 AddComponent로 추가됩니다.
/// 재피격 시 duration만 갱신되며 스택되지 않습니다.
///
/// 사용법:
///   DotEffect.Apply(employee, dps: 3f, duration: 5f);
/// </summary>
public class DotEffect : UnityEngine.MonoBehaviour
{
    #region 직렬화 필드 (인스펙터 확인용)

    [SerializeField] private float damagePerSecond;
    [SerializeField] private float remainingTime;

    #endregion

    private Employee _employee;

    // ─────────────────────────────────────────
    #region 정적 진입점

    /// <summary>
    /// 지정 직원에게 DoT를 적용합니다.
    /// 이미 효과가 있으면 duration을 갱신하고 dps를 최댓값으로 유지합니다.
    /// </summary>
    public static void Apply(Employee target, float dps, float duration)
    {
        if (target == null) return;

        var existing = target.GetComponent<DotEffect>();
        if (existing != null)
        {
            existing.Refresh(dps, duration);
            return;
        }

        var dot = target.gameObject.AddComponent<DotEffect>();
        dot.Initialize(target, dps, duration);
    }

    #endregion

    // ─────────────────────────────────────────
    #region 초기화 / 갱신

    private void Initialize(Employee employee, float dps, float duration)
    {
        _employee        = employee;
        damagePerSecond  = dps;
        remainingTime    = duration;
    }

    private void Refresh(float dps, float duration)
    {
        // 더 강한 dps를 유지하고, duration은 항상 갱신
        if (dps > damagePerSecond) damagePerSecond = dps;
        remainingTime = duration;
    }

    #endregion

    // ─────────────────────────────────────────
    #region 틱 처리

    private void Update()
    {
        if (_employee == null || _employee.State == EmployeeState.Dead)
        {
            Destroy(this);
            return;
        }

        remainingTime -= UnityEngine.Time.deltaTime;

        if (remainingTime <= 0f)
        {
            Destroy(this);
            return;
        }

        // 초당 피해를 프레임 단위로 적용 (받는 피해 배율 적용)
        _employee.TakeDamage(damagePerSecond * UnityEngine.Time.deltaTime);
    }

    #endregion
}
