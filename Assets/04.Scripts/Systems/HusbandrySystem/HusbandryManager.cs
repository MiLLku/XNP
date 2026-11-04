using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 침식체 사육 — 불길한 알 드롭, 둥지 방 판정, 중립 개체의 조건·생산·사건, 시체(드롭·부패), 세이브.
///
/// 씬에 붙이지 않는다: 게임 씬(MapGenerator가 있는 씬)이 열리면 스스로 생기고, 설정은 Resources/HusbandryConfig.
/// 둥지 = 문·벽으로 닫힌 방 중 보관·생산·가구·오락 건물(직원 시설)이 없는 방. 먹이통이 그 방의 종을 정한다.
/// </summary>
public class HusbandryManager : MonoBehaviour, ISaveModule
{
    private const float TICK = 1f;
    private const float NEST_CACHE_SECONDS = 3f;
    private const float CORPSE_CHECK_SECONDS = 10f;
    private const string BURST_KEY = "neutral_burst";

    public static HusbandryManager instance;
    private static HusbandryConfig _config;
    public static HusbandryConfig Config => _config != null ? _config : (_config = Resources.Load<HusbandryConfig>("HusbandryConfig"));

    private readonly List<NeutralCreature> creatures = new List<NeutralCreature>();
    private readonly Dictionary<int, (float time, bool nest, bool dark)> roomCache = new Dictionary<int, (float, bool, bool)>();
    /// <summary>아직 남아 있는(바닥·창고) 시체가 생긴 게임일 — 오래된 것부터 부패</summary>
    private readonly List<float> corpseDays = new List<float>();
    private float tickTimer, eventTimer, corpseTimer;

    public IReadOnlyList<NeutralCreature> Creatures => creatures;

    #region 부트스트랩

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook() => SceneManager.sceneLoaded += (s, m) => EnsureInScene();

    private static void EnsureInScene()
    {
        if (instance != null || UnityEngine.Object.FindAnyObjectByType<MapGenerator>() == null) return;
        new GameObject("HusbandryManager").AddComponent<HusbandryManager>();
    }

    private void Awake() => instance = this;
    private void OnDestroy() { if (instance == this) instance = null; }

    #endregion

    #region 둥지·방

    /// <summary>자기 칸이 벽 취급이면 위·좌·우 칸의 방 (ErosionIncubator와 같은 규칙)</summary>
    public static Room RoomAt(Vector3 pos)
    {
        if (RoomManager.instance == null) return null;
        var c = Vector2Int.FloorToInt(pos);
        foreach (var d in new[] { Vector2Int.zero, Vector2Int.up, Vector2Int.left, Vector2Int.right })
        {
            var room = RoomManager.instance.GetRoom(c + d);
            if (room != null) return room;
        }
        return null;
    }

    public bool IsNestRoom(Room room) => room != null && Scan(room).nest;
    public bool IsDark(Room room) => room != null && Scan(room).dark;

    private (float time, bool nest, bool dark) Scan(Room room)
    {
        if (roomCache.TryGetValue(room.Id, out var c) && Time.time - c.time < NEST_CACHE_SECONDS) return c;
        bool nest = true, dark = true;
        var seen = new HashSet<Building>();
        foreach (var cell in room.Cells)
        {
            var b = Building.GetBuildingAt(cell);
            if (b == null || b.buildingData == null || !seen.Add(b)) continue;
            if (b.buildingData.buildingID == WORK_LAMP_ID) dark = false;
            if (b.GetComponent<Feeder>() != null || b.GetComponent<Incubator>() != null) continue;
            var cat = b.buildingData.category;
            if (cat == BuildingCategory.Storage || cat == BuildingCategory.Production || cat == BuildingCategory.Furniture || cat == BuildingCategory.Recreation)
                nest = false; // 직원 시설이 있는 방은 둥지가 아니다
        }
        c = (Time.time, nest, dark);
        roomCache[room.Id] = c;
        return c;
    }

    private const int WORK_LAMP_ID = 4008;

    public int CountIn(Room room)
    {
        if (room == null) return 0;
        int n = 0;
        foreach (var x in creatures) if (x != null && RoomAt(x.transform.position)?.Id == room.Id) n++;
        return n;
    }

