using System;
using UnityEngine;

/// <summary>
/// 직원 성장 시스템 컴포넌트 (유니크 직원 전용).
/// 경험치 획득, 레벨업, 능력치 향상을 담당합니다.
///
/// 성장 공식:
///   - 레벨업 필요 경험치: level^1.5 × 100
///   - 레벨업 시: 체력 +(5+level), 정신력 +(3+level/2), 3레벨마다 공격력 +1
/// </summary>
public class EmployeeGrowth : MonoBehaviour
{
    #region 상수

    /// <summary>초기 필요 경험치</summary>
    private const int INITIAL_EXP_REQUIRED = 100;

    #endregion

    #region 필드

    [Header("성장 시스템")]
    [SerializeField] private int level = 1;
    [SerializeField] private int experience = 0;
    [SerializeField] private int experienceToNextLevel = INITIAL_EXP_REQUIRED;

    [Header("운반 성장 보너스")]
    [Tooltip("레벨업으로 누적된 운반 용량 보너스 (5레벨마다 +1)")]
    [SerializeField] private int carryCapacityBonus = 0;

    [Header("작업 적성 (작업별 숙련)")]
    [Tooltip("해당 작업을 수행해야만 오르는 작업별 레벨. 스킬 해금 조건으로 사용됩니다.")]
    [SerializeField] private WorkAptitude aptitude = new WorkAptitude();

    /// <summary>코디네이터 참조</summary>
    private Employee employee;

    /// <summary>스탯 컨트롤러 참조</summary>
    private EmployeeStatsController statsController;

    /// <summary>성장 활성화 여부 (유니크 직원만)</summary>
    private bool growthEnabled = false;

    #endregion

    #region 이벤트

    public delegate void LevelUpDelegate(int newLevel);
    public event LevelUpDelegate OnLevelUp;

    /// <summary>작업 적성이 레벨업했을 때 발생 (작업 종류, 새 레벨)</summary>
    public event Action<WorkType, int> OnAptitudeLevelUp;

    #endregion

    #region 프로퍼티

    /// <summary>현재 레벨</summary>
    public int Level => level;

    /// <summary>현재 경험치</summary>
    public int Experience => experience;

    /// <summary>다음 레벨까지 필요한 경험치</summary>
    public int ExperienceToNextLevel => experienceToNextLevel;

    /// <summary>레벨업으로 누적된 운반 용량 보너스</summary>
    public int CarryCapacityBonus => carryCapacityBonus;

    #endregion

    #region 초기화

    void Awake()
    {
        employee = GetComponent<Employee>();
        statsController = GetComponent<EmployeeStatsController>();
    }

    /// <summary>
    /// 성장 시스템을 초기화합니다.
    /// </summary>
    /// <param name="isUnique">유니크 직원 여부 (유니크만 성장)</param>
public void Initialize(bool isUnique)
    {
        growthEnabled = isUnique;
        level = 1;
        experience = 0;
        experienceToNextLevel = INITIAL_EXP_REQUIRED;
        carryCapacityBonus = 0;
    }

    #endregion

    #region 경험치 및 레벨업

    /// <summary>
    /// 경험치를 획득합니다.
    /// </summary>
    /// <param name="amount">획득 경험치량</param>
    public void GainExperience(int amount)
    {
        if (!growthEnabled) return;

        float gainMult = statsController != null ? statsController.CachedSkillGainRateModifier : 1f;
        experience += Mathf.Max(1, Mathf.RoundToInt(amount * gainMult));

        Debug.Log($"[Growth] {employee?.DisplayName} 경험치 획득: +{amount} ({experience}/{experienceToNextLevel})");

        while (experience >= experienceToNextLevel)
        {
            LevelUp();
        }
    }

    /// <summary>
    /// 레벨업을 수행합니다.
    /// </summary>
private void LevelUp()
    {
        experience -= experienceToNextLevel;
        level++;

        experienceToNextLevel = CalculateExperienceToNextLevel(level);

        // 스탯 증가
        int healthGain = 5 + level;
        int mentalGain = 3 + level / 2;
        int attackGain = level % 3 == 0 ? 1 : 0;

        // 운반 용량 증가 (5레벨마다 +1)
        int carryGain = level % 5 == 0 ? 1 : 0;
        carryCapacityBonus += carryGain;

        if (statsController != null)
        {
            statsController.IncreaseMaxStats(healthGain, mentalGain, attackGain);
        }

        string carryLog = carryGain > 0 ? $", Carry+{carryGain}(총 +{carryCapacityBonus})" : "";
        Debug.Log($"[Growth] {employee?.DisplayName} 레벨업! Lv.{level} (HP+{healthGain}, Mental+{mentalGain}{carryLog})");

        OnLevelUp?.Invoke(level);
    }

    /// <summary>
    /// 다음 레벨 필요 경험치를 계산합니다.
    /// </summary>
    private int CalculateExperienceToNextLevel(int currentLevel)
    {
        return Mathf.RoundToInt(Mathf.Pow(currentLevel, 1.5f) * 100);
    }

    #endregion

    #region 저장/복원

    /// <summary>
    /// 저장 데이터에 성장 정보를 기록합니다.
    /// </summary>
public void PopulateSaveData(EmployeeSaveData data)
    {
        data.level = level;
        data.experience = experience;
        data.experienceToNextLevel = experienceToNextLevel;
        data.carryCapacityBonus = carryCapacityBonus;
        data.workAptitudes = new System.Collections.Generic.List<WorkAptitude.Entry>(aptitude.Entries);
    }

    /// <summary>
    /// 저장 데이터에서 성장 정보를 복원합니다.
    /// </summary>
public void RestoreFromSaveData(EmployeeSaveData data, bool isUnique)
    {
        growthEnabled = isUnique;
        level = data.level;
        experience = data.experience;
        experienceToNextLevel = data.experienceToNextLevel;
        carryCapacityBonus = data.carryCapacityBonus;
        aptitude.Restore(data.workAptitudes);
    }

    #endregion

    #region 작업 적성

    /// <summary>작업 적성 데이터 (읽기용)</summary>
    public WorkAptitude Aptitude => aptitude;

    /// <summary>해당 작업의 적성 레벨.</summary>
    public int GetAptitudeLevel(WorkType type) => aptitude.GetLevel(type);

    /// <summary>
    /// 작업 적성 경험치를 획득합니다. 해당 작업을 실제로 수행할 때만 호출됩니다.
    /// 통합 레벨(GainExperience)과 달리 성장 비활성 직원도 적성은 오릅니다 —
    /// 적성은 스킬 해금 조건이라 모든 직원에게 필요합니다.
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
}
