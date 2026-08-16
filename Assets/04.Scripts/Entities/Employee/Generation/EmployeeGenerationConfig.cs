using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무작위 직원 생성 설정 데이터 (ScriptableObject).
/// 이름·헤어·특성 풀과 기본 스탯 고정값을 한 곳에서 관리합니다.
/// 메뉴: StampSystem ▶ Employee Generation Config
/// </summary>
[CreateAssetMenu(fileName = "EmployeeGenerationConfig", menuName = "StampSystem/Employee Generation Config")]
public class EmployeeGenerationConfig : ScriptableObject
{
    #region 이름 풀

    [Header("이름 풀")]
    [Tooltip("무작위 성 목록")]
    public string[] lastNames =
    {
        "김", "이", "박", "최", "정", "강", "조", "윤", "장", "임",
        "한", "오", "서", "신", "권", "황", "안", "송", "류", "전"
    };

    [Tooltip("무작위 이름 목록")]
    public string[] firstNames =
    {
        "민준", "서준", "도윤", "예준", "시우", "지호", "주원", "하준", "지훈", "준서",
        "서연", "서윤", "지우", "서현", "하은", "민서", "수아", "유나", "지민", "지아",
        "건우", "현우", "유준", "동현", "재원", "지수", "채원", "수빈", "다은", "민지"
    };

    #endregion

    #region 외형 풀

    [Header("외형 풀 (헤어 에셋 추가 후 채워주세요)")]
    [Tooltip("헤어 스타일 스프라이트 목록. 비어있으면 Hair 렌더러를 비활성화합니다.")]
    public List<Sprite> hairStylePool = new List<Sprite>();

    [Tooltip("헤어 색상 목록. 비어있으면 기본 검정을 사용합니다.")]
    public Color[] hairColorPool =
    {
        new Color(0.10f, 0.08f, 0.06f), // 흑발
        new Color(0.55f, 0.27f, 0.07f), // 갈색
        new Color(1.00f, 0.90f, 0.50f), // 금발
        new Color(0.60f, 0.60f, 0.60f), // 은발/회색
        new Color(0.80f, 0.10f, 0.10f), // 적발
    };

    #endregion

    #region 특성 풀

    [Header("특성 풀")]
    [Tooltip("후보 생성 시 선택 가능한 전체 EmployeeTrait 목록")]
    public List<EmployeeTrait> traitPool = new List<EmployeeTrait>();

    [Header("특성 개수 범위")]
    [Tooltip("부여할 특성 최소 개수 (0 가능)")]
    [Range(0, 3)]
    public int traitCountMin = 0;

    [Tooltip("부여할 특성 최대 개수")]
    [Range(0, 3)]
    public int traitCountMax = 3;

    #endregion

    #region 기본 스탯 (고정)

    [Header("기본 스탯 (고정 — 변화는 특성·장비로)")]
    [Tooltip("모든 무작위 생성 직원의 기본 최대 체력")]
    [Range(50, 200)]
    public int baseMaxHealth = 100;

    [Tooltip("모든 무작위 생성 직원의 기본 최대 정신력 (상한)")]
    [Range(50, 200)]
    public int baseMaxMental = 100;

    [Tooltip("모든 무작위 생성 직원의 기본 정신력 — 모디파이어가 없을 때 수렴하는 값.\n" +
             "긍정 요소는 이 위로, 굶주림·탈진은 이 아래로 움직이며 원인이 사라지면 여기로 돌아옵니다.")]
    [Range(0, 200)]
    public int baseMentalValue = 50;

    [Tooltip("무작위 생성 직원의 근접 숙련 시작 레벨. 전투 능력치 자체는 무기가 갖고, 숙련은 그것을 조정합니다.")]
    [Range(1, CombatAptitude.MAX_LEVEL)]
    public int baseMeleeLevel = 1;

    [Tooltip("무작위 생성 직원의 원거리 숙련 시작 레벨")]
    [Range(1, CombatAptitude.MAX_LEVEL)]
    public int baseRangedLevel = 1;

    [Header("욕구 기본값")]
    [Range(0.1f, 5f)]
    public float baseHungerDecayRate = 1f;

    [Range(0.1f, 5f)]
    public float baseFatigueIncreaseRate = 0.5f;

    #endregion

    #region 기본 작업 능력

    [Header("기본 작업 능력 (무작위 생성 직원 공통)")]
    public bool defaultCanMine      = true;
    public bool defaultCanChop      = true;
    public bool defaultCanHaul      = true;
    public bool defaultCanBuild     = true;
    public bool defaultCanDemolish  = true;
    public bool defaultCanCraft     = false;
    public bool defaultCanResearch  = false;
    public bool defaultCanGarden    = false;

    #endregion

    #region 운반 용량

    [Header("운반 용량 범위 (무작위 생성)")]
    [Tooltip("무작위 생성 시 부여할 기본 운반 용량의 최소값")]
    [Range(1, 50)]
    public int baseCarryCapacityMin = 3;

    [Tooltip("무작위 생성 시 부여할 기본 운반 용량의 최대값 (포함). 최소값과 같으면 고정.")]
    [Range(1, 50)]
    public int baseCarryCapacityMax = 8;

    #endregion

    #region 초기 결격 작업

    [Header("작업별 결격 확률 (각 작업은 독립적으로 롤됩니다)")]
    [Tooltip("0%이면 해당 작업은 결격 대상에서 제외됩니다.")]
    public List<WorkTypeDisqualChance> disqualChances = new List<WorkTypeDisqualChance>
    {
        new WorkTypeDisqualChance { workType = WorkType.Mining,    chance = 30f },
        new WorkTypeDisqualChance { workType = WorkType.Chopping,  chance = 25f },
        new WorkTypeDisqualChance { workType = WorkType.Building,  chance = 20f },
        new WorkTypeDisqualChance { workType = WorkType.Hauling,   chance = 15f },
        new WorkTypeDisqualChance { workType = WorkType.Demolish,  chance = 20f },
        new WorkTypeDisqualChance { workType = WorkType.Crafting,  chance = 10f },
        new WorkTypeDisqualChance { workType = WorkType.Research,  chance =  5f },
        new WorkTypeDisqualChance { workType = WorkType.Gardening, chance = 15f },
    };

    [Tooltip("확률 롤 결과로 나온 결격 수가 이 값을 초과하면 초과분을 무작위 제거합니다. (최대 4)")]
    [Range(0, 4)]
    public int disqualCountMax = 4;

    [Header("결격 보정")]
    [Tooltip("결격 작업 1개당 비결격 작업들의 속도 보정 (%). 10이면 결격 2개 시 비결격 작업 속도 +20%")]
    [Range(0f, 30f)]
    public float speedBonusPerDisqual = 10f;

    #endregion
}

/// <summary>
/// 특정 작업 타입의 결격 확률 설정.
/// EmployeeGenerationConfig.disqualChances 리스트의 항목으로 사용됩니다.
/// </summary>
[System.Serializable]
public class WorkTypeDisqualChance
{
    [Tooltip("결격 대상 작업 타입")]
    public WorkType workType;

    [Tooltip("이 작업이 결격될 확률 (0~100%). 0이면 결격되지 않습니다.")]
    [Range(0f, 100f)]
    public float chance;
}
