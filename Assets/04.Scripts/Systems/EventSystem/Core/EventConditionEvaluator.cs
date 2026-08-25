using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 이벤트 조건 평가기
/// 각 조건 타입에 따라 현재 게임 상태를 평가합니다.
/// </summary>
public static class EventConditionEvaluator
{
    /// <summary>
    /// 모든 조건을 만족하는지 확인
    /// </summary>
    public static bool CheckAllConditions(List<EventCondition> conditions)
    {
        if (conditions == null || conditions.Count == 0)
            return true;

        foreach (var condition in conditions)
        {
            if (!EvaluateCondition(condition))
                return false;
        }

        return true;
    }

    /// <summary>
    /// 개별 조건 평가
    /// </summary>
    public static bool EvaluateCondition(EventCondition condition)
    {
        float actualValue = GetConditionValue(condition.type, condition.targetId);
        return Compare(actualValue, condition.comparison, condition.value);
    }

    /// <summary>
    /// 조건 타입에 따른 현재 값 조회
    /// </summary>
    private static float GetConditionValue(ConditionType type, int targetId = 0)
    {
        switch (type)
        {
            // ===== 직원 관련 =====
            case ConditionType.EmployeeCount:
                return GetEmployeeCount();

            case ConditionType.EmployeeAverageHealth:
                return GetEmployeeAverageStat(e => e.Stats.health);

            case ConditionType.EmployeeAverageMental:
                return GetEmployeeAverageStat(e => e.Stats.mental);

            case ConditionType.EmployeeAverageHunger:
                return GetEmployeeAverageStat(e => e.Needs.hunger);

            case ConditionType.EmployeeAverageFatigue:
                return GetEmployeeAverageStat(e => e.Needs.fatigue);

            case ConditionType.EmployeeWorkingCount:
                return GetEmployeeCountByState(EmployeeState.Working);

            case ConditionType.EmployeeIdleCount:
                return GetEmployeeCountByState(EmployeeState.Idle);

            // ===== 자원 관련 =====
            case ConditionType.InventoryItemCount:
                return GetInventoryItemCount(targetId);

            case ConditionType.TotalInventoryCount:
                return GetTotalInventoryCount();

            // ===== 건물 관련 =====
            case ConditionType.BuildingCount:
                return GetBuildingCount();

            case ConditionType.ConstructionCount:
                return GetConstructionCount();

            // ===== 시간 관련 =====
            case ConditionType.PlayTime:
                return Time.time;

            case ConditionType.DayNumber:
                return GetDayNumber();

            // ===== 이벤트 관련 =====
            case ConditionType.ActivePersistentEvent:
                return IsEventActive(targetId) ? 1f : 0f;

            case ConditionType.EventTriggeredBefore:
                return WasEventTriggered(targetId) ? 1f : 0f;

            // ===== 기타 =====
            case ConditionType.RandomChance:
                return Random.Range(0f, 100f);

            case ConditionType.Always:
                return 1f;

            default:
                Debug.LogWarning($"[EventConditionEvaluator] 알 수 없는 조건 타입: {type}");
                return 0f;
        }
    }

    /// <summary>
    /// 비교 연산 수행
    /// </summary>
    private static bool Compare(float actual, ComparisonOperator op, float expected)
    {
        return op switch
        {
            ComparisonOperator.GreaterThan => actual > expected,
            ComparisonOperator.GreaterOrEqual => actual >= expected,
            ComparisonOperator.LessThan => actual < expected,
            ComparisonOperator.LessOrEqual => actual <= expected,
            ComparisonOperator.Equal => Mathf.Approximately(actual, expected),
            ComparisonOperator.NotEqual => !Mathf.Approximately(actual, expected),
            _ => false
        };
    }

    #region 헬퍼 메서드

    private static int GetEmployeeCount()
    {
        if (EmployeeManager.instance == null) return 0;
        return EmployeeManager.instance.EmployeeCount;
    }

    private static float GetEmployeeAverageStat(System.Func<Employee, float> selector)
    {
        if (EmployeeManager.instance == null) return 0f;

        var employees = EmployeeManager.instance.AllEmployees;
        if (employees == null || employees.Count == 0) return 0f;

        return employees.Average(selector);
    }

    private static int GetEmployeeCountByState(EmployeeState state)
    {
        if (EmployeeManager.instance == null) return 0;

        return EmployeeManager.instance.AllEmployees
            .Count(e => e != null && e.State == state);
    }

    private static int GetInventoryItemCount(int itemId)
    {
        if (InventoryManager.instance == null) return 0;

        // GameDatabase에서 ItemData 찾기
        if (GameDatabase.Instance == null) return 0;

        ItemData itemData = GameDatabase.Instance.GetItemData(itemId);
        if (itemData == null) return 0;

        return InventoryManager.instance.GetItemCount(itemData);
    }

    private static int GetTotalInventoryCount()
    {
        if (InventoryManager.instance == null) return 0;
        return InventoryManager.instance.GetTotalItemCount();
    }

    private static int GetBuildingCount()
    {
        return UnityEngine.Object.FindObjectsByType<Building>().Length;
    }

    private static int GetConstructionCount()
    {
        return UnityEngine.Object.FindObjectsByType<ConstructionSite>().Length;
    }

    private static int GetDayNumber()
    {
        // TODO: 시간 시스템과 연동
        // 임시로 플레이 시간을 일수로 변환 (1일 = 600초 = 10분)
        return Mathf.FloorToInt(Time.time / 600f) + 1;
    }

    private static bool IsEventActive(int eventId)
    {
        if (EventManager.instance == null) return false;
        return EventManager.instance.IsEventActive(eventId);
    }

    private static bool WasEventTriggered(int eventId)
    {
        if (EventManager.instance == null) return false;
        return EventManager.instance.WasEventTriggered(eventId);
    }

    #endregion
}
