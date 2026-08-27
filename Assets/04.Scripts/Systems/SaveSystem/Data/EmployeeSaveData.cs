using System;
using System.Collections.Generic;

/// <summary>
/// 직원 저장 데이터.
///
/// 직원 유형:
///   유니크 직원(isUnique=true): 템플릿 기반 + 성장 시스템
///   일반 직원(isUnique=false): 랜덤 생성 (추후 구현)
/// </summary>
[Serializable]
public class EmployeeSaveData
{
    #region 식별

    /// <summary>런타임 고유 ID</summary>
    public int instanceId;

    /// <summary>EmployeeData ScriptableObject ID (외형/프리팹용)</summary>
    public int templateId;

    /// <summary>유니크 직원 여부</summary>
    public bool isUnique;

    #endregion

    #region 기본 정보

    /// <summary>커스텀 이름 (null이면 템플릿 이름 사용)</summary>
    public string customName;

    #endregion

    #region 위치

    /// <summary>월드 X 좌표</summary>
    public float posX;

    /// <summary>월드 Y 좌표</summary>
    public float posY;

    #endregion

    #region 상태

    /// <summary>직원 상태 (EmployeeState enum)</summary>
    public int state;

    #endregion

    #region 성장 시스템

    /// <summary>현재 레벨</summary>
    public int level;

    /// <summary>현재 경험치</summary>
    public int experience;

    /// <summary>다음 레벨까지 필요 경험치</summary>
    public int experienceToNextLevel;

    /// <summary>레벨업으로 누적된 운반 용량 보너스</summary>
    public int carryCapacityBonus;

    #endregion

    #region 스탯

    /// <summary>최대 체력 (템플릿 기본값 + 성장치)</summary>
    public int maxHealth;

    /// <summary>현재 체력</summary>
    public int currentHealth;

    /// <summary>최대 멘탈 (상한)</summary>
    public int maxMental;

    /// <summary>
    /// 현재 멘탈 — v9부터는 baseMental + 모디파이어에서 파생되는 <b>표시용 스냅샷</b>입니다.
    /// 복원 시에는 이 값을 쓰지 않고 재계산합니다.
    /// </summary>
    public int currentMental;

    /// <summary>기본 정신력 — 모디파이어가 전부 사라졌을 때 수렴하는 값 (v9)</summary>
    public float baseMental;

    /// <summary>활성 정신력 모디파이어 목록 (v9)</summary>
    public List<MentalModifierSaveData> mentalModifiers = new List<MentalModifierSaveData>();

    /// <summary>
    /// [구 필드 — v8 이하 전용] 공격력.
    /// v9부터 전투력은 무기(EquipmentData)와 전투 숙련(combatAptitudes)이 결정합니다.
    /// v8→v9 마이그레이션이 이 값을 읽어 초기 숙련으로 환산하므로 필드는 남겨둡니다.
    /// </summary>
    public int attackPower;

    #endregion

    #region 욕구

    /// <summary>배고픔 (0~100, 낮을수록 배고픔)</summary>
    public float hunger;

    /// <summary>피로 (0~100, 낮을수록 피곤함)</summary>
    public float fatigue;

    /// <summary>재미 (0~100, 낮을수록 침식 취약). v4 추가 — 구 세이브는 마이그레이션에서 채움</summary>
    public float fun;

    #endregion

    #region 작업

    /// <summary>작업 능력 데이터</summary>
    public WorkAbilitiesSaveData abilities;

    /// <summary>작업 우선순위 목록</summary>
    public List<WorkPrioritySaveData> workPriorities;

    /// <summary>배정된 작업 명령 ID (-1이면 없음)</summary>
    public int assignedWorkOrderId;

    /// <summary>동적 비자격 작업 타입 목록 (WorkType int 값)</summary>
    public List<int> disqualifiedWorkTypes;

    /// <summary>소지 중인 식량 아이템 ID (0 = 없음)</summary>
    public int heldFoodItemId;

    /// <summary>소지 중인 식량 개수</summary>
    public int heldFoodCount;

    /// <summary>소지 중인 약물 아이템 ID (0 = 없음, v5)</summary>
    public int heldDrugItemId;

    /// <summary>소지 중인 약물 개수 (v5)</summary>
    public int heldDrugCount;

    /// <summary>필수 소지 식량 개수 설정 (v5 — 구 세이브는 마이그레이션에서 1로 보정)</summary>
    public int desiredFoodCount;

    /// <summary>필수 소지 약물 개수 설정 (v5)</summary>
    public int desiredDrugCount;

    #endregion

    #region 정신 이상

    /// <summary>활성 정신 이상 목록 (일반 계열 + 침식 계열 공용, v8부터 침식 계열 포함)</summary>
    public List<MentalEventSaveData> activeMentalEvents;

    /// <summary>정신 이상 종료 후 재판정 유예 남은 시간 (초, v8)</summary>
    public float mentalBreakGraceRemaining;

    #endregion

    #region 장비

    /// <summary>장착 중인 장비 목록</summary>
    public List<EquipmentSlotSaveData> equippedItems;

