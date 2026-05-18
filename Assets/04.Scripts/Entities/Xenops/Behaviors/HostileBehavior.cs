using UnityEngine;

/// <summary>
/// 적대적 생명체 제노프스 행동.
/// 근처 직원과 건물에 주기적으로 피해를 입힙니다.
///
/// 예시:
///   - 제압 시 희귀 자원 드랍 (이점)
///   - 근처 직원 체력/정신력 감소 (해악)
///
/// 해석도를 올리려면 제압 후 플레이어가 "연구" 상호작용해야 합니다.
/// (Xenops.Interact() 호출)
/// </summary>
public class HostileBehavior : MonoBehaviour, IXenopsBehavior
{
    #region 필드

    private Xenops xenops;
    private XenopsData data;
    private bool isActive = false;

    /// <summary>공격 타이머</summary>
    private float attackTimer;

    #endregion

    #region IXenopsBehavior 구현

    public XenopsType BehaviorType => XenopsType.Hostile;
    public bool IsActive => isActive;

    public void OnActivated()
    {
        isActive = true;
        attackTimer = 0f;

        Debug.Log($"[HostileBehavior] {xenops?.DisplayName} 적대 활성화");
    }

    public void OnDeactivated()
    {
        isActive = false;

        Debug.Log($"[HostileBehavior] {xenops?.DisplayName} 적대 비활성화");
    }

    public void UpdateBehavior()
    {
        if (!isActive) return;

        // 제압 상태에서는 공격하지 않음
        if (xenops != null && xenops.State == XenopsState.Subdued) return;

        attackTimer -= Time.deltaTime;
        if (attackTimer <= 0f)
        {
            attackTimer = data != null ? (data.hostileStats.attackSpeed > 0f ? 1f / data.hostileStats.attackSpeed : 1f) : 1f;
            AttackNearbyTargets();
        }
    }

    #endregion

    #region 초기화

    void Awake()
    {
        xenops = GetComponent<Xenops>();
    }

    void Start()
    {
        if (xenops != null)
        {
            data = xenops.Data;
        }
    }

    #endregion

    #region 공격

    void Update()
    {
        UpdateBehavior();
    }

    /// <summary>
    /// 범위 내 직원과 건물에 피해를 입힙니다.
    /// </summary>
    private void AttackNearbyTargets()
    {
        if (data == null) return;
        if (EmployeeManager.instance == null) return;

        float radius = data.effectRadius;
        float damage = data.hostileStats.attackDamage;
        Vector3 pos = transform.position;

        // 범위 내 직원 공격
        foreach (var employee in EmployeeManager.instance.AllEmployees)
        {
            if (employee == null) continue;
            if (employee.State == EmployeeState.Dead) continue;

            if (Vector3.Distance(pos, employee.transform.position) <= radius)
            {
                employee.ModifyHealth(-damage);

                if (xenops != null && xenops.Data != null)
                {
                    Debug.Log($"[HostileBehavior] {xenops.DisplayName} → {employee.DisplayName}에 {damage} 피해");
                }
            }
        }

        // 범위 내 건물 공격
        var colliders = Physics2D.OverlapCircleAll(pos, radius);
        foreach (var col in colliders)
        {
            var building = col.GetComponent<Building>();
            if (building != null)
            {
                // TODO: Building.ModifyHealth() 메서드와 연동
            }
        }
    }

    #endregion
}
