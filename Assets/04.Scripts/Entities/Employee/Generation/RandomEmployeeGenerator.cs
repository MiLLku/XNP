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
/// 저장 시 CreateSnapshot()으로 생성 결과를 EmployeeSaveData.generated에 보존하고,
/// 로드 시 GameDatabase 조회가 실패하면 Rebuild()로 EmployeeData를 재구성합니다.
/// </summary>
public static class RandomEmployeeGenerator
{
    // GameDatabase의 고정 ID와 충돌하지 않도록 오프셋 적용
    public const int RANDOM_ID_OFFSET = 10_000;

    /// <summary>해당 employeeID가 무작위 생성 직원의 ID 대역인지 여부.</summary>
    public static bool IsGeneratedId(int employeeId) => employeeId >= RANDOM_ID_OFFSET;

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
        data.baseMental          = config.baseMentalValue;
        data.initialMeleeLevel   = config.baseMeleeLevel;
        data.initialRangedLevel  = config.baseRangedLevel;
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

    // ─── 저장/복원 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 생성된 EmployeeData에서 저장용 스냅샷을 만듭니다 (Employee.CreateSaveData에서 호출).
    /// </summary>
    public static GeneratedEmployeeSaveData CreateSnapshot(EmployeeData data)
    {
        if (data == null) return null;

        var snap = new GeneratedEmployeeSaveData
        {
            isGenerated         = true,
            employeeName        = data.employeeName,
            maxHealth           = data.maxHealth,
            maxMental           = data.maxMental,
            baseMental          = data.baseMental,
            initialMeleeLevel   = data.initialMeleeLevel,
            initialRangedLevel  = data.initialRangedLevel,
            hungerDecayRate     = data.hungerDecayRate,
            fatigueIncreaseRate = data.fatigueIncreaseRate,
        };

        if (data.initialDisqualifications != null)
        {
            foreach (WorkType wt in data.initialDisqualifications)
                snap.initialDisqualifications.Add((int)wt);
        }

        if (data.traits != null)
        {
            foreach (EmployeeTrait trait in data.traits)
            {
                if (trait != null) snap.traitNames.Add(trait.name);
            }
        }

        if (data.appearance != null)
        {
            snap.hairSpriteName = data.appearance.hairSprite != null ? data.appearance.hairSprite.name : "";
            Color c = data.appearance.hairColor;
            snap.hairColorR = c.r;
            snap.hairColorG = c.g;
            snap.hairColorB = c.b;
            snap.hairColorA = c.a;
        }

        return snap;
    }

    /// <summary>
    /// 저장된 스냅샷에서 런타임 EmployeeData를 재구성합니다 (EmployeeManager.Restore에서 호출).
    /// 특성·헤어는 config 풀에서 이름으로 찾으며, config가 null이거나 풀에서 못 찾으면
    /// 해당 항목만 생략하고 직원 자체는 복원합니다.
    /// </summary>
    /// <param name="snap">저장된 생성 스냅샷</param>
    /// <param name="employeeId">저장된 employeeID (templateId)</param>
    /// <param name="abilities">저장된 런타임 작업 능력 (템플릿 값으로 재사용)</param>
    /// <param name="config">이름 조회용 생성 설정 (EmployeeManager.GenerationConfig)</param>
    public static EmployeeData Rebuild(
        GeneratedEmployeeSaveData snap,
        int employeeId,
        WorkAbilitiesSaveData abilities,
        EmployeeGenerationConfig config)
    {
        if (snap == null || !snap.isGenerated) return null;

        EmployeeData data = ScriptableObject.CreateInstance<EmployeeData>();

        data.employeeName        = snap.employeeName;
        data.employeeID          = employeeId;
        data.isUnique            = true;
        data.maxHealth           = snap.maxHealth;
        data.maxMental           = snap.maxMental;
        data.baseMental          = snap.baseMental > 0 ? snap.baseMental : 50;
        data.initialMeleeLevel   = Mathf.Max(1, snap.initialMeleeLevel);
        data.initialRangedLevel  = Mathf.Max(1, snap.initialRangedLevel);
        data.hungerDecayRate     = snap.hungerDecayRate;
        data.fatigueIncreaseRate = snap.fatigueIncreaseRate;

        data.abilities = abilities != null ? abilities.ToWorkAbilities() : new WorkAbilities();

        data.initialDisqualifications = new List<WorkType>();
        if (snap.initialDisqualifications != null)
        {
            foreach (int wt in snap.initialDisqualifications)
                data.initialDisqualifications.Add((WorkType)wt);
        }

        // 특성: config.traitPool에서 에셋 이름으로 조회
        data.traits = new List<EmployeeTrait>();
        if (snap.traitNames != null && snap.traitNames.Count > 0)
        {
            foreach (string traitName in snap.traitNames)
            {
                EmployeeTrait found = FindTraitByName(config, traitName);
                if (found != null) data.traits.Add(found);
                else Debug.LogWarning($"[RandomEmployeeGenerator] 특성 '{traitName}'을 traitPool에서 찾지 못해 생략 (직원: {snap.employeeName})");
            }
        }

        // 외형: 헤어 스프라이트 이름 조회 + 색상 복원
        data.appearance = new EmployeeAppearance
        {
            hairColor = new Color(snap.hairColorR, snap.hairColorG, snap.hairColorB, snap.hairColorA)
        };
        if (!string.IsNullOrEmpty(snap.hairSpriteName))
        {
            data.appearance.hairSprite = FindHairSpriteByName(config, snap.hairSpriteName);
            if (data.appearance.hairSprite == null)
                Debug.LogWarning($"[RandomEmployeeGenerator] 헤어 '{snap.hairSpriteName}'을 hairStylePool에서 찾지 못해 생략 (직원: {snap.employeeName})");
        }

        return data;
    }

    private static EmployeeTrait FindTraitByName(EmployeeGenerationConfig config, string traitName)
    {
        if (config == null || config.traitPool == null || string.IsNullOrEmpty(traitName)) return null;
        foreach (EmployeeTrait trait in config.traitPool)
        {
            if (trait != null && trait.name == traitName) return trait;
        }
        return null;
    }

    private static Sprite FindHairSpriteByName(EmployeeGenerationConfig config, string spriteName)
    {
        if (config == null || config.hairStylePool == null || string.IsNullOrEmpty(spriteName)) return null;
        foreach (Sprite sprite in config.hairStylePool)
        {
            if (sprite != null && sprite.name == spriteName) return sprite;
        }
        return null;
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
