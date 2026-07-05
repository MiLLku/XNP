using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 직원 침식 제어 서브 컴포넌트.
/// Employee 코디네이터에 의해 관리됩니다.
///
/// 담당 기능:
///   - 침식 수치 → 단계 판정 및 이벤트 발행
///   - 단계별 작업/이동 속도 디버프 모디파이어 제공
///   - 이상 행동 타이머 기반 확률 판정 및 실행
///   - 4단계(Critical) 침식 전파 오라
///   - 자연/휴식/포스트레이드 회복 처리
///   - 완전 침식(200) 시 ErosionManager.MutateEmployeeToXenops() 호출
///
/// 오라 노출 추적:
///   HostileErosionAura가 침식을 적용할 때 MarkAuraExposure()를 호출합니다.
///   이 플래그를 통해 "오라 밖에서 20초 경과" 회복 조건을 관리합니다.
/// </summary>
[RequireComponent(typeof(Employee))]
public class EmployeeErosionController : MonoBehaviour
{
    #region 상수

    private const float PROPAGATION_CHECK_INTERVAL = 0.5f;
    private const float FULL_EROSION_THRESHOLD = 200f;

    #endregion

    #region 설정

    private ErosionStageConfig stageConfig;
    private ErosionRecoveryConfig recoveryConfig;

    #endregion

    #region 상태

    /// <summary>현재 침식 단계</summary>
    [SerializeField] private ErosionStage currentStage = ErosionStage.Normal;

    /// <summary>이상 행동 판정 타이머</summary>
    private float behaviorCheckTimer;

    /// <summary>마지막 오라 노출 후 경과 시간 (초)</summary>
    [SerializeField] private float timeSinceLastAuraExposure;

    /// <summary>현재 프레임에 오라 노출 여부 (HostileErosionAura에서 설정)</summary>
    private bool auraExposureThisFrame;

    /// <summary>현재 실행 중인 이상 행동</summary>
    private IAbnormalBehavior activeAbnormalBehavior;

    /// <summary>이상 행동 남은 지속 시간</summary>
    private float abnormalBehaviorRemainingTime;

    /// <summary>이상 행동 실행 중 작업 배정 차단 여부</summary>
    private bool isBlockingWorkAssignment;

    /// <summary>전파 오라 타이머</summary>
    private float propagationTimer;

    /// <summary>변이 처리 완료 여부 (중복 변이 방지)</summary>
    private bool hasMutated;

    /// <summary>
    /// 자연 침식 최고 노출 수치 워터마크.
    /// 이 값 이하의 자연 침식 수치는 무시됩니다 (증분만 적용).
    /// ErosionLevel이 0으로 완전 회복되면 0으로 초기화됩니다.
    /// </summary>
    private float naturalErosionWatermark;

    #endregion

    #region 캐시

    private float cachedWorkSpeedModifier = 1f;
    private float cachedMoveSpeedModifier = 1f;
    private ErosionStageDefinition cachedStageDef;

    #endregion

    #region 컴포넌트 참조

    private Employee employee;
    private EmployeeStatsController statsController;

    #endregion

    #region 이벤트

    /// <summary>침식 단계가 변경될 때 발행됩니다. (이전 단계, 새 단계)</summary>
    public event Action<ErosionStage, ErosionStage> OnStageChanged;

    #endregion

    #region 프로퍼티

    /// <summary>현재 침식 단계</summary>
    public ErosionStage CurrentStage => currentStage;

    /// <summary>작업 속도에 곱해질 침식 디버프 배율 (1.0 = 정상)</summary>
    public float WorkSpeedModifier => cachedWorkSpeedModifier;

    /// <summary>이동 속도에 곱해질 침식 디버프 배율 (1.0 = 정상)</summary>
    public float MoveSpeedModifier => cachedMoveSpeedModifier;

    /// <summary>이상 행동 실행 중 여부</summary>
    public bool IsPerformingAbnormalBehavior => activeAbnormalBehavior != null;

    /// <summary>작업 배정이 차단된 상태인지 여부</summary>
    public bool IsWorkBlocked => isBlockingWorkAssignment;

    #endregion

    #region 초기화

    private void Awake()
    {
        employee = GetComponent<Employee>();
        statsController = GetComponent<EmployeeStatsController>();
    }

