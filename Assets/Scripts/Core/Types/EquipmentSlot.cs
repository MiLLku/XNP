using System;

/// <summary>
/// 장비 슬롯 종류.
/// 각 직원은 4개 슬롯에 장비를 장착할 수 있습니다.
/// </summary>
public enum EquipmentSlot
{
    /// <summary>슈트 — 방어/환경 보호</summary>
    Suit,

    /// <summary>헬멧 — 정신력/센서</summary>
    Helmet,

    /// <summary>무기 — 공격/전투</summary>
    Weapon,

    /// <summary>다용도구 — 작업 효율</summary>
    MultiTool
}

/// <summary>
/// 장비 슬롯에 장착된 장비의 출처.
/// </summary>
public enum EquipmentSourceType
{
    /// <summary>비어 있음</summary>
    None,

    /// <summary>일반 장비 아이템 (EquipmentData)</summary>
    Item,

    /// <summary>장비형 제노프스</summary>
    Xenops
}

/// <summary>
/// 장비 능력 발동 조건.
/// </summary>
public enum AbilityTriggerType
{
    /// <summary>플레이어가 수동 발동</summary>
    Active,

    /// <summary>공격 시 자동 발동</summary>
    OnHit,

    /// <summary>피격 시 자동 발동</summary>
    OnTakeDamage,

    /// <summary>주기적 자동 발동</summary>
    Periodic,

    /// <summary>작업 완료 시 자동 발동</summary>
    OnWorkComplete
}

/// <summary>
/// 장비 능력 효과 종류.
/// </summary>
public enum AbilityEffectType
{
    /// <summary>범위 피해</summary>
    AoEDamage,

    /// <summary>일시적 보호막 (duration 동안 피해 흡수)</summary>
    TemporaryShield,

    /// <summary>체력/정신력 회복</summary>
    Heal,

    /// <summary>일시적 작업/이동 속도 증가</summary>
    SpeedBurst,

    /// <summary>자원 2배 산출</summary>
    ResourceDuplicate,

    /// <summary>범위 내 적대 Xenops 기절</summary>
    Stun,

    /// <summary>지속 회복 (HP/Mental)</summary>
    Regeneration
}
