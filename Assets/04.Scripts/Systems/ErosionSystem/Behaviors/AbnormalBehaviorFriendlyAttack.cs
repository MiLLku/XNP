using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 이상 행동 — 동료 공격.
///
/// 가장 가까운 동료를 찾아가 지속 시간 동안 공격한다.
/// 대상이 도망치거나 이동하면 계속 쫓아간다.
///
/// 피해량은 <b>직원의 현재 공격력을 그대로 쓴다</b>(무기·숙련·특성·연구 반영).
/// 세 가지 침식 이상 행동 중 가장 직접적인 손실이라 지속 시간을 가장 짧게 잡았다.
/// </summary>
public class AbnormalBehaviorFriendlyAttack : AbnormalBehaviorBase
{
    #region 수치 (밸런스 조정 지점)

    /// <summary>지속 시간 (초)</summary>
    private const float DURATION = 15f;

    /// <summary>대상을 찾는 반경 (타일). 이 밖의 동료는 무시한다.</summary>
    private const float SEARCH_RADIUS = 12f;

    /// <summary>대상이 움직이므로 경로를 다시 잡는 주기 (초)</summary>
    private const float REPATH_INTERVAL = 0.5f;

    #endregion

    #region 직원별 상태

    private class AttackState
    {
        /// <summary>현재 노리는 동료</summary>
        public Employee target;

        /// <summary>다음 공격까지 남은 시간</summary>
        public float attackTimer;

        /// <summary>다음 경로 재계산까지 남은 시간</summary>
        public float repathTimer;

        /// <summary>대상으로 이동 명령을 내린 상태인지</summary>
        public bool approaching;
    }

    private readonly Dictionary<Employee, AttackState> states = new Dictionary<Employee, AttackState>();

    #endregion

    public override AbnormalBehaviorType BehaviorType => AbnormalBehaviorType.FriendlyAttack;

    #region 실행

    /// <summary>주변에 때릴 동료가 아무도 없으면 이 행동은 후보에서 빠진다.</summary>
    public override bool CanExecute(Employee employee)
    {
        return base.CanExecute(employee) && FindNearestTarget(employee) != null;
    }

    public override float Execute(Employee employee)
    {
        SeizeControl(employee);

        var target = FindNearestTarget(employee);
        states[employee] = new AttackState { target = target, attackTimer = 0f };

        Debug.Log($"[AbnormalBehavior] {employee.DisplayName}: 동료 공격 → {target?.DisplayName} ({DURATION:F0}초)");
        return DURATION;
    }

    public override void Tick(Employee employee, float deltaTime)
    {
        if (employee == null || employee.State == EmployeeState.Dead) return;

        var state = GetOrCreateState(employee);
        var movement = employee.GetComponent<EmployeeMovement>();

        // 대상이 죽었거나 사라졌으면 새로 고른다
        if (state.target == null || state.target.State == EmployeeState.Dead)
        {
            state.target = FindNearestTarget(employee);
            state.approaching = false;

            if (state.target == null)
            {
                if (movement != null && movement.IsMoving) movement.StopMoving();
                return;
            }
        }

        GetAttackProfile(employee, out float damage, out float interval, out float range);

        Vector3 targetPos = state.target.transform.position;
        float dist = Vector2.Distance(employee.transform.position, targetPos);

        if (dist > range)
        {
            // 대상이 계속 움직이므로 주기적으로 경로를 다시 잡는다
            state.repathTimer -= deltaTime;
            if (!state.approaching || state.repathTimer <= 0f)
            {
                state.repathTimer = REPATH_INTERVAL;
                state.approaching = true;
                movement?.MoveTo(targetPos);
            }
            return;
        }

        if (state.approaching)
        {
            movement?.StopMoving();
            state.approaching = false;
        }

        state.attackTimer -= deltaTime;
        if (state.attackTimer > 0f) return;
        state.attackTimer = interval;

        state.target.TakeDamage(damage);
        Debug.Log($"[AbnormalBehavior] {employee.DisplayName} → {state.target.DisplayName}: 동료 공격 {damage:F1} 피해");
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
    /// 세이브 로드 직후 Tick부터 재개되는 경로에서도 통제권을 다시 확보합니다.
    /// </summary>
    private AttackState GetOrCreateState(Employee employee)
    {
        if (states.TryGetValue(employee, out var existing)) return existing;

        SeizeControl(employee);

        var state = new AttackState { target = FindNearestTarget(employee), attackTimer = 0f };
        states[employee] = state;
        return state;
    }

    /// <summary>반경 내에서 가장 가까운 살아있는 동료를 찾습니다.</summary>
    private static Employee FindNearestTarget(Employee employee)
    {
        if (EmployeeManager.instance == null || employee == null) return null;

        Vector3 myPos = employee.transform.position;

        Employee nearest = null;
        float nearestDist = SEARCH_RADIUS;

        foreach (var other in EmployeeManager.instance.AllEmployees)
        {
            if (other == null || other == employee) continue;
            if (other.State == EmployeeState.Dead) continue;

            float dist = Vector2.Distance(myPos, other.transform.position);
            if (dist > nearestDist) continue;

            nearestDist = dist;
            nearest = other;
        }

        return nearest;
    }

    #endregion
}
