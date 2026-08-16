using UnityEngine;

/// <summary>
/// 직원 스탯 데이터 구조체.
/// 체력·정신력 등 직원의 생존 관련 수치를 저장합니다.
///
/// <b>공격력(attackPower)은 2026-07-29 개편으로 제거됐습니다.</b>
/// 데미지·명중률·공격 간격·사거리·관통력은 전적으로 <b>장비(무기)</b>가 결정하고,
/// 직원 쪽에서는 근접/원거리 <b>숙련</b>(EmployeeGrowth.CombatAptitude)이 그 값들을 조정합니다.
/// </summary>
[System.Serializable]
public struct EmployeeStats
{
    /// <summary>현재 체력</summary>
    public float health;

    /// <summary>최대 체력</summary>
    public float maxHealth;

    /// <summary>현재 정신력 — 기본값 + 활성 모디파이어에서 파생되는 값</summary>
    public float mental;

    /// <summary>최대 정신력 (상한)</summary>
    public float maxMental;

    /// <summary>침식 수치 — 제노프스 등의 영향으로 부여되는 디버프 값 (0 = 없음)</summary>
    public float erosionLevel;
}
