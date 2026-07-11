using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 직원 엔티티 코디네이터.
/// 서브 컴포넌트(StatsController, Work, Mental, Growth, Movement, AI)를 조율하며,
/// 외부 시스템에 대한 파사드 역할을 합니다.
///
/// 서브 컴포넌트:
///   - EmployeeStatsController: 스탯/욕구 관리, 특성 보정
///   - EmployeeWork: 작업 할당/실행/완료, 비자격 리스트
///   - EmployeeMental: 정신 이벤트 시스템
///   - EmployeeGrowth: 경험치/레벨업 (유니크 전용)
///   - EmployeeMovement: 이동/낙하
///   - EmployeeAI: 긴급 욕구 자율 행동
///
/// 상태 흐름:
///   Idle → Moving → Working → Idle
///           ↓                    ↓
///        Eating/Resting     MentalBreak/Dead
/// </summary>
[RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(Collider2D))]
public class Employee : MonoBehaviour
{
    #region 상수

    /// <summary>디버그 로그 출력 시간 간격 (초)</summary>
    private const float DEBUG_LOG_INTERVAL_SECONDS = 10f;

    #endregion

    #region 필드

    [Header("직원 정보")]
    [SerializeField] private EmployeeData employeeData;
    [SerializeField] private int instanceId;
    [SerializeField] private bool isUnique;
    [SerializeField] private string customName;

    [Header("상태")]
    [SerializeField] private EmployeeState currentState = EmployeeState.Idle;

    [Header("외형")]
    [Tooltip("헤어 스프라이트를 렌더링할 자식 SpriteRenderer. 없으면 Awake에서 자동 생성합니다.")]
    [SerializeField] private SpriteRenderer hairRenderer;

    [Header("디버그")]
    [SerializeField] private bool showDebugInfo = false;

    /// <summary>초기화 완료 여부 (로드 시 중복 초기화 방지)</summary>
    private bool _isInitialized = false;

    /// <summary>디버그 로그 타이머</summary>
    private float _debugLogTimer = 0f;

    // 서브 컴포넌트 참조
    private EmployeeStatsController statsController;
    private EmployeeWork work;
    private EmployeeMental mental;
    private EmployeeGrowth growth;
    private EmployeeMovement movement;
    private EmployeeAI aiController;
    private EmployeeEquipment equipment;
    private EmployeeErosionController erosionController;
    private EmployeeSchedule schedule;
    private EmployeeDraft draft;
    private EmployeeCombat combat;
    private EmployeeZoneAssignment zoneAssignment;
    private SpriteRenderer spriteRenderer;

    #endregion

    #region 이벤트

    public delegate void StateChangedDelegate(EmployeeState state);
    public event StateChangedDelegate OnStateChanged;

    /// <summary>스탯 변경 이벤트 (StatsController 패스스루)</summary>
    public event EmployeeStatsController.StatsChangedDelegate OnStatsChanged
    {
        add { if (statsController != null) statsController.OnStatsChanged += value; }
        remove { if (statsController != null) statsController.OnStatsChanged -= value; }
    }

    /// <summary>욕구 변경 이벤트 (StatsController 패스스루)</summary>
    public event EmployeeStatsController.NeedsChangedDelegate OnNeedsChanged
    {
        add { if (statsController != null) statsController.OnNeedsChanged += value; }
        remove { if (statsController != null) statsController.OnNeedsChanged -= value; }
    }

    /// <summary>레벨업 이벤트 (Growth 패스스루)</summary>
    public event EmployeeGrowth.LevelUpDelegate OnLevelUp
    {
        add { if (growth != null) growth.OnLevelUp += value; }
        remove { if (growth != null) growth.OnLevelUp -= value; }
    }

    #endregion

    #region 프로퍼티 — 식별

    /// <summary>런타임 고유 ID</summary>
    public int InstanceId => instanceId;

    /// <summary>유니크 직원 여부</summary>
    public bool IsUnique => isUnique;

    /// <summary>표시 이름 (커스텀 이름 우선)</summary>
    public string DisplayName => string.IsNullOrEmpty(customName) ? employeeData?.employeeName : customName;

    /// <summary>직원 데이터 (템플릿)</summary>
    public EmployeeData Data => employeeData;

    #endregion

    #region 프로퍼티 — 스탯/욕구 (StatsController 위임)

