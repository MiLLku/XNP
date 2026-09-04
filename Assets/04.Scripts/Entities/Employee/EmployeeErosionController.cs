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
public class EmployeeErosionController : MonoBehaviour, IEntityErosionSource
{
    #region 상수

    private const float FULL_EROSION_THRESHOLD = 200f;

    /// <summary>침식 유지 수치의 하한.</summary>
    public const float MIN_MAINTAIN_TARGET = 0f;

    /// <summary>침식 유지 수치의 상한. Critical(150) 이상은 전파 오라를 켜므로 허용하지 않는다.</summary>
    public const float MAX_MAINTAIN_TARGET = 145f;

    /// <summary>침식 유지 수치 기본값 (0 = 세척 시 완전 제거).</summary>
    public const float DEFAULT_MAINTAIN_TARGET = 0f;

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
    /// 플레이어가 직원마다 설정하는 '이 정도 침식은 감수한다' 수치.
    /// 이 값을 넘으면 세척 시간대에 세척 시설을 찾아가고, 세척은 이 값까지만 진행한다.
    /// </summary>
    [SerializeField] private float erosionMaintainTarget = DEFAULT_MAINTAIN_TARGET;

    /// <summary>
    /// 자연 침식 최고 노출 수치 워터마크.
    /// 이 값 이하의 자연 침식 수치는 무시됩니다 (증분만 적용).
    /// ErosionLevel이 0으로 완전 회복되면 0으로 초기화됩니다.
    /// </summary>
    /// <summary>환경 노출 판정 누적 시간</summary>
    private float exposureTimer;

    /// <summary>지금 침식을 걸어둔 개체 발원지 키 (범위를 벗어나면 돌려준다)</summary>
    private readonly HashSet<string> appliedEntitySourceKeys = new HashSet<string>();

    /// <summary>이번 판정에서 범위 안이었던 키 (작업 버퍼)</summary>
    private readonly HashSet<string> activeEntitySourceKeys = new HashSet<string>();

    /// <summary>이번 판정에서 벗어난 키 (작업 버퍼)</summary>
    private readonly List<string> expiredEntitySourceKeys = new List<string>();

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

    /// <summary>
    /// 침식 유지 수치. 세척 트리거이자 세척 목표치입니다.
    ///
    /// 주의: 자연 회복 하한(ErosionManager.EffectiveRecoveryFloor, 기본 50) 이상으로 설정하면
    /// 자연 회복만으로 조건이 충족되어 직원이 세척하러 가지 않습니다.
    /// </summary>
    public float ErosionMaintainTarget
    {
        get => erosionMaintainTarget;
        set => erosionMaintainTarget = Mathf.Clamp(value, MIN_MAINTAIN_TARGET, MAX_MAINTAIN_TARGET);
    }

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
        exposureTimer = 0f;

