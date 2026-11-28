using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 가시 함정 — 초반 소수 습격을 자원과 노동으로 넘기는 장치 (2026-11-23 사용자 결정, 수치는 자리표시값).
///
/// <list type="bullet">
/// <item>적(적대 제놉스 — 침식된 인간 포함)이 이 칸에 들어오면 한 번 작동해 그 적 하나에게 <see cref="damage"/>만큼 피해를 준다.
///       직원·위장 중인 위장체(직원 모습)는 제놉스가 아니라서 지나가도 작동하지 않는다.</item>
/// <item>작동하면 '해제됨' — 어둡게 보이고 다시 작동하지 않는다. 직원이 건설 일로 재장전한다(나무 1, 짧은 작업).</item>
/// <item>적은 함정을 공격 대상·막는 건물로 보지 않는다 — 건물 체력 0(부술 수 없는 건물)이면 적의 목표 후보에서 빠지고,
///       통행을 막지 않으므로 경로도 피하지 않는다.</item>
/// </list>
/// </summary>
[RequireComponent(typeof(Building))]
public class SpikeTrap : MonoBehaviour, IBuildingFunction, IBuildingExtraSerializable
{
    #region 인스펙터

    [Tooltip("한 번 작동할 때 주는 피해 (방어력 적용 전). 자리표시값 25: 3~4일차 침식 사수(체력 약 40~45)가 두 번에 죽는다.\n" +
             "고정값이라 날이 갈수록·깊이 위협으로 적 체력이 오르면 상대적으로 약해진다.")]
    [Min(0f)] [SerializeField] private float damage = 25f;

    [Tooltip("재장전에 드는 재료 (자리표시값: 나무 1)")]
    [SerializeField] private ItemData rearmItem;
    [Min(0)] [SerializeField] private int rearmAmount = 1;

    [Tooltip("재장전 작업량 (자리표시값)")]
    [Min(0.1f)] [SerializeField] private float rearmWork = 3f;

    [Tooltip("해제됐을 때 그림에 곱하는 색")]
    [SerializeField] private Color disarmedTint = new Color(0.4f, 0.4f, 0.4f, 1f);

    #endregion

    #region 상태

    private const float SCAN_INTERVAL = 0.1f;

    private Building _building;
    private SpriteRenderer _renderer;
    private Color _armedColor = Color.white;
    private bool _armed = true;
    private float _scanTimer;
    private WorkOrder _rearmWorkOrder;
    internal float RearmWorkDone { get; private set; }

    // 함정 여럿이 같은 프레임에 제놉스 목록을 매번 새로 만들지 않게 한 번만 모은다
    private static readonly List<Xenops> s_hostiles = new List<Xenops>();
    private static int s_hostilesFrame = -1;

    #endregion

    #region 프로퍼티

    /// <summary>장전되어 있는지 (해제되면 재장전 전까지 작동하지 않는다)</summary>
    public bool IsArmed => _armed;