    /// <summary>방 수용 수 — 칸 수 / cellsPerCreature, 종의 밀집 상한도 넘지 않게</summary>
    public int CapacityOf(Room room, NeutralSpeciesData species)
        => room == null || Config == null ? 0 : Mathf.Min(room.CellCount / Config.cellsPerCreature, species != null ? species.maxPerRoom : int.MaxValue);

    #endregion

    #region 개체

    public NeutralCreature Release(NeutralSpeciesData species, Vector3 pos, int roomId)
    {
        if (species == null) return null;
        var go = new GameObject();
        go.transform.position = pos;
        var c = go.AddComponent<NeutralCreature>();
        c.Setup(species);
        creatures.Add(c);
        Debug.Log($"[Husbandry] {species.displayName} 둥지로 (방 {roomId})");
        return c;
    }

    private void Remove(NeutralCreature c)
    {
        creatures.Remove(c);
        if (c != null) Destroy(c.gameObject);
    }

    private void Update()
    {
        if (SaveManager.instance != null && SaveManager.instance.IsLoading) return;
        tickTimer += Time.deltaTime;
        if (tickTimer >= TICK) { TickCreatures(tickTimer); tickTimer = 0f; }

        var cfg = Config;
        eventTimer += Time.deltaTime;
        if (cfg != null && eventTimer >= cfg.eventCheckSeconds) { eventTimer = 0f; RollEvents(); }

        corpseTimer += Time.deltaTime;
        if (corpseTimer >= CORPSE_CHECK_SECONDS) { corpseTimer = 0f; DecayCorpses(); }
    }

    private static float DayLength => DayCycle.instance != null ? DayCycle.instance.DayLengthInSeconds : 1000f;

    private void TickCreatures(float dt)
    {
        creatures.RemoveAll(x => x == null);
        Smelter[] noisy = null;
        foreach (var c in creatures.ToArray())
        {
            var s = c.Species;
            var room = RoomAt(c.transform.position);
            var feeder = FeederIn(room);

            // 먹이 — 하루 feedPerDay/daysPerFood개
            c.FeedAccumulator += dt / DayLength * s.feedPerDay / s.daysPerFood;
            if (c.FeedAccumulator >= 1f)
            {
                c.Fed = feeder != null && feeder.TryFeed(1);
                if (c.Fed) c.FeedAccumulator -= 1f;
            }

            string unmet = null;
            if (room == null || !IsNestRoom(room)) unmet = "둥지 아님";
            else if (room.Temperature < s.temperatureRange.x || room.Temperature > s.temperatureRange.y) unmet = $"온도 {room.Temperature:F0}℃";
            else if (room.Erosion < s.erosionRange.x || room.Erosion > s.erosionRange.y) unmet = $"침식 {room.Erosion:F0}";
            else if (!c.Fed) unmet = "먹이 없음";
            else if (s.needsDark && !IsDark(room)) unmet = "밝음";
            else if (CountIn(room) > s.maxPerRoom) unmet = "밀집";
            else if (s.hatesNoise && NearNoise(c.transform.position, ref noisy)) unmet = "소음";
            c.LastUnmet = unmet ?? "";

            if (unmet != null) { c.UnmetSeconds += dt; continue; }
            c.UnmetSeconds = Mathf.Max(0f, c.UnmetSeconds - dt);
            if (Time.time < c.HaltUntil || Time.time < c.WanderUntil) continue;

            c.ProduceSeconds += dt;
            if (c.ProduceSeconds < s.produceSeconds || s.product == null) continue;
            c.ProduceSeconds = 0f;
            DroppedItemManager.instance?.SpawnItem(s.product, s.productAmount, c.transform.position);
        }
    }

    private Feeder FeederIn(Room room)
    {
        if (room == null) return null;
        foreach (var f in FindObjectsByType<Feeder>(FindObjectsSortMode.None)) if (f.Room?.Id == room.Id) return f;
        return null;
    }

    private bool NearNoise(Vector3 pos, ref Smelter[] noisy)
    {
        noisy ??= FindObjectsByType<Smelter>(FindObjectsSortMode.None);
        float r = Config != null ? Config.noiseRadius : 8f;
        foreach (var n in noisy) if (n != null && Vector2.Distance(n.transform.position, pos) <= r) return true;
        return false;
    }

    #endregion

    #region 사건