    /// <summary>
    /// 설정을 주입합니다. Employee.Initialize()에서 호출됩니다.
    /// </summary>
    public void Initialize(ErosionStageConfig config, ErosionRecoveryConfig recovery)
    {
        stageConfig = config;
        recoveryConfig = recovery;

        currentStage = ErosionStage.Normal;
        timeSinceLastAuraExposure = 0f;
        auraExposureThisFrame = false;
        hasMutated = false;
        naturalErosionWatermark = 0f;

        RefreshStageCache(ErosionStage.Normal);
    }

    #endregion

    #region 업데이트

    private void Update()
    {
        if (employee == null || employee.State == EmployeeState.Dead || hasMutated) return;
        if (statsController == null || stageConfig == null) return;

        float dt = Time.deltaTime;

        UpdateStage();
        UpdateAbnormalBehaviorTimer(dt);
        UpdatePropagationAura(dt);
        UpdateRecovery(dt);

        // 프레임 오라 노출 플래그 리셋
        auraExposureThisFrame = false;
    }

    /// <summary>
    /// 현재 erosionLevel을 읽어 단계를 갱신합니다.
    /// </summary>
    private void UpdateStage()
    {
        float erosion = statsController.ErosionLevel;

        // 완전 침식 체크
        if (erosion >= FULL_EROSION_THRESHOLD && !hasMutated)
        {
            TriggerMutation();
            return;
        }

        if (stageConfig == null) return;

        // 이상행동 임계점 배율: 저항이 높을수록 유효 침식을 낮춰 단계 상승(=이상행동)을 늦춘다.
        // 재미가 낮으면 저항이 깎여(GetFunErosionFactor < 1) 유효 침식이 높아진다 — 취약.
        // (완전 침식/변이 판정은 실제 침식 그대로 — 위에서 처리)
        float resist = statsController.CachedAbnormalResistMult * statsController.GetFunErosionFactor();
        float effectiveErosion = resist > 0f ? erosion / resist : erosion;

        var def = stageConfig.GetStageDefinition(effectiveErosion);
        if (def == null) return;

        ErosionStage newStage = def.stage;
        if (newStage == currentStage) return;

        ErosionStage prevStage = currentStage;
        currentStage = newStage;
        RefreshStageCache(newStage);

        OnStageChanged?.Invoke(prevStage, newStage);

        Debug.Log($"[ErosionController] {employee.DisplayName}: 침식 단계 변경 {prevStage} → {newStage} (침식: {erosion:F1})");
    }

    /// <summary>
    /// 이상 행동 타이머와 지속 시간을 처리합니다.
    /// </summary>
    private void UpdateAbnormalBehaviorTimer(float dt)
    {
        // 이상 행동 지속 시간 소모
        if (activeAbnormalBehavior != null)
        {
            abnormalBehaviorRemainingTime -= dt;
            if (abnormalBehaviorRemainingTime <= 0f)
            {
                EndCurrentAbnormalBehavior();
            }
            return; // 이상 행동 진행 중에는 새 판정 없음
        }

        if (cachedStageDef == null || cachedStageDef.behaviorCheckInterval <= 0f) return;
        if (cachedStageDef.behaviorChance <= 0f) return;
        if (cachedStageDef.availableBehaviors == null || cachedStageDef.availableBehaviors.Count == 0) return;

        behaviorCheckTimer -= dt;
        if (behaviorCheckTimer > 0f) return;

        behaviorCheckTimer = cachedStageDef.behaviorCheckInterval;
        RollAbnormalBehavior();
    }

    /// <summary>
    /// 4단계 침식 전파 오라를 주변 직원에게 적용합니다.
    /// </summary>
    private void UpdatePropagationAura(float dt)
    {
        if (cachedStageDef == null || !cachedStageDef.hasErosionAura) return;
        if (EmployeeManager.instance == null) return;

        propagationTimer -= dt;
        if (propagationTimer > 0f) return;
        propagationTimer = PROPAGATION_CHECK_INTERVAL;

        float erosionThisTick = cachedStageDef.auraErosionPerSecond * PROPAGATION_CHECK_INTERVAL;
        Vector2 myPos = transform.position;

        foreach (var other in EmployeeManager.instance.AllEmployees)
        {
            if (other == null || other == employee) continue;
            if (other.State == EmployeeState.Dead) continue;

            float dist = Vector2.Distance(myPos, other.transform.position);
            if (dist > cachedStageDef.auraRange) continue;

            float armorIgnore = other.Equipment?.GetTotalErosionIgnore() ?? 0f;
            if (armorIgnore >= cachedStageDef.auraErosionPerSecond) continue;

            float newErosion = other.ErosionLevel + erosionThisTick;
            other.SetErosion(newErosion);
            other.ErosionController?.MarkAuraExposure();
        }
    }

