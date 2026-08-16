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
///   - 4단계(Critical) 침식 전파 오라
///   - 자연/휴식/포스트레이드 회복 처리
///   - 완전 침식(200) 시 ErosionManager.MutateEmployeeToXenops() 호출
///
/// <b>이상 행동은 더 이상 여기서 굴리지 않습니다 (2026-07-29 개편).</b>
///   정신 이상 발생 판정은 EmployeeMental 한 곳으로 통합됐고, 침식 수치는
///   "발생한 정신 이상이 침식 계열일 확률"만 높입니다. 침식 단계는 속도 디버프·
///   전파 오라·변이에만 관여하며, 단계 판정도 실제 침식 수치를 그대로 씁니다.
///   (재미·피로 계수는 EmployeeMental의 정신 이상 임계점 쪽으로 옮겨졌습니다.)
///
/// 오라 노출 추적:
///   HostileErosionAura가 침식을 적용할 때 MarkAuraExposure()를 호출합니다.
///   이 플래그를 통해 "오라 밖에서 20초 경과" 회복 조건을 관리합니다.
/// </summary>
[RequireComponent(typeof(Employee))]
public class EmployeeErosionController : MonoBehaviour
{
    #region 상수

    private const float FULL_EROSION_THRESHOLD = 200f;

    #endregion

    #region 설정

    private ErosionStageConfig stageConfig;
    private ErosionRecoveryConfig recoveryConfig;

    #endregion

    #region 상태

    /// <summary>현재 침식 단계</summary>
    [SerializeField] private ErosionStage currentStage = ErosionStage.Normal;

    /// <summary>마지막 오라 노출 후 경과 시간 (초)</summary>
    [SerializeField] private float timeSinceLastAuraExposure;

    /// <summary>현재 프레임에 오라 노출 여부 (HostileErosionAura에서 설정)</summary>
    private bool auraExposureThisFrame;

    /// <summary>출처별 침식 누적 내역 (UI 표시용)</summary>
    [SerializeField] private List<ErosionSourceEntry> erosionSources = new List<ErosionSourceEntry>();

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

    /// <summary>출처별 침식 누적 내역 (읽기 전용 — UI 표시용)</summary>
    public IReadOnlyList<ErosionSourceEntry> ErosionSources => erosionSources;

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

