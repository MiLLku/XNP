using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>식생물의 부류.</summary>
public enum PlantLifecycle
{
    /// <summary>1회성 — 수확하면 사라집니다 (침식 식물).</summary>
    SingleUse = 0,
    /// <summary>뿌리 재성장 — 수확해도 뿌리가 남아 다시 자랍니다. 제초하면 묘목이 나옵니다 (나무·베리 덤불).</summary>
    Regrow = 1,
}

/// <summary>
/// 식물을 품은 건물 칸(밭·수경재배기). 설정되면 식물의 저장·환경 판정 일부를 넘겨받습니다.
/// </summary>
public interface IPlantHost
{
    /// <summary>true면 방 환경(성장 조건·적정 온도) 대신 <see cref="CanGrow"/>로만 판정 (수경재배기)</summary>
    bool OverridesEnvironment { get; }

    /// <summary>OverridesEnvironment일 때의 성장 가능 판정</summary>
    bool CanGrow(List<string> failures);

    /// <summary>식물이 제거되기 직전 (수확·제초)</summary>
    void OnPlantRemoved(PlantBase plant);
}

/// <summary>
/// 모든 식생물의 공통 부모.
///
/// <b>상태는 성장도(0~1) 하나뿐입니다.</b> 단계·스프라이트·크기는 성장도에서 파생되고,
/// "수확 후 재성장"도 성장도를 <see cref="regrowFrom"/>으로 되돌리는 것으로 표현합니다.
/// 그래서 세이브는 성장도 float 하나로 충분합니다 (MapEntitySaveData.remainingResource).
///
/// <b>명령</b> — 모두 제초 작업(WorkType.Weeding)으로 처리됩니다.
///   수확·벌목: <see cref="Harvest"/> — 수확물 드롭, 1회성은 사라지고 재성장은 뿌리가 남음.
///   제초: <see cref="Weed"/> — 뿌리까지 제거. 다 자랐으면 수확물도, 묘목이 있으면 묘목도 드롭.
///
/// <b>성장 정지</b> — 개체 정의의 성장 조건(<see cref="EntityDefinition.growthConditions"/>)과
/// 하위 클래스의 추가 조건(나무 공간 등)을 만족하지 않으면 성장이 멈춥니다. 시들지는 않습니다.
/// </summary>
public abstract class PlantBase : MonoBehaviour, IHarvestable
{
    /// <summary>수확물 한 종류.</summary>
    [System.Serializable]
    public class PlantYield
    {
        public ItemData item;
        [Min(0)] public int amount = 1;
        [Range(0f, 1f)] public float chance = 1f;
    }

    /// <summary>성장 조건 재검사 주기(초). 매 프레임 조건을 읽지 않기 위함.</summary>
    private const float CHECK_INTERVAL = 1f;

    #region 설정

    [Header("식생물 — 부류")]
    [SerializeField] protected PlantLifecycle lifecycle = PlantLifecycle.Regrow;

    [Header("식생물 — 성장")]
    [Tooltip("성장도 0에서 다 자랄 때까지 걸리는 시간(초). 0이면 생성 즉시 성체.")]
    [SerializeField] protected float growthTime = 60f;

    [Tooltip("수확 후 되돌아갈 성장도(0~1). 재성장 부류만 사용합니다.")]
    [SerializeField, Range(0f, 1f)] protected float regrowFrom = 0f;

    [Header("식생물 — 작업 시간(초)")]
    [FormerlySerializedAs("chopTime")]
    [SerializeField] protected float harvestTime = 2f;

    [FormerlySerializedAs("removalTime")]
    [SerializeField] protected float weedTime = 3f;

    [Header("식생물 — 드롭")]
    [Tooltip("수확(벌목) 시 떨어뜨릴 수확물. 비어 있으면 수확 명령 대상이 아닙니다 (제초만 가능).")]
    [SerializeField] protected List<PlantYield> yields = new List<PlantYield>();