    #endregion

    #region 침식

    /// <summary>현재 침식 수치</summary>
    public float erosionLevel;

    /// <summary>마지막 오라 노출 이후 경과 시간 (초) — 회복 타이머용</summary>
    public float timeSinceLastAuraExposure;

    /// <summary>
    /// [구 필드 — v7 이하 전용] 침식 단계가 굴리던 이상 행동 타입 (AbnormalBehaviorType int 값, 0 = 없음).
    /// v8부터 침식 계열 정신 이상은 activeMentalEvents가 관리합니다.
    /// v7→v8 마이그레이션이 이 값을 읽어 이관하므로 필드 자체는 남겨둡니다.
    /// </summary>
    public int activeAbnormalBehavior;

    /// <summary>[구 필드 — v7 이하 전용] 이상 행동 남은 지속 시간 (초)</summary>
    public float abnormalBehaviorRemainingTime;

    /// <summary>
    /// 자연 침식 최고 노출 수치 워터마크.
    /// ApplyNaturalErosion에서 이 값 이하의 수치는 무시됩니다.
    /// ErosionLevel이 0으로 완전 회복되면 함께 초기화됩니다.
    /// </summary>
    /// <summary>[v11 폐기] 구 워터마크 방식 잔재. 읽기만 하고 쓰지 않습니다.</summary>
    public float naturalErosionWatermark;

    /// <summary>출처별 침식 누적 내역 (v10) — "자연 침식 +3 / 제놉스 A 오라침식 +7"</summary>
    public List<ErosionSourceEntry> erosionSources = new List<ErosionSourceEntry>();

    #endregion

    #region 스케줄

    /// <summary>24시간 스케줄 (ScheduleActivity int 값 배열, null이면 기본 스케줄 사용)</summary>
    public int[] scheduleActivities;

    #endregion

    #region 소집

    /// <summary>소집 상태 (로드 후 즉시 해제 권장이나 상태 보존용으로 저장)</summary>
    public bool isDrafted;

    /// <summary>전투 태세 (CombatStance int 값). 구 세이브는 마이그레이션이 Guard로 보정</summary>
    public int combatStance = (int)CombatStance.Guard;

    #endregion

    #region 작업 적성 · 스킬 포인트

    /// <summary>작업 종류별 적성 레벨·경험치 (스킬 해금 조건)</summary>
    public List<WorkAptitude.Entry> workAptitudes = new List<WorkAptitude.Entry>();

    /// <summary>근접·원거리 전투 숙련 레벨·경험치 (v9)</summary>
    public List<CombatAptitude.Entry> combatAptitudes = new List<CombatAptitude.Entry>();

    #endregion

    #region 구역 할당

    /// <summary>수면 구역 ID (-1 = 미할당)</summary>
    public int sleepZoneId = -1;

    /// <summary>오락 구역 ID (-1 = 미할당)</summary>
    public int recreationZoneId = -1;

    /// <summary>세척 구역 ID (-1 = 미할당)</summary>
    public int washZoneId = -1;

    /// <summary>작업 구역 ID (-1 = 미할당)</summary>
    public int workZoneId = -1;

    #endregion

    #region 무작위 생성 직원

    /// <summary>
    /// 무작위 생성 직원(templateId ≥ RandomEmployeeGenerator.RANDOM_ID_OFFSET)의 재구성 데이터.
    /// GameDatabase에서 templateId 조회가 실패하면 이 데이터로 EmployeeData를 런타임 재생성합니다.
    /// 주의: JsonUtility는 null 객체도 기본값 인스턴스로 역직렬화하므로 isGenerated 플래그로 구분합니다.
    /// </summary>
    public GeneratedEmployeeSaveData generated;

    #endregion

    public EmployeeSaveData()
    {
        assignedWorkOrderId = -1;
        workPriorities = new List<WorkPrioritySaveData>();
        disqualifiedWorkTypes = new List<int>();
        activeMentalEvents = new List<MentalEventSaveData>();
        equippedItems = new List<EquipmentSlotSaveData>();
        level = 1;
        experience = 0;
        experienceToNextLevel = 100;
        sleepZoneId = -1;
        recreationZoneId = -1;
        washZoneId = -1;
        workZoneId = -1;
    }
}

/// <summary>
/// 작업 능력 저장 데이터.
/// 유니크 직원은 성장에 따라 작업 속도가 변경될 수 있습니다.
/// </summary>
[Serializable]
public class WorkAbilitiesSaveData
{
    #region 작업 가능 여부

    public bool canMine;
    public bool canChop;
    public bool canResearch;
    public bool canCraft;
    public bool canGarden;
    public bool canBuild;
    public bool canHaul;
    public bool canDemolish;

    #endregion

    #region 작업 속도

    public float miningSpeed;
    public float choppingSpeed;
    public float researchSpeed;
    public float craftingSpeed;
    public float gardeningSpeed;
    public float buildingSpeed;
    public float haulingSpeed;
    public float demolishSpeed;

    #endregion

    #region 운반 용량

