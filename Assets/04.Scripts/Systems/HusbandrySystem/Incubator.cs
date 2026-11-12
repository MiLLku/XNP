using UnityEngine;

/// <summary>
/// 부화기 — 불길한 알 1개를 받아(운반 보급) 종을 정하고, 방 온도·침식이 그 종의 범위일 때만 부화를 진행한다.
/// 다 되면 그 종의 유체 아이템을 바닥에 떨군다 → 창고로 운반 → 둥지 방 먹이통이 받아 풀어 준다.
/// 알은 창고에 있는 것 중 높은 티어부터 받는다.
/// </summary>
[RequireComponent(typeof(Building))]
public class Incubator : MonoBehaviour, IBuildingExtraSerializable, IBuildingFunction
{
    [SerializeField] private ItemSupply eggSupply = new ItemSupply { capacity = 1, refillBelow = 1 };

    private Building building;
    private NeutralSpeciesData species; // 알을 받으면 정해진다
    private float progress;             // 초

    public NeutralSpeciesData Species => species;
    public float Progress01 => species != null ? Mathf.Clamp01(progress / species.incubateSeconds) : 0f;

    private void Awake()
    {
        building = GetComponent<Building>();
        eggSupply.Bind(this);
    }

    private void Update()
    {
        var cfg = HusbandryManager.Config;
        if (cfg == null) return;

        if (species == null)
        {
            // 들어온 알이 있으면 종을 정하고, 없으면 창고의 알(높은 티어 먼저)을 요청
            if (eggSupply.item != null && eggSupply.Stock > 0 && eggSupply.TryConsume(1))
            {
                species = cfg.RollSpecies(cfg.TierOfEgg(eggSupply.item));
                progress = 0f;
                if (species == null) return;
                Debug.Log($"[Incubator] 알에서 {species.displayName} 부화 시작");
            }
            else if (eggSupply.Stock == 0)
            {
                if (!eggSupply.Pending) PickEgg(cfg); // 운반 중에 알을 바꾸면 들고 오던 알이 취소된다
                eggSupply.Tick();
            }
            return;
        }

        if (building != null && !building.IsFunctional) return;
        if (!ConditionsOk()) return;
        progress += Time.deltaTime;
        if (progress < species.incubateSeconds) return;

        HusbandryManager.instance?.Discover(species);
        DroppedItemManager.instance?.SpawnItem(species.hatchlingItem, 1, transform.position + new Vector3(0.5f, 0.2f, 0f));
        NotificationManager.instance?.PushLetter(new Letter { title = "부화", body = $"부화기에서 {species.displayName}이(가) 깨어났습니다. 둥지 방의 먹이통으로 옮겨집니다.", type = LetterType.Positive });
        species = null;
        progress = 0f;
    }

    private void PickEgg(HusbandryConfig cfg)
    {
        if (InventoryManager.instance == null) return;
        for (int t = cfg.eggByTier.Length - 1; t >= 0; t--)
        {
            var egg = cfg.eggByTier[t];
            // 창고에 들어온 알만 — 바닥의 알은 예약이 안 돼서 보급이 영영 안 걸린다 (창고로 옮겨진 뒤 받는다)
            if (egg == null || InventoryManager.instance.GetAvailableAmount(egg) <= 0) continue;
            if (eggSupply.item != egg) { eggSupply.Cancel(); eggSupply.item = egg; }
            return;
        }
    }

    private bool ConditionsOk()
    {
        var room = HusbandryManager.RoomAt(transform.position);
        if (room == null) return false;
        return InRange(room.Temperature, species.temperatureRange) && InRange(room.Erosion, species.erosionRange);
    }

    private static bool InRange(float v, Vector2 r) => v >= r.x && v <= r.y;

    /// <summary>상태 문구</summary>
    public string DescribeStatus()
    {
        if (species == null) return eggSupply.Stock > 0 ? "알 확인 중" : "불길한 알 대기";
        if (!ConditionsOk())
            return $"{species.displayName} — 방 조건이 맞지 않아 멈춤 (온도 {species.temperatureRange.x:F0}~{species.temperatureRange.y:F0}℃, 침식 {species.erosionRange.x:F0}~{species.erosionRange.y:F0})";
        return $"{species.displayName} 부화 중 {Progress01 * 100f:F0}%";
    }

    private void OnDestroy()
    {
        if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;
        eggSupply.Cancel();
    }

    public void OnBuildingDisabled() { }
    public void OnBuildingEnabled() { }
    public bool IsOperating => species != null && ConditionsOk();

    [System.Serializable]
    private class SaveState
    {
        public int eggItemId;
        public ItemSupply.SaveState egg;
        public int speciesId;
        public float progress;
    }

    public string SerializeExtra() => JsonUtility.ToJson(new SaveState
    {
        eggItemId = eggSupply.item != null ? eggSupply.item.itemID : 0,
        egg = eggSupply.Capture(),
        speciesId = species != null ? species.speciesId : 0,
        progress = progress
    });

    public void DeserializeExtra(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var s = JsonUtility.FromJson<SaveState>(json);
        if (s == null) return;
        eggSupply.item = s.eggItemId != 0 ? GameDatabase.Instance?.GetItemData(s.eggItemId) : null;
        eggSupply.Restore(s.egg);
        species = HusbandryManager.Config?.GetSpecies(s.speciesId);
        progress = s.progress;
    }
}
