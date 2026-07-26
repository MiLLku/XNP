using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 작업 종류별 적성(숙련) 레벨.
///
/// 통합 직원 레벨(EmployeeGrowth.Level)과 별개로, **해당 작업을 실제로 수행해야만** 오른다.
/// 채광을 해야 채광 적성이 오르고, 그 적성이 채광 스킬의 해금 조건이 된다.
///   채광 경험 → 채광 적성 레벨 → (스킬 포인트 소모) 스킬 해금 → 해당 광물 채광 가능
///
/// 레벨업 필요 경험치: EXP_BASE × 레벨 (Lv1→2: 60, 2→3: 120 …)
/// </summary>
[Serializable]
public class WorkAptitude
{
    /// <summary>레벨당 필요 경험치 계수</summary>
    private const int EXP_BASE = 60;

    /// <summary>적성 레벨 상한</summary>
    public const int MAX_LEVEL = 10;

    [Serializable]
    public class Entry
    {
        public WorkType workType;
        public int level = 1;
        public int experience;
    }

    [SerializeField] private List<Entry> entries = new List<Entry>();

    /// <summary>전체 항목 (저장·UI용)</summary>
    public IReadOnlyList<Entry> Entries => entries;

    /// <summary>해당 작업의 적성 레벨 (미기록이면 1).</summary>
    public int GetLevel(WorkType type)
    {
        var e = Find(type);
        return e != null ? e.level : 1;
    }

    /// <summary>해당 작업의 현재 경험치.</summary>
    public int GetExperience(WorkType type)
    {
        var e = Find(type);
        return e != null ? e.experience : 0;
    }

    /// <summary>다음 레벨까지 필요한 경험치.</summary>
    public static int ExpToNext(int level) => EXP_BASE * Mathf.Max(1, level);

    /// <summary>
    /// 작업 경험치를 더하고, 필요치를 넘으면 레벨업합니다.
    /// </summary>
    /// <returns>레벨업이 발생했으면 새 레벨, 아니면 0</returns>
    public int GainExperience(WorkType type, int amount)
    {
        if (amount <= 0) return 0;

        var e = Find(type);
        if (e == null)
        {
            e = new Entry { workType = type, level = 1, experience = 0 };
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

    /// <summary>저장 데이터로부터 복원합니다.</summary>
    public void Restore(List<Entry> saved)
    {
        entries = saved != null ? new List<Entry>(saved) : new List<Entry>();
    }

    private Entry Find(WorkType type)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].workType == type) return entries[i];
        return null;
    }
}
