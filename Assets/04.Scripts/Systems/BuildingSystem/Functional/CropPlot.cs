using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 재배 칸 — 밭과 수경재배기가 함께 씁니다. 칸마다 다른 작물을 고를 수 있습니다.
///
/// 한 칸의 순환:
///   작물 선택 → 씨앗 1개 예약·운반(<see cref="SowSite"/>) → <b>파종 작업</b> → 성장
///   → 다 자라면 <b>제초(수확) 작업</b> 자동 등록 → 수확물·씨앗(확률) 드롭 → 빈 칸 → 다시 파종
///
/// <b>밭</b>(hydroponic=false): 작물이 놓인 칸의 <b>방 환경</b>으로 자랍니다 — 성장 조건(침식 등)과 적정 온도.
/// <b>수경재배기</b>(hydroponic=true): 방 환경을 무시하고, 전력이 들어오는 동안 자랍니다.
///   심을 때마다 침식 결정체 1개를 씁니다 (<see cref="ItemSupply"/>가 자동 보급).
///
/// 작물은 MapEntityIdentity 없이 만들어 맵 개체 세이브에 끼지 않고, 이 건물이 저장합니다.
/// </summary>
[RequireComponent(typeof(Building))]
public class CropPlot : MonoBehaviour, IBuildingExtraSerializable, IBuildingFunction
{
    private const float TICK = 1f;

    [Header("재배 방식")]
    [Tooltip("켜면 수경재배기 — 방 환경 무시, 전력 + 침식 결정체로 재배")]
    [SerializeField] private bool hydroponic = false;

    [Header("칸")]
    [Tooltip("칸 수")]
    [SerializeField, Min(1)] private int slotCount = 1;

    [Tooltip("첫 칸의 작물 위치 (건물 좌하단 기준)")]
    [SerializeField] private Vector2 firstSlotOffset = new Vector2(0f, 1f);

    [Tooltip("칸 사이 간격")]
    [SerializeField] private Vector2 slotSpacing = new Vector2(1f, 0f);

    [Tooltip("직원이 서서 일하는 위치 — 작물 위치 기준 (설 수 있는 칸이어야 함). 밭: 작물 아래 흙 칸, 수경재배기: 트레이 위")]
    [SerializeField] private Vector2 workOffsetFromCrop = new Vector2(0.5f, -1f);

    [Tooltip("작물 표시 배율 (수경재배기는 트레이에 맞게 작게)")]
    [SerializeField, Range(0.2f, 1f)] private float cropScale = 1f;

    [Header("심을 수 있는 작물 (씨앗)")]
    [SerializeField] private List<ItemData> allowedSeeds = new List<ItemData>();

    [Header("수경재배기 — 침식 결정체")]
    [SerializeField] private ItemSupply crystalSupply = new ItemSupply();

    private readonly List<Slot> slots = new List<Slot>();
    private Building building;
    private PowerConsumer power;
    private float tickTimer;

    public bool IsHydroponic => hydroponic;

    /// <summary>표시 이름</summary>
    public string PlotName => building != null && building.buildingData != null ? building.buildingData.buildingName : name;
    public IReadOnlyList<Slot> Slots => slots;
    public IReadOnlyList<ItemData> AllowedSeeds => allowedSeeds;
    public ItemSupply CrystalSupply => crystalSupply;

    /// <summary>수경재배기 가동 여부 (기반·전력)</summary>
    public bool IsPowered => (building == null || building.IsFunctional) && (power == null || power.IsPowered);

    #region 칸

    /// <summary>재배 칸 하나 — 식물의 호스트이자 파종 예정지의 주인</summary>
    public class Slot : IPlantHost, ISowOwner
    {
        public readonly CropPlot plot;
        public readonly int index;

        /// <summary>플레이어가 고른 씨앗 (null = 비움)</summary>
        public ItemData SelectedSeed { get; internal set; }
        public PlantBase Plant { get; internal set; }
        public SowSite PendingSow { get; internal set; }
        internal WorkOrder harvestOrder;

        public Slot(CropPlot plot, int index) { this.plot = plot; this.index = index; }

        public Vector3 SowPosition => plot.transform.position + (Vector3)(plot.firstSlotOffset + plot.slotSpacing * index);

        public Vector3 WorkPosition => SowPosition + (Vector3)plot.workOffsetFromCrop;

