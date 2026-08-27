/// <summary>
/// 작업 타입 카테고리 헬퍼.
/// 작업 할당 방식을 결정합니다.
///
/// - AutoPickup: 단순 노동 작업. 자격을 갖춘 모든 직원이 우선순위에 따라 자동으로 작업을 가져갑니다.
///   별도의 직원 지정 없이 작업 명령만 생성하면 됩니다 (채광, 건설, 벌목, 운반 등).
///
/// - Dedicated: 침식 노출 위험 등 특수 작업. 플레이어가 명시적으로 직원을 지정해야 합니다.
///   지정된 직원만 해당 작업을 수행할 수 있습니다 (연구, 제작 등).
/// </summary>
public static class WorkTypeCategory
{
    /// <summary>
    /// 자동 픽업 작업인지 여부를 반환합니다.
    /// 자동 픽업 작업은 자격을 갖춘 직원이 우선순위에 따라 자유롭게 가져갈 수 있습니다.
    /// </summary>
    public static bool IsAutoPickup(WorkType type)
    {
        switch (type)
        {
            case WorkType.Mining:
            case WorkType.Chopping:
            case WorkType.Building:
            case WorkType.Demolish:
            case WorkType.Cleaning:
            case WorkType.Hauling:
            case WorkType.Gardening:
                return true;

            case WorkType.Crafting:
            case WorkType.Research:
                return false;

            // 작업 외 활동 (휴식/식사/없음 등)
            default:
                return false;
        }
    }

    /// <summary>
    /// 전용 직원 할당이 필요한 작업인지 여부를 반환합니다.
    /// 침식 노출 위험이 있는 작업은 플레이어가 직원을 직접 지정해야 합니다.
    /// </summary>
    public static bool RequiresDedicatedAssignment(WorkType type)
    {
        switch (type)
        {
            case WorkType.Crafting:
            case WorkType.Research:
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// 사용자 친화 표시명을 반환합니다 (UI 표시용).
    /// </summary>
    public static string GetDisplayName(WorkType type)
    {
        switch (type)
        {
            case WorkType.None:      return "없음";
            case WorkType.Mining:    return "채광";
            case WorkType.Chopping:  return "벌목";
            case WorkType.Research:  return "연구";
            case WorkType.Crafting:  return "제작";
            case WorkType.Gardening: return "원예";
            case WorkType.Hauling:   return "운반";
            case WorkType.Building:  return "건설";
            case WorkType.Demolish:  return "철거";
            case WorkType.Cleaning:  return "세척";
            case WorkType.Resting:   return "휴식";
            case WorkType.Eating:    return "식사";
            default:                 return type.ToString();
        }
    }
}