    /// <summary>
    /// 침식 회복을 처리합니다.
    /// </summary>
    private void UpdateRecovery(float dt)
    {
        if (recoveryConfig == null) return;

        float erosion = statsController.ErosionLevel;
        if (erosion <= 0f) return;

        // 오라 외 시간 추적
        if (!auraExposureThisFrame)
        {
            timeSinceLastAuraExposure += dt;
        }
        else
        {
            timeSinceLastAuraExposure = 0f;
        }

        // 오라 범위 내이면 회복 없음
        if (auraExposureThisFrame) return;
        // 자연 회복 대기 시간 미충족
        if (timeSinceLastAuraExposure < recoveryConfig.outOfAuraDuration) return;

        float recovery = recoveryConfig.naturalRecoveryPerSecond;

        // 휴식 보너스
        if (employee.State == EmployeeState.Resting)
            recovery += recoveryConfig.restRecoveryPerSecond;

        // 포스트 레이드 보너스
        if (ErosionManager.instance != null && ErosionManager.instance.IsPostRaidRecovery)
            recovery += recoveryConfig.postRaidRecoveryPerSecond;

        float newErosion = Mathf.Max(0f, erosion - recovery * dt);
        statsController.SetErosion(newErosion);

        // 침식이 0으로 완전 회복되면 자연 침식 워터마크 초기화
        if (newErosion <= 0f && naturalErosionWatermark > 0f)
        {
            naturalErosionWatermark = 0f;
            Debug.Log($"[ErosionController] {employee.DisplayName}: 자연 침식 워터마크 초기화");
        }
    }

    #endregion

    #region 이상 행동

    /// <summary>
    /// 이상 행동 발동 여부를 확률 판정 후 실행합니다.
    /// </summary>
    private void RollAbnormalBehavior()
    {
        if (UnityEngine.Random.value > cachedStageDef.behaviorChance) return;

        var available = AbnormalBehaviorRegistry.FilterRegistered(cachedStageDef.availableBehaviors);
        if (available.Count == 0) return;

        var type = available[UnityEngine.Random.Range(0, available.Count)];
        var behavior = AbnormalBehaviorRegistry.Get(type);

        if (behavior == null || !behavior.CanExecute(employee)) return;

        ExecuteAbnormalBehavior(behavior);
    }

    private void ExecuteAbnormalBehavior(IAbnormalBehavior behavior)
    {
        activeAbnormalBehavior = behavior;
        float duration = behavior.Execute(employee);
        abnormalBehaviorRemainingTime = duration;

        // IgnoreCommand 계열 행동은 작업 배정을 차단
        if (behavior.BehaviorType == AbnormalBehaviorType.IgnoreCommand ||
            behavior.BehaviorType == AbnormalBehaviorType.IgnoreCommandEnhanced)
        {
            isBlockingWorkAssignment = true;
        }
    }

    private void EndCurrentAbnormalBehavior()
    {
        if (activeAbnormalBehavior == null) return;

        activeAbnormalBehavior.OnEnd(employee);
        activeAbnormalBehavior = null;
        abnormalBehaviorRemainingTime = 0f;
        isBlockingWorkAssignment = false;
    }

    #endregion

    #region 변이

