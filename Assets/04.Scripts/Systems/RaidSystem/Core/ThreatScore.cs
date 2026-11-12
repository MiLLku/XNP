using System;
using UnityEngine;

/// <summary>
/// 습격 위협 점수 — 기본 + Σ(항목 값 × 난이도 배율), 그 뒤 적응 보정을 곱한다.
/// 항목: 경과 일수 · 기지 재산(건물 건설 비용 + 창고 자원 + 장비) · 전투력(무기·방어구·전투 숙련, 머릿수 아님) ·
///       최대 깊이 단계 · 기지 폭(플레이어 건물의 좌우 끝 사이 칸 수).
/// 마리 수는 RaidManager가 이 점수를 습격 종류의 한 마리당 점수로 나눠 정한다.
/// </summary>
public static class ThreatScore
{
    /// <summary>장비 하나의 재산 가치 (임시 — 장비에 가치 값이 없어 고정)</summary>
    public const float EQUIPMENT_VALUE = 10f;

    /// <summary>전투 숙련 1레벨의 전투력</summary>
    public const float COMBAT_LEVEL_VALUE = 5f;

    [Serializable]
    public struct Breakdown
    {
        public float day, wealth, combat, stage, width;   // 항목 값
        public float adapt;                                // 적응 보정 배율
        public float baseScore, total;
        public string difficulty;

        // 항목별 기여 (값 × 배율)
        public float dayPts, wealthPts, combatPts, stagePts, widthPts;

        public override string ToString()
            => $"위협 {total:F0} ({difficulty}) = [기본 {baseScore:F0} + 일수 {day:F1}→{dayPts:F1} + 재산 {wealth:F0}→{wealthPts:F1} + 전투력 {combat:F0}→{combatPts:F1}"
             + $" + 깊이 {stage:F0}단→{stagePts:F1} + 폭 {width:F0}칸→{widthPts:F1}] × 적응 {adapt:F2}";
    }

    /// <summary>지금 기지 상태로 점수를 낸다</summary>
    public static Breakdown Compute(ThreatDifficulty d, float adapt, int stage)
    {
        float day = DayCycle.instance != null ? DayCycle.instance.Day : 1;
        return Compute(d, adapt, day, Wealth(), Combat(), stage, BaseWidth());
    }

    /// <summary>항목 값을 직접 넣어 점수를 낸다 (테스트·표 뽑기용)</summary>
    public static Breakdown Compute(ThreatDifficulty d, float adapt, float day, float wealth, float combat, int stage, float width)
    {
        var b = new Breakdown { day = day, wealth = wealth, combat = combat, stage = stage, width = width, adapt = adapt };
        if (d == null) { b.total = 20f; b.difficulty = "-"; return b; }
        b.difficulty = d.displayName;
        b.baseScore = d.baseScore;
        b.dayPts = day * d.perDay;
        b.wealthPts = wealth * d.perWealth;
        b.combatPts = combat * d.perCombat;
        b.stagePts = stage * d.perDepthStage;
        b.widthPts = width * d.perWidth;
        b.total = (b.baseScore + b.dayPts + b.wealthPts + b.combatPts + b.stagePts + b.widthPts) * adapt;
        return b;
    }

    /// <summary>기지 재산 = 기능하는 플레이어 건물의 건설 비용 합 + 창고 자원 수 + 장비(보관소 + 착용) × EQUIPMENT_VALUE</summary>
    public static float Wealth()
    {
        float w = 0f;
        foreach (var b in UnityEngine.Object.FindObjectsByType<Building>(FindObjectsSortMode.None))
        {
            if (b == null || b.buildingData == null || !b.IsFunctional || b.buildingData.requiredResources == null) continue;
            foreach (var c in b.buildingData.requiredResources) w += c.amount;
        }

        if (InventoryManager.instance != null)
            foreach (var kv in InventoryManager.instance.GetAllItems()) w += kv.Value;

        int equipment = EquipmentStorageManager.instance != null ? EquipmentStorageManager.instance.PooledCount : 0;
        if (EmployeeManager.instance != null)
            foreach (var e in EmployeeManager.instance.AllEmployees)
            {
                if (e == null || e.State == EmployeeState.Dead) continue;
                var eq = e.GetComponent<EmployeeEquipment>();
                if (eq == null) continue;
                foreach (EquipmentSlot s in Enum.GetValues(typeof(EquipmentSlot)))
                    if (eq.GetItemInSlot(s) != null) equipment++;
            }
        return w + equipment * EQUIPMENT_VALUE;
    }

    /// <summary>전투력 = 살아 있는 직원마다 (무기 기본 피해 + 방어구 피해 감소% + 근접·원거리 숙련 × COMBAT_LEVEL_VALUE)</summary>
    public static float Combat()
    {
        float c = 0f;
        if (EmployeeManager.instance == null) return c;
        foreach (var e in EmployeeManager.instance.AllEmployees)
        {
            if (e == null || e.State == EmployeeState.Dead) continue;
            var eq = e.GetComponent<EmployeeEquipment>();
            if (eq != null)
            {
                var weapon = eq.GetItemInSlot(EquipmentSlot.Weapon);
                if (weapon != null) c += weapon.Damage;
                c += eq.GetTotalDamageReduction();
            }
            if (e.Growth != null)
                c += (e.Growth.GetCombatLevel(CombatSkillType.Melee) + e.Growth.GetCombatLevel(CombatSkillType.Ranged)) * COMBAT_LEVEL_VALUE;
        }
        return c;
    }

    /// <summary>기지 폭 = 플레이어 건물(데이터 있는 것)의 가장 왼쪽과 오른쪽 사이 칸 수</summary>
    public static float BaseWidth()
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var b in UnityEngine.Object.FindObjectsByType<Building>(FindObjectsSortMode.None))
        {
            if (b == null || b.buildingData == null || !b.IsFunctional) continue;
            float x = b.transform.position.x;
            min = Mathf.Min(min, x);
            max = Mathf.Max(max, x + Mathf.Max(1, b.buildingData.size.x));
        }
        return max > min ? max - min : 0f;
    }
}