    public Vector2Int Cell => new Vector2Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y));

    public ItemData RearmItem => rearmItem;
    public int RearmAmount => rearmAmount;
    public float RearmWork => rearmWork;

    public bool IsOperating => _armed;

    #endregion

    #region 생명주기

    void Awake()
    {
        _building = GetComponent<Building>();
        _renderer = GetComponent<SpriteRenderer>();
        if (_renderer != null) _armedColor = _renderer.color;
    }

    void Start()
    {
        ApplyVisual();
    }

    void Update()
    {
        if (!_armed)
        {
            // 불러오기는 작업 목록을 새로 채우므로, 불러오는 도중 걸어 둔 재장전 작업이 빠질 수 있다 — 주기적으로 확인해 다시 건다
            _scanTimer -= Time.deltaTime;
            if (_scanTimer > 0f) return;
            _scanTimer = 1f;
            if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;
            EnsureRearmOrder();
            return;
        }
        if (_building == null || !_building.IsFunctional) return;

        _scanTimer -= Time.deltaTime;
        if (_scanTimer > 0f) return;
        _scanTimer = SCAN_INTERVAL;

        var victim = FindEnemyOnCell();
        if (victim != null) Trigger(victim);
    }

    void OnDestroy()
    {
        RemoveRearmOrder();
    }

    #endregion

    #region 작동

    /// <summary>이 칸(발 위치)에 들어온 살아 있는 적 하나 — 직원·위장체는 제놉스가 아니므로 애초에 후보가 아니다</summary>
    private Xenops FindEnemyOnCell()
    {
        if (XenopsManager.instance == null) return null;
        if (s_hostilesFrame != Time.frameCount)
        {
            s_hostilesFrame = Time.frameCount;
            s_hostiles.Clear();
            foreach (var x in XenopsManager.instance.GetAllXenops())
                if (x != null && x.Type == XenopsType.Hostile && x.State == XenopsState.Active) s_hostiles.Add(x);
        }

        var cell = Cell;
        foreach (var x in s_hostiles)
        {
            if (x == null) continue;
            var h = x.GetComponent<XenopsHealth>();
            if (h == null || h.CurrentHealth <= 0f) continue;
            // 구조물(거점·스포너)은 걸어 다니지 않으니 함정에 걸리지 않는다
            if (x.GetComponent<OutpostStructure>() != null || x.GetComponent<SpawnerStructure>() != null) continue;
            Vector3 p = x.transform.position;
            if (Mathf.FloorToInt(p.x) == cell.x && Mathf.Abs(p.y - (cell.y + 0.5f)) <= 0.9f) return x;
        }
        return null;
    }

    private void Trigger(Xenops victim)
    {
        _armed = false;
        var h = victim.GetComponent<XenopsHealth>();
        if (h != null) h.TakeDamage(damage);
        ApplyVisual();
        EnsureRearmOrder();
        Debug.Log($"[SpikeTrap] {name} 작동 — {victim.DisplayName}에게 {damage} 피해, 해제됨");
    }

    /// <summary>재장전 — 재장전 작업이 끝나면 부른다</summary>
    public void Rearm()
    {
        _armed = true;
        RearmWorkDone = 0f;
        _rearmWorkOrder = null;
        ApplyVisual();
    }

    private void ApplyVisual()
    {
        if (_renderer == null) return;
        _renderer.color = _armed ? _armedColor : _armedColor * disarmedTint;
    }

    #endregion

    #region 재장전 작업

    internal void EnsureRearmOrder()
    {
        var work = WorkSystemManager.instance;
        if (_armed || !isActiveAndEnabled || work == null || work.HasWorkOrder(_rearmWorkOrder)) return;
        _rearmWorkOrder = work.CreateWorkOrder($"함정 재장전: {name}", WorkType.Building, 0, 4);
        _rearmWorkOrder.AddTarget(new TrapRearmOrder(this));
    }

    internal void ChangeRearmWork(float amount)
    {
        if (!_armed) RearmWorkDone = Mathf.Clamp(RearmWorkDone + amount, 0f, RearmWork);
    }

    private void RemoveRearmOrder()
    {
        if (_rearmWorkOrder == null || WorkSystemManager.instance == null) return;
        WorkSystemManager.instance.RemoveWorkOrder(_rearmWorkOrder, isCancellation: true);
        _rearmWorkOrder = null;
    }

    #endregion

    #region IBuildingFunction

    public void OnBuildingDisabled() { }
    public void OnBuildingEnabled() { }

    #endregion

    #region 세이브 — 장전/해제 상태와 재장전 작업량

    [System.Serializable]
    private class SaveState
    {
        public bool armed = true;
        public float rearmWorkDone;
    }

    public string SerializeExtra() => JsonUtility.ToJson(new SaveState { armed = _armed, rearmWorkDone = RearmWorkDone });

    public void DeserializeExtra(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var s = JsonUtility.FromJson<SaveState>(json);
        if (s == null) return;
        _armed = s.armed;
        RearmWorkDone = _armed ? 0f : Mathf.Clamp(s.rearmWorkDone, 0f, RearmWork);
        ApplyVisual();
    }

    #endregion
}

/// <summary>
/// 가시 함정 재장전 — 건설 일. 재료(나무 1)가 창고에 있을 때만 할 수 있고, 끝나면 창고에서 빼고 함정을 다시 장전한다.
/// 누적 작업량은 함정에 저장한다.
/// </summary>
public class TrapRearmOrder : IWorkTarget, IProgressiveWork, IExpiringWorkTarget
{
    private readonly SpikeTrap trap;
    private bool completed;

    public TrapRearmOrder(SpikeTrap trap) { this.trap = trap; }

    public bool IsExpired => trap == null || completed || trap.IsArmed;

    public Vector3 GetWorkPosition() => trap != null ? trap.transform.position + new Vector3(0.5f, 0f, 0f) : Vector3.zero;
    public WorkType GetWorkType() => WorkType.Building;
    public float GetWorkTime() => trap != null ? trap.RearmWork : 1f;

    public bool IsWorkAvailable()
    {
        if (trap == null || completed || trap.IsArmed) return false;
        if (trap.RearmItem == null || trap.RearmAmount <= 0) return true;
        return InventoryManager.instance != null && InventoryManager.instance.GetAvailableAmount(trap.RearmItem) >= trap.RearmAmount;
    }

    public void CompleteWork(Employee worker)
    {
        if (IsExpired) return;
        if (trap.RearmItem != null && trap.RearmAmount > 0)
        {
            if (InventoryManager.instance == null || !InventoryManager.instance.RemoveItem(trap.RearmItem, trap.RearmAmount)) return; // 재료가 사라졌으면 다음에
        }
        completed = true;
        trap.Rearm();
    }

    public void CancelWork(Employee worker) { }

    public float GetWorkAmount() => trap != null ? trap.RearmWork : 1f;
    public float GetAccumulatedWork() => trap != null ? trap.RearmWorkDone : 0f;
    public void AddWork(float amount) { if (amount > 0f && !IsExpired) trap.ChangeRearmWork(amount); }
    public void ReduceWork(float amount) { if (amount > 0f && !IsExpired) trap.ChangeRearmWork(-amount); }
}
