using UnityEngine;

/// <summary>
/// 직원 스탯 데이터 구조체.
/// 체력, 정신력, 공격력 등 직원의 전투/생존 관련 수치를 저장합니다.
/// </summary>
[System.Serializable]
public struct EmployeeStats
{
    /// <summary>현재 체력</summary>
    public float health;

    /// <summary>최대 체력</summary>
    public float maxHealth;

    /// <summary>현재 정신력</summary>
    public float mental;

    /// <summary>최대 정신력</summary>
    public float maxMental;

    /// <summary>공격력</summary>
    public int attackPower;

    /// <summary>침식 수치 — 제노프스 등의 영향으로 부여되는 디버프 값 (0 = 없음)</summary>
    public float erosionLevel;
}
