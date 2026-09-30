using System;
using UnityEngine;

/// <summary>
/// 직원 성장 — 모든 직원.
///   - <b>레벨</b>: 작업으로 오르지 않고 단련장에서 습격 전리품으로 단련해야 오름(<see cref="LevelUpStation"/>). 1레벨 = 스킬 포인트 1,
///     그 외 보상은 최대 체력·운반 용량(<see cref="LevelUpConfig"/>).
///   - <b>작업 적성</b>: 해당 작업을 하면 오름 — 스킬 해금 조건.
///   - <b>전투 숙련</b>: 전투로 오름.
/// </summary>
public class EmployeeGrowth : MonoBehaviour
{
    #region 필드

    [Header("레벨 (습격 전리품으로만 오름)")]
    [SerializeField] private int level = 1;

    [Header("운반 성장 보너스")]
    [Tooltip("레벨업으로 누적된 운반 용량 보너스")]
    [SerializeField] private int carryCapacityBonus = 0;

    [Header("작업 적성 (작업별 숙련)")]
    [Tooltip("해당 작업을 수행해야만 오르는 작업별 레벨. 스킬 해금 조건으로 사용됩니다.")]
    [SerializeField] private WorkAptitude aptitude = new WorkAptitude();

    [Header("전투 숙련 (근접/원거리)")]
    [Tooltip("전투를 수행하거나 특수 아이템으로 오르는 숙련. 무기의 데미지·명중률·공격 간격을 조정합니다.")]
    [SerializeField] private CombatAptitude combatAptitude = new CombatAptitude();

    /// <summary>코디네이터 참조</summary>
    private Employee employee;

    /// <summary>스탯 컨트롤러 참조</summary>
    private EmployeeStatsController statsController;

    #endregion

    #region 이벤트

    public delegate void LevelUpDelegate(int newLevel);
    public event LevelUpDelegate OnLevelUp;

    /// <summary>작업 적성이 레벨업했을 때 발생 (작업 종류, 새 레벨)</summary>
    public event Action<WorkType, int> OnAptitudeLevelUp;

    /// <summary>전투 숙련이 레벨업했을 때 발생 (숙련 종류, 새 레벨)</summary>
    public event Action<CombatSkillType, int> OnCombatLevelUp;

    #endregion

    #region 프로퍼티

    /// <summary>현재 레벨</summary>
    public int Level => level;

    /// <summary>레벨업으로 누적된 운반 용량 보너스</summary>
    public int CarryCapacityBonus => carryCapacityBonus;

    #endregion

    #region 초기화

    void Awake()
    {
        employee = GetComponent<Employee>();
        statsController = GetComponent<EmployeeStatsController>();
    }

    /// <summary>성장 시스템을 초기화합니다 (새 직원).</summary>
    public void Initialize()
    {
        level = 1;
        carryCapacityBonus = 0;
        combatAptitude = new CombatAptitude();
    }

    #endregion

    #region 레벨업 (습격 전리품)

    private static LevelUpConfig Config => EmployeeManager.instance != null ? EmployeeManager.instance.LevelUpConfig : null;

    /// <summary>다음 레벨 비용 (전리품 개수)</summary>
    public int NextLevelCost => Config != null ? Config.CostFor(level) : 0;

    /// <summary>다음 레벨 단련 시간(초)</summary>
    public float NextTrainingSeconds => Config != null ? Config.TrainingSecondsFor(level) : 0f;

    /// <summary>단련을 시작할 수 없는 이유 (가능하면 null) — 전리품이 모자라는지만 봄 (단련장 상태는 단련장이 봄)</summary>
    public string TrainingBlockReason()
    {
        var cfg = Config;
        if (cfg == null || cfg.growthItem == null) return "설정 없음";
        int have = InventoryManager.instance != null ? InventoryManager.instance.GetAvailableAmount(cfg.growthItem) : 0;
        return have < NextLevelCost ? $"{cfg.growthItem.itemName} {have}/{NextLevelCost}" : null;
    }

