/// <summary>
/// 제노프스 타입 분류.
/// 새 타입 추가 시 이 enum에 값을 추가하고,
/// IXenopsBehavior를 구현하는 MonoBehaviour를 작성합니다.
/// </summary>
public enum XenopsType
{
    /// <summary>환경 간섭 — 주변 타일/건물에 영향</summary>
    Environmental,

    /// <summary>적대적 생명체 — 직원/건물에 피해</summary>
    Hostile,

    /// <summary>잠입체 — 건물에 잠입하여 간섭</summary>
    Infiltrator,

    /// <summary>장비형 — 직원에 장착하여 보정</summary>
    Equipment
}

/// <summary>
/// 제노프스 난이도 등급.
/// 등급이 높을수록 강력하며 희귀합니다.
/// </summary>
public enum XenopsGrade
{
    /// <summary>F등급 — 가장 낮음, 매우 흔함</summary>
    F,
    /// <summary>E등급 — 낮음</summary>
    E,
    /// <summary>D등급 — 보통 이하</summary>
    D,
    /// <summary>C등급 — 보통</summary>
    C,
    /// <summary>B등급 — 보통 이상</summary>
    B,
    /// <summary>A등급 — 강함</summary>
    A,
    /// <summary>S등급 — 최강, 매우 희귀</summary>
    S
}

/// <summary>
/// 제노프스 상태.
/// </summary>
public enum XenopsState
{
    /// <summary>대기 — 아무 효과 없음</summary>
    Idle,

    /// <summary>활성 — 효과 적용 중</summary>
    Active,

    /// <summary>제압됨 — 적대적 생명체가 제압된 상태</summary>
    Subdued,

    /// <summary>장착됨 — 장비형이 직원에 장착된 상태</summary>
    Equipped,

    /// <summary>휴면 — 비활성 (플레이어가 비활성화)</summary>
    Dormant
}