    /// <summary>현재 스탯</summary>
    public EmployeeStats Stats => statsController != null ? statsController.Stats : default;

    /// <summary>현재 욕구</summary>
    public EmployeeNeeds Needs => statsController != null ? statsController.Needs : default;

    /// <summary>스탯 컨트롤러 직접 접근 (침식 무시 보너스 등 캐시 읽기용)</summary>
    public EmployeeStatsController StatsController => statsController;

    #endregion

    #region 프로퍼티 — 작업 (Work 위임)

    /// <summary>현재 수행 중인 작업 타입</summary>
    public WorkType CurrentWork => work != null ? work.CurrentWork : WorkType.None;

    /// <summary>현재 작업 진행도 (0~1)</summary>
    public float WorkProgress => work != null ? work.WorkProgress : 0f;

    /// <summary>작업 할당 가능 여부 (Idle + 소집 아님)</summary>
    public bool IsAvailableForWork => currentState == EmployeeState.Idle && !IsDrafted;

    /// <summary>작업 능력 (런타임 값 우선)</summary>
    public WorkAbilities Abilities => work != null ? work.Abilities : employeeData?.abilities;

    #endregion

    #region 프로퍼티 — 성장 (Growth 위임)

    /// <summary>현재 레벨</summary>
    public int Level => growth != null ? growth.Level : 1;

    /// <summary>현재 경험치</summary>
    public int Experience => growth != null ? growth.Experience : 0;

    /// <summary>다음 레벨까지 필요한 경험치</summary>
    public int ExperienceToNextLevel => growth != null ? growth.ExperienceToNextLevel : 100;

    #endregion

    #region 프로퍼티 — 상태

    /// <summary>현재 상태</summary>
    public EmployeeState State => currentState;

    #endregion

    #region 초기화 및 생명주기

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        // Hair 자식 SpriteRenderer 자동 탐색/생성
        if (hairRenderer == null)
        {
            Transform hairTf = transform.Find("Hair");
            if (hairTf != null)
            {
                hairRenderer = hairTf.GetComponent<SpriteRenderer>();
            }
            else
            {
                GameObject hairGo = new GameObject("Hair");
                hairGo.transform.SetParent(transform, false);
                hairGo.transform.localPosition = Vector3.zero;
                hairGo.transform.localScale = Vector3.one;
                hairRenderer = hairGo.AddComponent<SpriteRenderer>();
                // 본체보다 한 레이어 앞에 렌더링
                hairRenderer.sortingLayerName = spriteRenderer != null ? spriteRenderer.sortingLayerName : "Default";
                hairRenderer.sortingOrder     = spriteRenderer != null ? spriteRenderer.sortingOrder + 1 : 1;
            }
        }
        hairRenderer.enabled = false; // 스프라이트 풀이 채워지기 전까지 비활성