    /// <summary>단련 완료 — 레벨을 1 올립니다 (전리품 소모는 단련장이 함).</summary>
    public void ApplyLevelUp()
    {
        var cfg = Config;
        if (cfg == null) return;

        level++;
        int healthGain = cfg.healthPerLevel;
        int carryGain = (level - 1) % cfg.carryEveryLevels == 0 ? 1 : 0;
        carryCapacityBonus += carryGain;
        if (statsController != null) statsController.IncreaseMaxStats(healthGain, 0);

        Debug.Log($"[Growth] {employee?.DisplayName} 레벨업! Lv.{level} (HP+{healthGain}, 운반+{carryGain}, 스킬 포인트 +1)");
        OnLevelUp?.Invoke(level);
    }

    #endregion

    #region 저장/복원

    /// <summary>
    /// 저장 데이터에 성장 정보를 기록합니다.
    /// </summary>
public void PopulateSaveData(EmployeeSaveData data)
    {
        data.level = level;
        data.carryCapacityBonus = carryCapacityBonus;
        data.workAptitudes = new System.Collections.Generic.List<WorkAptitude.Entry>(aptitude.Entries);
        data.combatAptitudes = new System.Collections.Generic.List<CombatAptitude.Entry>(combatAptitude.Entries);
    }

    /// <summary>
    /// 저장 데이터에서 성장 정보를 복원합니다.
    /// </summary>
public void RestoreFromSaveData(EmployeeSaveData data)
    {
        level = Mathf.Max(1, data.level);
        carryCapacityBonus = data.carryCapacityBonus;
        aptitude.Restore(data.workAptitudes);
        combatAptitude.Restore(data.combatAptitudes);
    }

    #endregion

    #region 작업 적성

    /// <summary>작업 적성 데이터 (읽기용)</summary>
    public WorkAptitude Aptitude => aptitude;

    /// <summary>해당 작업의 적성 레벨.</summary>
    public int GetAptitudeLevel(WorkType type) => aptitude.GetLevel(type);

    /// <summary>
    /// 작업 적성 경험치를 획득합니다. 해당 작업을 실제로 수행할 때만 호출됩니다.
    /// 적성은 스킬 해금 조건입니다.
    /// </summary>
    public void GainWorkExperience(WorkType type, int amount)
    {
        int newLevel = aptitude.GainExperience(type, amount);
        if (newLevel > 0)
        {
            Debug.Log($"[Growth] {employee?.DisplayName} {type} 적성 레벨업! Lv.{newLevel}");
            OnAptitudeLevelUp?.Invoke(type, newLevel);
        }
    }

    #endregion

    #region 전투 숙련

    /// <summary>전투 숙련 데이터 (읽기용)</summary>
    public CombatAptitude Combat => combatAptitude;

    /// <summary>해당 전투 숙련 레벨.</summary>
    public int GetCombatLevel(CombatSkillType type) => combatAptitude.GetLevel(type);

    /// <summary>
    /// 전투 숙련 경험치를 획득합니다. 실제로 적을 공격했을 때만 호출됩니다.
    ///     /// </summary>
    public void GainCombatExperience(CombatSkillType type, int amount)
    {
        int newLevel = combatAptitude.GainExperience(type, amount);
        if (newLevel > 0)
        {
            Debug.Log($"[Growth] {employee?.DisplayName} {type} 숙련 레벨업! Lv.{newLevel}");
            OnCombatLevelUp?.Invoke(type, newLevel);
        }
    }

    /// <summary>
    /// 전투 숙련 레벨을 즉시 올립니다 (훈련 혈청 등 특수 아이템 전용).
    /// </summary>
    /// <returns>실제로 오른 레벨 수 (상한에 걸리면 0일 수 있음)</returns>
    public int RaiseCombatLevel(CombatSkillType type, int levels)
    {
        int gained = combatAptitude.RaiseLevel(type, levels);
        if (gained > 0)
        {
            int newLevel = combatAptitude.GetLevel(type);
            Debug.Log($"[Growth] {employee?.DisplayName} {type} 숙련 상승 (특수 아이템): +{gained} → Lv.{newLevel}");
            OnCombatLevelUp?.Invoke(type, newLevel);
        }
        return gained;
    }

    /// <summary>템플릿의 초기 숙련 레벨을 적용합니다.</summary>
    public void ApplyInitialCombatLevels(int meleeLevel, int rangedLevel)
    {
        combatAptitude.SetLevel(CombatSkillType.Melee, meleeLevel);
        combatAptitude.SetLevel(CombatSkillType.Ranged, rangedLevel);
    }

    #endregion
}
