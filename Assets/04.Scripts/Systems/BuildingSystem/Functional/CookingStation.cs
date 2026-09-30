using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스토브 — 생산 계획대로 요리합니다 (나무 스토브 = 나무 연료, 전기 스토브 = 전력·더 빠름).
///
/// 흐름 (한 번에 요리 1개):
///   1. 목록 맨 위부터 더 만들어야 하는 계획을 찾고, 재료 필터에 맞는 재료 N개를 골라 예약
///   2. <b>조리 작업</b>(WorkType.Cooking)으로 재료 운반 — 조리사가 직접 창고에서 가져옴 (WithdrawOrder)
///   3. 모두 도착하면 조리 작업(<see cref="CookOrder"/>) — 끝나면 연료 1개를 쓰고 요리를 스토브 옆에 떨굼
///
/// 요리 영양은 넣은 재료 합 × 배율 → <see cref="InventoryManager.AddMealNutrition"/>.
/// 작업물은 세이브하지 않고(WorkSystemManager가 Cooking을 건너뜀) 이 건물이 상태를 저장했다가 다시 만듭니다.
/// </summary>
[RequireComponent(typeof(Building))]
public class CookingStation : MonoBehaviour, IPlanHost, IMaterialReceiver, IBuildingExtraSerializable, IBuildingFunction
{
    private const float CHECK_INTERVAL = 1f;
    private const int PRIORITY = 3;

    [Header("조리")]
    [SerializeField] private List<CookingRecipe> recipes = new List<CookingRecipe>();

    [Tooltip("조리 속도 배율 (전기 스토브 > 1)")]
    [SerializeField, Min(0.1f)] private float speedMultiplier = 1f;

    [Tooltip("직원이 재료를 건네고 조리하는 위치 (건물 좌측 하단 기준)")]
    [SerializeField] private Vector2 workOffset = new Vector2(0.5f, 0f);

    [Header("연료 (나무 스토브)")]
    [SerializeField] private bool usesFuel = true;
    [SerializeField] private ItemSupply fuelSupply = new ItemSupply();
    [Tooltip("연료 1개로 조리할 수 있는 시간(초) — 조리 작업 중에만 탐")]
    [SerializeField, Min(1f)] private float burnSecondsPerFuel = 120f;

    /// <summary>불에 넣은 연료가 남은 시간(초)</summary>
    private float burnRemaining;

    [SerializeField, HideInInspector] private List<ProductionPlan> plans = new List<ProductionPlan>();

    private Building building;
    private PowerConsumer power;
    private float nextCheck;

    // ── 진행 중인 요리 ──
    private CookingRecipe jobRecipe;
    private ProductionPlan jobPlan;
    private readonly Dictionary<ItemData, int> pending = new Dictionary<ItemData, int>();
    private readonly Dictionary<ItemData, int> delivered = new Dictionary<ItemData, int>();
    private int reservationId = -1;
    private WorkOrder haulOrder;
    private WorkOrder cookOrder;
    /// <summary>로드 직후 — 작업물을 다음 Update에서 다시 만든다 (Restore 순서상 먼저 만들면 WorkSystemManager가 지움)</summary>
    private bool rebuildOrders;

    public string HostName => building != null && building.buildingData != null ? building.buildingData.buildingName : name;
    public List<ProductionPlan> Plans => plans;
    public IReadOnlyList<IPlanRecipe> Recipes => recipes;
    public ItemSupply FuelSupply => usesFuel ? fuelSupply : null;

    public Vector3 WorkPosition => transform.position + (Vector3)workOffset;

    /// <summary>조리 작업량 — 직원 조리 속도는 작업 루프가 따로 곱함</summary>
    public float CookWorkTime => (jobRecipe != null ? jobRecipe.cookTime : 1f) / speedMultiplier;

    private bool IsRunning => (building == null || building.IsFunctional) && (power == null || power.IsPowered);
    private bool HasFuel => !usesFuel || burnRemaining > 0f || fuelSupply.Stock > 0;

