using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 위장체(위장체 습격) — 스폰 때 살아 있는 직원 한 명의 모습(몸·머리·이름)을 복사해 기지 안에 섞인다.
/// 몸체·전투는 같은 개체의 GrapplerBehavior가 맡고, 이 컴포넌트는 위장 동안 EnemyNavigator 지키기(반경 0)로
/// "갈 곳"만 정해 준다 — 원본의 작업 우선순위대로 같은 종류의 작업 대상에 가서 서 있다(진행·예약·점유 없음), 없으면 기지 안을 돌아다닌다.
///
/// 침식: 서 있는 방의 방 침식을 올리고(ITerrainErosionSource, 실외 무효), 곁의 직원을 직접 침식시킨다. 범위 오라는 없다.
/// 경고: 이 위장체의 방이 방 침식 경고 수준이거나 무드 낮은 직원이 있으면 CHECK_INTERVAL마다 확률로 경고 레터(위치 없음).
///       도감 100%면 스폰 즉시 경고(누르면 위치로).
/// 정체 공개: 피해를 입거나, 플레이어가 선택해 소집 명령을 내리면(경고 레터) — 본모습으로 바뀌고 그래플러처럼 싸운다.
/// </summary>
[RequireComponent(typeof(Xenops))]
public class DisguiseBehavior : MonoBehaviour, ITerrainErosionSource
{
    // shortcut: 임시 수치 — 밸런스 확정 시 데이터로 뺄 것
    private const float ROOM_EROSION_PER_SECOND = 0.4f;
    private const float STAFF_EROSION_PER_SECOND = 0.3f;
    private const float STAFF_RANGE = 2f;
    private const float ROOM_WARNING_THRESHOLD = 60f;   // RoomErosionAlertConfig.cautionThreshold 기본값과 같게
    private const float LOW_MOOD = 30f;
    private const float CHECK_INTERVAL = 60f;
    private const float ALERT_CHANCE = 0.25f;
    private const float ALERT_COOLDOWN = 300f;
    private const float RETARGET_INTERVAL = 15f;
    private const string ALERT_BODY = "알 수 없는 원인으로 기지 내 침식 수치가 오르고 있습니다. 내부 침입이 일어났을 지도 모릅니다.";
    private static readonly Color RevealedTint = new Color(0.75f, 0.45f, 1f, 1f);

    private Xenops _xenops;
    private XenopsHealth _health;
    private EnemyNavigator _nav;
    private Animator _anim;
    private SpriteRenderer _sr;
    private SpriteRenderer _hair;
    private Sprite _bodySprite;
    private Color _bodyColor = Color.white;
    private float _checkTimer = CHECK_INTERVAL;
    private float _retargetTimer;
    private float _staffTick;

    /// <summary>정체를 드러냈는지 (저장됨)</summary>
    public bool Revealed { get; private set; }

    /// <summary>다음 경고까지 남은 쿨다운(초, 저장됨)</summary>
    public float AlertCooldown { get; private set; }

    /// <summary>복사한 원본 직원 인스턴스 ID (저장됨)</summary>
    public int SourceEmployeeId { get; private set; }

    /// <summary>원본의 작업 종류 (우선순위 순, 스폰 때 고정, 저장됨)</summary>
    public List<int> WorkTypes { get; } = new List<int>();

    /// <summary>복사한 원본 직원 (살아 있으면)</summary>
    public Employee SourceEmployee
        => RuntimeIDRegistry.instance != null ? RuntimeIDRegistry.instance.ResolveComponent<Employee>(SourceEmployeeId) : null;

    /// <summary>위장 중인 위장체인지 — 직원 전투 자동 조준·도감 관찰에서 빠진다</summary>
    public static bool IsDisguised(Xenops x)
    {
        var d = x != null ? x.GetComponent<DisguiseBehavior>() : null;
        return d != null && !d.Revealed;
    }

    #region ITerrainErosionSource — 서 있는 방의 방 침식을 올린다

