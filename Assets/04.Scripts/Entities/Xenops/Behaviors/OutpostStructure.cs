using System.Collections.Generic;
using UnityEngine;

/// <summary>거점 습격 구조물 종류</summary>
public enum OutpostKind
{
    /// <summary>침식 송신탑 — 반경 안의 칸 침식·방 침식을 서서히 올림</summary>
    ErosionTower = 0,

    /// <summary>공포 토템 — 반경 안 직원 무드 하락 + 정신 이상 저항 하락</summary>
    FearTotem = 1,
}

/// <summary>
/// 거점 습격 구조물 — "움직이지 않는 적 개체"(Hostile Xenops)라 직원의 기존 전투가 그대로 공격 대상으로 잡는다.
///
/// 흐름: 건설자(경비 중 1마리)가 BUILD_REACH 안에 붙어 있는 동안 buildTime에 걸쳐 지어진다(진행 막대).
///       건설 중 건설자가 죽으면 구조물은 생기지 않고 사라진다(드롭 없음). 정화 수류탄은 진행도를 0으로 되돌린다.
///       다 지어지면 종류별 효과를 낸다. 부서지면 XenopsData의 처치 드롭(침식 결정체)이 떨어진다.
/// 정화 등(<see cref="PurifyLamp"/>) 반경 안이면 건설 속도·효과가 절반.
/// </summary>
[RequireComponent(typeof(Xenops))]
public class OutpostStructure : MonoBehaviour, IXenopsBehavior, IEntityErosionSource
{
    // shortcut: 임시 수치 — 밸런스 확정 시 데이터로 뺄 것
    private const float BUILD_REACH = 3f;
    private const float TOWER_RADIUS = 15f;
    private const float TOWER_RATE = 0.5f;     // 초당 침식 상승
    private const float TOWER_MAX = 30f;
    private const float TOTEM_RADIUS = 20f;
    private const float TOTEM_MOOD = -10f;
    private const float TOTEM_RESIST_MULT = 0.8f; // 정신 이상 저항 ×0.8 = 임계점이 올라 더 잘 무너진다
    private const float EFFECT_TICK = 1f;

    [SerializeField] private OutpostKind kind = OutpostKind.ErosionTower;
    [SerializeField] private float buildTime = 30f;

    private static readonly List<OutpostStructure> active = new List<OutpostStructure>();

    private Xenops _xenops;
    private float _effectTimer;
    private float _builtSeconds;
    private bool _registered;
    private Transform _bar;

    /// <summary>건설 진행도 0~1 (1 = 완성)</summary>
    public float Progress { get; private set; }
    public bool IsBuilt => Progress >= 1f;
    public OutpostKind Kind => kind;

    /// <summary>짓는 개체 — 건설 중 죽으면 구조물이 사라진다. 불러온 뒤에는 근처 습격 적 중에서 다시 고른다</summary>
    public Xenops Builder { get; set; }

    public static string KindLabel(OutpostKind k) => k == OutpostKind.FearTotem ? "공포 토템" : "침식 송신탑";

    public XenopsType BehaviorType => XenopsType.Hostile;
    public bool IsActive { get; private set; }
    public void OnActivated() => IsActive = true;
    public void OnDeactivated() => IsActive = false;
    public void UpdateBehavior() { }

    // Xenops가 스폰 때 그림을 XenopsData.icon으로, 색을 흰색으로 덮는다 — 구조물은 프리팹 그림·색을 지킨다
    private SpriteRenderer _renderer;
    private Sprite _ownSprite;
    private Color _ownColor;

    private void Awake()
    {
        _xenops = GetComponent<Xenops>();
        _renderer = GetComponent<SpriteRenderer>();
        if (_renderer != null) { _ownSprite = _renderer.sprite; _ownColor = _renderer.color; }
        BuildProgressBar();
        active.Add(this);
    }

    private void OnDestroy()
    {
        active.Remove(this);
        if (_registered) EntityErosionField.instance?.UnregisterSource(this);
        ClearTotemMood();
    }

    /// <summary>불러온 진행도 (구조물이 아닌 세이브 값 -1은 무시)</summary>
    public void RestoreProgress(float p) { if (p >= 0f) Progress = Mathf.Clamp01(p); }

    /// <summary>정화 수류탄 — 체력 50% 피해, 건설 중이면 진행도 0</summary>
    public void HitByPurifyGrenade()
    {
        var h = GetComponent<XenopsHealth>();
        if (!IsBuilt) Progress = 0f;
        if (h != null) h.TakeDamage(h.MaxHealth * 0.5f, 1f);
        Debug.Log($"[Outpost] 정화 수류탄 명중 — {KindLabel(kind)} 진행도 {Progress:P0}");
    }

    private void Update()
    {
        // 불러오는 중엔 습격 정보(건설자 후보)가 아직 복원 전이다 — 건설자 없음으로 무너뜨리지 않게
        if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;

        if (_renderer != null) { _renderer.sprite = _ownSprite; _renderer.color = IsBuilt ? _ownColor : _ownColor * new Color(1f, 1f, 1f, 0.5f); }
        bool suppressed = PurifyLamp.Suppresses(transform.position);
        UpdateBar();

        if (!IsBuilt)
        {
            if (Builder == null) Builder = FindBuilder();
            if (Builder == null) { Collapse(); return; }
            if (Vector2.Distance(Builder.transform.position, transform.position) <= BUILD_REACH)
                Progress = Mathf.Min(1f, Progress + Time.deltaTime / Mathf.Max(1f, buildTime) * (suppressed ? 0.5f : 1f));
            return;
        }

        _builtSeconds += Time.deltaTime;
        _effectTimer -= Time.deltaTime;
        if (_effectTimer > 0f) return;
        _effectTimer = EFFECT_TICK;

        float mult = suppressed ? 0.5f : 1f;
        if (kind == OutpostKind.ErosionTower) TickTower(mult);
        else TickTotem(mult);
    }