        // IPlantHost
        public bool OverridesEnvironment => plot.hydroponic;

        public bool CanGrow(List<string> failures)
        {
            if (plot.IsPowered) return true;
            failures?.Add("전력 없음");
            return false;
        }

        public void OnPlantRemoved(PlantBase plant)
        {
            if (Plant == plant) Plant = null;
            harvestOrder = null;
        }

        // ISowOwner
        public bool CanSowNow => !plot.hydroponic || plot.crystalSupply.Stock > 0;

        public void OnSown(ItemData seed)
        {
            PendingSow = null;
            if (plot.hydroponic && !plot.crystalSupply.TryConsume(1)) return; // 게이트를 지났으니 정상이면 오지 않는다
            plot.SpawnCrop(this, seed.plantEntity, 0f);
        }

        /// <summary>UI 표시용 상태 문구</summary>
        public string Describe()
        {
            if (Plant != null)
            {
                var reasons = new List<string>();
                bool ok = Plant.CanGrowNow(reasons);
                string name = Plant.Definition != null ? Plant.Definition.Label : "작물";
                if (Plant.IsMature) return $"{name} — 수확 대기";
                if (!ok) return $"{name} {Plant.Growth * 100f:0}% — 멈춤: {string.Join(", ", reasons)}";
                return $"{name} {Plant.Growth * 100f:0}% (속도 {Plant.GrowthRate * 100f:0}%)";
            }
            if (PendingSow != null)
                return PendingSow.IsDelivered
                    ? (CanSowNow ? "파종 대기" : "파종 대기 — 침식 결정체 없음")
                    : "씨앗 운반 대기";
            if (SelectedSeed != null)
                return SowSite.HasStock(SelectedSeed) ? "파종 준비 중" : $"{SelectedSeed.itemName} 재고 없음";
            return "비어 있음";
        }
    }

    #endregion

    #region 생명주기

    private void Awake()
    {
        building = GetComponent<Building>();
        power = GetComponent<PowerConsumer>();
        crystalSupply.Bind(this);
        for (int i = 0; i < slotCount; i++) slots.Add(new Slot(this, i));
    }

    private void Update()
    {
        if (hydroponic) crystalSupply.Tick();

        tickTimer -= Time.deltaTime;
        if (tickTimer > 0f) return;
        tickTimer = TICK;

        if (building != null && !building.IsFunctional) return;
        foreach (var slot in slots) TickSlot(slot);
    }

    private void OnDestroy()
    {
        // 로드 중에는 SaveManager가 건물을 통째로 지우고 다시 만든다 — 환불·예약 해제를 하면 복원 상태와 엉킨다
        if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;

        // 건물이 사라지면 진행 중 요청을 정리하고, 도착한 씨앗은 SowSite가 바닥에 돌려준다
        foreach (var slot in slots)
        {
            if (slot.PendingSow != null) slot.PendingSow.Cancel();
            RemoveHarvestOrder(slot);
            if (slot.Plant != null) Destroy(slot.Plant.gameObject);
        }
        crystalSupply.Cancel();
    }

    private void TickSlot(Slot slot)
    {
        // 빈 칸 + 작물 선택됨 → 씨앗 요청 (재고 없으면 다음 틱에 재시도)
        if (slot.Plant == null && slot.PendingSow == null && slot.SelectedSeed != null)
            slot.PendingSow = SowSite.CreateForOwner(slot.SelectedSeed, slot);

        // 다 자람 → 수확 작업 (제초 작업)
        if (slot.Plant != null && slot.Plant.IsMature && slot.harvestOrder == null && WorkSystemManager.instance != null)
        {
            slot.harvestOrder = WorkSystemManager.instance.CreateWorkOrder(
                $"수확: {PlotName}", WorkType.Weeding, 0, 4);
            slot.harvestOrder.AddTarget(new HarvestOrder
            {
                target = slot.Plant,
                position = slot.WorkPosition,
                priority = 4
            });
        }
    }

    #endregion

    #region 조작 (UI)