    /// <summary>
    /// 완전 침식(200) 시 ErosionManager에 변이를 요청합니다.
    /// </summary>
    private void TriggerMutation()
    {
        hasMutated = true;

        if (ErosionManager.instance != null)
        {
            ErosionManager.instance.MutateEmployeeToXenops(employee);
        }
        else
        {
            Debug.LogWarning($"[ErosionController] {employee.DisplayName}: ErosionManager가 없어 변이를 처리할 수 없습니다.");
        }
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// HostileErosionAura 또는 4단계 전파 오라에서 호출합니다.
    /// 이 프레임에 오라 노출됨을 표시하고 회복 타이머를 리셋합니다.
    /// </summary>
    public void MarkAuraExposure()
    {
        auraExposureThisFrame = true;
        timeSinceLastAuraExposure = 0f;
    }

    /// <summary>
    /// 직원이 자연 침식 영향력이 있는 타일에 진입할 때 호출합니다.
    ///
    /// 워터마크 방식:
    ///   - 이전 최대치(naturalErosionWatermark)를 초과하는 경우에만 차이값을 침식에 추가
    ///   - 예) 워터마크=5 → intensity=10 진입: +5 적용, 워터마크=10으로 갱신
    ///   - 예) 이후 intensity=7 진입: 7 < 10이므로 무시
    ///   - 침식이 0으로 완전 회복되면 워터마크도 0으로 초기화됨 (UpdateRecovery에서 처리)
    /// </summary>
    /// <param name="tileIntensity">진입한 타일의 자연 침식 수치</param>
    public void ApplyNaturalErosion(float tileIntensity)
    {
        if (tileIntensity <= 0f) return;
        if (tileIntensity <= naturalErosionWatermark) return;

        float rawDelta = tileIntensity - naturalErosionWatermark;
        naturalErosionWatermark = tileIntensity;

        if (statsController != null)
        {
            // 받는 침식 피해 배율 적용 (특성·스킬, 0.8 = 침식 20% 덜 쌓임)
            float delta = rawDelta * statsController.CachedErosionDamageMult;
            float newErosion = statsController.ErosionLevel + delta;
            statsController.SetErosion(newErosion);
            Debug.Log($"[ErosionController] {employee.DisplayName}: 자연 침식 +{delta:F1} (수치={newErosion:F1}, 워터마크={naturalErosionWatermark:F1})");
        }
    }

    #endregion

    #region 내부 유틸

    private void RefreshStageCache(ErosionStage stage)
    {
        if (stageConfig == null)
        {
            cachedWorkSpeedModifier = 1f;
            cachedMoveSpeedModifier = 1f;
            cachedStageDef = null;
            return;
        }

        cachedStageDef = stageConfig.GetStageDefinition(stage);
        if (cachedStageDef != null)
        {
            cachedWorkSpeedModifier = cachedStageDef.workSpeedModifier;
            cachedMoveSpeedModifier = cachedStageDef.moveSpeedModifier;

            // 이상 행동 타이머 리셋
            behaviorCheckTimer = cachedStageDef.behaviorCheckInterval;
        }
        else
        {
            cachedWorkSpeedModifier = 1f;
            cachedMoveSpeedModifier = 1f;
        }
    }

    #endregion

    #region 저장/복원

    public void PopulateSaveData(EmployeeSaveData data)
    {
        data.erosionLevel = statsController != null ? statsController.ErosionLevel : 0f;
        data.timeSinceLastAuraExposure = timeSinceLastAuraExposure;
        data.activeAbnormalBehavior = activeAbnormalBehavior != null
            ? (int)activeAbnormalBehavior.BehaviorType
            : 0;
        data.abnormalBehaviorRemainingTime = abnormalBehaviorRemainingTime;
        data.naturalErosionWatermark = naturalErosionWatermark;
    }

    public void RestoreFromSaveData(EmployeeSaveData data)
    {
        timeSinceLastAuraExposure = data.timeSinceLastAuraExposure;
        naturalErosionWatermark   = data.naturalErosionWatermark;

        if (statsController != null)
            statsController.SetErosion(data.erosionLevel);

        // 이상 행동 복원
        var behaviorType = (AbnormalBehaviorType)data.activeAbnormalBehavior;
        if (behaviorType != AbnormalBehaviorType.None && data.abnormalBehaviorRemainingTime > 0f)
        {
            var behavior = AbnormalBehaviorRegistry.Get(behaviorType);
            if (behavior != null)
            {
                activeAbnormalBehavior = behavior;
                abnormalBehaviorRemainingTime = data.abnormalBehaviorRemainingTime;
            }
        }

        // 단계 즉시 갱신
        if (stageConfig != null && statsController != null)
        {
            var def = stageConfig.GetStageDefinition(statsController.ErosionLevel);
            if (def != null)
            {
                currentStage = def.stage;
                RefreshStageCache(currentStage);
            }
        }
    }

    #endregion
}
