using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 장비 데이터 ScriptableObject.
/// 일반 아이템을 장비로 확장하여 스탯 보정, 패시브 효과, 능력을 정의합니다.
///
/// 사용법:
///   1. Create > StampSystem > Equipment Data 로 생성
///   2. itemData에 기반 ItemData SO 할당
///   3. slot, statModifier, passiveEffects, abilities 설정
///   4. GameDatabase.allEquipmentData에 등록
/// </summary>
[CreateAssetMenu(fileName = "NewEquipment", menuName = "StampSystem/Equipment Data")]
public class EquipmentData : ScriptableObject
{
    [Header("기본 정보")]
    [Tooltip("기반 아이템 데이터 (ID, 이름, 아이콘)")]
    public ItemData itemData;

    [TextArea(2, 4)]
    [Tooltip("장비 설명")]
    public string description;

    [Tooltip("장착 슬롯")]
    public EquipmentSlot slot;

    [Header("전투 (무기/방어구)")]
    [Tooltip("무기 분류 — 가용 전투 태세 결정 (근접: 점거/방어/경계, 원거리: 점거/경계/카이팅)")]
    public WeaponClass weaponClass = WeaponClass.Melee;

    [Tooltip("공격 사거리 (타일). 근접 ~1.5, 원거리 6~8")]
    [Min(0.5f)] public float attackRange = 1.5f;

    [Tooltip("공격 간격 (초)")]
    [Min(0.1f)] public float attackInterval = 1.2f;

    [Tooltip("방어형 장비 — 보유 시 '방어' 태세 활성 (감쇄 증폭 + 어그로)")]
    public bool isDefensiveGear = false;

    [Header("내구도")]
    [Tooltip("최대 내구도. 작업 마모·전투 소모로 감소하며 0이 되면 파괴됩니다.")]
    [Min(1)] public float maxDurability = 100f;

    [Tooltip("파괴 불가 (유니크 장비) — 내구도가 감소하지 않습니다.")]
    public bool indestructible = false;

    [Tooltip("작업 중 초당 마모량 (0.02 = 100 내구도 기준 약 83분 작업)")]
    [Min(0f)] public float workWearPerSecond = 0.02f;

    [Tooltip("전투 소모량 — 무기: 공격 1회당 / 방어구: 피격 1회당")]
    [Min(0f)] public float combatWearPerHit = 1f;

    [Header("스탯 보정")]
    [Tooltip("장착 시 직원 스탯 변경")]
    public EquipmentStatModifier statModifier;

    [Header("패시브 효과")]
    [Tooltip("항상 적용되는 특성")]
    public EquipmentPassiveEffects passiveEffects;

    [Header("능력")]
    [Tooltip("액티브/트리거 기반 스킬 목록")]
    public List<EquipmentAbility> abilities = new List<EquipmentAbility>();
}

/// <summary>
/// 장비 스탯 보정.
/// 장착 시 직원의 기본 스탯에 가감합니다.
/// </summary>
[Serializable]
public struct EquipmentStatModifier
{
    [Tooltip("최대 체력 변경 (절대값)")]
    public float maxHealthModifier;

    [Tooltip("최대 정신력 변경 (절대값)")]
    public float maxMentalModifier;

    [Tooltip("공격력 변경 (절대값)")]
    public int attackPowerModifier;

    [Tooltip("전체 작업 속도 보정 (%)")]
    public float workSpeedModifier;

    [Tooltip("배고픔 감소 속도 보정 (%)")]
    public float hungerRateModifier;

    [Tooltip("피로 증가 속도 보정 (%)")]
    public float fatigueRateModifier;

    [Tooltip("이동 속도 보정 (%)")]
    public float moveSpeedModifier;

    /// <summary>모든 값이 0인지 확인</summary>
    public bool IsEmpty =>
        maxHealthModifier == 0f && maxMentalModifier == 0f && attackPowerModifier == 0 &&
        workSpeedModifier == 0f && hungerRateModifier == 0f && fatigueRateModifier == 0f &&
        moveSpeedModifier == 0f;
}

/// <summary>
/// 장비 패시브 효과.
/// 장착 중 항상 적용되는 특성입니다.
/// </summary>
[Serializable]
public class EquipmentPassiveEffects
{
    [Header("환경")]
    [Tooltip("야간 작업 가능")]
    public bool enableNightWork;

    [Tooltip("비 보호")]
    public bool rainProtection;

    [Header("전투")]
    [Tooltip("피해 감소 (%)")]
    [Range(0, 100)]
    public float damageReduction;

    [Header("자원")]
    [Tooltip("추가 산출 확률 (0~1)")]
    [Range(0, 1)]
    public float bonusYieldChance;

    [Header("작업")]
    [Tooltip("작업 타입별 속도 보정")]
    public WorkSpeedModifier[] workTypeModifiers;

    [Header("침식 방어")]
    [Tooltip("침식 오라 무시 수치. 개체의 aoeErosionPerSecond가 이 값 이하이면 오라를 차단합니다.")]
    [Min(0)]
    public float erosionIgnoreValue;
}

/// <summary>
/// 장비 능력 (액티브/트리거 스킬).
/// 발동 조건에 따라 자동 또는 수동으로 발동됩니다.
/// </summary>
[Serializable]
public class EquipmentAbility
{
    [Header("기본 정보")]
    [Tooltip("능력 이름")]
    public string abilityName;

    [TextArea(1, 3)]
    [Tooltip("능력 설명")]
    public string description;

    [Tooltip("아이콘")]
    public Sprite icon;

    [Header("발동 조건")]
    [Tooltip("발동 조건 타입")]
    public AbilityTriggerType triggerType;

    [Tooltip("패시브 트리거 발동 확률 (0~1, Active는 무시)")]
    [Range(0, 1)]
    public float triggerChance = 1f;

    [Header("효과")]
    [Tooltip("효과 종류")]
    public AbilityEffectType effectType;

    [Tooltip("효과 수치 (피해량, 회복량, 보호막 HP 등)")]
    public float effectValue;

    [Tooltip("효과 범위 (AoE 계열)")]
    public float effectRadius;

    [Tooltip("효과 지속 시간 (보호막, 버프 등)")]
    public float duration;

    [Header("쿨다운")]
    [Tooltip("재사용 대기 시간 (초)")]
    public float cooldown = 10f;
}
