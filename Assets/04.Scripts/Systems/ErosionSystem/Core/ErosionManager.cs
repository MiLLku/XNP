using UnityEngine;

/// <summary>
/// 침식 시스템 전역 매니저.
/// DestroySingleton + ISaveModule 패턴을 따릅니다.
///
/// 담당 기능:
///   - EmployeeErosionController에 설정 주입
///   - 직원 → 제놉스 변이 처리
///   - 정화 약품 적용
///   - 포스트 레이드 회복 활성화/관리
///   - 정화 건물 등록/해제 및 근접 여부 조회
///   - ErosionSystemSaveData 저장/복원
///
/// 씬에 하나의 GameObject에 부착하여 사용하세요.
/// </summary>
public class ErosionManager : DestroySingleton<ErosionManager>, ISaveModule
{
    #region 인스펙터 설정

    [Header("침식 설정")]
    [Tooltip("침식 단계별 설정 ScriptableObject")]
    [SerializeField] private ErosionStageConfig stageConfig;

    [Tooltip("침식 회복 파라미터 ScriptableObject")]
    [SerializeField] private ErosionRecoveryConfig recoveryConfig;

    [Tooltip("세척 시설 밸런스 기준값 (전 티어 공통). 비어 있으면 코드 기본값을 씁니다.")]
    [SerializeField] private WashConfig washConfig;

    [Header("변이 설정")]
    [Tooltip("완전 침식 시 생성할 '침식자(Corroded)' XenopsData ID")]
    [SerializeField] private int corrodedXenopsDataId;

    [Header("디버그")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    #region 상태

    /// <summary>
    /// 자연 회복 하한을 낮추는 런타임 감소량 (연구 외 경로 — 이벤트·건물 등).
    /// 게임 진행에 따라 하한이 내려가도록 하는 축의 하나이며, 세이브에 저장된다.
    /// </summary>
    [SerializeField] private float runtimeFloorReduction;

    #endregion

    #region 프로퍼티

    public ErosionStageConfig StageConfig => stageConfig;
    public ErosionRecoveryConfig RecoveryConfig => recoveryConfig;
    public WashConfig WashConfig => washConfig;

    /// <summary>
    /// 씻어낸 침식 1당 산출되는 결정체 수 (전역 기본값).
    /// Config 에셋이 없어도 안전하게 동작하도록 폴백을 둡니다.
    /// </summary>
    public float CrystalPerErosion =>
        washConfig != null ? washConfig.crystalPerErosion : WashConfig.DEFAULT_CRYSTAL_PER_EROSION;

    /// <summary>런타임 하한 감소량 (읽기용)</summary>
    public float RuntimeFloorReduction => runtimeFloorReduction;

    /// <summary>
    /// 현재 유효한 자연 회복 하한.
    /// <code>max(0, 기본하한 - 연구 감소 - 런타임 감소)</code>
    /// 게임 초반에는 하한이 높아 세척 시설이 필수지만, 연구가 진행될수록 낮아져 자립도가 올라간다.
    /// </summary>
    public float EffectiveRecoveryFloor
    {
        get
        {
            if (recoveryConfig == null) return 0f;

            float researchReduction = 0f;
            var rt = ResearchTreeManager.instance;
            if (rt != null)
                researchReduction = rt.GetStatBonus(ResearchStatType.ErosionRecoveryFloorReduction);

            return Mathf.Max(0f,
                recoveryConfig.naturalRecoveryFloor - researchReduction - runtimeFloorReduction);
        }
    }

    #endregion

    #region ISaveModule

    /// <summary>Employee(50)보다 뒤, Xenops(55 가정)보다 앞에 복원</summary>
    public int SaveOrder => 52;

    public void Capture(SaveData data)
    {
        data.erosionSystem = new ErosionSystemSaveData
        {
            runtimeFloorReduction = runtimeFloorReduction
        };
    }

    public void Restore(SaveData data)
    {
        if (data.erosionSystem == null) return;

        runtimeFloorReduction = data.erosionSystem.runtimeFloorReduction;
    }

    public void PostRestore(SaveData data) { }

    #endregion

    #region 초기화 및 생명주기

    protected override void Awake()
    {
        base.Awake();
        AbnormalBehaviorRegistry.Initialize();
    }

    #endregion

    #region 공개 API — 회복 하한

    /// <summary>
    /// 자연 회복 하한을 영구적으로 낮춥니다 (이벤트·건물 완공 등).
    /// 연구로 인한 감소와는 별도로 누적됩니다.
    /// </summary>
    /// <param name="amount">낮출 수치 (양수)</param>
    public void ReduceRecoveryFloor(float amount)
    {
        if (amount <= 0f) return;

        runtimeFloorReduction += amount;

        if (showDebugLogs)
            Debug.Log($"[ErosionManager] 자연 회복 하한 감소 +{amount} → 현재 유효 하한 {EffectiveRecoveryFloor:F1}");
    }

    #endregion

    #region 공개 API — 변이

    /// <summary>
    /// 직원을 침식자(Corroded) 제놉스로 변이시킵니다.
    /// EmployeeErosionController.TriggerMutation()에서 호출됩니다.
    /// </summary>
    public void MutateEmployeeToXenops(Employee employee)
    {
        if (employee == null) return;

        Vector3 position = employee.transform.position;
        string name = employee.DisplayName;

        if (showDebugLogs)
            Debug.Log($"[ErosionManager] {name} 변이 시작. 위치: {position}");

        // 직원 제거
        if (EmployeeManager.instance != null)
            EmployeeManager.instance.RemoveEmployee(employee);

        // 침식자 스폰
        if (corrodedXenopsDataId > 0 && XenopsManager.instance != null)
        {
            var xenops = XenopsManager.instance.SpawnXenops(corrodedXenopsDataId, position);
            if (showDebugLogs && xenops != null)
                Debug.Log($"[ErosionManager] {name} → 침식자 변이 완료 (XenopsID: {xenops.InstanceId})");
        }
        else
        {
            Debug.LogWarning($"[ErosionManager] corrodedXenopsDataId({corrodedXenopsDataId})가 설정되지 않았거나 XenopsManager가 없습니다. 직원만 제거됩니다.");
        }

        GameMessageBus.Publish(new EmployeeMutatedMessage(employee));
    }

    #endregion

    #region 공개 API — 정화

    /// <summary>
    /// 정화 약품을 직원에게 적용합니다.
    /// </summary>
    public void ApplyPurificationItem(Employee employee)
    {
        if (employee == null || recoveryConfig == null) return;

        float before = employee.ErosionLevel;
        employee.ErosionController?.ReduceErosion(recoveryConfig.purificationItemAmount, "정화 약품");

        if (showDebugLogs)
            Debug.Log($"[ErosionManager] {employee.DisplayName} 정화 약품 사용: 침식 {before:F1} → {employee.ErosionLevel:F1}");
    }

    #endregion

    #region 컨텍스트 메뉴 (디버그)

    [ContextMenu("모든 직원 침식 초기화 (테스트)")]
    private void DebugResetAllErosion()
    {
        if (EmployeeManager.instance == null) return;
        foreach (var emp in EmployeeManager.instance.AllEmployees)
            emp?.ErosionController?.ClearErosion();
        Debug.Log("[ErosionManager] 모든 직원 침식 초기화 완료");
    }

    #endregion
}
