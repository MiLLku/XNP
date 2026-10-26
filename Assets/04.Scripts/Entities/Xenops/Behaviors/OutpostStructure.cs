using System.Collections.Generic;
using UnityEngine;

/// <summary>거점 습격 구조물 종류</summary>
public enum OutpostKind
{
    /// <summary>침식 송신탑 — 서 있는 동안 맵 전체 칸 침식을 일정량 올림</summary>
    ErosionTower = 0,

    /// <summary>공포 토템 — 모든 직원 무드가 시간이 갈수록 내려감</summary>
    FearTotem = 1,
}

/// <summary>
/// 거점 습격 구조물 — "움직이지 않는 적 개체"(Hostile Xenops)라 직원의 기존 전투가 그대로 공격 대상으로 잡는다.
///
/// 흐름: 건설자(경비 중 1마리)가 BUILD_REACH 안에 붙어 있는 동안 buildTime에 걸쳐 지어진다(진행 막대).
///       건설 중 건설자가 죽으면 구조물은 생기지 않고 사라진다(드롭 없음). 정화 수류탄은 진행도를 0으로 되돌린다.
///       다 지어지면 <b>맵 전체</b>에 효과를 낸다(범위 계산 없음). 부서지면 효과가 바로 풀리고 처치 드롭(침식 결정체)이 떨어진다.
/// </summary>
[RequireComponent(typeof(Xenops))]
public class OutpostStructure : MonoBehaviour, IXenopsBehavior
{
    // shortcut: 임시 수치 — 밸런스 확정 시 데이터로 뺄 것
    private const float BUILD_REACH = 3f;
    private const float TOWER_EROSION = 10f;        // 송신탑 하나당 맵 전체 칸 침식 +
    private const float TOTEM_START_MOOD = -1f;
    private const float TOTEM_MAX_MOOD = -12f;
    private const float TOTEM_STEP_SECONDS = 250f;  // 이 시간마다 무드 -1 (0.25일)
    private const float EFFECT_TICK = 1f;

    [SerializeField] private OutpostKind kind = OutpostKind.ErosionTower;
    [SerializeField] private float buildTime = 30f;

    private static readonly List<OutpostStructure> active = new List<OutpostStructure>();

    private Xenops _xenops;
    private float _effectTimer;
    private Transform _bar;

    /// <summary>건설 진행도 0~1 (1 = 완성)</summary>
    public float Progress { get; private set; }
    public bool IsBuilt => Progress >= 1f;
    public OutpostKind Kind => kind;

    /// <summary>다 지어진 뒤 흐른 시간(초) — 토템 무드가 깊어지는 기준, 저장됨</summary>
    public float BuiltSeconds { get; private set; }

    /// <summary>짓는 개체 — 건설 중 죽으면 구조물이 사라진다. 불러온 뒤에는 근처 습격 적 중에서 다시 고른다</summary>
    public Xenops Builder { get; set; }

    public static string KindLabel(OutpostKind k) => k == OutpostKind.FearTotem ? "공포 토템" : "침식 송신탑";

    /// <summary>다 지어진 침식 송신탑들이 더하는 맵 전체 칸 침식 (TerrainErosionManager가 실효 침식 상한에도 더한다)</summary>
    public static float GlobalErosionBonus
    {
        get
        {
            float sum = 0f;
            foreach (var s in active) if (s != null && s.IsBuilt && s.kind == OutpostKind.ErosionTower) sum += TOWER_EROSION;
            return sum;
        }
    }

    public XenopsType BehaviorType => XenopsType.Hostile;
    public bool IsActive { get; private set; }
    public void OnActivated() => IsActive = true;
    public void OnDeactivated() => IsActive = false;
    public void UpdateBehavior() { }

    // Xenops가 스폰 때 그림을 XenopsData.icon으로, 색을 흰색으로 덮는다 — 구조물은 프리팹 그림·색을 지킨다
    private SpriteRenderer _renderer;
    private Sprite _ownSprite;
    private Color _ownColor;

    private string EffectKey => $"outpost_{(int)kind}_{(_xenops != null ? _xenops.InstanceId : 0)}";

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
        ClearEffects();
    }

    /// <summary>불러온 진행도·완성 후 경과 시간 (구조물이 아닌 세이브 값 -1은 무시)</summary>
    public void RestoreProgress(float p, float builtSeconds = 0f)
    {
        if (p < 0f) return;
        Progress = Mathf.Clamp01(p);
        BuiltSeconds = Mathf.Max(0f, builtSeconds);
    }

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
        UpdateBar();

        if (!IsBuilt)
        {
            if (Builder == null) Builder = FindBuilder();
            if (Builder == null) { Collapse(); return; }
            if (Vector2.Distance(Builder.transform.position, transform.position) <= BUILD_REACH)
                Progress = Mathf.Min(1f, Progress + Time.deltaTime / Mathf.Max(1f, buildTime));
            return;
        }

        BuiltSeconds += Time.deltaTime;
        _effectTimer -= Time.deltaTime;
        if (_effectTimer > 0f) return;
        _effectTimer = EFFECT_TICK;

        if (kind == OutpostKind.ErosionTower)
            TerrainErosionManager.instance?.SetOutdoorErosionModifier(EffectKey, "침식 송신탑", TOWER_EROSION);
        else
            TickTotem();
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

    /// <summary>공포 토템 무드 — 다 지어진 순간 -1, TOTEM_STEP_SECONDS마다 -1, 최대 TOTEM_MAX_MOOD</summary>
    public float TotemMood => Mathf.Max(TOTEM_MAX_MOOD, TOTEM_START_MOOD - Mathf.Floor(BuiltSeconds / TOTEM_STEP_SECONDS));

    private void TickTotem()
    {
        if (EmployeeManager.instance == null) return;
        float mood = TotemMood;
        foreach (var e in EmployeeManager.instance.AllEmployees)
            if (e != null && e.State != EmployeeState.Dead) e.SetConditionalMental(EffectKey, "공포 토템", mood, true);
    }

    /// <summary>부서지거나 사라지면 효과를 바로 푼다</summary>
    private void ClearEffects()
    {
        if (kind == OutpostKind.ErosionTower) TerrainErosionManager.instance?.RemoveOutdoorErosionModifier(EffectKey);
        else if (EmployeeManager.instance != null)
            foreach (var e in EmployeeManager.instance.AllEmployees)
                if (e != null) e.SetConditionalMental(EffectKey, "공포 토템", 0f, false);
    }

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