    /// <summary>조리사가 붙어 조리 중인지 — 연료는 이때만 탄다</summary>
    private bool IsCooking => cookOrder != null && cookOrder.assignedWorkers.Count > 0;

    /// <summary>재료가 다 왔고 연료·전력이 있어 지금 조리할 수 있는지</summary>
    public bool CanCookNow => jobRecipe != null && pending.Count == 0 && IsRunning && HasFuel;

    private bool HasJob => jobRecipe != null;

    private void Awake()
    {
        building = GetComponent<Building>();
        power = GetComponent<PowerConsumer>();
        fuelSupply.Bind(this);
    }

    private void Update()
    {
        if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;
        if (usesFuel)
        {
            fuelSupply.Tick();
            if (IsCooking)
            {
                if (burnRemaining <= 0f && fuelSupply.TryConsume(1)) burnRemaining += burnSecondsPerFuel;
                burnRemaining = Mathf.Max(0f, burnRemaining - Time.deltaTime);
            }
        }

        if (rebuildOrders) { rebuildOrders = false; RebuildOrders(); }

        if (Time.time < nextCheck) return;
        nextCheck = Time.time + CHECK_INTERVAL;
        if (!HasJob && IsRunning && HasFuel) TryStartJob();
    }

    #region 요리 시작

    private void TryStartJob()
    {
        if (InventoryManager.instance == null || WorkSystemManager.instance == null) return;

        foreach (var plan in plans)
        {
            var recipe = GetRecipe(plan);
            if (recipe == null || !plan.WantsMore(recipe)) continue;

            var picked = PickIngredients(recipe.ingredientCount, plan.filter);
            if (picked == null) continue; // 재료 부족 — 다음 계획

            var costs = new List<ResourceCost>();
            foreach (var kv in picked) costs.Add(new ResourceCost { item = kv.Key, amount = kv.Value });
            int id = InventoryManager.instance.TryReserve(costs);
            if (id < 0) continue;

            jobRecipe = recipe;
            jobPlan = plan;
            reservationId = id;
            pending.Clear();
            delivered.Clear();
            foreach (var kv in picked) pending[kv.Key] = kv.Value;
            CreateHaulOrder();
            return;
        }
    }

    private CookingRecipe GetRecipe(ProductionPlan plan)
        => plan != null && plan.recipeIndex >= 0 && plan.recipeIndex < recipes.Count ? recipes[plan.recipeIndex] : null;

    /// <summary>
    /// 필터에 맞는 재료를 count개 고릅니다 (모자라면 null).
    /// 침식 없는 재료 먼저(침식 요리 피함) → 날로 먹기 나쁜 재료 먼저(베리는 날로 먹게 남김) → 많은 것부터.
    /// </summary>
    private static Dictionary<ItemData, int> PickIngredients(int count, IngredientFilter filter)
    {
        var inv = InventoryManager.instance;
        var candidates = new List<(ItemData item, int available)>();
        foreach (var item in IngredientFilter.AllIngredients())
        {
            if (!filter.Allows(item)) continue;
            int available = inv.GetWorldAvailable(item);
            if (available > 0) candidates.Add((item, available));
        }

        candidates.Sort((a, b) =>
        {
            int c = a.item.IsErosionFood.CompareTo(b.item.IsErosionFood);
            if (c != 0) return c;
            c = b.item.rawPenalty.CompareTo(a.item.rawPenalty);
            return c != 0 ? c : b.available.CompareTo(a.available);
        });

        var picked = new Dictionary<ItemData, int>();
        int left = count;
        foreach (var (item, available) in candidates)
        {
            int take = Mathf.Min(left, available);
            picked[item] = take;
            left -= take;
            if (left == 0) return picked;
        }
        return null;
    }

    private void CreateHaulOrder()
    {
        if (WorkSystemManager.instance == null || pending.Count == 0) return;
        haulOrder = WorkSystemManager.instance.CreateWorkOrder($"조리 재료: {jobRecipe.recipeName}", WorkType.Cooking, 0, PRIORITY);
        foreach (var kv in pending)
            haulOrder.AddTarget(new WithdrawOrder(new MaterialRequest(kv.Key, kv.Value, this, reservationId)));
    }