    /// <summary>기본 운반 용량 (한 번에 들 수 있는 DroppedItem 개수)</summary>
    public int baseCarryCapacity = 5;

    #endregion

    public WorkAbilitiesSaveData()
    {
        miningSpeed = 1f;
        choppingSpeed = 1f;
        researchSpeed = 1f;
        craftingSpeed = 1f;
        gardeningSpeed = 1f;
        buildingSpeed = 1f;
        haulingSpeed = 1f;
        demolishSpeed = 1f;
    }

    /// <summary>
    /// WorkAbilities에서 저장 데이터로 변환합니다.
    /// </summary>
    /// <param name="source">원본 WorkAbilities</param>
    /// <returns>저장 데이터</returns>
public static WorkAbilitiesSaveData FromWorkAbilities(WorkAbilities source)
    {
        if (source == null) return new WorkAbilitiesSaveData();

        return new WorkAbilitiesSaveData
        {
            canMine = source.canMine,
            canChop = source.canChop,
            canResearch = source.canResearch,
            canCraft = source.canCraft,
            canGarden = source.canGarden,
            canBuild = source.canBuild,
            canHaul = source.canHaul,
            canDemolish = source.canDemolish,
            miningSpeed = source.miningSpeed,
            choppingSpeed = source.choppingSpeed,
            researchSpeed = source.researchSpeed,
            craftingSpeed = source.craftingSpeed,
            gardeningSpeed = source.gardeningSpeed,
            buildingSpeed = source.buildingSpeed,
            haulingSpeed = source.haulingSpeed,
            demolishSpeed = source.demolishSpeed,
            baseCarryCapacity = source.baseCarryCapacity
        };
    }

    /// <summary>
    /// 저장 데이터를 WorkAbilities로 변환합니다.
    /// </summary>
    /// <returns>WorkAbilities 인스턴스</returns>
public WorkAbilities ToWorkAbilities()
    {
        return new WorkAbilities
        {
            canMine = canMine,
            canChop = canChop,
            canResearch = canResearch,
            canCraft = canCraft,
            canGarden = canGarden,
            canBuild = canBuild,
            canHaul = canHaul,
            canDemolish = canDemolish,
            miningSpeed = miningSpeed,
            choppingSpeed = choppingSpeed,
            researchSpeed = researchSpeed,
            craftingSpeed = craftingSpeed,
            gardeningSpeed = gardeningSpeed,
            buildingSpeed = buildingSpeed,
            haulingSpeed = haulingSpeed,
            demolishSpeed = demolishSpeed,
            baseCarryCapacity = baseCarryCapacity > 0 ? baseCarryCapacity : 5
        };
    }
}

/// <summary>
/// 작업 우선순위 저장 데이터.
/// </summary>
[Serializable]
public class WorkPrioritySaveData
{
    /// <summary>작업 타입 (WorkType enum)</summary>
    public int workType;

    /// <summary>우선순위 (1~9, 0이면 비활성)</summary>
    public int priority;

    /// <summary>활성화 여부</summary>
    public bool enabled;
}

/// <summary>
/// 무작위 생성 직원의 생성 결과 스냅샷.
/// 로드 시 RandomEmployeeGenerator.Rebuild()로 EmployeeData를 재구성하는 데 사용합니다.
///
/// 작업 능력(WorkAbilities)은 별도로 저장하지 않습니다 —
/// EmployeeSaveData.abilities(런타임 능력)가 이미 저장되므로 재구성 시 그것을 템플릿 값으로 씁니다.
/// 특성/헤어는 에셋 참조라서 이름으로 저장하고 EmployeeGenerationConfig 풀에서 이름으로 찾습니다.
/// </summary>
[Serializable]
public class GeneratedEmployeeSaveData
{
    /// <summary>유효한 생성 스냅샷인지 여부 (JsonUtility의 기본값 역직렬화와 구분용)</summary>
    public bool isGenerated;

    /// <summary>생성된 이름</summary>
    public string employeeName;

    /// <summary>기본 스탯</summary>
    public int maxHealth;
    public int maxMental;
    /// <summary>기본 정신력 (v9)</summary>
    public int baseMental;

    /// <summary>초기 전투 숙련 레벨 (v9 — 구 attackPower를 대체)</summary>
    public int initialMeleeLevel = 1;
    public int initialRangedLevel = 1;
    public float hungerDecayRate;
    public float fatigueIncreaseRate;

    /// <summary>초기 결격 작업 목록 (WorkType int)</summary>
    public List<int> initialDisqualifications = new List<int>();

    /// <summary>특성 에셋 이름 목록 (EmployeeGenerationConfig.traitPool에서 이름으로 조회)</summary>
    public List<string> traitNames = new List<string>();

    /// <summary>헤어 스프라이트 이름 (EmployeeGenerationConfig.hairStylePool에서 이름으로 조회, 빈 문자열이면 없음)</summary>
    public string hairSpriteName;

    /// <summary>헤어 색상 (RGBA)</summary>
    public float hairColorR = 0f;
    public float hairColorG = 0f;
    public float hairColorB = 0f;
    public float hairColorA = 1f;
}
