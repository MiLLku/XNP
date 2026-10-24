using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 침식 발판 — 제놉스가 낭떠러지·높은 벽을 넘으려고 까는 지형 타일(<see cref="TileType.ErosionFloor"/>)과 그 주변 침식.
///
///   • 발판 자체는 보통 지형 타일이다 — 누구나 밟고, 맵과 함께 저장되고, 직원이 캐서 없앤다(드롭 없음).
///     방 경계에 닿으면 타일 정의의 erosionOutput으로 방 침식도 올린다.
///   • 바깥에서도 위험하도록 개체 발원지 하나로 등록해, 발판 FLOOR_RADIUS칸 안의 직원에게 고정 침식을 붙인다.
///     발판이 여러 개여도 키가 하나라 겹쳐 쌓이지 않는다.
///   • 습격 하나당 MAX_PER_RAID개까지만 깐다 (습격 시작 때 초기화).
///
/// 발판 목록은 맵을 주기적으로 훑어 만든다 — 캐거나 불러와도 따로 등록·해제할 것이 없다.
/// 씬 배선 없이 스스로 생긴다.
/// </summary>
public class ErosionFloorField : DestroySingleton<ErosionFloorField>, IEntityErosionSource
{
    // shortcut: 임시 수치 — 밸런스 확정 시 데이터로 뺄 것
    public const int MAX_PER_RAID = 20;
    private const float FLOOR_EROSION = 10f;
    private const float FLOOR_RADIUS = 2f;
    private const float RESCAN_INTERVAL = 3f;

    private readonly HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
    private float rescanTimer;
    private bool registered;
    private System.IDisposable subscription;

    /// <summary>이번 습격에서 깐 발판 수</summary>
    public int BuiltThisRaid { get; private set; }

    public bool CanBuildMore => BuiltThisRaid < MAX_PER_RAID;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        Ensure();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Ensure();

    private static void Ensure()
    {
        if (FindAnyObjectByType<MapGenerator>() == null) return; // 게임 씬에서만 (메인 메뉴 제외)
        if (FindAnyObjectByType<ErosionFloorField>() == null)
            new GameObject("ErosionFloorField").AddComponent<ErosionFloorField>();
    }

    private void Start()
    {
        subscription = GameMessageBus.Subscribe<RaidStartedMessage>(_ => BuiltThisRaid = 0);
    }

    private void OnDestroy()
    {
        subscription?.Dispose();
        if (registered) EntityErosionField.instance?.UnregisterSource(this);
    }

    private void Update()
    {
        if (!registered && EntityErosionField.instance != null)
        {
            EntityErosionField.instance.RegisterSource(this);
            registered = true;
        }

        rescanTimer -= Time.deltaTime;
        if (rescanTimer > 0f) return;
        rescanTimer = RESCAN_INTERVAL;
        Rescan();
    }

    private void Rescan()
    {
        cells.Clear();
        var map = MapGenerator.instance != null ? MapGenerator.instance.GameMapInstance : null;
        if (map == null) return;
        int id = (int)TileType.ErosionFloor;
        for (int x = 0; x < GameMap.MAP_WIDTH; x++)
            for (int y = 0; y < GameMap.MAP_HEIGHT; y++)
                if (map.TileGrid[x, y] == id) cells.Add(new Vector2Int(x, y));
    }

    /// <summary>
    /// 발판을 깐다. 칸이 비어 있지 않거나, 몸이 겹치는 개체가 있거나, 이번 습격 몫을 다 썼으면 false.
    /// </summary>
    public bool TryPlace(Vector2Int cell)
    {
        if (!CanBuildMore) return false;
        var map = MapGenerator.instance != null ? MapGenerator.instance.GameMapInstance : null;
        var renderer = MapGenerator.instance != null ? MapGenerator.instance.MapRendererInstance : null;
        if (map == null || renderer == null) return false;
        if (cell.x < 0 || cell.x >= GameMap.MAP_WIDTH || cell.y < 0 || cell.y >= GameMap.MAP_HEIGHT) return false;
        if (map.TileGrid[cell.x, cell.y] != 0 || map.DoesTileBlockMovement(cell.x, cell.y) || Building.GetBuildingAt(cell) != null) return false;

        // 직원·적 몸 안에 타일을 박지 않는다
        foreach (var c in Physics2D.OverlapBoxAll(new Vector2(cell.x + 0.5f, cell.y + 0.5f), new Vector2(0.9f, 0.9f), 0f))
            if (c.attachedRigidbody != null && c.attachedRigidbody.bodyType == RigidbodyType2D.Dynamic) return false;

        map.SetTile(cell.x, cell.y, (int)TileType.ErosionFloor);
        renderer.UpdateTileVisual(cell.x, cell.y);
        cells.Add(cell);
        BuiltThisRaid++;
        return true;
    }

    #region IEntityErosionSource

    // 표시 레이어는 중심±반경 사각형을 훑으며 Covers를 묻는다 — 발판 전체를 덮는 사각형을 준다
    public Vector2 EmitPosition
    {
        get
        {
            if (cells.Count == 0) return Vector2.zero;
            Vector2Int min = new Vector2Int(int.MaxValue, int.MaxValue), max = new Vector2Int(int.MinValue, int.MinValue);
            foreach (var c in cells) { min = Vector2Int.Min(min, c); max = Vector2Int.Max(max, c); }
            return new Vector2((min.x + max.x + 1) * 0.5f, (min.y + max.y + 1) * 0.5f);
        }
    }

    public float EmitRadius
    {
        get
        {
            if (cells.Count == 0) return 0f;
            Vector2Int min = new Vector2Int(int.MaxValue, int.MaxValue), max = new Vector2Int(int.MinValue, int.MinValue);
            foreach (var c in cells) { min = Vector2Int.Min(min, c); max = Vector2Int.Max(max, c); }
            return Mathf.Max(max.x - min.x + 1, max.y - min.y + 1) * 0.5f + FLOOR_RADIUS;
        }
    }

    public float FixedErosionAmount => FLOOR_EROSION;
    public bool IsEmitting => cells.Count > 0;
    public string ErosionSourceKey => "enemy_floor";
    public string ErosionSourceName => "침식 발판";
    public bool HorizontalOnly => false;

    /// <summary>근처(반경 안) 칸에 발판이 있는지 — 반경 주변 칸만 본다</summary>
    public bool Covers(Vector2 worldPosition)
    {
        int r = Mathf.CeilToInt(FLOOR_RADIUS);
        int cx = Mathf.FloorToInt(worldPosition.x), cy = Mathf.FloorToInt(worldPosition.y);
        for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                var c = new Vector2Int(cx + dx, cy + dy);
                if (cells.Contains(c) && Vector2.Distance(worldPosition, new Vector2(c.x + 0.5f, c.y + 0.5f)) <= FLOOR_RADIUS + 0.5f)
                    return true;
            }
        return false;
    }

    #endregion
}