    private void CreateCookOrder()
    {
        if (WorkSystemManager.instance == null) return;
        cookOrder = WorkSystemManager.instance.CreateWorkOrder($"조리: {jobRecipe.recipeName}", WorkType.Cooking, 0, PRIORITY);
        cookOrder.AddTarget(new CookOrder { station = this });
    }

    private void RebuildOrders()
    {
        if (!HasJob) return;
        if (pending.Count > 0) CreateHaulOrder();
        else CreateCookOrder();
    }

    #endregion

    #region 조리 완료·취소

    /// <summary>조리 작업 완료 — 연료를 쓰고 요리를 스토브 옆에 떨굽니다.</summary>
    public void CompleteCooking()
    {
        if (!HasJob) return;

        float nutrition = 0f;
        bool erosion = false;
        foreach (var kv in delivered)
        {
            nutrition += kv.Key.nutrition * kv.Value;
            erosion |= kv.Key.IsErosionFood;
        }
        nutrition *= jobRecipe.nutritionMultiplier;

        var meal = erosion && jobRecipe.erosionOutput != null ? jobRecipe.erosionOutput : jobRecipe.output;
        if (meal != null)
        {
            if (InventoryManager.instance != null) InventoryManager.instance.AddMealNutrition(meal, nutrition);
            ItemRefundHelper.SpawnRefunds(new Dictionary<ItemData, int> { { meal, 1 } }, WorkPosition, Vector2Int.one);
        }

        if (jobPlan != null) jobPlan.done++;
        cookOrder = null; // 완료 처리 중인 작업물 — 큐가 비면 WorkSystemManager가 스스로 정리
        ClearJob();
    }

    /// <summary>진행 중인 요리를 취소합니다. 이미 온 재료는 바닥에 돌려줍니다.</summary>
    public void CancelJob()
    {
        if (!HasJob) return;
        RemoveOrder(ref haulOrder);
        RemoveOrder(ref cookOrder);
        if (delivered.Count > 0) ItemRefundHelper.SpawnRefunds(new Dictionary<ItemData, int>(delivered), WorkPosition, Vector2Int.one);
        ReleaseReservation();
        ClearJob();
    }

    private void ClearJob()
    {
        jobRecipe = null;
        jobPlan = null;
        pending.Clear();
        delivered.Clear();
        ReleaseReservation();
    }

    private void ReleaseReservation()
    {
        if (reservationId >= 0 && InventoryManager.instance != null)
            InventoryManager.instance.CancelReservation(reservationId);
        reservationId = -1;
    }

    private static void RemoveOrder(ref WorkOrder order)
    {
        if (order != null && WorkSystemManager.instance != null)
            WorkSystemManager.instance.RemoveWorkOrder(order, isCancellation: true);
        order = null;
    }

    private void OnDestroy()
    {
        if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;
        CancelJob();
        fuelSupply.Cancel();
    }

    #endregion

    #region 상태 문구

    public string DescribeStatus()
    {
        string fuel = usesFuel && fuelSupply.item != null
            ? $"불 {Mathf.CeilToInt(burnRemaining)}초 · {fuelSupply.item.itemName} {fuelSupply.Stock}/{fuelSupply.capacity} · " : "";
        if (building != null && !building.IsFunctional) return fuel + "건물 작동 안 함";
        if (power != null && !power.IsPowered) return fuel + "전력 없음 — 멈춤";
        if (!HasFuel) return fuel + "연료 없음 — 보급 대기";
        if (!HasJob) return fuel + (plans.Count == 0 ? "계획 없음" : "대기 — 할 계획이 없거나 재료 부족");
        if (pending.Count > 0)
        {
            int left = 0;
            foreach (var kv in pending) left += kv.Value;
            return fuel + $"{jobRecipe.recipeName}: 재료 운반 중 (남은 재료 {left})";
        }
        return fuel + $"{jobRecipe.recipeName}: 조리 대기/조리 중";
    }

    #endregion

    #region 클릭

