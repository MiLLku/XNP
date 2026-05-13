using System;
using UnityEngine;

/// <summary>
/// 제노프스 해석도 서브컴포넌트.
/// EmployeeGrowth 패턴 기반으로, 플레이어의 적극적 상호작용 시에만
/// 경험치가 쌓여 해석도가 올라갑니다.
///
/// 해석도가 올라갈수록:
///   - 이로운 효과(benefit)가 강화됩니다.
///   - 해로운 효과(drawback)가 완화됩니다.
///
/// 레벨업 필요 경험치: baseExpRequired × (level + 1)^1.3
/// </summary>
public class XenopsInterpretation : MonoBehaviour
{
    #region 상수

    /// <summary>경험치 스케일링 지수</summary>
    private const float EXP_SCALING_EXPONENT = 1.3f;

    #endregion

    #region 필드

    [Header("해석도")]
    [SerializeField] private int level = 0;
    [SerializeField] private int experience = 0;
    [SerializeField] private int expToNextLevel;

    /// <summary>최대 해석도 레벨</summary>
    private int maxLevel;

    /// <summary>기본 필요 경험치 (XenopsData에서 설정)</summary>
    private int baseExpRequired;

    /// <summary>코디네이터 참조</summary>
    private Xenops xenops;

    /// <summary>초기화 완료 여부</summary>
    private bool _initialized = false;

    #endregion

    #region 이벤트

    /// <summary>레벨업 시 발생 (새 레벨 전달)</summary>
    public event Action<int> OnLevelUp;

    #endregion

    #region 프로퍼티

    /// <summary>현재 해석도 레벨 (0 = 미해석)</summary>
    public int Level => level;

    /// <summary>현재 경험치</summary>
    public int Experience => experience;

    /// <summary>다음 레벨까지 필요한 경험치</summary>
    public int ExpToNextLevel => expToNextLevel;

    /// <summary>최대 레벨</summary>
    public int MaxLevel => maxLevel;

    /// <summary>해석도 비율 (0~1). 효과 보간에 사용됩니다.</summary>
    public float Ratio => maxLevel > 0 ? (float)level / maxLevel : 0f;

    /// <summary>최대 레벨 도달 여부</summary>
    public bool IsMaxLevel => level >= maxLevel;

    #endregion

    #region 초기화

    void Awake()
    {
        xenops = GetComponent<Xenops>();
    }

    /// <summary>
    /// 해석도 시스템을 초기화합니다 (새 Xenops 생성 시).
    /// </summary>
    /// <param name="maxLevel">최대 해석도 레벨</param>
    /// <param name="baseExpRequired">기본 필요 경험치</param>
    public void Initialize(int maxLevel, int baseExpRequired)
    {
        _initialized = true;

        this.maxLevel = maxLevel;
        this.baseExpRequired = baseExpRequired;
        level = 0;
        experience = 0;
        expToNextLevel = CalculateExpToNextLevel(0);
    }

    #endregion

    #region 경험치 및 레벨업

    /// <summary>
    /// 해석 경험치를 획득합니다.
    /// 플레이어의 적극적 상호작용 시에만 호출되어야 합니다.
    /// </summary>
    /// <param name="amount">획득 경험치량</param>
    public void GainExperience(int amount)
    {
        if (!_initialized || IsMaxLevel) return;
        if (amount <= 0) return;

        experience += amount;

        Debug.Log($"[XenopsInterpretation] {xenops?.DisplayName} 해석 경험치 획득: +{amount} ({experience}/{expToNextLevel})");

        while (experience >= expToNextLevel && !IsMaxLevel)
        {
            LevelUp();
        }
    }

    /// <summary>
    /// 레벨업을 수행합니다.
    /// </summary>
    private void LevelUp()
    {
        experience -= expToNextLevel;
        level++;

        if (!IsMaxLevel)
        {
            expToNextLevel = CalculateExpToNextLevel(level);
        }
        else
        {
            experience = 0;
            expToNextLevel = 0;
        }

        Debug.Log($"[XenopsInterpretation] {xenops?.DisplayName} 해석도 상승! Lv.{level}/{maxLevel} (비율: {Ratio:P0})");

        OnLevelUp?.Invoke(level);
    }

    /// <summary>
    /// 다음 레벨 필요 경험치를 계산합니다.
    /// 공식: baseExpRequired × (level + 1)^1.3
    /// </summary>
    private int CalculateExpToNextLevel(int currentLevel)
    {
        return Mathf.RoundToInt(baseExpRequired * Mathf.Pow(currentLevel + 1, EXP_SCALING_EXPONENT));
    }

    #endregion

    #region 저장/복원

    /// <summary>
    /// 저장 데이터에 해석도 정보를 기록합니다.
    /// </summary>
    public void PopulateSaveData(XenopsSaveData data)
    {
        data.interpretationLevel = level;
        data.interpretationExp = experience;
        data.interpretationExpToNext = expToNextLevel;
    }

    /// <summary>
    /// 저장 데이터에서 해석도 정보를 복원합니다.
    /// </summary>
    public void RestoreFromSaveData(XenopsSaveData data, int maxLevel, int baseExpRequired)
    {
        _initialized = true;

        this.maxLevel = maxLevel;
        this.baseExpRequired = baseExpRequired;
        level = data.interpretationLevel;
        experience = data.interpretationExp;
        expToNextLevel = data.interpretationExpToNext;
    }

    #endregion
}
