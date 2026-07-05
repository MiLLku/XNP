using UnityEngine;

/// <summary>
/// 오락 시설 컴포넌트. 다트판·게임기 등 오락 건물 프리팹에 부착합니다.
///
/// 동작:
///   - Awake에서 Unity Tag를 FacilityTag.Recreation으로 설정 → 기존 AI 시설 탐색에 자동 편입
///   - 직원이 도착하면 EmployeeAI가 funPerSecond/mentalPerSecond를 읽어 틱 회복
///   - PowerConsumer가 붙어 있으면 정전 시 사용 불가 (CanUse=false)
///
/// 우선순위(priority)는 인스펙터에서 프리팹별로 조정 — 높을수록 직원이 선호합니다.
/// </summary>
public class RecreationFacility : MonoBehaviour, IBuildingFunction, IFunSource
{
    [Header("오락 성능")]
    [Tooltip("선택 우선순위 (높을수록 직원이 선호). 예: 다트판 0, 게임기 10")]
    [SerializeField] private int priority = 0;

    [Tooltip("이용 중 초당 재미 회복량")]
    [SerializeField] private float funPerSecond = 5f;

    [Tooltip("이용 중 초당 정신력 회복량 (오락의 부수 효과)")]
    [SerializeField] private float mentalPerSecond = 2f;

    /// <summary>기반 파괴 등으로 비활성화됐는지</summary>
    private bool buildingEnabled = true;

    /// <summary>전력 소비 컴포넌트 (없으면 무전력 시설)</summary>
    private PowerConsumer powerConsumer;

    #region IFunSource

    public int Priority => priority;

    public float FunPerSecond => funPerSecond;

    /// <summary>초당 정신력 회복량 (EmployeeAI 틱에서 참조)</summary>
    public float MentalPerSecond => mentalPerSecond;

    public bool CanUse(Employee employee)
    {
        return IsOperating;
    }

    #endregion

    #region IBuildingFunction

    public bool IsOperating => buildingEnabled && (powerConsumer == null || powerConsumer.IsPowered);

    public void OnBuildingDisabled() => buildingEnabled = false;

    public void OnBuildingEnabled() => buildingEnabled = true;

    #endregion

    private void Awake()
    {
        powerConsumer = GetComponent<PowerConsumer>();

        // 기존 AI 시설 탐색(FindGameObjectsWithTag)에 자동 편입
        if (!gameObject.CompareTag(FacilityTag.Recreation))
            gameObject.tag = FacilityTag.Recreation;
    }

    private void OnEnable()
    {
        // 시설 목록 캐시 무효화 (새 시설 즉시 인식)
        EmployeeAI.InvalidateTagCache(FacilityTag.Recreation);
    }

    private void OnDestroy()
    {
        EmployeeAI.InvalidateTagCache(FacilityTag.Recreation);
    }
}
