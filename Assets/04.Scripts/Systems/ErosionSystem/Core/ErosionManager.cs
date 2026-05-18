using System;
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

    [Header("변이 설정")]
    [Tooltip("완전 침식 시 생성할 '침식자(Corroded)' XenopsData ID")]
    [SerializeField] private int corrodedXenopsDataId;

    [Header("디버그")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    #region 상태

    private bool isPostRaidRecoveryActive;
    private float postRaidRecoveryTimer;

    #endregion

    #region 이벤트

    /// <summary>직원이 제놉스로 변이할 때 발행됩니다.</summary>
    public event Action<Employee> OnEmployeeMutated;

    #endregion

    #region 프로퍼티

    public ErosionStageConfig StageConfig => stageConfig;
    public ErosionRecoveryConfig RecoveryConfig => recoveryConfig;

    /// <summary>포스트 레이드 가속 회복이 활성화 중인지 여부</summary>
    public bool IsPostRaidRecovery => isPostRaidRecoveryActive;

    #endregion

    #region ISaveModule

    /// <summary>Employee(50)보다 뒤, Xenops(55 가정)보다 앞에 복원</summary>
    public int SaveOrder => 52;

    public void Capture(SaveData data)
    {
        data.erosionSystem = new ErosionSystemSaveData
        {
            isPostRaidRecoveryActive = isPostRaidRecoveryActive,
            postRaidRecoveryTimer = postRaidRecoveryTimer
        };
    }

    public void Restore(SaveData data)
    {
        if (data.erosionSystem == null) return;

        isPostRaidRecoveryActive = data.erosionSystem.isPostRaidRecoveryActive;
        postRaidRecoveryTimer = data.erosionSystem.postRaidRecoveryTimer;
    }

    public void PostRestore(SaveData data) { }

    #endregion

    #region 초기화 및 생명주기

    protected override void Awake()
    {
        base.Awake();
        AbnormalBehaviorRegistry.Initialize();
    }

    private void Update()
    {
        if (!isPostRaidRecoveryActive) return;
        if (recoveryConfig == null) return;

        postRaidRecoveryTimer -= Time.deltaTime;
        if (postRaidRecoveryTimer <= 0f)
        {
            isPostRaidRecoveryActive = false;
            postRaidRecoveryTimer = 0f;

            if (showDebugLogs)
                Debug.Log("[ErosionManager] 포스트 레이드 가속 회복 종료.");
        }
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

        OnEmployeeMutated?.Invoke(employee);
    }

    #endregion

    #region 공개 API — 정화

    /// <summary>
    /// 정화 약품을 직원에게 적용합니다.
    /// </summary>
    public void ApplyPurificationItem(Employee employee)
    {
        if (employee == null || recoveryConfig == null) return;

        float newErosion = Mathf.Max(0f, employee.ErosionLevel - recoveryConfig.purificationItemAmount);
        employee.SetErosion(newErosion);

        if (showDebugLogs)
            Debug.Log($"[ErosionManager] {employee.DisplayName} 정화 약품 사용: 침식 {employee.ErosionLevel + recoveryConfig.purificationItemAmount:F1} → {newErosion:F1}");
    }

    #endregion

    #region 공개 API — 포스트 레이드 회복

    /// <summary>
    /// 포스트 레이드 가속 회복을 시작합니다.
    /// RaidManager.CompleteRaid()에서 호출됩니다.
    /// </summary>
    public void StartPostRaidRecovery()
    {
        if (recoveryConfig == null) return;

        isPostRaidRecoveryActive = true;
        postRaidRecoveryTimer = recoveryConfig.postRaidRecoveryDuration;

        if (showDebugLogs)
            Debug.Log($"[ErosionManager] 포스트 레이드 가속 회복 시작 ({recoveryConfig.postRaidRecoveryDuration}초)");
    }

    #endregion

    #region 컨텍스트 메뉴 (디버그)

    [ContextMenu("포스트 레이드 회복 시작 (테스트)")]
    private void DebugStartPostRaid() => StartPostRaidRecovery();

    [ContextMenu("모든 직원 침식 초기화 (테스트)")]
    private void DebugResetAllErosion()
    {
        if (EmployeeManager.instance == null) return;
        foreach (var emp in EmployeeManager.instance.AllEmployees)
            emp?.SetErosion(0f);
        Debug.Log("[ErosionManager] 모든 직원 침식 초기화 완료");
    }

    #endregion
}