    public Vector2Int TilePosition => new Vector2Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y));
    public float ErosionPerSecond => ROOM_EROSION_PER_SECOND;
    public float SaturationLevel => 0f; // 한계 없음 (방 침식 상한까지)
    public bool IsActive => isActiveAndEnabled;
    public string SourceDisplayName => "알 수 없는 원인";

    #endregion

    private void Awake()
    {
        _xenops = GetComponent<Xenops>();
        _health = GetComponent<XenopsHealth>();
        _anim = GetComponent<Animator>();
        _sr = GetComponent<SpriteRenderer>();
    }

    private void Start()
    {
        _nav = GetComponent<EnemyNavigator>() ?? gameObject.AddComponent<EnemyNavigator>();
        TerrainErosionManager.instance?.RegisterSource(this);
    }

    private void OnDestroy() => TerrainErosionManager.instance?.UnregisterSource(this);

    /// <summary>스폰 직후 — 살아 있는 직원 하나를 골라 모습·작업을 복사하고, 도감 100%면 바로 경고</summary>
    public void OnSpawned()
    {
        var alive = EmployeeManager.instance != null
            ? EmployeeManager.instance.AllEmployees.FindAll(e => e != null && e.State != EmployeeState.Dead) : new List<Employee>();
        var src = alive.Count > 0 ? alive[Random.Range(0, alive.Count)] : null;
        if (src != null)
        {
            SourceEmployeeId = src.InstanceId;
            var types = src.GetEnabledWorkTypes();
            types.Sort((a, b) => src.GetWorkPriority(a).CompareTo(src.GetWorkPriority(b)));
            WorkTypes.Clear();
            foreach (var t in types) WorkTypes.Add((int)t);
            CopyLook(src);
        }
        if (_xenops != null && _xenops.Data != null && XenopsCodexManager.IsComplete(_xenops.Data.xenopsID))
            PushLetter("경고", ALERT_BODY, true);
        Debug.Log($"[Disguise] 위장체 스폰 — {(src != null ? src.DisplayName : "원본 없음")} 모습 @ {transform.position}");
    }

    /// <summary>불러오기 — 공개 여부·쿨다운·원본·작업 종류</summary>
    public void RestoreState(bool revealed, float cooldown, int sourceId, List<int> workTypes)
    {
        Revealed = revealed;
        AlertCooldown = cooldown;
        SourceEmployeeId = sourceId;
        WorkTypes.Clear();
        if (workTypes != null) WorkTypes.AddRange(workTypes);
        // shortcut: 원본이 사라졌으면 기본 직원 몸 그림만 남는다(머리·이름 없음) — 모습 스냅샷 저장이 필요해지면 그때
        var src = SourceEmployee;
        if (!revealed && src != null) CopyLook(src);
    }

    /// <summary>원본 직원의 몸·머리 그림과 이름을 복사한다 (그 뒤 원본이 바뀌어도 유지)</summary>
    private void CopyLook(Employee src)
    {
        var srcSr = src.GetComponent<SpriteRenderer>();
        if (srcSr != null) { _bodySprite = srcSr.sprite; _bodyColor = srcSr.color; }
        var srcHair = src.transform.Find("Hair");
        var srcHairSr = srcHair != null ? srcHair.GetComponent<SpriteRenderer>() : null;
        if (srcHairSr != null && srcHairSr.enabled && srcHairSr.sprite != null)
        {
            if (_hair == null)
            {
                var go = new GameObject("Hair", typeof(SpriteRenderer));
                go.transform.SetParent(transform, false);
                go.transform.localPosition = srcHair.localPosition;
                _hair = go.GetComponent<SpriteRenderer>();
            }
            _hair.sprite = srcHairSr.sprite;
            _hair.color = srcHairSr.color;
            _hair.sortingLayerID = srcHairSr.sortingLayerID;
            _hair.sortingOrder = (_sr != null ? _sr.sortingOrder : srcHairSr.sortingOrder) + 1;
        }
        if (_xenops != null) { _xenops.NameOverride = src.DisplayName; _xenops.name = src.name; }
    }

    /// <summary>플레이어가 위장체를 고른 채 소집 명령을 내렸다 — 경고 후 정체를 드러낸다</summary>
    public void ExposeByDraft()
    {
        if (Revealed) return;
        PushLetter("경고", "직원으로 위장한 침식체가 정체를 드러냈습니다!", true);
        Reveal();
    }

    /// <summary>이동 명령 — 따르는 척 그쪽으로 간다 (다음 목표 갱신 때까지)</summary>
    public void FakeMoveTo(Vector3 pos)
    {
        if (Revealed || _nav == null) return;
        _nav.GuardAnchor = pos;
        _nav.GuardRadius = 0f;
        _retargetTimer = RETARGET_INTERVAL;
    }

    /// <summary>본모습으로 — 이름·머리를 지우고 그래플러처럼 직원·건물을 노린다</summary>
    public void Reveal()
    {
        if (Revealed) return;
        Revealed = true;
        if (_nav != null) _nav.GuardAnchor = null;
        if (_hair != null) _hair.enabled = false;
        if (_xenops != null) { _xenops.NameOverride = null; _xenops.name = $"Xenops_{_xenops.DisplayName}_{_xenops.InstanceId}"; }
        if (InteractionManager.instance != null && InteractionManager.instance.SelectedDisguise == this)
            InteractionManager.instance.DeselectEmployee();
        Debug.Log($"[Disguise] 정체 공개 @ {transform.position}");
    }

    private void Update()
    {
        if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;
        float dt = Time.deltaTime;

        // 피해를 입으면 정체를 드러낸다
        if (!Revealed && _health != null && _health.CurrentHealth < _health.MaxHealth - 0.01f) Reveal();

        if (!Revealed && _nav != null)
        {
            _retargetTimer -= dt;
            if (_retargetTimer <= 0f || !_nav.GuardAnchor.HasValue)
            {
                _retargetTimer = RETARGET_INTERVAL;
                var p = PickDestination();
                if (p.HasValue) { _nav.GuardAnchor = p.Value; _nav.GuardRadius = 0f; }
            }
        }

        // 곁의 직원 침식 (초당)
        _staffTick += dt;
        if (_staffTick >= 1f)
        {
            _staffTick -= 1f;
            if (EmployeeManager.instance != null)
                foreach (var e in EmployeeManager.instance.AllEmployees)
                    if (e != null && e.State != EmployeeState.Dead && Vector2.Distance(e.transform.position, transform.position) <= STAFF_RANGE)
                        e.GetComponent<EmployeeErosionController>()?.AddErosion(STAFF_EROSION_PER_SECOND, "disguise_" + (_xenops != null ? _xenops.InstanceId : 0), "알 수 없는 원인");
        }

        // 확률 경고 — 위장 중에만 (드러나면 이미 보인다)
        if (AlertCooldown > 0f) AlertCooldown = Mathf.Max(0f, AlertCooldown - dt);
        _checkTimer -= dt;
        if (_checkTimer <= 0f)
        {
            _checkTimer = CHECK_INTERVAL;
            if (!Revealed && AlertCooldown <= 0f && AlertConditionMet() && Random.value < ALERT_CHANCE)
                PushLetter("경고", ALERT_BODY, false);
        }
    }

    // Xenops·애니메이터가 그림을 덮으므로 프레임 끝에 위장 모습/본모습 색을 다시 입힌다
    private void LateUpdate()
    {
        if (_sr == null) return;
        if (!Revealed && _bodySprite != null)
        {
            if (_anim != null) _anim.enabled = false;
            _sr.sprite = _bodySprite;
            _sr.color = _bodyColor;
        }
        else if (Revealed)
        {
            if (_anim != null && !_anim.enabled) { _anim.enabled = true; if (_xenops?.Data?.icon != null) _sr.sprite = _xenops.Data.icon; }
            _sr.color = RevealedTint;
        }
        if (_hair != null && _hair.enabled) _hair.flipX = _sr.flipX;
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
        var self = transform;
        NotificationManager.instance?.PushLetter(new Letter
        {
            title = title,
            body = body,
            type = LetterType.Threat,
            onClick = withLocation ? () =>
            {
                var cam = Camera.main;
                if (cam != null && self != null) cam.transform.position = new Vector3(self.position.x, self.position.y, cam.transform.position.z);
            } : (System.Action)null
        });
    }

    private static TilePathfinder _standFinder;
    private static GameMap _standMap;

    /// <summary>
    /// 갈 곳 — 원본의 작업 우선순위 순으로 같은 종류의 가장 가까운(걸어서 닿는) 작업 대상 곁, 없으면 기지 안 아무 건물 곁.
    /// 작업 위치는 채광 칸처럼 바위 속일 수 있어 2칸 안의 설 수 있는 칸으로 맞춘다.
    /// </summary>
    private Vector3? PickDestination()
    {
        var map = MapGenerator.instance != null ? MapGenerator.instance.GameMapInstance : null;
        if (map == null) return null;
        if (_standMap != map) { _standMap = map; _standFinder = new TilePathfinder(map); }
        var reach = ReachabilityMap.Current;
        var foot = TilePosition;

        var work = WorkSystemManager.instance;
        if (work != null)
            foreach (int t in WorkTypes)
            {
                Vector3? best = null;
                float bestD = float.MaxValue;
                foreach (var order in work.GetOrdersByType((WorkType)t))
                {
                    if (order == null || !order.isActive || order.isPaused) continue;
                    foreach (var target in order.targets)
                    {
                        if (target == null || !target.IsWorkAvailable()) continue;
                        var p = target.GetWorkPosition();
                        float d = Vector2.Distance(p, transform.position);
                        if (d >= bestD) continue;
                        var stand = StandNear(p, foot, reach);
                        if (stand.HasValue) { bestD = d; best = stand; }
                    }
                }
                if (best.HasValue) return best;
            }

        var buildings = UnityEngine.Object.FindObjectsByType<Building>(FindObjectsSortMode.None);
        for (int i = 0; i < 6 && buildings.Length > 0; i++)
        {
            var stand = StandNear(buildings[Random.Range(0, buildings.Length)].transform.position, foot, reach);
            if (stand.HasValue) return stand;
        }
        return null;
    }

    /// <summary>p 곁(2칸 안)의 설 수 있고 지금 자리에서 닿는 칸 중심 — 없으면 null</summary>
    private static Vector3? StandNear(Vector3 p, Vector2Int foot, ReachabilityMap reach)
    {
        var c = new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
        for (int r = 0; r <= 2; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                    var cell = c + new Vector2Int(dx, dy);
                    if (!_standFinder.IsValidPosition(cell)) continue;
                    if (reach != null && !reach.IsReachable(foot, cell)) continue;
                    return new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
                }
        return null;
    }
}
