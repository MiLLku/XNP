using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무작위 직원 데이터 생성 유틸리티.
/// EmployeeGenerationConfig를 기반으로 런타임 EmployeeData 인스턴스를 만듭니다.
///
/// 주의: 생성된 EmployeeData는 ScriptableObject.CreateInstance()로 만들어진
/// 런타임 전용 오브젝트입니다. 사용이 끝난 인스턴스는 Object.Destroy()로 해제하세요.
/// 채용 흐름: HiringOffice → Generate() × 3 → HiringPanel → 1개 선택
///           → MarkUsed(selected) → 미선택 2개 자동 해제
///
/// 저장 호환: 생성 직원의 employeeID는 RANDOM_ID_OFFSET(10000) 이상으로 설정됩니다.
/// 추후 GameDatabase에 무작위 생성 직원 복원 로직을 추가할 때 이 범위를 활용하세요.
/// </summary>
public static class RandomEmployeeGenerator
{
    // GameDatabase의 고정 ID와 충돌하지 않도록 오프셋 적용
    private const int RANDOM_ID_OFFSET = 10_000;

    /// <summary>
    /// config를 기반으로 무작위 EmployeeData 인스턴스를 생성합니다.
    /// </summary>
    public static EmployeeData Generate(EmployeeGenerationConfig config)
    {
        if (config == null)
        {
            Debug.LogError("[RandomEmployeeGenerator] config가 null입니다.");
            return null;
        }

        EmployeeData data = ScriptableObject.CreateInstance<EmployeeData>();

        // 이름
        data.employeeName = GenerateName(config);

        // ID: 고정 SO와 범위 분리
        data.employeeID = RANDOM_ID_OFFSET + Random.Range(0, 90_000);

        // 성장 시스템 활성 (채용 직원은 유니크 취급)
        data.isUnique = true;

        // 기본 스탯 고정
        data.maxHealth           = config.baseMaxHealth;
        data.maxMental           = config.baseMaxMental;
        data.attackPower         = config.baseAttackPower;
        data.hungerDecayRate     = config.baseHungerDecayRate;
        data.fatigueIncreaseRate = config.baseFatigueIncreaseRate;

        // 작업 능력
        data.abilities = new WorkAbilities
        {
            canMine     = config.defaultCanMine,
            canChop     = config.defaultCanChop,
            canHaul     = config.defaultCanHaul,
            canBuild    = config.defaultCanBuild,
            canDemolish = config.defaultCanDemolish,
            canCraft    = config.defaultCanCraft,
            canResearch = config.defaultCanResearch,
            canGarden   = config.defaultCanGarden,
        };

        // 운반 용량 무작위 배정 (특성·성장 보너스는 EmployeeWork.GetCarryCapacity()에서 추가 적용)
        data.abilities.baseCarryCapacity = Random.Range(
            config.baseCarryCapacityMin,
            config.baseCarryCapacityMax + 1
        );

        // 특성 무작위 배정 (0~traitCountMax개, 중복 없음)
        data.traits = GenerateTraits(config);

        // 초기 결격 작업 무작위 배정 (각 작업별 독립 확률 롤, 최대 disqualCountMax개)
        data.initialDisqualifications = GenerateDisqualifications(config);

        // 결격 수에 따라 비결격 작업 속도 보정
        ApplyDisqualSpeedBonus(data, config);

        // 외형 무작위 배정
        data.appearance = GenerateAppearance(config);

        return data;
    }

    // ─── private helpers ────────────────────────────────────────────────────

    private static string GenerateName(EmployeeGenerationConfig config)
    {
        string last  = config.lastNames.Length  > 0
            ? config.lastNames[Random.Range(0, config.lastNames.Length)]
            : "홍";
        string first = config.firstNames.Length > 0
            ? config.firstNames[Random.Range(0, config.firstNames.Length)]
            : "길동";
        return last + first;
    }