    private void OnMouseDown()
    {
        if (UIManager.PointerOverUI || UIManager.instance == null) return;
        if (InteractionManager.instance != null &&
            InteractionManager.instance.GetCurrentMode() != InteractionManager.InteractMode.Normal) return;

        ProductionPlanUI.Open(this);
    }

    #endregion

    #region IMaterialReceiver

    public Vector3 GetDeliveryPosition() => WorkPosition;

    public bool IsRequestStillValid() => this != null && HasJob && pending.Count > 0;

    public void OnMaterialDelivered(ItemData itemData, int amount)
    {
        if (!HasJob || itemData == null) return;

        delivered.TryGetValue(itemData, out int d);
        delivered[itemData] = d + amount;
        if (pending.TryGetValue(itemData, out int p))
        {
            if (p - amount <= 0) pending.Remove(itemData);
            else pending[itemData] = p - amount;
        }
        if (pending.Count > 0) return;

        // 모두 도착 — 재료는 출고 시점에 이미 창고에서 빠졌으므로 예약 잠금만 푼다 (CraftingTable과 같은 규칙)
        ReleaseReservation();
        if (haulOrder != null && WorkSystemManager.instance != null)
            WorkSystemManager.instance.RemoveWorkOrder(haulOrder, isCancellation: false);
        haulOrder = null;
        CreateCookOrder();
    }

    public void OnMaterialRequestFailed(ItemData itemData, int amount)
    {
        // 일시적 실패일 수 있다 — 작업은 큐에 남아 다른 조리사가 재시도
    }

    #endregion

    #region IBuildingFunction

    public void OnBuildingDisabled() { }
    public void OnBuildingEnabled() { }
    public bool IsOperating => HasJob && IsRunning;

    #endregion

    #region 세이브

    [System.Serializable]
    private class SaveState
    {
        public List<ProductionPlan> plans = new List<ProductionPlan>();
        public ItemSupply.SaveState fuel;
        public float burnRemaining;
        public int jobRecipe = -1;
        public int jobPlan = -1;
        public int reservationId = -1;
        public List<ItemStackSaveData> pending = new List<ItemStackSaveData>();
        public List<ItemStackSaveData> delivered = new List<ItemStackSaveData>();
    }

    public string SerializeExtra()
    {
        var s = new SaveState
        {
            plans = plans,
            fuel = fuelSupply.Capture(),
            burnRemaining = burnRemaining,
            jobRecipe = jobRecipe != null ? recipes.IndexOf(jobRecipe) : -1,
            jobPlan = jobPlan != null ? plans.IndexOf(jobPlan) : -1,
            reservationId = reservationId,
        };
        foreach (var kv in pending) s.pending.Add(new ItemStackSaveData { itemId = kv.Key.itemID, amount = kv.Value });
        foreach (var kv in delivered) s.delivered.Add(new ItemStackSaveData { itemId = kv.Key.itemID, amount = kv.Value });
        return JsonUtility.ToJson(s);
    }

    public void DeserializeExtra(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var s = JsonUtility.FromJson<SaveState>(json);
        if (s == null) return;

        plans = s.plans ?? new List<ProductionPlan>();
        fuelSupply.Restore(s.fuel);
        burnRemaining = s.burnRemaining;

        if (s.jobRecipe < 0 || s.jobRecipe >= recipes.Count || GameDatabase.Instance == null) return;
        jobRecipe = recipes[s.jobRecipe];
        jobPlan = s.jobPlan >= 0 && s.jobPlan < plans.Count ? plans[s.jobPlan] : null;
        reservationId = s.reservationId; // 인벤토리가 예약을 ID째 복원한다
        Fill(pending, s.pending);
        Fill(delivered, s.delivered);
        rebuildOrders = true;
    }

    private static void Fill(Dictionary<ItemData, int> dict, List<ItemStackSaveData> list)
    {
        dict.Clear();
        if (list == null) return;
        foreach (var st in list)
        {
            var item = GameDatabase.Instance.GetItemData(st.itemId);
            if (item != null && st.amount > 0) dict[item] = st.amount;
        }
    }

    #endregion
}