    private void RollEvents()
    {
        var cfg = Config;
        foreach (var c in creatures.ToArray())
        {
            if (c == null || c.UnmetSeconds <= 0f || c.Species.events.Count == 0) continue;
            float chance = Mathf.Min(0.9f, cfg.eventChancePerUnmetDay * c.UnmetSeconds / DayLength);
            if (Random.value >= chance) continue;
            Apply(c, c.Species.events[Random.Range(0, c.Species.events.Count)]);
        }
    }

    private void Apply(NeutralCreature c, NeutralEventType e)
    {
        var s = c.Species;
        var pos = c.transform.position;
        Debug.Log($"[Husbandry] {s.displayName} 사건 {e} (불충족 {c.LastUnmet}, {c.UnmetSeconds:F0}초)");
        switch (e)
        {
            case NeutralEventType.ProductionHalt:
                c.HaltUntil = Time.time + DayLength * 0.5f;
                Letter($"{s.displayName} 생산 중단", $"{s.displayName}이(가) 생산을 멈췄습니다 ({c.LastUnmet}).", LetterType.Neutral);
                break;
            case NeutralEventType.ErosionBurst:
                Burst(pos, s.burstErosion, 6f, s.displayName);
                Letter($"{s.displayName} 침식 분출", $"{s.displayName}이(가) 침식을 뿜었습니다 ({c.LastUnmet}).", LetterType.Threat);
                break;
            case NeutralEventType.Wander:
                c.WanderUntil = Time.time + 90f;
                Letter($"{s.displayName} 배회", $"{s.displayName}이(가) 둥지를 벗어나 떠돕니다 ({c.LastUnmet}).", LetterType.Neutral);
                break;
            case NeutralEventType.Overeat:
                FeederIn(RoomAt(pos))?.TryFeed(2);
                Letter($"{s.displayName} 과식", $"{s.displayName}이(가) 먹이를 한꺼번에 먹어 치웠습니다.", LetterType.Neutral);
                break;
            case NeutralEventType.Escape:
                Letter($"{s.displayName} 탈출", $"{s.displayName}이(가) 둥지를 빠져나가 사라졌습니다 ({c.LastUnmet}).", LetterType.Threat);
                Remove(c);
                break;
            case NeutralEventType.TurnHostile:
                XenopsManager.instance?.SpawnXenops(s.hostileXenopsId, pos);
                Letter($"{s.displayName} 적대화", $"{s.displayName}이(가) 돌변해 직원을 공격합니다 ({c.LastUnmet}). 소집해 제압하세요.", LetterType.Threat, true);
                Remove(c);
                break;
            case NeutralEventType.DeathBurst:
                Burst(pos, s.burstErosion, 10f, s.displayName);
                if (EmployeeManager.instance != null)
                    foreach (var emp in EmployeeManager.instance.AllEmployees)
                        if (emp != null && emp.State != EmployeeState.Dead && Vector2.Distance(emp.transform.position, pos) <= 10f)
                            emp.ModifyMental(-30f, "neutral_death_" + s.speciesId, $"{s.displayName}의 끔찍한 죽음", DayLength);
                Letter($"{s.displayName} 폐사", $"{s.displayName}이(가) 죽으며 강한 침식을 뿜었습니다. 근처 직원이 크게 동요합니다.", LetterType.Threat, true);
                Remove(c);
                break;
        }
    }

    private static void Burst(Vector3 pos, float amount, float radius, string source)
    {
        var room = RoomAt(pos);
        if (room != null) room.Erosion = Mathf.Min(100f, room.Erosion + amount * 0.5f);
        if (EmployeeManager.instance == null) return;
        foreach (var emp in EmployeeManager.instance.AllEmployees)
            if (emp != null && emp.State != EmployeeState.Dead && Vector2.Distance(emp.transform.position, pos) <= radius)
                emp.ErosionController?.AddErosion(amount, BURST_KEY, source + " 침식 분출");
    }

    private static void Letter(string title, string body, LetterType type, bool pause = false)
        => NotificationManager.instance?.PushLetter(new Letter { title = title, body = body, type = type, pauseUntilRead = pause });

    #endregion

    #region 알·시체

    /// <summary>제놉스가 죽을 때 (XenopsHealth.Die) — 습격 적이면 확률로 알, 침식된 인간이면 시체</summary>
    public void OnXenopsDied(Xenops x)
    {
        if (x == null) return;
        var cfg = Config;
        if (x.GetComponent<ErodedHuman>() != null) SpawnCorpse(x.transform.position);
        if (cfg == null || x.Data == null || RaidManager.instance == null || !RaidManager.instance.IsRaider(x)) return;
        if (Random.value >= cfg.eggDropChance) return;
        var egg = cfg.eggByTier[Mathf.Clamp(cfg.EggTierFor(x.Data.xenopsID) - 1, 0, cfg.eggByTier.Length - 1)];
        if (egg != null) DroppedItemManager.instance?.SpawnItem(egg, 1, x.transform.position);
    }

