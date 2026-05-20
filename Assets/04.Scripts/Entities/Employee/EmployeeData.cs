using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 직원 템플릿 데이터 (ScriptableObject).
/// 직원의 기본 정보, 스탯, 작업 능력, 특성, 욕구 설정을 정의합니다.
/// 인스턴스 생성 시 이 템플릿의 값이 초기값으로 사용됩니다.
/// StampSystem 메뉴에서 생성 가능합니다.
/// </summary>
[CreateAssetMenu(fileName = "NewEmployee", menuName = "StampSystem/Employee Data")]
public class EmployeeData : ScriptableObject
{
    #region 기본 정보

    [Header("기본 정보")]
    public int employeeID;
    public string employeeName;
    public Sprite portrait;

    [Header("직원 유형")]
    [Tooltip("유니크 직원은 성장 시스템이 적용됩니다")]
    public bool isUnique = true;

    #endregion

    #region 기본 스탯

    [Header("기본 스탯")]
    [Range(50, 200)]
    public int maxHealth = 100;
    [Range(50, 200)]
    public int maxMental = 100;
    [Range(1, 50)]
    public int attackPower = 10;

    #endregion

    #region 작업 능력

    [Header("작업 능력")]
    [Tooltip("이 직원이 수행할 수 있는 작업 종류")]
    public WorkAbilities abilities;

    #endregion

    #region 특성

    [Header("특성")]
    [Tooltip("이 직원이 가진 특성 목록")]
    public List<EmployeeTrait> traits = new List<EmployeeTrait>();

    #endregion

    #region 외형

    [Header("외형")]
    [Tooltip("헤어 스타일·색상 등 외형 데이터. null이면 외형 적용을 건너뜁니다.")]
    public EmployeeAppearance appearance;

    #endregion

    #region 초기 결격 작업

    [Header("초기 결격 작업")]
    [Tooltip("생성 시 비자격 상태로 시작할 작업 목록. 런타임에 AddDisqualification/RemoveDisqualification으로 변경 가능.")]
    public List<WorkType> initialDisqualifications = new List<WorkType>();

    #endregion

    #region 욕구 설정

    [Header("기본 욕구 설정")]
    [Range(0.1f, 5f)]
    [Tooltip("배고픔이 감소하는 속도 (포인트/초)")]
    public float hungerDecayRate = 1f;

    [Range(0.1f, 5f)]
    [Tooltip("피로가 증가하는 속도 (포인트/초)")]
    public float fatigueIncreaseRate = 0.5f;

    #endregion
}

/// <summary>
/// 직원의 작업 능력 데이터.
/// 각 작업 타입별 수행 가능 여부와 속도 보정을 정의합니다.
/// </summary>
[System.Serializable]
public class WorkAbilities
{
    #region 작업 가능 여부

    [Header("작업 능력 (체크된 항목만 수행 가능)")]
    public bool canMine = false;
    public bool canChop = false;
    public bool canResearch = false;
    public bool canCraft = false;
    public bool canGarden = false;
    public bool canBuild = false;
    public bool canHaul = false;
    public bool canDemolish = false;

    #endregion

    #region 운반 용량

    [Header("운반 (Hauling) 기본 용량")]
    [Tooltip("한 번에 들 수 있는 DroppedItem 개수의 기본값. 특성·성장 보너스로 추가 보정됩니다.")]
    [Range(1, 50)]
    public int baseCarryCapacity = 5;

    #endregion

    #region 속도 보정

    [Header("능력치 보정 (1.0 = 100% 속도)")]
    [Range(0.5f, 2f)]
    public float miningSpeed = 1f;
    [Range(0.5f, 2f)]
    public float choppingSpeed = 1f;
    [Range(0.5f, 2f)]
    public float researchSpeed = 1f;
    [Range(0.5f, 2f)]
    public float craftingSpeed = 1f;
    [Range(0.5f, 2f)]
    public float gardeningSpeed = 1f;
    [Range(0.5f, 2f)]
    public float buildingSpeed = 1f;
    [Range(0.5f, 2f)]
    public float haulingSpeed = 1f;
    [Range(0.5f, 2f)]
    public float demolishSpeed = 1f;

    #endregion

    #region 조회

    /// <summary>
    /// 지정한 작업 타입을 수행할 수 있는지 확인합니다.
    /// </summary>
    /// <param name="type">확인할 작업 타입</param>
    /// <returns>수행 가능 여부</returns>
    public bool CanPerformWork(WorkType type)
    {
        switch (type)
        {
            case WorkType.Mining: return canMine;
            case WorkType.Chopping: return canChop;
            case WorkType.Research: return canResearch;
            case WorkType.Crafting: return canCraft;
            case WorkType.Gardening: return canGarden;
            case WorkType.Building: return canBuild;
            case WorkType.Hauling: return canHaul;
            case WorkType.Demolish: return canDemolish;
            case WorkType.Resting: return true;
            case WorkType.Eating: return true;
            default: return false;
        }
    }

    /// <summary>
    /// 지정한 작업 타입의 속도 보정값을 반환합니다.
    /// 수행 불가능한 작업은 0을 반환합니다.
    /// </summary>
    /// <param name="type">조회할 작업 타입</param>
    /// <returns>속도 보정값 (0이면 수행 불가)</returns>
    public float GetWorkSpeed(WorkType type)
    {
        switch (type)
        {
            case WorkType.Mining: return canMine ? miningSpeed : 0f;
            case WorkType.Chopping: return canChop ? choppingSpeed : 0f;
            case WorkType.Research: return canResearch ? researchSpeed : 0f;
            case WorkType.Crafting: return canCraft ? craftingSpeed : 0f;
            case WorkType.Gardening: return canGarden ? gardeningSpeed : 0f;
            case WorkType.Building: return canBuild ? buildingSpeed : 0f;
            case WorkType.Hauling: return canHaul ? haulingSpeed : 0f;
            case WorkType.Demolish: return canDemolish ? demolishSpeed : 0f;
            case WorkType.Resting: return 1f;
            case WorkType.Eating: return 1f;
            default: return 0f;
        }
    }

    #endregion
}
