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

    #endregion

    #region 프로퍼티

    /// <summary>현재 레벨</summary>
    public int Level => level;

    /// <summary>현재 경험치</summary>
    public int Experience => experience;

    /// <summary>다음 레벨까지 필요한 경험치</summary>
    public int ExperienceToNextLevel => experienceToNextLevel;

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

        if (statsController != null)
        {
            statsController.IncreaseMaxStats(healthGain, mentalGain, attackGain);
        }

        Debug.Log($"[Growth] {employee?.DisplayName} 레벨업! Lv.{level} (HP+{healthGain}, Mental+{mentalGain})");

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
    }

    #endregion
}