        // 단계는 실제 침식 수치를 그대로 쓴다. 단계가 관여하는 것은 속도 디버프·전파 오라·변이뿐이며,
        // 이상행동(정신 이상) 발생은 EmployeeMental이 정신 수치로 판정한다.
        // 저항 배율(특성·재미·피로)은 그쪽 임계점 보정으로 옮겨졌다.
        var def = stageConfig.GetStageDefinition(erosion);
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
    /// 4단계 침식 전파 오라를 주변 직원에게 적용합니다.
    /// </summary>
    private void UpdatePropagationAura(float dt)
    {
        if (cachedStageDef == null || !cachedStageDef.hasErosionAura) return;
        if (EmployeeManager.instance == null) return;

        // 판정 주기는 Config에서 조절한다. 초당 전파량 × 주기로 한 번에 적용하므로
        // 주기를 늘려도 시간당 총 전파량은 같고 판정 빈도만 줄어든다.
        float interval = recoveryConfig != null ? recoveryConfig.propagationCheckInterval : 5f;

        propagationTimer -= dt;
        if (propagationTimer > 0f) return;
        propagationTimer = interval;

        float erosionThisTick = cachedStageDef.auraErosionPerSecond * interval;
        Vector2 myPos = transform.position;
        string sourceKey = ErosionSource.PropagationKey(employee.InstanceId);
        string sourceName = $"{employee.DisplayName} 전파침식";

        foreach (var other in EmployeeManager.instance.AllEmployees)
        {
            if (other == null || other == employee) continue;
            if (other.State == EmployeeState.Dead) continue;

            float dist = Vector2.Distance(myPos, other.transform.position);
            if (dist > cachedStageDef.auraRange) continue;

            float armorIgnore = other.Equipment?.GetTotalErosionIgnore() ?? 0f;
            if (armorIgnore >= cachedStageDef.auraErosionPerSecond) continue;

            other.ErosionController?.AddErosion(erosionThisTick, sourceKey, sourceName);
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

        // 자연 회복은 하한까지만. 그 아래로 지우려면 세척 시설이 필요하다.
        float floor = ErosionManager.instance != null
            ? ErosionManager.instance.EffectiveRecoveryFloor
            : recoveryConfig.naturalRecoveryFloor;

        if (erosion <= floor) return;

        float newErosion = Mathf.Max(floor, erosion - recoveryConfig.naturalRecoveryPerSecond * dt);
        ReduceErosion(erosion - newErosion, "자연 회복");

        // 침식이 0으로 완전 회복되면 자연 침식 워터마크 초기화
        if (newErosion <= 0f && naturalErosionWatermark > 0f)
        {
            naturalErosionWatermark = 0f;
            Debug.Log($"[ErosionController] {employee.DisplayName}: 자연 침식 워터마크 초기화");
        }
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
    /// 침식을 추가하는 <b>단일 진입점</b>입니다. 출처를 함께 기록해 내역을 남깁니다.
    /// 받는 침식 배율(특성·스킬 erosionDamageMult)은 여기서 일괄 적용됩니다.
    /// </summary>
    /// <param name="rawAmount">배율 적용 전 침식량 (양수)</param>
    /// <param name="sourceKey">출처 키 (ErosionSource 상수 사용)</param>
    /// <param name="displayName">UI 표시용 출처 이름</param>
    public void AddErosion(float rawAmount, string sourceKey, string displayName)
    {
        if (rawAmount <= 0f || statsController == null) return;

        float amount = rawAmount * statsController.CachedErosionDamageMult;
        if (amount <= 0f) return;

        statsController.SetErosion(statsController.ErosionLevel + amount);
        RecordSource(sourceKey, displayName, amount);
    }

    /// <summary>
    /// 침식을 줄이고 내역에서도 비례 차감합니다 (자연 회복·세척·정화 약품).
    /// 총량과 내역 합계가 어긋나지 않도록 모든 출처에서 같은 비율로 뺍니다.
    /// </summary>
    /// <param name="amount">줄일 침식량 (양수)</param>
    /// <param name="reason">로그용 사유</param>
    public void ReduceErosion(float amount, string reason)
    {
        if (amount <= 0f || statsController == null) return;

        float before = statsController.ErosionLevel;
        float after = Mathf.Max(0f, before - amount);
        statsController.SetErosion(after);

        ScaleSources(before > 0f ? after / before : 0f);
    }

    /// <summary>
    /// 특정 출처가 기여한 침식만 되돌립니다.
    /// 오염 구체처럼 "범위를 벗어나면 자기가 준 침식을 거둬가는" 효과에 사용합니다.
    /// 다른 출처에서 얻은 침식은 건드리지 않습니다.
    /// </summary>
    public void RemoveErosionBySource(string sourceKey)
    {
        if (string.IsNullOrEmpty(sourceKey) || statsController == null) return;

        for (int i = 0; i < erosionSources.Count; i++)
        {
            if (erosionSources[i].sourceKey != sourceKey) continue;

            float amount = erosionSources[i].amount;
            erosionSources.RemoveAt(i);
            statsController.SetErosion(Mathf.Max(0f, statsController.ErosionLevel - amount));
            return;
        }
    }

    /// <summary>
    /// 침식을 즉시 0으로 만들고 내역을 비웁니다 (세척 시설).
    /// </summary>
    public void ClearErosion()
    {
        if (statsController == null) return;

        statsController.SetErosion(0f);
        erosionSources.Clear();
        naturalErosionWatermark = 0f;
    }

    /// <summary>출처별 내역에 누적합니다.</summary>
    private void RecordSource(string sourceKey, string displayName, float amount)
    {
        if (string.IsNullOrEmpty(sourceKey)) sourceKey = ErosionSource.UNKNOWN;

        for (int i = 0; i < erosionSources.Count; i++)
        {
            if (erosionSources[i].sourceKey != sourceKey) continue;

            erosionSources[i].amount += amount;
            if (!string.IsNullOrEmpty(displayName)) erosionSources[i].displayName = displayName;
            return;
        }

        erosionSources.Add(new ErosionSourceEntry
        {
            sourceKey = sourceKey,
            displayName = string.IsNullOrEmpty(displayName) ? "알 수 없음" : displayName,
            amount = amount
        });
    }

    /// <summary>회복 시 모든 출처를 같은 비율로 줄입니다.</summary>
    private void ScaleSources(float ratio)
    {
        ratio = Mathf.Clamp01(ratio);

        for (int i = erosionSources.Count - 1; i >= 0; i--)
        {
            erosionSources[i].amount *= ratio;
            if (erosionSources[i].amount < 0.05f) erosionSources.RemoveAt(i);
        }
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

        AddErosion(rawDelta, ErosionSource.NATURAL, "자연 침식");

        if (statsController != null)
            Debug.Log($"[ErosionController] {employee.DisplayName}: 자연 침식 (수치={statsController.ErosionLevel:F1}, 워터마크={naturalErosionWatermark:F1})");
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
        data.naturalErosionWatermark = naturalErosionWatermark;

        data.erosionSources = new List<ErosionSourceEntry>();
        foreach (var s in erosionSources)
        {
            data.erosionSources.Add(new ErosionSourceEntry
            {
                sourceKey = s.sourceKey,
                displayName = s.displayName,
                amount = s.amount
            });
        }

        // 이상 행동(침식 계열 정신 이상)은 v8부터 EmployeeMental이 activeMentalEvents에 저장합니다.
    }

    public void RestoreFromSaveData(EmployeeSaveData data)
    {
        timeSinceLastAuraExposure = data.timeSinceLastAuraExposure;
        naturalErosionWatermark   = data.naturalErosionWatermark;

        if (statsController != null)
            statsController.SetErosion(data.erosionLevel);

        erosionSources.Clear();
        if (data.erosionSources != null)
        {
            foreach (var s in data.erosionSources)
            {
                if (s == null || string.IsNullOrEmpty(s.sourceKey)) continue;
                erosionSources.Add(new ErosionSourceEntry
                {
                    sourceKey = s.sourceKey,
                    displayName = s.displayName,
                    amount = s.amount
                });
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