    [Tooltip("제초 시 떨어뜨릴 묘목. 재성장 부류에 지정합니다.")]
    [SerializeField] protected ItemData saplingItem;

    [SerializeField, Min(0)] protected int saplingAmount = 1;

    #endregion

    #region 상태

    /// <summary>성장도 0~1. 맵 생성으로 놓인 자연 식생물은 다 자란 상태로 시작합니다.</summary>
    private float growth = 1f;

    private float checkTimer;
    private float growthRate = 1f;
    private EntityDefinition cachedDefinition;

    /// <summary>품고 있는 건물 칸 (야생이면 null)</summary>
    public IPlantHost Host { get; set; }

    /// <summary>현재 성장 속도 배율 (적정 온도 반영, 마지막 검사 결과)</summary>
    public float GrowthRate => IsGrowthBlocked ? 0f : growthRate;

    /// <summary>성장도 (0~1)</summary>
    public float Growth => growth;

    /// <summary>다 자랐는지</summary>
    public bool IsMature => growth >= 1f;

    /// <summary>성장 조건 불만족으로 멈춰 있는지 (마지막 검사 결과)</summary>
    public bool IsGrowthBlocked { get; private set; }

    public PlantLifecycle Lifecycle => lifecycle;

    /// <summary>제초 시 묘목 아이템 (없으면 null)</summary>
    public ItemData SaplingItem => saplingItem;

    /// <summary>수확 명령 대상인지 (수확물이 정의되어 있는지)</summary>
    public bool HasYield => yields != null && yields.Count > 0;

    public float WeedTime => weedTime;

    /// <summary>식물이 서는 칸 (스프라이트 피벗이 좌하단)</summary>
    public Vector2Int Cell => Vector2Int.FloorToInt(transform.position);

    /// <summary>MapEntityIdentity 없이 만든 개체(건물 칸의 작물)에 정의를 직접 지정합니다.</summary>
    public void SetDefinition(EntityDefinition def) => cachedDefinition = def;

    /// <summary>이 개체의 정의. MapEntityIdentity는 Instantiate 직후 붙으므로 지연 조회합니다.</summary>
    public EntityDefinition Definition
    {
        get
        {
            if (cachedDefinition == null)
            {
                var identity = GetComponentInParent<MapEntityIdentity>();
                if (identity != null) cachedDefinition = identity.Definition;
            }
            return cachedDefinition;
        }
    }

    #endregion

    #region 생명주기

    protected virtual void Awake()
    {
        // 수확 드래그 선택(Physics2D.OverlapBox)에 잡히도록 콜라이더 보장
        if (GetComponent<Collider2D>() == null)
            gameObject.AddComponent<BoxCollider2D>();
    }

    protected virtual void Start()
    {
        OnGrowthChanged();
    }

    protected virtual void Update()
    {
        if (IsMature) return;

        checkTimer -= Time.deltaTime;
        if (checkTimer <= 0f)
        {
            checkTimer = CHECK_INTERVAL;
            IsGrowthBlocked = !CanGrowNow();
            growthRate = HostOverrides ? 1f : PlantConditionChecker.TemperatureRate(Definition, Cell);
        }
        if (IsGrowthBlocked) return;

        SetGrowth(growthTime > 0f ? growth + Time.deltaTime * growthRate / growthTime : 1f);
    }

    #endregion

    #region 성장

    /// <summary>
    /// 지금 자랄 수 있는지 판정합니다. failures가 주어지면 멈춘 사유를 채웁니다.
    /// 비트 AND(&amp;)로 두 판정을 모두 실행해 사유를 빠짐없이 모읍니다.
    /// </summary>
    public bool CanGrowNow(List<string> failures = null)
    {
        if (HostOverrides) return Host.CanGrow(failures) & CheckExtraGrowth(failures);

        var def = Definition;
        bool env = PlantConditionChecker.Check(def != null ? def.growthConditions : null, Cell, failures);
        return env & CheckExtraGrowth(failures);
    }