        // 서브 컴포넌트 참조 (없으면 추가)
        statsController = GetComponent<EmployeeStatsController>() ?? gameObject.AddComponent<EmployeeStatsController>();
        work = GetComponent<EmployeeWork>() ?? gameObject.AddComponent<EmployeeWork>();
        mental = GetComponent<EmployeeMental>() ?? gameObject.AddComponent<EmployeeMental>();
        growth = GetComponent<EmployeeGrowth>() ?? gameObject.AddComponent<EmployeeGrowth>();
        movement = GetComponent<EmployeeMovement>() ?? gameObject.AddComponent<EmployeeMovement>();
        aiController = GetComponent<EmployeeAI>() ?? gameObject.AddComponent<EmployeeAI>();
        equipment = GetComponent<EmployeeEquipment>() ?? gameObject.AddComponent<EmployeeEquipment>();
        erosionController = GetComponent<EmployeeErosionController>() ?? gameObject.AddComponent<EmployeeErosionController>();
        schedule = GetComponent<EmployeeSchedule>() ?? gameObject.AddComponent<EmployeeSchedule>();
        draft = GetComponent<EmployeeDraft>() ?? gameObject.AddComponent<EmployeeDraft>();
        combat = GetComponent<EmployeeCombat>() ?? gameObject.AddComponent<EmployeeCombat>();
        zoneAssignment = GetComponent<EmployeeZoneAssignment>() ?? gameObject.AddComponent<EmployeeZoneAssignment>();
    }

    void Start()
    {
        if (!_isInitialized && employeeData != null)
        {
            Initialize(employeeData);
        }

        if (WorkSystemManager.instance != null)
        {
            WorkSystemManager.instance.RegisterEmployee(this);
        }
    }

    void OnDestroy()
    {
        if (instanceId > 0 && RuntimeIDRegistry.instance != null)
        {
            RuntimeIDRegistry.instance.Unregister(instanceId);
        }
        if (WorkSystemManager.instance != null)
        {
            WorkSystemManager.instance.UnregisterEmployee(this);
        }
    }

    void Update()
    {
        if (currentState == EmployeeState.Dead) return;

        if (showDebugInfo)
        {
            _debugLogTimer -= Time.deltaTime;
            if (_debugLogTimer <= 0f)
            {
                _debugLogTimer = DEBUG_LOG_INTERVAL_SECONDS;
                ShowDebugStatus();
            }
        }
    }

    /// <summary>
    /// 새 직원 초기화 (처음 고용 시)
    /// </summary>
    public void Initialize(EmployeeData data, int newInstanceId)
    {
        _isInitialized = true;

        employeeData = data;
        instanceId = newInstanceId;
        isUnique = data.isUnique;
        customName = null;

        name = $"Employee_{data.employeeName}_{instanceId}";

        // 서브 컴포넌트 초기화
        statsController.Initialize(data);
        work.Initialize(data);
        growth.Initialize(isUnique);
        erosionController.Initialize(
            ErosionManager.instance?.StageConfig,
            ErosionManager.instance?.RecoveryConfig);

        // 외형 적용
        ApplyAppearance(data.appearance);

        // RuntimeIDRegistry 등록
        if (instanceId > 0 && RuntimeIDRegistry.instance != null)
        {
            RuntimeIDRegistry.instance.Register(instanceId, this);
        }
    }

    /// <summary>
    /// 기존 Initialize 호환 (instanceId 없이 호출 시).
    /// </summary>
    public void Initialize(EmployeeData data)
    {
        int newId = SaveManager.instance != null
            ? SaveManager.instance.GenerateInstanceId()
            : UnityEngine.Random.Range(1000, 9999);
        Initialize(data, newId);
    }

    #endregion

    #region 외형

    /// <summary>
    /// EmployeeAppearance 데이터를 기반으로 Hair 렌더러에 스프라이트/색상을 적용합니다.
    /// hairSprite가 null이면 렌더러를 비활성 상태로 유지합니다.
    /// </summary>
    private void ApplyAppearance(EmployeeAppearance appearance)
    {
        if (hairRenderer == null) return;

        if (appearance != null && appearance.hairSprite != null)
        {
            hairRenderer.sprite  = appearance.hairSprite;
            hairRenderer.color   = appearance.hairColor;
            hairRenderer.enabled = true;
        }
        else
        {
            hairRenderer.enabled = false;
        }
    }

    #endregion

    #region 상태 관리

    /// <summary>
    /// 직원 상태를 변경합니다.
    /// StatsController, Mental 등 서브 컴포넌트에서도 호출 가능합니다.
    /// </summary>
    public void SetState(EmployeeState newState)
    {
        if (currentState == newState) return;

        currentState = newState;
        OnStateChanged?.Invoke(newState);
        UpdateVisualState();
    }

    private void UpdateVisualState()
    {
        Color color = currentState switch
        {
            EmployeeState.Working => Color.yellow,
            EmployeeState.Moving => Color.cyan,
            EmployeeState.Resting => new Color(0.5f, 0.5f, 1f),
            EmployeeState.Eating => new Color(0.5f, 1f, 0.5f),
            EmployeeState.Drafted => new Color(1f, 0.5f, 0f),
            EmployeeState.MentalBreak => Color.magenta,
            EmployeeState.Dead => Color.gray,
            _ => Color.white
        };

        if (spriteRenderer != null)
        {
            spriteRenderer.color = color;
        }
    }

    #endregion

    #region 파사드 — 작업 (Work 위임)

    /// <summary>작업 할당 (WorkSystemManager에서 호출)</summary>
    public void AssignWork(WorkOrder workOrder, IWorkTarget target)
        => work?.AssignWork(workOrder, target);

    /// <summary>제작 작업 할당 (ProductionBuilding에서 호출)</summary>
    public void AssignCraftingWork(CraftingOrder craftingOrder, Vector3 workPosition)
        => work?.AssignCraftingWork(craftingOrder, workPosition);

    /// <summary>연구 작업 할당 (ResearchWorkbench에서 호출)</summary>
    public void AssignResearchWork(ResearchWorkbench bench, Vector3 workPosition)
        => work?.AssignResearchWork(bench, workPosition);

    /// <summary>현재 작업 취소</summary>
    public void CancelWork()
        => work?.CancelWork();

    /// <summary>작업 수행 가능 여부</summary>
    public bool CanPerformWork(WorkType type)
        => work != null && work.CanPerformWork(type);

    /// <summary>작업 속도 반환</summary>
    public float GetWorkSpeed(WorkType type)
        => work != null ? work.GetWorkSpeed(type) : 0f;

    /// <summary>작업 우선순위 설정</summary>
    public void SetWorkPriority(WorkType type, int priority, bool enabled)
        => work?.SetWorkPriority(type, priority, enabled);

    /// <summary>활성화된 작업 타입 목록 (우선순위순)</summary>
    public List<WorkType> GetEnabledWorkTypes()
        => work != null ? work.GetEnabledWorkTypes() : new List<WorkType>();

    /// <summary>작업 우선순위 반환</summary>
    public int GetWorkPriority(WorkType type)
        => work != null ? work.GetWorkPriority(type) : 999;

    /// <summary>작업 범위 반환</summary>
    public List<Vector3Int> GetWorkableRange()
        => work != null ? work.GetWorkableRange() : new List<Vector3Int>();

    /// <summary>발 위치 타일 좌표</summary>
    public Vector3Int GetFootTile()
        => work != null ? work.GetFootTile() : new Vector3Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y), 0);

    #endregion

    #region 파사드 — 스탯/욕구 (StatsController 위임)

    /// <summary>식사하여 허기 회복</summary>
    public void Eat(float nutritionValue) => statsController?.Eat(nutritionValue);

    /// <summary>재미 수정 (오락 시설·약물·이벤트용 위임)</summary>
    public void ModifyFun(float amount) => statsController?.ModifyFun(amount);

    /// <summary>체력 수정</summary>
    public void ModifyHealth(float amount) => statsController?.ModifyHealth(amount);

    /// <summary>
    /// 외부(적·함정·지속피해)로부터 피해를 받습니다. 받는 피해 배율(특성·스킬)이 적용됩니다.
    /// 회복·이벤트 등 배율을 적용하면 안 되는 체력 변화는 ModifyHealth를 직접 사용하세요.
    /// </summary>
    /// <param name="amount">감산할 피해량(양수). 0 이하는 무시.</param>
    public void TakeDamage(float amount)
    {
        if (amount <= 0f) return;

        // 장비 피해 감소(%) + 방어구 내구도 소모
        if (equipment != null)
        {
            float reduction = equipment.GetTotalDamageReduction();
            // 방어 태세: 방어형 장비 감쇄 증폭 (최대 90% 클램프)
            if (combat != null && combat.IsDefending)
            {
                var cfg = EmployeeManager.instance?.CombatConfig;
                reduction = Mathf.Min(90f, reduction * (cfg != null ? cfg.defendReductionMultiplier : 1.5f));
            }
            if (reduction > 0f) amount *= 1f - reduction / 100f;
            equipment.NotifyDamageTaken();
        }

        float mult = statsController != null ? statsController.CachedDamageTakenMult : 1f;
        statsController?.ModifyHealth(-amount * mult);
    }

    /// <summary>정신력 수정</summary>
    public void ModifyMental(float amount) => statsController?.ModifyMental(amount);

    /// <summary>배고픔 수정</summary>
    public void ModifyHunger(float amount) => statsController?.ModifyHunger(amount);

    /// <summary>피로도 수정</summary>
    public void ModifyFatigue(float amount) => statsController?.ModifyFatigue(amount);

    /// <summary>침식 수치 설정 (0 = 회복)</summary>
    public void SetErosion(float level) => statsController?.SetErosion(level);

    /// <summary>현재 침식 수치</summary>
    public float ErosionLevel => statsController != null ? statsController.ErosionLevel : 0f;

    /// <summary>침식 컨트롤러 참조</summary>
    public EmployeeErosionController ErosionController => erosionController;

    #endregion

    #region 파사드 — 성장 (Growth 위임)

    /// <summary>경험치 획득</summary>
    public void GainExperience(int amount) => growth?.GainExperience(amount);

    /// <summary>작업 능력 향상</summary>
    public void ImproveAbility(WorkType workType, float amount)
    {
        if (!isUnique) return;

        var abilities = work?.Abilities;
        if (abilities == null) return;

        switch (workType)
        {
            case WorkType.Mining: abilities.miningSpeed += amount; break;
            case WorkType.Chopping: abilities.choppingSpeed += amount; break;
            case WorkType.Research: abilities.researchSpeed += amount; break;
            case WorkType.Crafting: abilities.craftingSpeed += amount; break;
            case WorkType.Gardening: abilities.gardeningSpeed += amount; break;
            case WorkType.Building: abilities.buildingSpeed += amount; break;
            case WorkType.Hauling: abilities.haulingSpeed += amount; break;
            case WorkType.Demolish: abilities.demolishSpeed += amount; break;
        }
    }

    #endregion

    #region 파사드 — 비자격 (Work 위임)

    /// <summary>동적 비자격 추가</summary>
    public void AddDisqualification(WorkType workType, string reason = "")
        => work?.AddDisqualification(workType, reason);

    /// <summary>동적 비자격 제거</summary>
    public void RemoveDisqualification(WorkType workType)
        => work?.RemoveDisqualification(workType);

    #endregion

    #region 파사드 — 스케줄 (Schedule 위임)

    /// <summary>현재 시간대의 스케줄 활동</summary>
    public ScheduleActivity CurrentScheduleActivity
        => schedule != null ? schedule.GetCurrentActivity() : ScheduleActivity.Anything;

    /// <summary>스케줄 컴포넌트 참조</summary>
    public EmployeeSchedule Schedule => schedule;

    #endregion

    #region 파사드 — 소집 (Draft 위임)

    /// <summary>소집 상태 여부</summary>
    public bool IsDrafted => draft != null && draft.IsDrafted;

    /// <summary>소집 토글</summary>
    public void ToggleDraft() => draft?.ToggleDraft();

    /// <summary>소집 상태에서 직접 이동 명령</summary>
    public void CommandMoveTo(Vector3 worldPosition) => draft?.CommandMoveTo(worldPosition);

    /// <summary>소집 컴포넌트 참조</summary>
    public EmployeeDraft Draft => draft;

    #endregion

    #region 파사드 — 구역 할당 (ZoneAssignment 위임)

    /// <summary>특정 활동 타입에 구역 할당 (UI에서 호출)</summary>
    public void AssignZone(ZoneType type, int zoneId) => zoneAssignment?.AssignZone(type, zoneId);

    /// <summary>특정 활동 타입의 구역 할당 해제</summary>
    public void ClearZone(ZoneType type) => zoneAssignment?.ClearZone(type);

    /// <summary>특정 활동 타입에 할당된 구역 ID (-1 = 미할당)</summary>
    public int GetAssignedZoneId(ZoneType type)
        => zoneAssignment != null ? zoneAssignment.GetAssignedZoneId(type) : -1;

    /// <summary>구역 할당 컴포넌트 참조</summary>
    public EmployeeZoneAssignment ZoneAssignment => zoneAssignment;

    #endregion

    #region 파사드 — 스킬 트리 (EmployeeSkillState 위임)

    /// <summary>이 직원의 스킬 트리 상태 컴포넌트 (없으면 null)</summary>
    public EmployeeSkillState SkillState => GetComponent<EmployeeSkillState>();

    #endregion

    #region 파사드 — 장비 (Equipment 위임)

    /// <summary>일반 장비 장착</summary>
    public bool EquipItem(EquipmentSlot slot, EquipmentData data)
        => equipment != null && equipment.EquipItem(slot, data);

    /// <summary>일반 장비 해제</summary>
    public EquipmentData UnequipItem(EquipmentSlot slot)
        => equipment?.UnequipItem(slot);

    /// <summary>제노프스 장비 장착</summary>
    public bool EquipXenops(EquipmentSlot slot, Xenops xenops)
        => equipment != null && equipment.EquipXenops(slot, xenops);

    /// <summary>제노프스 장비 해제</summary>
    public Xenops UnequipXenops(EquipmentSlot slot)
        => equipment?.UnequipXenops(slot);

    /// <summary>야간 작업 가능 여부</summary>
    public bool CanWorkAtNight()
        => equipment != null && equipment.CanWorkAtNight();

    /// <summary>비 보호 여부</summary>
    public bool HasRainProtection()
        => equipment != null && equipment.HasRainProtection();

    /// <summary>능력 트리거 발동</summary>
    public void TriggerAbilities(AbilityTriggerType trigger)
        => equipment?.TriggerAbilities(trigger);

    /// <summary>장비 슬롯이 비어있는지 확인</summary>
    public bool IsEquipmentSlotEmpty(EquipmentSlot slot)
        => equipment == null || equipment.IsSlotEmpty(slot);

    /// <summary>장비 컴포넌트 참조</summary>
    public EmployeeEquipment Equipment => equipment;

    #endregion

    #region 저장/로드

    /// <summary>
    /// 저장용 데이터 생성. 각 서브 컴포넌트의 Populate 메서드를 호출합니다.
    /// </summary>
    public EmployeeSaveData CreateSaveData()
    {
        var saveData = new EmployeeSaveData
        {
            instanceId = instanceId,
            templateId = employeeData?.employeeID ?? 0,
            isUnique = isUnique,
            customName = customName,
            posX = transform.position.x,
            posY = transform.position.y,
            state = (int)currentState
        };

        // 각 서브 컴포넌트 데이터 수집
        statsController?.PopulateSaveData(saveData);
        work?.PopulateSaveData(saveData);
        growth?.PopulateSaveData(saveData);
        mental?.PopulateSaveData(saveData);
        equipment?.PopulateSaveData(saveData);
        erosionController?.PopulateSaveData(saveData);
        schedule?.PopulateSaveData(saveData);
        draft?.PopulateSaveData(saveData);
        zoneAssignment?.PopulateSaveData(saveData);

        // 무작위 생성 직원은 GameDatabase로 복원할 수 없으므로 생성 스냅샷을 함께 저장
        if (employeeData != null && RandomEmployeeGenerator.IsGeneratedId(employeeData.employeeID))
        {
            saveData.generated = RandomEmployeeGenerator.CreateSnapshot(employeeData);
        }

        return saveData;
    }

    /// <summary>
    /// 저장된 데이터로 복원. 각 서브 컴포넌트의 Restore 메서드를 호출합니다.
    /// </summary>
    public void RestoreFromSaveData(EmployeeSaveData data)
    {
        _isInitialized = true;

        instanceId = data.instanceId;
        isUnique = data.isUnique;
        customName = data.customName;
        currentState = (EmployeeState)data.state;
        transform.position = new Vector3(data.posX, data.posY, 0f);

        name = $"Employee_{DisplayName}_{instanceId}";

        // RuntimeIDRegistry 등록
        if (instanceId > 0 && RuntimeIDRegistry.instance != null)
        {
            RuntimeIDRegistry.instance.Register(instanceId, this);
        }

        // 서브 컴포넌트 복원
        statsController?.RestoreFromSaveData(data);
        work?.RestoreFromSaveData(data);
        growth?.RestoreFromSaveData(data, isUnique);
        mental?.RestoreFromSaveData(data);
        equipment?.RestoreFromSaveData(data);
        erosionController?.RestoreFromSaveData(data);
        schedule?.RestoreFromSaveData(data);
        draft?.RestoreFromSaveData(data);
        zoneAssignment?.RestoreFromSaveData(data);

        UpdateVisualState();

        if (showDebugInfo)
        {
            Debug.Log($"[Employee] {DisplayName} 복원 완료: Lv.{Level}, HP:{Stats.health}/{Stats.maxHealth}");
        }
    }

    #endregion

    #region 디버그

    private void ShowDebugStatus()
    {
        string status = $"[{DisplayName}] ";
        status += $"Lv.{Level} State:{currentState} Work:{CurrentWork} ";
        status += $"HP:{Stats.health:F0}/{Stats.maxHealth} ";
        status += $"Mental:{Stats.mental:F0}/{Stats.maxMental} ";
        status += $"Hunger:{Needs.hunger:F0}% Fatigue:{Needs.fatigue:F0}%";
        Debug.Log(status);
    }

    #endregion
}
