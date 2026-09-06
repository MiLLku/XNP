/// <summary>
/// 소집(Draft) 중 전투 태세. 가용 태세는 장착 무기 타입이 결정한다.
///   근접: 점거 / 방어(방어형 장비 보유 시) / 경계
///   원거리: 점거 / 경계 / 카이팅
/// </summary>
public enum CombatStance
{
    HoldPosition = 0, //점거
    Defend = 1, //방어
    Guard = 2, //경계
    Kiting = 3, //카이팅
}

/// <summary>무기 분류 — 가용 태세와 교전 방식 결정.</summary>
public enum WeaponClass
{
    Melee = 0, //근거리
    Ranged = 1, //원거리
}