    private bool HostOverrides => Host != null && Host.OverridesEnvironment;

    /// <summary>하위 클래스의 추가 성장 조건 (나무의 공간 등).</summary>
    protected virtual bool CheckExtraGrowth(List<string> failures) => true;

    /// <summary>아이템 설명용 공간 요구 문구 (프리팹에서도 호출되므로 Awake 상태에 기대지 말 것). 없으면 null.</summary>
    public virtual string DescribeSpaceNeed() => null;

    /// <summary>
    /// 성장도 0일 때의 모습 — 파종 고스트·예정지 표시용. 프리팹에서도 호출됩니다.
    /// 기본은 현재 스프라이트, 배율 1.
    /// </summary>
    public virtual void GetPreview(out Sprite sprite, out float scale)
    {
        sprite = TryGetComponent(out SpriteRenderer sr) ? sr.sprite : null;
        scale = 1f;
    }

    /// <summary>성장도를 설정합니다 (세이브 복원·자연 생성·파종·재성장).</summary>
    public void SetGrowth(float value)
    {
        float v = Mathf.Clamp01(value);
        if (Mathf.Approximately(v, growth)) { growth = v; return; }
        growth = v;
        OnGrowthChanged();
    }

    /// <summary>성장도가 바뀌면 호출됩니다 — 스프라이트·크기 갱신.</summary>
    protected abstract void OnGrowthChanged();

    #endregion

    #region 명령 — 수확·벌목

    public virtual bool CanHarvest() => IsMature && HasYield;

    public void Harvest()
    {
        if (!CanHarvest()) return;

        DropYields();

        if (lifecycle == PlantLifecycle.SingleUse) Remove();
        else SetGrowth(regrowFrom);
    }

    public float GetHarvestTime() => harvestTime;

    /// <summary>수확·벌목·제초 모두 제초 작업입니다.</summary>
    public WorkType GetHarvestType() => WorkType.Weeding;

    #endregion

    #region 명령 — 제초

    /// <summary>뿌리까지 제거합니다. 다 자랐으면 수확물을, 묘목이 있으면 묘목을 함께 떨어뜨립니다.</summary>
    public void Weed()
    {
        if (IsMature) DropYields();
        if (saplingItem != null) Drop(saplingItem, saplingAmount);
        Remove();
    }

    /// <summary>개체를 제거합니다. 제거 전에 정리할 것이 있으면 재정의합니다.</summary>
    protected virtual void Remove()
    {
        Host?.OnPlantRemoved(this);
        Destroy(gameObject);
    }

    #endregion

    #region 드롭

    private void DropYields()
    {
        if (yields == null) return;
        foreach (var y in yields)
        {
            if (y.item == null || y.amount <= 0) continue;
            if (Random.value <= y.chance) Drop(y.item, y.amount);
        }
    }

    /// <summary>
    /// 아이템을 식물 발치에 떨어뜨립니다 — 직원이 창고로 운반 (Haul 루프).
    /// DroppedItemManager가 없으면 인벤토리로 직접 반납 (폴백).
    /// </summary>
    protected void Drop(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return;

        if (DroppedItemManager.instance == null)
        {
            InventoryManager.instance?.AddItem(item, amount);
            return;
        }

        // 피벗이 좌하단이라 렌더러 중심 X를 기준으로 흩뿌린다
        var sr = GetComponent<SpriteRenderer>();
        float cx = sr != null ? sr.bounds.center.x : transform.position.x + 0.5f;

        for (int i = 0; i < amount; i++)
        {
            var pos = new Vector3(cx + Random.Range(-0.5f, 0.5f), transform.position.y + 0.25f, 0f);
            DroppedItemManager.instance.SpawnItem(item, 1, pos);
        }
    }

    #endregion
}
