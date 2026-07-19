using System;
using UnityEngine;

/// <summary>
/// 제노프스 엔티티 코디네이터.
/// Employee.cs 패턴을 따라 서브 컴포넌트를 조율하며,
/// 외부 시스템에 대한 파사드 역할을 합니다.
///
/// 서브 컴포넌트:
///   - XenopsInterpretation: 해석도 경험치/레벨업
///   - XenopsEffectController: 효과 계산 및 적용
///   - IXenopsBehavior: 타입별 행동 (프리팹에 부착)
///
/// 상태 흐름:
///   Idle → Active → (Subdued / Equipped / Dormant)
/// </summary>
public class Xenops : MonoBehaviour
{
    #region 필드

    [Header("제노프스 정보")]
    [SerializeField] private XenopsData xenopsData;
    [SerializeField] private int instanceId;
    [SerializeField] private XenopsState currentState = XenopsState.Idle;

    [Header("디버그")]
    [SerializeField] private bool showDebugInfo = false;

    /// <summary>초기화 완료 여부</summary>
    private bool _isInitialized = false;

    // 서브 컴포넌트 참조
    private XenopsInterpretation interpretation;
    private XenopsEffectController effectController;
    private IXenopsBehavior behavior;
    private SpriteRenderer spriteRenderer;

    // 장비형 전용: 장착 대상 직원
    private Employee equippedEmployee;
    private int equippedEmployeeInstanceId;

    // 잠입체 전용: 잠입 대상 건물
    private Building infiltratedBuilding;
    private int infiltratedBuildingInstanceId;

    #endregion

    #region 이벤트

    public event Action<XenopsState> OnStateChanged;
    public event Action<int> OnInterpretationLevelUp;

    #endregion

    #region 프로퍼티 — 식별

    /// <summary>런타임 고유 ID</summary>
    public int InstanceId => instanceId;

    /// <summary>표시 이름</summary>
    public string DisplayName => xenopsData != null ? xenopsData.xenopsName : "Unknown Xenops";

    /// <summary>제노프스 데이터 (템플릿)</summary>
    public XenopsData Data => xenopsData;

    /// <summary>제노프스 타입</summary>
    public XenopsType Type => xenopsData != null ? xenopsData.xenopsType : XenopsType.Environmental;

    #endregion

    #region 프로퍼티 — 상태

    /// <summary>현재 상태</summary>
    public XenopsState State => currentState;

    /// <summary>기절 중 여부 (장비 능력 Stun — 행동 정지)</summary>
    public bool IsStunned => stunTimer > 0f;

    #endregion

    #region 스턴

    private float stunTimer;

    /// <summary>
    /// 지정 시간 동안 기절시킵니다 — Behavior 정지 + 이동 정지.
    /// 중첩 시 더 긴 잔여 시간이 유지됩니다. (장비 능력 Stun에서 호출)
    /// </summary>
    public void Stun(float duration)
    {
        if (duration <= 0f) return;
        stunTimer = Mathf.Max(stunTimer, duration);

        SetBehaviorScriptEnabled(false);

        var rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;

        if (showDebugInfo)
            Debug.Log($"[Xenops] {DisplayName} 기절 ({duration:F1}초)");
    }

    private void Update()
    {
        if (stunTimer <= 0f) return;

        stunTimer -= Time.deltaTime;
        // 무조건 재활성 — enabled는 스턴 여부만 추적한다 (상태별 행동 여부는
        // Behavior가 OnActivated/OnDeactivated의 isActive 플래그로 스스로 게이트).
        // 상태 조건을 걸면 스턴 중 상태가 바뀐 개체가 영구 정지하는 경로가 생긴다.
        if (stunTimer <= 0f)
            SetBehaviorScriptEnabled(true);
    }

    /// <summary>Behavior MonoBehaviour의 Update 실행을 켜고 끕니다 (스턴용).</summary>
    private void SetBehaviorScriptEnabled(bool value)
    {
        if (behavior is MonoBehaviour mb) mb.enabled = value;
    }

    #endregion

    #region 프로퍼티 — 해석도 (Interpretation 위임)

    /// <summary>현재 해석도 레벨</summary>
    public int InterpretationLevel => interpretation != null ? interpretation.Level : 0;

    /// <summary>해석도 비율 (0~1)</summary>
    public float InterpretationRatio => interpretation != null ? interpretation.Ratio : 0f;

    /// <summary>최대 해석도 레벨</summary>
    public int MaxInterpretationLevel => interpretation != null ? interpretation.MaxLevel : 0;

