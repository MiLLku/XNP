using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 위장 중인 위장체(위장체 습격) — 원본 직원 하나를 복제한 직원 개체.
///
/// 직원 목록(EmployeeManager)에는 넣지 않는다 — 설정 UI·직원 수 집계·적 조준·무드 판정에서 빠진다.
/// 작업 시스템에는 등록돼 일반 직원처럼 작업을 예약하고 실제로 한다. 속도는 원본 직원이 그 작업에서 낼 속도의 SPEED_SCALE.
/// 설정(작업 순서·일정·구역)은 SYNC_INTERVAL마다 원본을 읽어 따라간다 — 원본이 없으면 마지막으로 읽은 값(복제체 자신의 설정)을 쓴다.
/// 외형은 복제 때 그대로. 욕구는 고정(먹고 자지 않음), 정신 이상 없음.
///
/// 침식: 서 있는 방의 방 침식을 올리고(ITerrainErosionSource, 실외 무효), 곁의 직원을 직접 침식시킨다.
/// 경고: 그 방이 방 침식 경고 수준이거나 무드 낮은 직원이 있으면 CHECK_INTERVAL마다 확률로 경고 레터(위치 없음).
///       위장체 도감 100%면 복제 즉시 경고(누르면 위치로).
/// 정체 공개: 피해를 입거나(죽을 피해 포함) 소집 명령 → 그 자리에 침식체(위장체 XenopsData)로 바뀐다.
/// </summary>
[RequireComponent(typeof(Employee))]
public class DisguisedEmployee : MonoBehaviour, ITerrainErosionSource
{
    // shortcut: 임시 수치 — 밸런스 확정 시 데이터로 뺄 것
    private const float SPEED_SCALE = 0.1f;
    private const float SYNC_INTERVAL = 2f;
    private const float ROOM_EROSION_PER_SECOND = 0.4f;
    private const float STAFF_EROSION_PER_SECOND = 0.3f;
    private const float STAFF_RANGE = 2f;
    private const float ROOM_WARNING_THRESHOLD = 60f;   // RoomErosionAlertConfig.cautionThreshold 기본값과 같게
    private const float LOW_MOOD = 30f;
    private const float CHECK_INTERVAL = 60f;
    private const float ALERT_CHANCE = 0.25f;
    private const float ALERT_COOLDOWN = 300f;
    private const int XENOPS_ID = 6105;
    private const string ALERT_BODY = "알 수 없는 원인으로 기지 내 침식 수치가 오르고 있습니다. 내부 침입이 일어났을 지도 모릅니다.";

    private static readonly List<DisguisedEmployee> active = new List<DisguisedEmployee>();

    /// <summary>지금 기지에 있는 위장 중인 위장체들 (세이브용)</summary>
    public static IReadOnlyList<DisguisedEmployee> All => active;

    private Employee _emp;
    private float _syncTimer;
    private float _staffTick;
    private float _checkTimer = CHECK_INTERVAL;
    private bool _revealed;

    /// <summary>복제한 원본 직원 인스턴스 ID (저장됨)</summary>
    public int SourceEmployeeId { get; private set; }

    /// <summary>다음 경고까지 남은 쿨다운(초, 저장됨)</summary>
    public float AlertCooldown { get; private set; }

    /// <summary>복제한 원본 직원 (살아 있으면)</summary>
    public Employee Source
    {
        get
        {
            var e = RuntimeIDRegistry.instance != null ? RuntimeIDRegistry.instance.ResolveComponent<Employee>(SourceEmployeeId) : null;
            return e != null && e.State != EmployeeState.Dead ? e : null;
        }
    }

    public Employee Body => _emp;

    #region ITerrainErosionSource — 서 있는 방의 방 침식을 올린다

