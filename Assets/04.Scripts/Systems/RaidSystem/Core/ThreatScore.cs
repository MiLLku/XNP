using System;
using UnityEngine;

/// <summary>
/// 습격 점수 (림월드식) = (직원 가치 + 자원 가치 + 건물 가치) × 난이도 배수 × 플레이 시간 보정 × 적응 보정.
///   · 직원 가치: 살아 있는 직원마다 (작업 적성 레벨 합 + 전투 숙련 레벨 합 + 착용 장비 수 × 장비 가치 × 품질 배율)
///   · 자원 가치: 창고 재고 개수 + 장비 보관소의 장비 × 장비 가치
///   · 건물 가치: 기능하는 플레이어 건물의 건설 비용 합
/// 위협 단계(Ⅰ~Ⅴ)는 보정 전 총 가치로 정한다 (<see cref="RaidManager.StageForValue"/>, 회차 최대 유지).
/// 마리 수는 RaidManager가 이 점수를 습격 종류의 한 마리당 점수로 나눠 정한다.
/// </summary>
public static class ThreatScore
{
    /// <summary>장비 하나의 가치 (장비에 가치 값이 없어 고정)</summary>
    public const float EQUIPMENT_VALUE = 10f;

    [Serializable]
    public struct Breakdown
    {
        public float staff, resources, buildings;   // 가치
        public float diffMult, timeMult, adapt;     // 배수
        public float total;
        public string difficulty;

        public float Value => staff + resources + buildings;

        public override string ToString()
            => $"위협 {total:F0} ({difficulty}) = (직원 {staff:F0} + 자원 {resources:F0} + 건물 {buildings:F0} = {Value:F0}) × 난이도 {diffMult:F3} × 시간 {timeMult:F2} × 적응 {adapt:F2}";
    }

    /// <summary>지금 기지 상태로 점수를 낸다</summary>
    public static Breakdown Compute(ThreatDifficulty d, float adapt)
    {
        float day = DayCycle.instance != null ? DayCycle.instance.Day + DayCycle.instance.TimeNormalized : 1f;
        return Compute(d, adapt, day, StaffValue(), ResourceValue(), BuildingValue());
    }

    /// <summary>값을 직접 넣어 점수를 낸다 (테스트·표 뽑기용)</summary>
    public static Breakdown Compute(ThreatDifficulty d, float adapt, float day, float staff, float resources, float buildings)
    {
        var b = new Breakdown { staff = staff, resources = resources, buildings = buildings, adapt = adapt };
        if (d == null) { b.diffMult = 1f; b.timeMult = 1f; b.total = 20f; b.difficulty = "-"; return b; }
        b.difficulty = d.displayName;
        b.diffMult = d.valueMultiplier;
        b.timeMult = d.TimeMultiplier(day);
        b.total = b.Value * b.diffMult * b.timeMult * adapt;
        return b;
    }

    /// <summary>보정 전 총 가치 (위협 단계 판정용)</summary>
    public static float TotalValue() => StaffValue() + ResourceValue() + BuildingValue();

    public static float StaffValue()
    {
        float v = 0f;
        if (EmployeeManager.instance == null) return v;
        foreach (var e in EmployeeManager.instance.AllEmployees)
        {
            if (e == null || e.State == EmployeeState.Dead) continue;
            if (e.Growth != null)
            {
                foreach (WorkType wt in Enum.GetValues(typeof(WorkType))) v += e.Growth.GetAptitudeLevel(wt);
                v += e.Growth.GetCombatLevel(CombatSkillType.Melee) + e.Growth.GetCombatLevel(CombatSkillType.Ranged);
            }
            var eq = e.GetComponent<EmployeeEquipment>();
            if (eq == null) continue;
            foreach (EquipmentSlot s in Enum.GetValues(typeof(EquipmentSlot)))
                if (eq.GetItemInSlot(s) != null) v += EQUIPMENT_VALUE * eq.QualityMult(s);
        }
        return v;
    }

    public static float ResourceValue()
    {
        float v = 0f;
        if (InventoryManager.instance != null)
            foreach (var kv in InventoryManager.instance.GetAllItems()) v += kv.Value;
        if (EquipmentStorageManager.instance != null) v += EquipmentStorageManager.instance.PooledCount * EQUIPMENT_VALUE;
        return v;
    }

    public static float BuildingValue()
    {
        float v = 0f;
        foreach (var b in UnityEngine.Object.FindObjectsByType<Building>(FindObjectsSortMode.None))
        {
            if (b == null || b.buildingData == null || !b.IsFunctional || b.buildingData.requiredResources == null) continue;
            foreach (var c in b.buildingData.requiredResources) v += c.amount;
        }
        return v;
    }
}
