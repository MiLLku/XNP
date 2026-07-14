using UnityEngine;

/// <summary>
/// 적 행동(Behavior)들의 직원 타겟 선정 공통 헬퍼.
///
/// 방어(Defend) 태세 직원은 유효 거리를 CombatConfig.defendAggroWeight로 나눠
/// 실제보다 가깝게 취급된다 (어그로 — 방어 직원이 우선 타겟).
/// 새 적 행동을 추가할 때 직원 타겟이 필요하면 FindNearestEmployee 직접 구현 대신
/// PickEmployeeTarget을 사용할 것.
/// </summary>
public static class CombatTargeting
{
    /// <summary>어그로 가중치가 적용된 유효 거리 (방어 태세면 거리 ÷ defendAggroWeight).</summary>
    public static float EffectiveDistance(Vector2 from, Employee emp)
    {
        float d = Vector2.Distance(from, emp.transform.position);

        if (emp.Combat != null && emp.Combat.IsDefending)
        {
            var cfg = EmployeeManager.instance != null ? EmployeeManager.instance.CombatConfig : null;
            float w = cfg != null ? cfg.defendAggroWeight : 3f;
            if (w > 1f) d /= w;
        }
        return d;
    }

    /// <summary>
    /// 어그로 가중 최근접 직원을 선정합니다 (사망 제외).
    /// </summary>
    /// <param name="from">탐색 기준점 (적 위치)</param>
    /// <param name="maxRadius">실거리 기준 탐색 반경 제한 (기본: 무제한)</param>
    public static Employee PickEmployeeTarget(Vector2 from, float maxRadius = float.MaxValue)
    {
        if (EmployeeManager.instance == null) return null;

        Employee best = null;
        float bestScore = float.MaxValue;

        foreach (var emp in EmployeeManager.instance.AllEmployees)
        {
            if (emp == null || emp.State == EmployeeState.Dead) continue;
            if (Vector2.Distance(from, emp.transform.position) > maxRadius) continue;

            float score = EffectiveDistance(from, emp);
            if (score < bestScore) { bestScore = score; best = emp; }
        }
        return best;
    }
}