    public Vector2Int TilePosition => new Vector2Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y));
    public float ErosionPerSecond => ROOM_EROSION_PER_SECOND;
    public float SaturationLevel => 0f; // 한계 없음 (방 침식 상한까지)
    public bool IsActive => isActiveAndEnabled && !_revealed;
    public string SourceDisplayName => "알 수 없는 원인";

    #endregion

    #region 생성·복원

    /// <summary>원본 직원을 복제해 pos에 둔다 — 장비·소지품·작업 배정·정신 이벤트는 복제하지 않는다(진짜 아이템이 늘지 않게)</summary>
    public static DisguisedEmployee Spawn(Employee src, Vector3 pos)
    {
        if (src == null || EmployeeManager.instance == null) return null;
        var esd = src.CreateSaveData();
        esd.instanceId = SaveManager.instance != null ? SaveManager.instance.GenerateInstanceId() : Random.Range(100000, 999999);
        esd.posX = pos.x;
        esd.posY = pos.y;
        esd.state = (int)EmployeeState.Idle;
        esd.assignedWorkOrderId = -1;
        esd.heldFoodItemId = 0; esd.heldFoodCount = 0;
        esd.heldDrugItemId = 0; esd.heldDrugCount = 0;
        esd.equippedItems = new List<EquipmentSlotSaveData>();
        esd.activeMentalEvents = new List<MentalEventSaveData>();
        esd.activeAbnormalBehavior = 0;
        esd.isDrafted = false;

        var body = EmployeeManager.instance.InstantiateFromSave(esd);
        if (body == null) return null;
        var d = body.gameObject.AddComponent<DisguisedEmployee>();
        d.SourceEmployeeId = src.InstanceId;
        d.CopyLook(src);
        if (XenopsCodexManager.IsComplete(XENOPS_ID)) d.PushLetter("경고", ALERT_BODY, true);
        Debug.Log($"[Disguise] 위장체 — {src.DisplayName} 복제 @ {pos}");
        return d;
    }

    /// <summary>불러오기 — 저장된 복제체 데이터로 다시 만든다 (원본이 살아 있으면 모습을 다시 복사)</summary>
    public static DisguisedEmployee Restore(EmployeeSaveData esd, int sourceId, float alertCooldown)
    {
        if (esd == null || EmployeeManager.instance == null) return null;
        var body = EmployeeManager.instance.InstantiateFromSave(esd);
        if (body == null) return null;
        var d = body.gameObject.AddComponent<DisguisedEmployee>();
        d.SourceEmployeeId = sourceId;
        d.AlertCooldown = alertCooldown;
        var src = d.Source;
        if (src != null) d.CopyLook(src);
        return d;
    }

    /// <summary>원본의 지금 몸·머리 그림 (장비 그림은 게임에 따로 없다) — 그 뒤 원본이 바뀌어도 유지</summary>
    private void CopyLook(Employee src)
    {
        var a = src.GetComponent<SpriteRenderer>();
        var b = GetComponent<SpriteRenderer>();
        if (a != null && b != null) { b.sprite = a.sprite; b.color = a.color; }
        var ah = src.transform.Find("Hair");
        var bh = transform.Find("Hair");
        var ahr = ah != null ? ah.GetComponent<SpriteRenderer>() : null;
        var bhr = bh != null ? bh.GetComponent<SpriteRenderer>() : null;
        if (ahr != null && bhr != null) { bhr.sprite = ahr.sprite; bhr.color = ahr.color; bhr.enabled = ahr.enabled; }
    }

    #endregion

    private void Awake()
    {
        _emp = GetComponent<Employee>();
        active.Add(this);
    }

    private void Start()
    {
        TerrainErosionManager.instance?.RegisterSource(this);
        var mental = GetComponent<EmployeeMental>();
        if (mental != null) mental.enabled = false; // 정신 이상 없음
        _syncTimer = 0f;
    }

    private void OnDestroy()
    {
        active.Remove(this);
        TerrainErosionManager.instance?.UnregisterSource(this);
    }

    private void Update()
    {
        if (_revealed || (SaveManager.instance != null && SaveManager.instance.IsLoading)) return;
        float dt = Time.deltaTime;

        // 피해를 입으면 정체를 드러낸다
        var stats = _emp.Stats;
        if (stats.health < stats.maxHealth - 0.01f) { Reveal(); return; }

        // 작업 속도 = 원본 속도 × SPEED_SCALE (원본이 없으면 복제체 자신의 속도 × SPEED_SCALE)
        var work = GetComponent<EmployeeWork>();
        var src = Source;
        if (work != null) { work.SpeedSource = src; work.SpeedScale = SPEED_SCALE; }

        _syncTimer -= dt;
        if (_syncTimer <= 0f)
        {
            _syncTimer = SYNC_INTERVAL;
            if (src != null) SyncSettings(src);
            // 욕구 고정 — 먹고 자러 가지 않는다
            _emp.StatsController?.ModifyHunger(100f);
            _emp.StatsController?.ModifyFatigue(100f); // 피로도는 높을수록 개운하다
            _emp.StatsController?.ModifyFun(100f);
        }

        // 곁의 직원 침식 (초당)
        _staffTick += dt;
        if (_staffTick >= 1f)
        {
            _staffTick -= 1f;
            if (EmployeeManager.instance != null)
                foreach (var e in EmployeeManager.instance.AllEmployees)
                    if (e != null && e.State != EmployeeState.Dead && Vector2.Distance(e.transform.position, transform.position) <= STAFF_RANGE)
                        e.GetComponent<EmployeeErosionController>()?.AddErosion(STAFF_EROSION_PER_SECOND, "disguise_" + _emp.InstanceId, "알 수 없는 원인");
        }

        // 확률 경고
        if (AlertCooldown > 0f) AlertCooldown = Mathf.Max(0f, AlertCooldown - dt);
        _checkTimer -= dt;
        if (_checkTimer <= 0f)
        {
            _checkTimer = CHECK_INTERVAL;
            if (AlertCooldown <= 0f && AlertConditionMet() && Random.value < ALERT_CHANCE)
                PushLetter("경고", ALERT_BODY, false);
        }
    }

    /// <summary>원본의 작업 순서·일정·구역을 그대로 가져온다</summary>
    private void SyncSettings(Employee src)
    {
        var order = src.GetEnabledWorkTypes();
        order.Sort((a, b) => src.GetWorkPriority(a).CompareTo(src.GetWorkPriority(b)));
        GetComponent<EmployeeWork>()?.SetWorkOrder(order);

        var tmp = new EmployeeSaveData();
        src.Schedule?.PopulateSaveData(tmp);
        src.ZoneAssignment?.PopulateSaveData(tmp);
        _emp.Schedule?.RestoreFromSaveData(tmp);
        _emp.ZoneAssignment?.RestoreFromSaveData(tmp);
    }

    /// <summary>플레이어가 위장체를 소집하려 했다 — 경고 후 정체를 드러낸다</summary>
    public void ExposeByDraft()
    {
        PushLetter("경고", "직원으로 위장한 침식체가 정체를 드러냈습니다!", true);
        Reveal();
    }

    /// <summary>
    /// 본모습으로 — 그 자리에 침식체를 세우고 복제체는 없앤다(맡던 작업은 풀린다).
    /// 경고 레터의 '위치로 이동'이 가리키는 곳은 이 자리.
    /// </summary>
    public void Reveal()
    {
        if (_revealed) return;
        _revealed = true;
        var pos = transform.position;
        if (InteractionManager.instance != null && InteractionManager.instance.SelectedEmployee == _emp)
            InteractionManager.instance.DeselectEmployee();
        WorkSystemManager.instance?.OnWorkerCancelledWork(_emp);
        WorkSystemManager.instance?.UnregisterEmployee(_emp);

        var x = XenopsManager.instance != null ? XenopsManager.instance.SpawnXenops(XENOPS_ID, pos) : null;
        if (x != null && RunStatsManager.instance != null)
        {
            var raid = RaidManager.instance;
            x.ApplyThreatScaling(raid != null ? raid.StageMultiplierFor(RunStatsManager.instance.ThreatStage) : 1f, true);
        }
        Debug.Log($"[Disguise] 정체 공개 @ {pos}");
        Destroy(gameObject);
    }

    /// <summary>이 위장체가 있는 방이 방 침식 경고 수준이거나, 무드가 낮은 직원이 있는지</summary>
    private bool AlertConditionMet()
    {
        var room = RoomManager.instance != null ? RoomManager.instance.GetRoom(TilePosition) : null;
        if (room != null && room.Erosion >= ROOM_WARNING_THRESHOLD) return true;
        if (EmployeeManager.instance != null)
            foreach (var e in EmployeeManager.instance.AllEmployees)
                if (e != null && e.State != EmployeeState.Dead && e.Stats.mental <= LOW_MOOD) return true;
        return false;
    }

    private void PushLetter(string title, string body, bool withLocation)
    {
        AlertCooldown = ALERT_COOLDOWN;
        Vector3 at = transform.position;
        NotificationManager.instance?.PushLetter(new Letter
        {
            title = title,
            body = body,
            type = LetterType.Threat,
            onClick = withLocation ? () =>
            {
                var cam = Camera.main;
                if (cam != null) cam.transform.position = new Vector3(at.x, at.y, cam.transform.position.z);
            } : (System.Action)null
        });
    }
}
