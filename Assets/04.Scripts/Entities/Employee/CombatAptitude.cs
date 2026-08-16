using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전투 숙련 종류. 장착한 무기의 분류(WeaponClass)에 대응한다.
/// </summary>
public enum CombatSkillType
{
    /// <summary>근접 — 근접 무기와 맨손</summary>
    Melee = 0,

    /// <summary>원거리 — 투사체 무기</summary>
    Ranged = 1,
}

/// <summary>
/// 직원의 근접·원거리 숙련.
///
/// 설계 원칙(2026-07-29 확정): <b>데미지·명중률·공격 간격·사거리·관통력은 전적으로 '장비'가 결정한다.</b>
/// 직원의 숙련 수치는 그 값들을 <b>조정</b>할 뿐이며, 직원 자체가 공격력을 갖지는 않는다.
/// (구 EmployeeStats.attackPower를 대체)
///
/// 상승 경로 2가지:
///   • 전투 수행 — 실제로 적을 공격하면 오른다 (WorkAptitude가 작업 수행으로 오르는 것과 같은 방식)
///   • 특수 아이템 — 훈련 혈청 등으로 즉시 상승
///
/// 레벨업 필요 경험치: EXP_BASE × 레벨 (Lv1→2: 40, 2→3: 80 …)
/// </summary>
[Serializable]
public class CombatAptitude
{
    /// <summary>레벨당 필요 경험치 계수</summary>
    private const int EXP_BASE = 40;

    /// <summary>숙련 레벨 상한</summary>
    public const int MAX_LEVEL = 10;

    [Serializable]
    public class Entry
    {
        public CombatSkillType skillType;
        public int level = 1;
        public int experience;
    }

    [SerializeField] private List<Entry> entries = new List<Entry>();

    /// <summary>전체 항목 (저장·UI용)</summary>
    public IReadOnlyList<Entry> Entries => entries;

    /// <summary>해당 숙련 레벨 (미기록이면 1).</summary>
    public int GetLevel(CombatSkillType type)
    {
        var e = Find(type);
        return e != null ? e.level : 1;
    }

    /// <summary>해당 숙련의 현재 경험치.</summary>
    public int GetExperience(CombatSkillType type)
    {
        var e = Find(type);
        return e != null ? e.experience : 0;
    }

    /// <summary>다음 레벨까지 필요한 경험치.</summary>
    public static int ExpToNext(int level) => EXP_BASE * Mathf.Max(1, level);

    /// <summary>
    /// 전투 경험치를 더하고, 필요치를 넘으면 레벨업합니다.
    /// </summary>
    /// <returns>레벨업이 발생했으면 새 레벨, 아니면 0</returns>
    public int GainExperience(CombatSkillType type, int amount)
    {
        if (amount <= 0) return 0;

        var e = Find(type);
        if (e == null)
        {
            e = new Entry { skillType = type, level = 1, experience = 0 };
            entries.Add(e);
        }

        if (e.level >= MAX_LEVEL) return 0;

        e.experience += amount;

        int leveled = 0;
        while (e.level < MAX_LEVEL && e.experience >= ExpToNext(e.level))
        {
            e.experience -= ExpToNext(e.level);
            e.level++;
            leveled = e.level;
        }

        if (e.level >= MAX_LEVEL) e.experience = 0;
        return leveled;
    }

    /// <summary>
    /// 숙련 레벨을 직접 올립니다 (훈련 혈청 등 특수 아이템 전용).
    /// </summary>
    /// <returns>실제로 오른 레벨 수 (상한에 걸리면 요청보다 적을 수 있음)</returns>
    public int RaiseLevel(CombatSkillType type, int levels)
    {
        if (levels <= 0) return 0;

        var e = Find(type);
        if (e == null)
        {
            e = new Entry { skillType = type, level = 1, experience = 0 };
            entries.Add(e);
        }

        int before = e.level;
        e.level = Mathf.Min(MAX_LEVEL, e.level + levels);
        if (e.level >= MAX_LEVEL) e.experience = 0;

        return e.level - before;
    }

    /// <summary>초기 레벨을 설정합니다 (템플릿 적용용).</summary>
    public void SetLevel(CombatSkillType type, int level)
    {
        level = Mathf.Clamp(level, 1, MAX_LEVEL);

        var e = Find(type);
        if (e == null)
        {
            entries.Add(new Entry { skillType = type, level = level, experience = 0 });
            return;
        }
        e.level = level;
        e.experience = 0;
    }

    /// <summary>저장 데이터로부터 복원합니다.</summary>
    public void Restore(List<Entry> saved)
    {
        entries = saved != null ? new List<Entry>(saved) : new List<Entry>();
    }

    /// <summary>무기 분류에 대응하는 숙련 종류를 반환합니다. 맨손은 근접입니다.</summary>
    public static CombatSkillType FromWeaponClass(WeaponClass wc)
        => wc == WeaponClass.Ranged ? CombatSkillType.Ranged : CombatSkillType.Melee;

    private Entry Find(CombatSkillType type)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].skillType == type) return entries[i];
        return null;
    }
}