        RefreshStageCache(ErosionStage.Normal);
    }

    #endregion

    #region 업데이트

    private void OnEnable()
    {
        // IsEmitting이 단계를 보고 판단하므로 항상 등록해 두어도 안전하다
        EntityErosionField.instance?.RegisterSource(this);
    }

    private void OnDisable()
    {
        EntityErosionField.instance?.UnregisterSource(this);
    }

    private void Update()
    {
        if (employee == null || employee.State == EmployeeState.Dead || hasMutated) return;
        if (statsController == null || stageConfig == null) return;

        float dt = Time.deltaTime;

        UpdateStage();
        UpdateEnvironmentExposure(dt);
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

    #region 전파 오라 (IEntityErosionSource)

    /// <summary>
    /// 4단계 침식 직원은 <b>자기 주변 타일을 오염시킵니다</b>.
    ///
    /// 예전에는 주변 직원을 직접 찾아 침식을 꽂았지만, 지금은 제놉스 오라와 같은 방식으로
    /// 타일 레이어에 깔아두기만 하고 그 위에 선 직원이 스스로 받습니다.
    /// 오염이 사람이 아니라 <b>장소</b>에 생기므로 "저 통로는 지나가면 안 된다"가 성립합니다.
    ///
    /// 방 침식에는 기여하지 않습니다 — 개체가 떠나면 사라지는 오염입니다.
    /// </summary>
    public Vector2 EmitPosition => transform.position;

    public float EmitRadius => cachedStageDef != null ? cachedStageDef.auraRange : 0f;

    /// <summary>범위 안 동료에게 붙는 고정량. 벗어나면 돌아갑니다.</summary>
    public float FixedErosionAmount => cachedStageDef != null ? cachedStageDef.auraErosionPerSecond : 0f;

    public bool HorizontalOnly => false;

    public bool Covers(Vector2 worldPosition)
        => cachedStageDef != null
        && Vector2.Distance(EmitPosition, worldPosition) <= cachedStageDef.auraRange;

    public bool IsEmitting
        => isActiveAndEnabled
        && employee != null && employee.State != EmployeeState.Dead
        && cachedStageDef != null && cachedStageDef.hasErosionAura
        && cachedStageDef.auraErosionPerSecond > 0f
        && cachedStageDef.auraRange > 0f;

    public string ErosionSourceKey => ErosionSource.PropagationKey(employee != null ? employee.InstanceId : GetEntityId().GetHashCode());

    public string ErosionSourceName => $"{(employee != null ? employee.DisplayName : "직원")} 전파침식";

    #endregion

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

        // 디버그: 침식 축적 차단 (회복 경로는 그대로 둔다)
        if (DebugManager.IsBlocked(DebugFlag.ErosionGain)) return;

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
        exposureTimer = 0f;
    }

    /// <summary>
    /// 침식을 지정한 하한까지만 씻어냅니다 (직원별 침식 유지 수치).
    /// 완전 세척이 아니므로 내역은 비우지 않고 비례 축소하며, 노출 타이머도 유지합니다.
    /// </summary>
    public void ClearErosionTo(float floor)
    {
        if (statsController == null) return;

        floor = Mathf.Clamp(floor, 0f, FULL_EROSION_THRESHOLD);
        if (floor <= 0f) { ClearErosion(); return; }

        float before = statsController.ErosionLevel;
        if (before <= floor) return;

        statsController.SetErosion(floor);
        ScaleSources(floor / before);
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
    /// 환경 침식을 갱신합니다. 두 갈래가 성격이 다릅니다.
    ///
    /// <b>지형 발원지 → 방 침식(농도)</b>: 머무는 동안 <b>계속 누적</b>됩니다.
    ///   침식 = 방 침식 × 노출계수 × 시간. 오래 있을수록 위험해집니다.
    ///
    /// <b>개체 발원지</b>: 범위에 들어오면 <b>고정량이 붙고</b>, 벗어나면 <b>그대로 돌아갑니다</b>.
    ///   시간이 지나도 늘지 않으므로 "지나가면 안 되는 구역"으로 동작합니다.
    /// </summary>
    private void UpdateEnvironmentExposure(float dt)
    {
        if (recoveryConfig == null) return;

        float interval = Mathf.Max(0.1f, recoveryConfig.exposureCheckInterval);

        exposureTimer += dt;
        if (exposureTimer < interval) return;

        float elapsed = exposureTimer;
        exposureTimer = 0f;

        UpdateEntitySourceErosion();
        UpdateAmbientErosion(elapsed);
    }

    /// <summary>
    /// 개체 발원지의 고정 침식을 붙이거나 돌려줍니다.
    /// 범위 안이면 그 발원지의 고정량이 정확히 걸려 있고, 밖이면 0이 됩니다.
    /// </summary>
    private void UpdateEntitySourceErosion()
    {
        var field = EntityErosionField.instance;
        if (field == null) return;

        Vector2 myPos = transform.position;
        bool coveredByAny = false;

        // 이번 판정에서 마주친 발원지 키를 모아, 예전에 걸려 있었지만 이제 벗어난 것을 걷어낸다
        activeEntitySourceKeys.Clear();

        foreach (var source in field.Sources)
        {
            if (source == null || !source.IsEmitting) continue;
            if (!source.Covers(myPos)) continue;

            // 장비·특성의 침식 무시가 고정량 이상이면 완전히 막는다 (기존 오라 규칙과 동일)
            float ignore = (employee.Equipment?.GetTotalErosionIgnore() ?? 0f)
                         + (statsController != null ? statsController.CachedErosionIgnoreBonus : 0f);
            if (ignore >= source.FixedErosionAmount) continue;

            coveredByAny = true;
            activeEntitySourceKeys.Add(source.ErosionSourceKey);

            SetConditionalErosion(source.ErosionSourceKey, source.ErosionSourceName, source.FixedErosionAmount);
        }

        // 범위를 벗어난 개체 발원지의 침식을 돌려준다
        if (appliedEntitySourceKeys.Count > 0)
        {
            expiredEntitySourceKeys.Clear();
            foreach (string key in appliedEntitySourceKeys)
                if (!activeEntitySourceKeys.Contains(key)) expiredEntitySourceKeys.Add(key);

            foreach (string key in expiredEntitySourceKeys)
            {
                RemoveErosionBySource(key);
                appliedEntitySourceKeys.Remove(key);
            }
        }

        if (coveredByAny) MarkAuraExposure();
    }

    /// <summary>
    /// 개체 발원지 하나의 침식이 정확히 <paramref name="amount"/>만큼 걸려 있게 맞춥니다.
    /// 이미 그만큼 걸려 있으면 아무것도 하지 않습니다 — 시간이 지나도 늘지 않는 이유입니다.
    /// </summary>
    private void SetConditionalErosion(string sourceKey, string displayName, float amount)
    {
        float current = GetErosionFromSource(sourceKey);
        if (current >= amount)
        {
            appliedEntitySourceKeys.Add(sourceKey);
            return;
        }

        AddErosion(amount - current, sourceKey, displayName);
        appliedEntitySourceKeys.Add(sourceKey);
    }

    /// <summary>특정 출처가 지금까지 기여한 침식량</summary>
    private float GetErosionFromSource(string sourceKey)
    {
        foreach (var entry in erosionSources)
            if (entry.sourceKey == sourceKey) return entry.amount;
        return 0f;
    }

    /// <summary>
    /// 방 침식(농도)에 비례한 누적 노출. 이쪽은 머무는 동안 계속 오릅니다.
    /// </summary>
    private void UpdateAmbientErosion(float elapsed)
    {
        Vector2Int cell = new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y));

        float ambient = TerrainErosionManager.instance != null
            ? TerrainErosionManager.instance.GetRoomErosionAt(cell)
            : 0f;

        if (ambient <= 0f) return;

        float ignore = employee.Equipment != null ? employee.Equipment.GetTotalErosionIgnore() : 0f;
        float perSecond = ambient * recoveryConfig.exposurePerErosionPoint - ignore;
        if (perSecond <= 0f) return;

        MarkAuraExposure();
        AddErosion(perSecond * elapsed, ErosionSource.NATURAL, "환경 침식");
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
        data.erosionMaintainTarget = erosionMaintainTarget;

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
        ErosionMaintainTarget = data.erosionMaintainTarget;   // 프로퍼티 경유 — 범위를 벗어난 값 보정

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