    /// <summary>건설자 없이 짓던 자리는 무너진다 — 드롭 없이 사라짐</summary>
    private void Collapse()
    {
        Debug.Log($"[Outpost] 건설자를 잃어 {KindLabel(kind)} 건설이 무너졌습니다");
        if (XenopsManager.instance != null) XenopsManager.instance.RemoveXenops(_xenops);
        else Destroy(gameObject);
    }

    /// <summary>근처(BUILD_REACH×2)의 살아 있는 다른 습격 적 — 불러온 뒤 건설자 다시 잇기</summary>
    private Xenops FindBuilder()
    {
        if (RaidManager.instance == null || XenopsManager.instance == null) return null;
        foreach (var x in XenopsManager.instance.GetXenopsByType(XenopsType.Hostile))
            if (x != null && x != _xenops && x.GetComponent<OutpostStructure>() == null && RaidManager.instance.IsRaider(x)
                && Vector2.Distance(x.transform.position, transform.position) <= BUILD_REACH * 2f)
                return x;
        return null;
    }

    #region 침식 송신탑

    private void TickTower(float mult)
    {
        if (!_registered && EntityErosionField.instance != null) { EntityErosionField.instance.RegisterSource(this); _registered = true; }
        towerMult = mult;

        // 반경 안 방의 침식도 서서히 올린다 (방 대표 칸 기준)
        var rooms = RoomManager.instance;
        if (rooms == null) return;
        foreach (var pair in rooms.Rooms)
        {
            var room = pair.Value;
            if (Vector2.Distance(new Vector2(room.AverageX, room.AverageY), transform.position) > TOWER_RADIUS) continue;
            if (room.Erosion < TOWER_MAX) room.Erosion = Mathf.Min(TOWER_MAX, room.Erosion + TOWER_RATE * mult * EFFECT_TICK);
        }
    }

    private float towerMult = 1f;

    public Vector2 EmitPosition => transform.position;
    public float EmitRadius => TOWER_RADIUS;
    public float FixedErosionAmount => Mathf.Min(TOWER_MAX, TOWER_RATE * _builtSeconds) * towerMult;
    public bool IsEmitting => isActiveAndEnabled && IsBuilt && kind == OutpostKind.ErosionTower && FixedErosionAmount > 0f;
    public string ErosionSourceKey => $"outpost_tower_{GetEntityId().GetHashCode()}";
    public string ErosionSourceName => "침식 송신탑";
    public bool HorizontalOnly => false;
    public bool Covers(Vector2 worldPosition) => Vector2.Distance(worldPosition, transform.position) <= TOWER_RADIUS;

    #endregion

    #region 공포 토템

    private string MoodKey => $"outpost_totem_{GetEntityId().GetHashCode()}";

    private void TickTotem(float mult)
    {
        if (EmployeeManager.instance == null) return;
        foreach (var e in EmployeeManager.instance.AllEmployees)
        {
            if (e == null || e.State == EmployeeState.Dead) continue;
            bool inRange = Vector2.Distance(e.transform.position, transform.position) <= TOTEM_RADIUS;
            e.SetConditionalMental(MoodKey, "공포 토템", TOTEM_MOOD * mult, inRange);
        }
    }

    private void ClearTotemMood()
    {
        if (kind != OutpostKind.FearTotem || EmployeeManager.instance == null) return;
        foreach (var e in EmployeeManager.instance.AllEmployees)
            if (e != null) e.SetConditionalMental(MoodKey, "공포 토템", 0f, false);
    }

    /// <summary>
    /// 이 자리의 정신 이상 저항 배율 — 다 지어진 공포 토템 반경 안이면 낮아진다(정화 등이 효과 절반).
    /// EmployeeMental.GetBreakResistance가 곱한다.
    /// </summary>
    public static float TotemResistFactorAt(Vector2 pos)
    {
        float f = 1f;
        foreach (var s in active)
        {
            if (s == null || !s.IsBuilt || s.kind != OutpostKind.FearTotem) continue;
            if (Vector2.Distance(pos, s.transform.position) > TOTEM_RADIUS) continue;
            float drop = 1f - TOTEM_RESIST_MULT;
            if (PurifyLamp.Suppresses(s.transform.position)) drop *= 0.5f;
            f = Mathf.Min(f, 1f - drop);
        }
        return f;
    }

    #endregion

    #region 진행 막대

    private void BuildProgressBar()
    {
        var tex = Texture2D.whiteTexture;
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0f, 0.5f), tex.width);
        var go = new GameObject("BuildBar", typeof(SpriteRenderer));
        go.transform.SetParent(transform, false);
        var sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = new Color(0.75f, 0.35f, 1f, 0.9f);
        sr.sortingOrder = 50;
        _bar = go.transform;
    }

    private void UpdateBar()
    {
        if (_bar == null) return;
        bool show = !IsBuilt;
        _bar.gameObject.SetActive(show);
        if (!show) return;
        // 부모 크기와 무관하게 월드 기준 폭 1.5칸 × 진행도
        Vector3 ls = transform.lossyScale;
        _bar.localPosition = new Vector3(-0.75f / Mathf.Max(0.01f, ls.x), 2.3f / Mathf.Max(0.01f, ls.y), 0f);
        _bar.localScale = new Vector3(1.5f * Mathf.Max(0.02f, Progress) / Mathf.Max(0.01f, ls.x), 0.15f / Mathf.Max(0.01f, ls.y), 1f);
    }

    #endregion
}