    private static List<EmployeeTrait> GenerateTraits(EmployeeGenerationConfig config)
    {
        var result = new List<EmployeeTrait>();
        if (config.traitPool == null || config.traitPool.Count == 0) return result;

        int count = Random.Range(config.traitCountMin, config.traitCountMax + 1);
        count = Mathf.Min(count, config.traitPool.Count);

        // Fisher-Yates 셔플 후 앞 count개 선택 → 중복 방지
        var pool = new List<EmployeeTrait>(config.traitPool);
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        for (int i = 0; i < count; i++)
        {
            if (pool[i] != null)
                result.Add(pool[i]);
        }

        return result;
    }

    /// <summary>
    /// 각 작업별 독립 확률 롤로 결격 목록을 생성합니다.
    /// disqualCountMax 초과 시 초과분을 무작위 제거합니다.
    /// </summary>
    private static List<WorkType> GenerateDisqualifications(EmployeeGenerationConfig config)
    {
        var result = new List<WorkType>();
        if (config.disqualChances == null || config.disqualChances.Count == 0) return result;

        // 각 작업 독립 확률 롤
        foreach (WorkTypeDisqualChance entry in config.disqualChances)
        {
            if (entry.chance <= 0f) continue;
            if (Random.Range(0f, 100f) < entry.chance)
                result.Add(entry.workType);
        }

        // 최대 개수 초과 시 셔플 후 앞 N개만 유지
        if (result.Count > config.disqualCountMax)
        {
            for (int i = result.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (result[i], result[j]) = (result[j], result[i]);
            }
            result = result.GetRange(0, config.disqualCountMax);
        }

        return result;
    }

    /// <summary>
    /// 결격 작업 수만큼 비결격 작업들의 속도를 상향 보정합니다.
    /// canXxx=false인 작업(원래 수행 불가)은 보정 대상에서 제외합니다.
    /// </summary>
    private static void ApplyDisqualSpeedBonus(EmployeeData data, EmployeeGenerationConfig config)
    {
        int disqualCount = data.initialDisqualifications?.Count ?? 0;
        if (disqualCount == 0 || config.speedBonusPerDisqual <= 0f) return;

        float bonus = 1f + (disqualCount * config.speedBonusPerDisqual / 100f);
        WorkAbilities ab = data.abilities;
        if (ab == null) return;

        var d = data.initialDisqualifications;

        // 수행 가능(canXxx=true)하면서 결격되지 않은 작업에만 보정 적용
        if (ab.canMine     && !d.Contains(WorkType.Mining))    ab.miningSpeed    *= bonus;
        if (ab.canChop     && !d.Contains(WorkType.Chopping))  ab.choppingSpeed  *= bonus;
        if (ab.canBuild    && !d.Contains(WorkType.Building))  ab.buildingSpeed  *= bonus;
        if (ab.canHaul     && !d.Contains(WorkType.Hauling))   ab.haulingSpeed   *= bonus;
        if (ab.canDemolish && !d.Contains(WorkType.Demolish))  ab.demolishSpeed  *= bonus;
        if (ab.canCraft    && !d.Contains(WorkType.Crafting))  ab.craftingSpeed  *= bonus;
        if (ab.canResearch && !d.Contains(WorkType.Research))  ab.researchSpeed  *= bonus;
        if (ab.canGarden   && !d.Contains(WorkType.Gardening)) ab.gardeningSpeed *= bonus;
    }

    private static EmployeeAppearance GenerateAppearance(EmployeeGenerationConfig config)
    {
        var appearance = new EmployeeAppearance();

        if (config.hairStylePool != null && config.hairStylePool.Count > 0)
            appearance.hairSprite = config.hairStylePool[Random.Range(0, config.hairStylePool.Count)];

        if (config.hairColorPool != null && config.hairColorPool.Length > 0)
            appearance.hairColor = config.hairColorPool[Random.Range(0, config.hairColorPool.Length)];

        return appearance;
    }
}