    /// <summary>현재 해석 경험치</summary>
    public int InterpretationExp => interpretation != null ? interpretation.Experience : 0;

    /// <summary>다음 레벨 필요 경험치</summary>
    public int InterpretationExpToNext => interpretation != null ? interpretation.ExpToNextLevel : 0;

    #endregion

    #region 프로퍼티 — 크로스레퍼런스

    /// <summary>장착 대상 직원 (장비형 전용)</summary>
    public Employee EquippedEmployee => equippedEmployee;

    /// <summary>잠입 대상 건물 (잠입체 전용)</summary>
    public Building InfiltratedBuilding => infiltratedBuilding;

    #endregion

    #region 초기화 및 생명주기

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        // 서브 컴포넌트 참조 (없으면 추가)
        interpretation = GetComponent<XenopsInterpretation>() ?? gameObject.AddComponent<XenopsInterpretation>();
        effectController = GetComponent<XenopsEffectController>() ?? gameObject.AddComponent<XenopsEffectController>();

        // 타입별 Behavior는 프리팹에 미리 부착되어 있어야 함
        behavior = GetComponent<IXenopsBehavior>();
    }

    void OnDestroy()
    {
        if (instanceId > 0 && RuntimeIDRegistry.instance != null)
        {
            RuntimeIDRegistry.instance.Unregister(instanceId);
        }

        // 장비/잠입 해제
        if (equippedEmployee != null) Unequip();
        if (infiltratedBuilding != null) Expel();
    }

    /// <summary>
    /// 새 제노프스 초기화 (처음 스폰 시).
    /// </summary>
    public void Initialize(XenopsData data, int newInstanceId)
    {
        _isInitialized = true;

        xenopsData = data;
        instanceId = newInstanceId;
        currentState = XenopsState.Idle;

        name = $"Xenops_{data.xenopsName}_{instanceId}";

        // 서브 컴포넌트 초기화
        interpretation.Initialize(data.maxInterpretationLevel, data.baseExpRequired);
        effectController.Initialize(data);

        // 해석도 레벨업 이벤트 연결
        interpretation.OnLevelUp += HandleInterpretationLevelUp;

        // Behavior 초기화
        if (behavior == null)
        {
            behavior = GetComponent<IXenopsBehavior>();
        }

        // 체력 즉시 초기화 — XenopsHealth.Start만 기다리면 스폰 프레임에 받은 피해가
        // HP 0 기준으로 계산되어 즉사하는 경로가 있다 (AoE 능력 + 스폰 타이밍)
        if (data.xenopsType == XenopsType.Hostile)
        {
            var health = GetComponent<XenopsHealth>();
            if (health != null)
                health.Initialize(data.hostileStats.maxHealth, data.hostileStats.defense);
        }

        // RuntimeIDRegistry 등록
        if (instanceId > 0 && RuntimeIDRegistry.instance != null)
        {
            RuntimeIDRegistry.instance.Register(instanceId, this);
        }

        // 아이콘 설정
        if (spriteRenderer != null && data.icon != null)
        {
            spriteRenderer.sprite = data.icon;
        }

        if (showDebugInfo)
        {
            Debug.Log($"[Xenops] {DisplayName} 초기화 완료 (ID:{instanceId}, Type:{data.xenopsType})");
        }
    }

    /// <summary>
    /// instanceId 자동 발급 버전.
    /// </summary>
    public void Initialize(XenopsData data)
    {
        int newId = SaveManager.instance != null
            ? SaveManager.instance.GenerateInstanceId()
            : UnityEngine.Random.Range(10000, 99999);
        Initialize(data, newId);
    }

    #endregion

    #region 상태 관리

    /// <summary>
    /// 제노프스 상태를 변경합니다.
    /// </summary>
    public void SetState(XenopsState newState)
    {
        if (currentState == newState) return;

        var prevState = currentState;
        currentState = newState;

        // Behavior 활성/비활성 통보
        if (behavior != null)
        {
            if (newState == XenopsState.Active || newState == XenopsState.Equipped)
            {
                behavior.OnActivated();
            }
            else if (prevState == XenopsState.Active || prevState == XenopsState.Equipped)
            {
                behavior.OnDeactivated();
            }
        }

        OnStateChanged?.Invoke(newState);
        UpdateVisualState();

        if (showDebugInfo)
        {
            Debug.Log($"[Xenops] {DisplayName} 상태 변경: {prevState} → {newState}");
        }
    }

    private void UpdateVisualState()
    {
        if (spriteRenderer == null) return;

        // 적대형(Hostile)은 스프라이트/애니메이션 아트 본연의 색을 유지한다.
        // (상태 색 틴트는 플레이스홀더용이며, 실제 적 스프라이트를 덮어쓰지 않도록 제외)
        if (Type == XenopsType.Hostile)
        {
            spriteRenderer.color = Color.white;
            return;
        }

        Color color = currentState switch
        {
            XenopsState.Active => Color.green,
            XenopsState.Subdued => Color.yellow,
            XenopsState.Equipped => Color.cyan,
            XenopsState.Dormant => Color.gray,
            _ => Color.white
        };

        spriteRenderer.color = color;
    }

    #endregion

    #region 플레이어 상호작용

    /// <summary>
    /// 해석 경험치를 직접 부여합니다 (EquipmentBehavior 등 내부 호출용).
    /// </summary>
    public void GainInterpretationExp(int amount)
    {
        interpretation?.GainExperience(amount);
    }

    /// <summary>
    /// 플레이어가 제노프스와 상호작용합니다.
    /// 해석 경험치를 획득합니다.
    /// </summary>
    public void Interact()
    {
        if (xenopsData == null) return;

        int expAmount = xenopsData.expPerInteraction;
        interpretation?.GainExperience(expAmount);

        if (showDebugInfo)
        {
            Debug.Log($"[Xenops] {DisplayName} 상호작용 — 해석 경험치 +{expAmount} (Lv.{InterpretationLevel})");
        }
    }

    /// <summary>
    /// 적대적 생명체를 제압합니다.
    /// </summary>
    public void Subdue()
    {
        if (xenopsData == null || xenopsData.xenopsType != XenopsType.Hostile) return;
        if (currentState == XenopsState.Subdued) return;

        SetState(XenopsState.Subdued);

        // 드랍 아이템 처리
        var dropStats = xenopsData.hostileStats;
        if (dropStats.subdueDropItemId > 0 && dropStats.subdueDropAmount > 0)
        {
            if (InventoryManager.instance != null && GameDatabase.Instance != null)
            {
                var dropItem = GameDatabase.Instance.GetItemData(dropStats.subdueDropItemId);
                if (dropItem != null)
                {
                    InventoryManager.instance.AddItem(dropItem, dropStats.subdueDropAmount);
                    Debug.Log($"[Xenops] {DisplayName} 제압 — 아이템 드랍: {dropItem.itemName} x{dropStats.subdueDropAmount}");
                }
                else
                {
                    Debug.LogWarning($"[Xenops] {DisplayName} 제압 — 드랍 아이템 없음: ID {dropStats.subdueDropItemId}");
                }
            }
        }

        // 제압 시에도 해석 경험치 부여
        Interact();
    }

    #endregion

    #region 장비/잠입 관리

    /// <summary>
    /// 직원에게 장착합니다 (장비형 전용).
    /// </summary>
    public void EquipTo(Employee employee)
    {
        if (xenopsData == null || xenopsData.xenopsType != XenopsType.Equipment) return;
        if (employee == null) return;

        // 기존 장착 해제
        if (equippedEmployee != null) Unequip();

        equippedEmployee = employee;
        equippedEmployeeInstanceId = employee.InstanceId;
        SetState(XenopsState.Equipped);

        if (showDebugInfo)
        {
            Debug.Log($"[Xenops] {DisplayName} → {employee.DisplayName}에 장착");
        }
    }

    /// <summary>
    /// 장착을 해제합니다.
    /// </summary>
    public void Unequip()
    {
        if (equippedEmployee == null) return;

        if (showDebugInfo)
        {
            Debug.Log($"[Xenops] {DisplayName} → {equippedEmployee.DisplayName}에서 해제");
        }

        equippedEmployee = null;
        equippedEmployeeInstanceId = 0;
        SetState(XenopsState.Idle);
    }

    /// <summary>
    /// 건물에 잠입합니다 (잠입체 전용).
    /// </summary>
    public void InfiltrateTo(Building building)
    {
        if (xenopsData == null || xenopsData.xenopsType != XenopsType.Infiltrator) return;
        if (building == null) return;

        // 기존 잠입 해제
        if (infiltratedBuilding != null) Expel();

        infiltratedBuilding = building;
        infiltratedBuildingInstanceId = building.InstanceId;
        SetState(XenopsState.Active);

        if (showDebugInfo)
        {
            Debug.Log($"[Xenops] {DisplayName} → {building.name}에 잠입");
        }
    }

    /// <summary>
    /// 잠입을 추방합니다.
    /// </summary>
    public void Expel()
    {
        if (infiltratedBuilding == null) return;

        if (showDebugInfo)
        {
            Debug.Log($"[Xenops] {DisplayName} → {infiltratedBuilding.name}에서 추방");
        }

        infiltratedBuilding = null;
        infiltratedBuildingInstanceId = 0;
        SetState(XenopsState.Idle);
    }

    #endregion

    #region 이벤트 핸들러

    private void HandleInterpretationLevelUp(int newLevel)
    {
        OnInterpretationLevelUp?.Invoke(newLevel);

        // 장비형: 해석도 레벨업 시 스탯 보정 재계산
        if (xenopsData != null && xenopsData.xenopsType == XenopsType.Equipment)
        {
            var equipBehavior = GetComponent<EquipmentBehavior>();
            equipBehavior?.RefreshStatModifier();
        }
    }

    #endregion

    #region 저장/로드

    /// <summary>
    /// 저장용 데이터를 생성합니다.
    /// </summary>
    public XenopsSaveData CreateSaveData()
    {
        var saveData = new XenopsSaveData
        {
            instanceId = instanceId,
            templateId = xenopsData != null ? xenopsData.xenopsID : 0,
            state = (int)currentState,
            posX = transform.position.x,
            posY = transform.position.y,
            equippedEmployeeInstanceId = equippedEmployeeInstanceId,
            infiltratedBuildingInstanceId = infiltratedBuildingInstanceId,
            equippedSlot = xenopsData != null && xenopsData.xenopsType == XenopsType.Equipment && currentState == XenopsState.Equipped
                ? (int)xenopsData.equipmentSlot : -1
        };

        // 서브 컴포넌트 데이터 수집
        interpretation?.PopulateSaveData(saveData);

        return saveData;
    }

    /// <summary>
    /// 저장된 데이터에서 복원합니다.
    /// </summary>
    public void RestoreFromSaveData(XenopsSaveData data)
    {
        _isInitialized = true;

        instanceId = data.instanceId;
        currentState = (XenopsState)data.state;
        transform.position = new Vector3(data.posX, data.posY, 0f);

        // 크로스레퍼런스 ID 보관 (PostRestore에서 해결)
        equippedEmployeeInstanceId = data.equippedEmployeeInstanceId;
        infiltratedBuildingInstanceId = data.infiltratedBuildingInstanceId;

        name = $"Xenops_{DisplayName}_{instanceId}";

        // RuntimeIDRegistry 등록
        if (instanceId > 0 && RuntimeIDRegistry.instance != null)
        {
            RuntimeIDRegistry.instance.Register(instanceId, this);
        }

        // 서브 컴포넌트 복원
        if (xenopsData != null)
        {
            interpretation?.RestoreFromSaveData(data, xenopsData.maxInterpretationLevel, xenopsData.baseExpRequired);
            effectController?.Initialize(xenopsData);
            interpretation.OnLevelUp += HandleInterpretationLevelUp;
        }

        // Behavior 참조
        behavior = GetComponent<IXenopsBehavior>();

        UpdateVisualState();

        if (showDebugInfo)
        {
            Debug.Log($"[Xenops] {DisplayName} 복원 완료: 해석도 Lv.{InterpretationLevel}, State:{currentState}");
        }
    }

    /// <summary>
    /// 크로스레퍼런스를 복원합니다 (PostRestore 단계에서 호출).
    /// </summary>
    public void ResolveReferences()
    {
        if (RuntimeIDRegistry.instance == null) return;

        // 장비형 — 장착 대상 직원 복원
        if (equippedEmployeeInstanceId > 0)
        {
            equippedEmployee = RuntimeIDRegistry.instance.ResolveComponent<Employee>(equippedEmployeeInstanceId);
            if (equippedEmployee == null)
            {
                Debug.LogWarning($"[Xenops] {DisplayName}: 장착 대상 직원(ID:{equippedEmployeeInstanceId}) 복원 실패");
                equippedEmployeeInstanceId = 0;
                if (currentState == XenopsState.Equipped) SetState(XenopsState.Idle);
            }
        }

        // 잠입체 — 잠입 대상 건물 복원
        if (infiltratedBuildingInstanceId > 0)
        {
            infiltratedBuilding = RuntimeIDRegistry.instance.ResolveComponent<Building>(infiltratedBuildingInstanceId);
            if (infiltratedBuilding == null)
            {
                Debug.LogWarning($"[Xenops] {DisplayName}: 잠입 대상 건물(ID:{infiltratedBuildingInstanceId}) 복원 실패");
                infiltratedBuildingInstanceId = 0;
                if (currentState == XenopsState.Active) SetState(XenopsState.Idle);
            }
        }
    }

    #endregion
}
