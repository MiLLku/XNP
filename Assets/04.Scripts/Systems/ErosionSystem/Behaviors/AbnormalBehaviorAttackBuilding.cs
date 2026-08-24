using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 이상 행동 — 건물 파괴 충동.
///
/// 지속 시간 동안 주변에서 <b>내구도를 가진 건설물</b>을 찾아가 부순다.
/// 벽·바닥 같은 기반시설(BuildingCategory.Infrastructure)은 대상에서 제외한다 —
/// 통행로가 끊기면 이상 행동 하나로 기지 구조가 통째로 망가지기 때문이다.
///
/// 피해량은 <b>직원의 현재 공격력을 그대로 쓴다</b>.
/// 공격력은 전적으로 장착 무기가 정하고 숙련·특성·연구가 조정하므로,
/// 잘 무장한 직원일수록 자기 기지를 더 크게 부순다 — 전투력 투자에 대한 대가.
/// </summary>
public class AbnormalBehaviorAttackBuilding : AbnormalBehaviorBase
{
    #region 수치 (밸런스 조정 지점)

    /// <summary>지속 시간 (초)</summary>
    private const float DURATION = 40f;

    /// <summary>대상 건물을 찾는 반경 (타일)</summary>
    private const int SEARCH_RADIUS = 8;

    /// <summary>대상이 이동했을 때 경로를 다시 잡는 주기 (초)</summary>
    private const float REPATH_INTERVAL = 1.0f;

    #endregion

    #region 직원별 상태

    private class AttackState
    {
        /// <summary>현재 노리는 건물 (파괴되면 Unity 규칙에 따라 null로 평가된다)</summary>
        public Building target;

        /// <summary>다음 공격까지 남은 시간</summary>
        public float attackTimer;

        /// <summary>대상으로 이동 명령을 내린 상태인지</summary>
        public bool approaching;

        /// <summary>다음 경로 재계산까지 남은 시간</summary>
        public float repathTimer;
    }

    private readonly Dictionary<Employee, AttackState> states = new Dictionary<Employee, AttackState>();

    #endregion

    public override AbnormalBehaviorType BehaviorType => AbnormalBehaviorType.AttackBuilding;

    #region 실행

    public override float Execute(Employee employee)
    {
        SeizeControl(employee);
        states[employee] = new AttackState { attackTimer = 0f };

        Debug.Log($"[AbnormalBehavior] {employee.DisplayName}: 건물 파괴 충동 ({DURATION:F0}초)");
        return DURATION;
    }

    public override void Tick(Employee employee, float deltaTime)
    {
        if (employee == null || employee.State == EmployeeState.Dead) return;

        var state = GetOrCreateState(employee);
        var movement = employee.GetComponent<EmployeeMovement>();

        // 대상이 없거나 이미 부서졌으면 새로 고른다
        if (state.target == null)
        {
            state.target = FindNearestTarget(employee);
            state.approaching = false;

            if (state.target == null)
            {
                // 부술 것이 주변에 없으면 그 자리에서 헛돈다
                if (movement != null && movement.IsMoving) movement.StopMoving();
                return;
            }

            Debug.Log($"[AbnormalBehavior] {employee.DisplayName}: 파괴 대상 → {state.target.buildingData?.buildingName}");
        }

        GetAttackProfile(employee, out float damage, out float interval, out float range);

        Vector3 targetPos = state.target.transform.position;
        float dist = Vector2.Distance(employee.transform.position, targetPos);

        if (dist > range)
        {
            ApproachTarget(movement, state, targetPos, deltaTime);
            return;
        }

        // 사거리 안 — 멈춰서 때린다
        if (state.approaching)
        {
            movement?.StopMoving();
            state.approaching = false;
        }

        state.attackTimer -= deltaTime;
        if (state.attackTimer > 0f) return;
        state.attackTimer = interval;

        int applied = Mathf.Max(1, Mathf.RoundToInt(damage));
        state.target.TakeDamage(applied);
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

        var state = new AttackState { attackTimer = 0f };
        states[employee] = state;
        return state;
    }

    /// <summary>대상 쪽으로 이동합니다. 경로는 REPATH_INTERVAL마다 다시 잡습니다.</summary>
    private static void ApproachTarget(EmployeeMovement movement, AttackState state, Vector3 targetPos, float deltaTime)
    {
        if (movement == null) return;

        state.repathTimer -= deltaTime;

        if (state.approaching && state.repathTimer > 0f) return;

        state.repathTimer = REPATH_INTERVAL;
        state.approaching = true;
        movement.MoveTo(targetPos);
    }

    /// <summary>
    /// 반경 내에서 가장 가까운 공격 대상 건물을 찾습니다.
    ///
    /// 제외 대상:
    ///   • 기반시설(벽·바닥) — 구조가 무너지면 이상 행동 하나로 기지가 마비된다
    ///   • 이미 기능이 꺼진 건물 — TakeDamage가 무시하므로 시간만 버린다
    /// 벽·바닥 중 타일로만 존재하는 것들은 애초에 Building이 아니라 자동으로 제외된다.
    /// </summary>
    private static Building FindNearestTarget(Employee employee)
    {
        var movement = employee.GetComponent<EmployeeMovement>();
        if (movement == null) return null;

        Vector2Int origin = movement.GetFootTile();
        Vector3 myPos = employee.transform.position;

        Building nearest = null;
        float nearestDist = float.MaxValue;

        for (int dx = -SEARCH_RADIUS; dx <= SEARCH_RADIUS; dx++)
        {
            for (int dy = -SEARCH_RADIUS; dy <= SEARCH_RADIUS; dy++)
            {
                var tile = new Vector2Int(origin.x + dx, origin.y + dy);

                var building = Building.GetBuildingAt(tile);
                if (building == null || building.buildingData == null) continue;
                if (building.buildingData.category == BuildingCategory.Infrastructure) continue;
                if (building.buildingData.maxHealth <= 0) continue;
                if (!building.IsFunctional) continue;

                float dist = Vector2.Distance(myPos, building.transform.position);
                if (dist >= nearestDist) continue;

                nearestDist = dist;
                nearest = building;
            }
        }

        return nearest;
    }

    #endregion
}