    private void OnMouseDown()
    {
        if (UIManager.PointerOverUI || UIManager.instance == null) return;
        if (InteractionManager.instance != null &&
            InteractionManager.instance.GetCurrentMode() != InteractionManager.InteractMode.Normal) return;

        var panel = UIManager.instance.GetPanel<CropPlotUI>(UIPanelType.CropPlotUI);
        if (panel == null) { Debug.LogError("[CropPlot] CropPlotUI 패널이 등록되지 않았습니다."); return; }

        panel.Setup(this);
        UIManager.instance.ShowPanel(UIPanelType.CropPlotUI);
    }

    /// <summary>칸의 작물을 고릅니다. null이면 비움 — 진행 중 파종 요청은 취소, 자라는 작물은 그대로 둡니다.</summary>
    public void SelectSeed(int index, ItemData seed)
    {
        if (index < 0 || index >= slots.Count) return;
        if (seed != null && !allowedSeeds.Contains(seed)) return;

        var slot = slots[index];
        if (slot.SelectedSeed == seed) return;
        slot.SelectedSeed = seed;

        if (slot.PendingSow != null)
        {
            slot.PendingSow.Cancel();
            slot.PendingSow = null;
        }
    }

    #endregion

    #region 작물 생성

    private void SpawnCrop(Slot slot, EntityDefinition def, float growth)
    {
        if (def == null || def.prefab == null) return;

        var go = Instantiate(def.prefab, slot.SowPosition, Quaternion.identity);
        var plant = go.GetComponentInChildren<PlantBase>();
        if (plant == null) { Destroy(go); return; }

        plant.SetDefinition(def);
        plant.Host = slot;
        plant.SetGrowth(growth);
        if (plant is CropPlant crop) crop.SetBaseScale(cropScale);
        else go.transform.localScale = Vector3.one * cropScale;

        slot.Plant = plant;
    }

    private static void RemoveHarvestOrder(Slot slot)
    {
        if (slot.harvestOrder != null && WorkSystemManager.instance != null)
            WorkSystemManager.instance.RemoveWorkOrder(slot.harvestOrder, isCancellation: true);
        slot.harvestOrder = null;
    }

    #endregion

    #region IBuildingFunction

    public void OnBuildingDisabled() { }
    public void OnBuildingEnabled() { }

    /// <summary>자라는 작물이 하나라도 있고 멈춰 있지 않은지</summary>
    public bool IsOperating => slots.Exists(s => s.Plant != null && !s.Plant.IsMature && s.Plant.GrowthRate > 0f);

    #endregion

    #region 세이브

    [System.Serializable]
    private class SlotState
    {
        public int selectedSeedId;
        public int plantEntityId;
        public float growth;
        public bool hasPendingSow;
        public bool sowDelivered;
        public int sowReservationId = -1;
    }

    [System.Serializable]
    private class SaveState
    {
        public List<SlotState> slots = new List<SlotState>();
        public ItemSupply.SaveState crystal;
    }

    public string SerializeExtra()
    {
        var state = new SaveState { crystal = crystalSupply.Capture() };
        foreach (var slot in slots)
        {
            var def = slot.Plant != null ? slot.Plant.Definition : null;
            state.slots.Add(new SlotState
            {
                selectedSeedId = slot.SelectedSeed != null ? slot.SelectedSeed.itemID : 0,
                plantEntityId = def != null ? def.id : 0,
                growth = slot.Plant != null ? slot.Plant.Growth : 0f,
                hasPendingSow = slot.PendingSow != null,
                sowDelivered = slot.PendingSow != null && slot.PendingSow.IsDelivered,
                sowReservationId = slot.PendingSow != null ? slot.PendingSow.ReservationId : -1
            });
        }
        return JsonUtility.ToJson(state);
    }

    public void DeserializeExtra(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var state = JsonUtility.FromJson<SaveState>(json);
        if (state == null) return;

        crystalSupply.Restore(state.crystal);

        for (int i = 0; i < slots.Count && i < state.slots.Count; i++)
        {
            var s = state.slots[i];
            var slot = slots[i];
            var seed = s.selectedSeedId != 0 ? GameDatabase.Instance?.GetItemData(s.selectedSeedId) : null;
            slot.SelectedSeed = seed;

            if (s.plantEntityId != 0)
                SpawnCrop(slot, DefinitionDatabase.Instance?.GetEntity(s.plantEntityId), s.growth);

            if (s.hasPendingSow && seed != null)
                slot.PendingSow = SowSite.RestoreForOwner(seed, slot, s.sowDelivered, s.sowReservationId);
        }
    }

    #endregion
}
