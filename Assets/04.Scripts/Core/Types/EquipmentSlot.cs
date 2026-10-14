using System;

/// <summary>
/// 장비 슬롯 종류 — 직원 한 명이 7칸을 동시에 착용합니다.
/// 방어력·견딜 수 있는 온도·방한·견딜 수 있는 침식은 7칸 전부 합산합니다.
/// 값은 세이브에 int로 저장되므로 <b>순서를 바꾸지 말고 뒤에만 추가</b>하세요.
/// </summary>
public enum EquipmentSlot
{
    /// <summary>옷 — 방호복·내열 슈트·기본 옷 (환경 보호)</summary>
    Suit,

    /// <summary>헬멧 — 방호 헬멧·기본 모자</summary>
    Helmet,

    /// <summary>무기 — 공격/전투</summary>
    Weapon,

    /// <summary>다용도구 — 1회용 도구 (조건이 되면 자동 사용 후 사라짐)</summary>
    MultiTool,

    /// <summary>장갑 — 작업 속도·소량의 내성</summary>
    Gloves,

    /// <summary>신발 — 이동 속도·물속 감속 무시·소량의 내성</summary>
    Shoes,

    /// <summary>보호구 — 갑옷류 (피해 감소, 옷과 합산)</summary>
    Protector
}

/// <summary>장비 칸 표시 이름</summary>
public static class EquipmentSlotNames
{
    public static string Label(EquipmentSlot slot)
    {
        switch (slot)
        {
            case EquipmentSlot.Suit:      return "옷";
            case EquipmentSlot.Helmet:    return "헬멧";
            case EquipmentSlot.Weapon:    return "무기";
            case EquipmentSlot.MultiTool: return "다용도구";
            case EquipmentSlot.Gloves:    return "장갑";
            case EquipmentSlot.Shoes:     return "신발";
            case EquipmentSlot.Protector: return "보호구";
            default:                      return slot.ToString();
        }
    }
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
