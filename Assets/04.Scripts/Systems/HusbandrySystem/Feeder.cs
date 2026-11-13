using UnityEngine;

/// <summary>
/// 먹이통 — 둥지 방(격리된 방)에 둔다. 방에 사는 종을 정하고(처음 들어온 유체의 종),
///   · 그 종의 유체를 운반받아 방에 풀어 준다 (방 수용 수까지) — '개체를 둥지 방으로 옮기기'
///   · 그 종의 먹이(침식 사료·시체)를 운반받아 쌓아 두고, 같은 방 개체가 먹는다.
/// 둥지가 아닌 방(직원 시설이 있는 방·실외)이면 아무것도 받지 않는다.
/// </summary>
[RequireComponent(typeof(Building))]
public class Feeder : MonoBehaviour, IBuildingExtraSerializable, IBuildingFunction
{
    [SerializeField] private ItemSupply foodSupply = new ItemSupply { capacity = 10, refillBelow = 4 };
    [SerializeField] private ItemSupply hatchSupply = new ItemSupply { capacity = 1, refillBelow = 1 };

    private Building building;
    private NeutralSpeciesData species;

    public NeutralSpeciesData Species => species;
    public int FoodStock => foodSupply.Stock;
    public Room Room => HusbandryManager.RoomAt(transform.position);

    private void Awake()
    {
        building = GetComponent<Building>();
        foodSupply.Bind(this);
        hatchSupply.Bind(this);
    }

    private void Update()
    {
        var mgr = HusbandryManager.instance;
        if (mgr == null || (building != null && !building.IsFunctional)) return;
        var room = Room;
        if (room == null || !mgr.IsNestRoom(room)) return;

        int living = mgr.CountIn(room);
        // 둥지가 비었고 그 종 유체도 더 없으면 다른 종을 받을 수 있다
        if (living == 0 && hatchSupply.Stock == 0 && !hatchSupply.Pending && species != null && InventoryManager.instance != null
            && InventoryManager.instance.GetItemCount(species.hatchlingItem) <= 0
            && InventoryManager.instance.GetWorldAvailable(species.hatchlingItem) <= 0) SetSpecies(null);
        if (species == null) PickSpecies();
        if (species == null) return;

        // 들어온 유체는 방에 풀어 준다
        if (hatchSupply.Stock > 0 && hatchSupply.TryConsume(1))
            mgr.Release(species, transform.position + new Vector3(0.5f, 0f, 0f), room.Id);

        if (living + hatchSupply.Stock < mgr.CapacityOf(room, species)) hatchSupply.Tick();
        if (living > 0) foodSupply.Tick();
    }

    /// <summary>창고에 있는 유체 중 이 방에 맞는 첫 종</summary>
    private void PickSpecies()
    {
        var cfg = HusbandryManager.Config;
        if (cfg == null || InventoryManager.instance == null) return;
        foreach (var s in cfg.species)
            if (s != null && s.hatchlingItem != null && InventoryManager.instance.GetWorldAvailable(s.hatchlingItem) > 0) { SetSpecies(s); return; }
    }

    private void SetSpecies(NeutralSpeciesData s)
    {
        if (species == s) return;
        foodSupply.Cancel();
        hatchSupply.Cancel();
        species = s;
        foodSupply.item = s != null ? s.food : null;
        hatchSupply.item = s != null ? s.hatchlingItem : null;
    }

    /// <summary>개체가 먹이를 먹는다 — 모자라면 false</summary>
    public bool TryFeed(int amount) => foodSupply.TryConsume(amount);

    public string DescribeStatus()
    {
        var mgr = HusbandryManager.instance;
        var room = Room;
        if (room == null) return "밀폐된 방이 아님";
        if (mgr != null && !mgr.IsNestRoom(room)) return "둥지가 아님 — 침대·작업대·창고 같은 직원 시설이 없는 격리된 방이어야 합니다";
        if (species == null) return "유체 대기 (부화기에서 깨어난 침식체)";
        return $"{species.displayName} {mgr?.CountIn(room) ?? 0}/{mgr?.CapacityOf(room, species) ?? 0}마리 · 먹이({species.food?.itemName}) {foodSupply.Stock}";
    }

    private void OnDestroy()
    {
        if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;
        foodSupply.Cancel();
        hatchSupply.Cancel();
    }

    public void OnBuildingDisabled() { }
    public void OnBuildingEnabled() { }
    public bool IsOperating => species != null && HusbandryManager.instance != null && HusbandryManager.instance.IsNestRoom(Room);

    [System.Serializable]
    private class SaveState
    {
        public int speciesId;
        public ItemSupply.SaveState food;
        public ItemSupply.SaveState hatch;
    }

    public string SerializeExtra() => JsonUtility.ToJson(new SaveState
    {
        speciesId = species != null ? species.speciesId : 0,
        food = foodSupply.Capture(),
        hatch = hatchSupply.Capture()
    });

    public void DeserializeExtra(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var s = JsonUtility.FromJson<SaveState>(json);
        if (s == null) return;
        SetSpecies(HusbandryManager.Config?.GetSpecies(s.speciesId));
        foodSupply.Restore(s.food);
        hatchSupply.Restore(s.hatch);
    }
}