    /// <summary>직원·침식된 인간이 죽으면 시체 — 어려운 종의 먹이, corpseDecayDays 뒤 부패해 사라진다</summary>
    public void SpawnCorpse(Vector3 pos)
    {
        var cfg = Config;
        if (cfg == null || cfg.corpseItem == null) return;
        DroppedItemManager.instance?.SpawnItem(cfg.corpseItem, 1, pos);
        corpseDays.Add(GameDayNow);
    }

    private static float GameDayNow => DayCycle.instance != null ? DayCycle.instance.Day + DayCycle.instance.TimeNormalized : 0f;

    // shortcut: 시체는 개체별이 아니라 '바닥+창고 수량'과 생긴 날짜 목록으로 부패를 센다(먹이통에 들어간 시체는 부패하지 않음) — 시체별 상태가 필요해지면 DroppedItem에 타이머를 단다
    private void DecayCorpses()
    {
        var cfg = Config;
        if (cfg == null || cfg.corpseItem == null) return;
        var floor = new List<DroppedItem>();
        int count = 0;
        foreach (var d in FindObjectsByType<DroppedItem>(FindObjectsSortMode.None))
            if (d != null && d.itemData == cfg.corpseItem) { floor.Add(d); count += d.quantity; }
        int stored = InventoryManager.instance != null ? InventoryManager.instance.GetItemCount(cfg.corpseItem) : 0;
        count += stored;

        // 먹이통으로 들어가 줄어든 만큼 오래된 기록을 지운다
        while (corpseDays.Count > count) corpseDays.RemoveAt(0);
        while (corpseDays.Count < count) corpseDays.Add(GameDayNow); // 기록 없는 시체(구 세이브 등)는 지금부터

        while (corpseDays.Count > 0 && GameDayNow - corpseDays[0] > cfg.corpseDecayDays)
        {
            corpseDays.RemoveAt(0);
            if (floor.Count > 0)
            {
                var d = floor[0];
                if (d.quantity > 1) d.quantity--; else { floor.RemoveAt(0); Destroy(d.gameObject); }
            }
            else if (stored > 0 && InventoryManager.instance.RemoveItem(cfg.corpseItem, 1)) stored--;
        }
    }

    #endregion

    #region 세이브

    public int SaveOrder => 75; // 바닥 아이템(70) 다음

    public void Capture(SaveData data)
    {
        var s = new HusbandrySaveData();
        foreach (var c in creatures)
        {
            if (c == null) continue;
            s.creatures.Add(new NeutralCreatureSaveData
            {
                speciesId = c.Species.speciesId,
                x = c.transform.position.x, y = c.transform.position.y,
                unmet = c.UnmetSeconds, produce = c.ProduceSeconds, feed = c.FeedAccumulator, fed = c.Fed
            });
        }
        s.corpseDays.AddRange(corpseDays);
        data.husbandry = s;
    }

    public void Restore(SaveData data)
    {
        foreach (var c in creatures) if (c != null) Destroy(c.gameObject);
        creatures.Clear();
        corpseDays.Clear();
        var s = data.husbandry;
        if (s == null || Config == null) return;
        foreach (var cs in s.creatures)
        {
            var c = Release(Config.GetSpecies(cs.speciesId), new Vector3(cs.x, cs.y, 0f), -1);
            if (c == null) continue;
            c.UnmetSeconds = cs.unmet; c.ProduceSeconds = cs.produce; c.FeedAccumulator = cs.feed; c.Fed = cs.fed;
        }
        corpseDays.AddRange(s.corpseDays);
    }

    public void PostRestore(SaveData data) { }

    #endregion
}

[System.Serializable]
public class HusbandrySaveData
{
    public List<NeutralCreatureSaveData> creatures = new List<NeutralCreatureSaveData>();
    public List<float> corpseDays = new List<float>();
}

[System.Serializable]
public class NeutralCreatureSaveData
{
    public int speciesId;
    public float x, y;
    public float unmet, produce, feed;
    public bool fed;
}
