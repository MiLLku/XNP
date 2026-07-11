/// <summary>
/// 소집(Draft) 중 전투 태세. 가용 태세는 장착 무기 타입이 결정한다.
///   근접: 점거 / 방어(방어형 장비 보유 시) / 경계
///   원거리: 점거 / 경계 / 카이팅
/// </summary>
public enum CombatStance
{
    /// <summary>점거 — 위치 고정, 자기 사거리 내 적만 공격 (이동 없음)</summary>
    HoldPosition = 0,

    /// <summary>방어 — 공격 안 함, 감쇄 증폭 + 적 어그로 (방어형 장비 필요)</summary>
    Defend = 1,

    /// <summary>경계 — 경계 반경(기본값+특성 가감) 내 적 접근·교전, 이탈 시 복귀 (기본 태세)</summary>
    Guard = 2,

    /// <summary>카이팅 — 원거리 전용. 최소 거리 유지 후퇴 + 사거리 내 사격</summary>
    Kiting = 3,
}

/// <summary>무기 분류 — 가용 태세와 교전 방식 결정.</summary>
public enum WeaponClass
{
    /// <summary>근접 (기본, 무기 없음 포함)</summary>
    Melee = 0,

    /// <summary>원거리 (투사체)</summary>
    Ranged = 1,
}
