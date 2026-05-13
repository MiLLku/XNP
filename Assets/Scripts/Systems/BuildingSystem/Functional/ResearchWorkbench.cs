using UnityEngine;

/// <summary>
/// 연구 작업대 건물 컴포넌트.
/// 1명의 직원만 작업 가능하며, 직원의 '연구(researchSpeed)' 능력치에 비례하여 연구 포인트가 쌓입니다.
///
/// 작업 흐름:
///   1. 플레이어가 건물 클릭 → 직원 할당 패널 열기
///   2. 직원 선택 → StartResearch() → 직원이 작업 위치로 이동
///   3. 작업 중: ResearchManager에 매 프레임 포인트 누적
///   4. 중단: StopResearch() → 직원 해제, 효과 종료
///
/// 저장 위치: Assets/Scripts/Systems/BuildingSystem/Functional/ResearchWorkbench.cs
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ResearchWorkbench : MonoBehaviour, IBuildingFunction
{
    #region 필드 및 설정

    [Header("연구 설정")]
    [Tooltip("초당 기본 연구 포인트 (직원의 researchSpeed 배율로 실제 값 결정)")]
    [SerializeField] private float basePointsPerSecond = 1f;

    [Header("작업 위치")]
    [Tooltip("직원이 서서 작업할 위치 (비워두면 건물 중앙 자동 생성)")]
    [SerializeField] private Transform workPosition;

    [Header("시각 효과 (선택사항)")]
    [SerializeField] private GameObject workingEffectPrefab;
    [SerializeField] private Vector3 effectOffset = new Vector3(0, 0.5f, 0);

    // 상태
    private Employee _currentWorker;
    private bool _isResearching = false;
    private GameObject _effectInstance;

    // 컴포넌트 참조
    private Building _building;

    #endregion

    #region 프로퍼티

    /// <summary>현재 연구 진행 여부</summary>
    public bool IsResearching => _isResearching;

    /// <summary>건물이 현재 동작 중인지 여부 (IBuildingFunction)</summary>
    public bool IsOperating => _isResearching;

    /// <summary>현재 작업 중인 직원 (없으면 null)</summary>
    public Employee CurrentWorker => _currentWorker;

    /// <summary>직원이 연구 작업을 수행할 월드 좌표</summary>
    public Vector3 WorkPosition => workPosition != null ? workPosition.position : transform.position;

    /// <summary>
    /// 작업 가능 여부 확인.
    /// EmployeeWork의 ResearchWorkCoroutine에서 매 프레임 체크합니다.
    /// </summary>
    public bool IsWorkAvailable()
    {
        return _isResearching &&
               _building != null &&
               _building.IsFunctional;
    }

    /// <summary>초당 기본 연구 포인트</summary>
    public float BasePointsPerSecond => basePointsPerSecond;

    #endregion

    #region 초기화

    protected virtual void Awake()
    {
        _building = GetComponent<Building>();

        if (workPosition == null)
        {
            CreateDefaultWorkPosition();
        }
    }

    private void CreateDefaultWorkPosition()
    {
        GameObject posObj = new GameObject("WorkPosition_Auto");
        posObj.transform.SetParent(transform);

        // 건물 데이터가 있으면 크기 기반으로 배치
        if (_building != null && _building.buildingData != null)
        {
            Vector2Int size = _building.buildingData.size;
            posObj.transform.localPosition = new Vector3(size.x * 0.5f, 0f, 0f);
        }
        else
        {
            posObj.transform.localPosition = Vector3.zero;
        }

        workPosition = posObj.transform;
    }

    protected virtual void OnDestroy()
    {
        // 건물이 파괴될 때 작업 중이라면 중단
        if (_isResearching)
        {
            StopResearch();
        }
    }

    #endregion

    #region 클릭 및 UI 연동

    private void OnMouseDown()
    {
        if (_building != null && !_building.IsFunctional)
        {
            Debug.Log("[연구 작업대] 건물이 비활성화 상태입니다. 기반을 복구하세요.");
            return;
        }

        // 클릭 시 항상 ResearchWorkbenchUI 패널을 열어 상태 표시 + 버튼 제공
        OpenResearchStatusPanel();
    }

    /// <summary>
    /// 연구 작업대 UI 패널을 열어 현재 상태를 표시합니다.
    /// </summary>
    private void OpenResearchStatusPanel()
    {
        var panel = UIManager.instance?.GetPanel<ResearchWorkbenchUIPanel>(UIPanelType.ResearchWorkbenchUI);
        if (panel != null)
        {
            panel.Setup(this);
            UIManager.instance.ShowPanel(UIPanelType.ResearchWorkbenchUI);
        }
        else
        {
            Debug.LogWarning("[연구 작업대] ResearchWorkbenchUIPanel을 찾을 수 없습니다.");
        }
    }

    /// <summary>
    /// 직원 할당 패널을 엽니다.
    /// ResearchWorkbenchUIPanel의 '연구 진행' 버튼에서 호출됩니다.
    /// </summary>
    public void RequestWorkerAssignment()
    {
        WorkOrder order = new WorkOrder
        {
            orderName = "연구",
            workType = WorkType.Research,
            maxAssignedWorkers = 1
        };

        var panel = UIManager.instance?.GetPanel<WorkAssignmentPanel>(UIPanelType.WorkAssignment);
        if (panel != null)
        {
            panel.Setup(
                order,
                OnWorkerAssigned,
                OnAssignmentPanelClosed,
                OnAssignmentCancelled
            );
            UIManager.instance.ShowPanel(UIPanelType.WorkAssignment);
        }
        else
        {
            Debug.LogError("[연구 작업대] WorkAssignmentPanel을 찾을 수 없습니다!");
        }
    }

    #endregion

    #region 연구 흐름

    /// <summary>
    /// 직원이 할당되었을 때 WorkAssignmentPanel에서 호출됩니다.
    /// </summary>
    private void OnWorkerAssigned(Employee worker)
    {
        UIManager.instance?.HidePanel(UIPanelType.WorkAssignment);
        StartResearch(worker);
    }

    private void OnAssignmentPanelClosed()
    {
        UIManager.instance?.HidePanel(UIPanelType.WorkAssignment);
    }

    private void OnAssignmentCancelled()
    {
        UIManager.instance?.HidePanel(UIPanelType.WorkAssignment);
    }

    /// <summary>
    /// 연구를 시작합니다. 직원을 작업 위치로 이동시키고 연구 코루틴을 시작합니다.
    /// </summary>
    /// <param name="worker">연구를 수행할 직원</param>
    public void StartResearch(Employee worker)
    {
        if (worker == null)
        {
            Debug.LogWarning("[연구 작업대] 직원이 null입니다.");
            return;
        }

        if (_isResearching)
        {
            Debug.LogWarning("[연구 작업대] 이미 연구 진행 중입니다. 먼저 중단하세요.");
            return;
        }

        _currentWorker = worker;
        _isResearching = true;

        // 직원에게 연구 작업 할당 (이동 포함)
        Vector3 pos = workPosition != null ? workPosition.position : transform.position;
        worker.AssignResearchWork(this, pos);

        StartWorkingEffect();
        Debug.Log($"[연구 작업대] {worker.DisplayName}이(가) 연구를 시작합니다. (기본 {basePointsPerSecond}/s)");
    }

    /// <summary>
    /// 연구를 중단합니다. 직원을 해제하고 상태를 초기화합니다.
    /// </summary>
    public void StopResearch()
    {
        if (_currentWorker != null)
        {
            _currentWorker.CancelWork();
            _currentWorker = null;
        }

        _isResearching = false;
        StopWorkingEffect();

        Debug.Log("[연구 작업대] 연구가 중단되었습니다.");
    }

    /// <summary>
    /// EmployeeWork의 연구 코루틴에서 매 프레임 호출됩니다.
    /// 직원의 researchSpeed를 반영한 포인트를 ResearchManager에 추가합니다.
    /// </summary>
    /// <param name="speedMultiplier">직원의 최종 연구 속도 배율</param>
    /// <param name="deltaTime">이번 프레임의 경과 시간</param>
    public void OnResearchTick(float speedMultiplier, float deltaTime)
    {
        if (!_isResearching || ResearchManager.instance == null) return;

        float xenopsBonus = ContaminationSphereBehavior.GetWorkSpeedBonusAt(transform.position);
        float points = basePointsPerSecond * speedMultiplier * (1f + xenopsBonus) * deltaTime;
        ResearchManager.instance.AddPoints(points);
    }

    /// <summary>
    /// 직원이 연구를 떠났을 때 EmployeeWork 코루틴에서 호출됩니다.
    /// (코루틴 자연 종료 또는 CancelWork() 시)
    /// </summary>
    public void OnWorkerLeft()
    {
        _currentWorker = null;
        _isResearching = false;
        StopWorkingEffect();

        Debug.Log("[연구 작업대] 직원이 연구를 떠났습니다.");
    }

    #endregion

    #region 시각 효과

    private void StartWorkingEffect()
    {
        if (workingEffectPrefab == null || _effectInstance != null) return;
        _effectInstance = Instantiate(workingEffectPrefab, transform.position + effectOffset, Quaternion.identity, transform);
    }

    private void StopWorkingEffect()
    {
        if (_effectInstance != null)
        {
            Destroy(_effectInstance);
            _effectInstance = null;
        }
    }

    #endregion

    #region IBuildingFunction 구현

    /// <summary>건물이 비활성화될 때 (기반 파괴 등) 연구를 중단합니다.</summary>
    public void OnBuildingDisabled()
    {
        if (_isResearching)
        {
            StopResearch();
            Debug.Log("[연구 작업대] 건물이 비활성화되어 연구가 중단되었습니다.");
        }
    }

    /// <summary>건물이 다시 활성화될 때 호출됩니다.</summary>
    public void OnBuildingEnabled()
    {
        Debug.Log("[연구 작업대] 건물이 다시 활성화되었습니다. 연구를 재시작하려면 직원을 할당하세요.");
    }

    #endregion
}
