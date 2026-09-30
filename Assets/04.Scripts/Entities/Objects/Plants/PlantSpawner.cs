using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 중 식생물 자연 생성기.
///
/// <see cref="EntityDefinition.spawnAtRuntime"/>이 켜진 개체마다 <see cref="EntityDefinition.runtimeSpawnInterval"/>초에
/// 한 번, 무작위 열의 노출면을 골라 생성 조건을 만족하면 하나 생성합니다.
/// 1회성은 다 자란 상태, 재성장 부류는 묘목 단계(성장도 0)로 나옵니다.
///
/// 씬에 배치해야 동작합니다 (다른 매니저처럼 DestroySingleton 하위).
/// </summary>
public class PlantSpawner : DestroySingleton<PlantSpawner>, ISaveModule
{
    [Tooltip("주기마다 후보 열을 몇 번 뽑아볼지. 조건이 까다로운 식물일수록 크게.")]
    [SerializeField, Min(1)] private int attemptsPerTick = 20;

    private readonly Dictionary<EntityDefinition, float> timers = new Dictionary<EntityDefinition, float>();

    private void Update()
    {
        var db = DefinitionDatabase.Instance;
        if (db == null || MapGenerator.instance == null || MapGenerator.instance.GameMapInstance == null) return;

        foreach (var def in db.entities)
        {
            if (def == null || !def.spawnAtRuntime || def.prefab == null) continue;

            timers.TryGetValue(def, out float t);
            t += Time.deltaTime;
            if (t >= def.runtimeSpawnInterval)
            {
                t = 0f;
                TrySpawn(def);
            }
            timers[def] = t;
        }
    }

    /// <summary>한 번 생성을 시도합니다. 생성했으면 true.</summary>
    public bool TrySpawn(EntityDefinition def)
    {
        var map = MapGenerator.instance.GameMapInstance;
        var candidates = new List<int>();

        for (int attempt = 0; attempt < attemptsPerTick; attempt++)
        {
            int x = Random.Range(0, GameMap.MAP_WIDTH);

            // 이 열의 노출면(아래가 단단하고 자신은 공기) 중 하나를 고른다 — 지표/동굴 구분은 생성 조건이 한다
            candidates.Clear();
            for (int y = 1; y < GameMap.MAP_HEIGHT; y++)
                if (map.TileGrid[x, y] == (int)TileType.Air && map.IsSolidGround(x, y - 1)) candidates.Add(y);
            if (candidates.Count == 0) continue;

            var cell = new Vector2Int(x, candidates[Random.Range(0, candidates.Count)]);
            if (!PlantPlacement.CanSpawnNaturallyAt(def, cell)) continue;

            var prefabPlant = def.prefab.GetComponentInChildren<PlantBase>();
            float growth = prefabPlant != null && prefabPlant.Lifecycle == PlantLifecycle.Regrow ? 0f : 1f;
            PlantPlacement.Spawn(def, cell, growth);
            return true;
        }
        return false;
    }

    #region ISaveModule — 파종 예정지

    // 식생물 자체는 MapGenerator가 MapEntityIdentity로 저장한다. 여기서는 파종 예정지만.
    // ponytail: 생성 주기 타이머는 저장하지 않는다 — 로드하면 주기가 처음부터 다시 돈다 (주기가 짧아 체감 없음)

    /// <summary>인벤토리(20)·바닥 아이템(70) 복원 뒤 — 묘목 예약을 다시 잡아야 한다</summary>
    public int SaveOrder => 75;

    public void Capture(SaveData data)
    {
        data.sowSites = new List<SowSiteSaveData>();
        foreach (var site in SowSite.All)
        {
            if (site == null || site.Sapling == null || site.Owner != null) continue; // 재배 칸 소속은 건물이 저장
            data.sowSites.Add(new SowSiteSaveData
            {
                itemId = site.Sapling.itemID,
                x = site.Cell.x,
                y = site.Cell.y,
                delivered = site.IsDelivered,
                reservationId = site.ReservationId
            });
        }
    }

    public void Restore(SaveData data)
    {
        // 로드 전 남아 있던 예정지는 환불 없이 치운다 (인벤토리·작업물이 통째로 새로 복원되므로)
        foreach (var site in SowSite.All.ToArray())
            if (site != null) Destroy(site.gameObject);
        SowSite.All.Clear();

        if (data.sowSites == null || GameDatabase.Instance == null) return;

        foreach (var s in data.sowSites)
        {
            var item = GameDatabase.Instance.GetItemData(s.itemId);
            if (SowSite.Restore(item, new Vector2Int(s.x, s.y), s.delivered, s.reservationId) == null)
                Debug.LogWarning($"[PlantSpawner] 파종 예정지 복원 실패 ({s.x},{s.y})");
        }
    }

    public void PostRestore(SaveData data) { }

    #endregion
}

/// <summary>파종 예정지 저장 데이터.</summary>
[System.Serializable]
public class SowSiteSaveData
{
    public int itemId;
    public int x;
    public int y;
    /// <summary>묘목이 이미 도착했는지 — true면 예약 없이 파종 작업부터 재개</summary>
    public bool delivered;
    /// <summary>묘목 예약 ID — 인벤토리가 예약을 ID째로 복원하므로 그대로 이어받는다</summary>
    public int reservationId = -1;
}
