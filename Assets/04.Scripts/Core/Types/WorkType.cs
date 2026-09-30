/// <summary>
/// 작업 종류 열거형.
/// 직원의 작업 할당, 우선순위 설정, 작업 능력 판단에 사용됩니다.
/// </summary>
public enum WorkType
{
    /// <summary>작업 없음 (기본값)</summary>
    None,
    /// <summary>채광</summary>
    Mining,
    /// <summary>제초 — 수확·벌목·제초(뿌리 제거) 명령을 모두 처리. 원예 능력치 사용. (구 Chopping, 정수값 2 유지)</summary>
    Weeding,
    /// <summary>연구</summary>
    Research,
    /// <summary>제작</summary>
    Crafting,
    /// <summary>파종 — 묘목 심기. 원예 능력치 사용. (구 Gardening, 정수값 5 유지)</summary>
    Sowing,
    /// <summary>운반</summary>
    Hauling,
    /// <summary>건설</summary>
    Building,
    /// <summary>철거 — 건설 작업에 합쳐져 더 쓰지 않음 (구 세이브·주문 호환용으로만 남김, 정수값 유지)</summary>
    Demolish,
    /// <summary>세척 (방에 고인 침식 제거)</summary>
    Cleaning,
    /// <summary>휴식</summary>
    Resting,
    /// <summary>식사</summary>
    Eating,
    /// <summary>요양 — 체력이 낮으면 요양 시설에서 회복 (작업물 없이 EmployeeAI가 직접 수행)</summary>
    Recuperation,
    /// <summary>조리 — 식재료를 모아 스토브에서 요리. 결격 가능, 능력은 조리 속도만.</summary>
    Cooking,
    /// <summary>단련 — 단련장에서 레벨업. 누구나 가능, 결격·능력과 무관 (지정된 직원만 맡음)</summary>
    Training
}

/// <summary>
/// 작업 종류의 기본 순서 — 단일 출처.
///
/// 플레이어가 직원 UI에서 박스를 드래그해 바꾸기 전까지의 초기 순서입니다.
/// 우선순위는 '줄에서의 위치'이고, 저장되는 값은 그 인덱스입니다
/// (EmployeeWork.InitializeWorkPriorities / NormalizeOrder).
///
/// 앞에 있을수록 먼저 수행. 여기 순서를 바꾸면 새 직원의 초기값이 바뀝니다.
/// </summary>
public static class WorkTypeDefaults
{
    /// <summary>자동 픽업 작업의 기본 우선순위 순서 (앞에 있을수록 먼저).</summary>
    public static readonly WorkType[] BaseOrder =
    {
        WorkType.Recuperation,   // 맨 앞: 기본값에선 요양이 가장 먼저
        WorkType.Mining,
        WorkType.Weeding,
        WorkType.Cooking,
        WorkType.Training,
        WorkType.Crafting,
        WorkType.Research,
        WorkType.Sowing,
        WorkType.Cleaning,
        WorkType.Hauling,
        WorkType.Building,
    };
}
