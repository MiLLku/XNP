using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 맵을 밀폐 공간(<see cref="Room"/>) 단위로 나누는 매니저.
/// 온도(Phase 2)와 침식(Phase 3)이 공유하는 기반입니다.
///
/// <b>실외 판정</b> — flood fill이 하늘이나 맵 가장자리에 닿으면 그 공간 전체가 실외입니다.
/// 실외는 방이 아니며 환경 수치가 축적되지 않습니다. 밀폐된 동굴만 위험해지는 구조가 여기서 나옵니다.
///
/// <b>재계산 방식</b> — 칸이 바뀌면 예약만 걸고 <see cref="rebuildDelay"/> 뒤에 전체를 다시 훑습니다.
/// 병합/분할을 따로 구현하지 않는 이유: 전체 재계산 뒤 새 방의 값을
/// <b>예전 방들의 칸 수 가중 평균</b>으로 이어받으면 병합(합쳐서 평균)과 분할(양쪽이 같은 값)이
/// 모두 저절로 맞습니다. 특수 케이스 코드가 사라지고 결과도 물리적으로 옳습니다.
/// </summary>
public class RoomManager : DestroySingleton<RoomManager>, ISaveModule
{
    #region 상수

    /// <summary>실외 또는 고체 칸을 뜻하는 방 번호</summary>
    public const int OUTDOOR_ID = 0;

    private static readonly Vector2Int[] NeighborOffsets =
    {
        new Vector2Int( 1,  0),
        new Vector2Int(-1,  0),
        new Vector2Int( 0,  1),
        new Vector2Int( 0, -1)
    };

    #endregion

    #region 인스펙터

    [Header("재계산")]
    [Tooltip("맵이 바뀐 뒤 방을 다시 계산하기까지 기다리는 시간(초). 채광처럼 연달아 바뀔 때 한 번으로 묶습니다.")]
    [SerializeField] private float rebuildDelay = 0.25f;

    [Tooltip("재계산 소요 시간과 방 개수를 로그로 남깁니다.")]
    [SerializeField] private bool showDebugLogs = false;

    #endregion

    #region 상태

    /// <summary>칸 → 방 번호. 0이면 실외이거나 고체입니다.</summary>
    private int[,] roomIdGrid;

    /// <summary>열별 하늘 하한. y가 이 값 이상이면 그 칸은 하늘에 열려 있습니다.</summary>
    private int[] skyBottom;

    private readonly Dictionary<int, Room> rooms = new Dictionary<int, Room>();

    /// <summary>문 아래칸 → 그 문이 잇는 두 공간의 방 번호</summary>
    private readonly Dictionary<Vector2Int, (int a, int b)> doorLinks
        = new Dictionary<Vector2Int, (int a, int b)>();

    private int nextRoomId = 1;

    private bool dirty;
    private float dirtyTimer;

    private GameMap gameMap;
    private GameMap subscribedMap;

    // 재계산 작업용 버퍼 (매번 새로 만들지 않도록 유지)
    private bool[,] visited;
    private readonly Queue<Vector2Int> floodQueue = new Queue<Vector2Int>();
    private readonly List<Vector2Int> floodCells = new List<Vector2Int>();
    private readonly List<Vector2Int> floodBoundary = new List<Vector2Int>();

    #endregion

    #region 프로퍼티

    /// <summary>현재 방 목록 (실외 제외)</summary>
    public IReadOnlyDictionary<int, Room> Rooms => rooms;

    /// <summary>방 개수 (실외 제외)</summary>
    public int RoomCount => rooms.Count;

    /// <summary>마지막 재계산에 걸린 시간(ms)</summary>
    public float LastRebuildMs { get; private set; }

    /// <summary>마지막 재계산에서 방에 속한 총 칸 수</summary>
    public int LastIndoorCellCount { get; private set; }

    #endregion

    #region 초기화

    protected override void Awake()
    {
        base.Awake();
        roomIdGrid = new int[GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT];
        visited    = new bool[GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT];
        skyBottom  = new int[GameMap.MAP_WIDTH];
    }

    private void Start()
    {
        EnsureGameMap();

        // MapGenerator.Start가 먼저 돌았다면 OnBulkChanged를 놓쳤을 수 있으므로 한 번 계산해둔다.
        // 나중에 돌면 OnBulkChanged로 한 번 더 계산된다 (결과는 같다).
        if (gameMap != null) Rebuild();
    }

    private void OnDestroy()
    {
        UnsubscribeFromMap();
    }

    private void EnsureGameMap()
    {
        if (gameMap == null && MapGenerator.instance != null)
        {
            gameMap = MapGenerator.instance.GameMapInstance;
            SubscribeToMap();
        }
    }

    private void SubscribeToMap()
    {
        if (gameMap == null || subscribedMap == gameMap) return;

        UnsubscribeFromMap();
        gameMap.OnCellChanged += HandleCellChanged;
        gameMap.OnBulkChanged += HandleBulkChanged;
        subscribedMap = gameMap;
    }

    private void UnsubscribeFromMap()
    {
        if (subscribedMap == null) return;

        subscribedMap.OnCellChanged -= HandleCellChanged;
        subscribedMap.OnBulkChanged -= HandleBulkChanged;
        subscribedMap = null;
    }

    #endregion

    #region 변경 감지

    private void HandleCellChanged(int x, int y)
    {
        dirty = true;
        dirtyTimer = rebuildDelay;
    }

    private void HandleBulkChanged()
    {
        EnsureGameMap();
        dirty = false;
        dirtyTimer = 0f;
        Rebuild();
    }

    private void Update()
    {
        if (gameMap == null)
        {
            EnsureGameMap();
            return;
        }

        if (!dirty) return;

        dirtyTimer -= Time.deltaTime;
        if (dirtyTimer > 0f) return;

        dirty = false;
        Rebuild();
    }

    #endregion

    #region 조회

    /// <summary>해당 칸의 방 번호. 실외이거나 고체면 <see cref="OUTDOOR_ID"/>입니다.</summary>
    public int GetRoomId(int x, int y)
    {
        if (!IsInBounds(x, y)) return OUTDOOR_ID;
        return roomIdGrid[x, y];
    }

    /// <inheritdoc cref="GetRoomId(int,int)"/>
    public int GetRoomId(Vector2Int cell) => GetRoomId(cell.x, cell.y);

    /// <summary>해당 칸이 속한 방. 실외이거나 고체면 null입니다.</summary>
    public Room GetRoom(int x, int y)
    {
        int id = GetRoomId(x, y);
        return id == OUTDOOR_ID ? null : (rooms.TryGetValue(id, out Room room) ? room : null);
    }

    /// <inheritdoc cref="GetRoom(int,int)"/>
    public Room GetRoom(Vector2Int cell) => GetRoom(cell.x, cell.y);

    /// <summary>번호로 방을 찾습니다.</summary>
    public Room GetRoomById(int id) => rooms.TryGetValue(id, out Room room) ? room : null;

    /// <summary>
    /// 해당 칸이 실외인지 여부.
    /// 고체 칸도 true를 반환하므로, "밀폐되지 않았다"는 뜻으로 읽으세요.
    /// </summary>
    public bool IsOutdoor(int x, int y) => GetRoomId(x, y) == OUTDOOR_ID;

    /// <summary>해당 칸이 하늘에 열려 있는지 (그 위로 막힌 것이 없는지)</summary>
    public bool IsOpenToSky(int x, int y)
    {
        if (!IsInBounds(x, y)) return false;
        return y >= skyBottom[x];
    }

    /// <summary>문이 잇는 두 방 번호를 반환합니다. 등록되지 않은 문이면 false.</summary>
    public bool TryGetDoorLink(Vector2Int doorTile, out int roomA, out int roomB)
    {
        if (doorLinks.TryGetValue(doorTile, out var link))
        {
            roomA = link.a;
            roomB = link.b;
            return true;
        }

        roomA = roomB = OUTDOOR_ID;
        return false;
    }

    #endregion

    #region 재계산

    /// <summary>
    /// 방을 처음부터 다시 계산합니다.
    /// 새 방의 온도·침식은 그 칸들이 예전에 속해 있던 방들의 <b>칸 수 가중 평균</b>으로 이어집니다.
    /// </summary>
    public void Rebuild()
    {
        EnsureGameMap();
        if (gameMap == null) return;

        var stopwatch = Stopwatch.StartNew();

        // 이전 상태를 값 이어받기에 쓰기 위해 보관
        int[,] previousGrid = roomIdGrid;
        var previousRooms = new Dictionary<int, Room>(rooms);

        roomIdGrid = new int[GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT];
        Array.Clear(visited, 0, visited.Length);
        rooms.Clear();
        doorLinks.Clear();
        nextRoomId = 1;

        RecomputeSkyBottom();

        int indoorCells = 0;

        for (int x = 0; x < GameMap.MAP_WIDTH; x++)
        {
            for (int y = 0; y < GameMap.MAP_HEIGHT; y++)
            {
                if (visited[x, y]) continue;
                if (!IsOpenCell(x, y))
                {
                    visited[x, y] = true;
                    continue;
                }

                Room room = FloodFrom(x, y);
                if (room != null) indoorCells += room.CellCount;
            }
        }

        foreach (var room in rooms.Values)
            InheritState(room, previousGrid, previousRooms);

        RebuildDoorLinks();

        stopwatch.Stop();
        LastRebuildMs = (float)stopwatch.Elapsed.TotalMilliseconds;
        LastIndoorCellCount = indoorCells;

        if (showDebugLogs)
            Debug.Log($"[RoomManager] 방 재계산: {rooms.Count}개 / 실내 {indoorCells}칸 / {LastRebuildMs:F2}ms");

        GameMessageBus.Publish(new RoomsRebuiltMessage());
    }

    /// <summary>
    /// 열마다 하늘 하한을 다시 구합니다.
    /// 위에서부터 내려오다 처음 막힌 칸을 만나면 그 바로 위까지가 하늘입니다.
    /// 지붕을 차단 건물로 지어도 하늘이 끊기도록 <see cref="IsOpenCell"/>과 같은 기준을 씁니다.
    /// </summary>
    private void RecomputeSkyBottom()
    {
        for (int x = 0; x < GameMap.MAP_WIDTH; x++)
        {
            int y = GameMap.MAP_HEIGHT - 1;
            while (y >= 0 && IsOpenCell(x, y)) y--;
            skyBottom[x] = y + 1;
        }
    }

    /// <summary>
    /// 한 공간을 flood fill 합니다.
    /// 하늘이나 맵 가장자리에 닿으면 실외로 확정하고 방을 만들지 않습니다.
    /// </summary>
    private Room FloodFrom(int startX, int startY)
    {
        floodQueue.Clear();
        floodCells.Clear();
        floodBoundary.Clear();

        bool isOutdoor = false;

        floodQueue.Enqueue(new Vector2Int(startX, startY));
        visited[startX, startY] = true;

        while (floodQueue.Count > 0)
        {
            Vector2Int cell = floodQueue.Dequeue();
            floodCells.Add(cell);

            // 하늘에 열려 있거나 맵 끝에 닿으면 실외 — 계속 훑어서 공간 전체를 실외로 만든다
            if (IsOpenToSky(cell.x, cell.y) || IsMapEdge(cell.x, cell.y))
                isOutdoor = true;

            foreach (var offset in NeighborOffsets)
            {
                int nx = cell.x + offset.x;
                int ny = cell.y + offset.y;

                if (!IsInBounds(nx, ny)) continue;

                if (!IsOpenCell(nx, ny))
                {
                    // 맞닿은 면 하나. visited 검사보다 먼저 세야 같은 벽의 여러 면이 빠지지 않는다.
                    floodBoundary.Add(new Vector2Int(nx, ny));
                    visited[nx, ny] = true;
                    continue;
                }

                if (visited[nx, ny]) continue;
                visited[nx, ny] = true;
                floodQueue.Enqueue(new Vector2Int(nx, ny));
            }
        }

        if (isOutdoor)
        {
            // roomIdGrid는 이미 0(OUTDOOR_ID)으로 초기화되어 있으므로 따로 쓸 것이 없다
            return null;
        }

        var room = new Room(nextRoomId++);
        foreach (var cell in floodCells)
        {
            room.AddCell(cell);
            roomIdGrid[cell.x, cell.y] = room.Id;
        }
        room.BoundaryFaces.AddRange(floodBoundary);

        rooms[room.Id] = room;
        return room;
    }

    /// <summary>
    /// 새 방의 환경 수치를 <b>칸 수 가중 평균</b>으로 이어받습니다.
    /// 병합은 섞이고 분할은 같은 값에서 갈라집니다.
    ///
    /// 예전에 <b>실외였던 칸은 실외 값</b>(실외 온도·실외 침식)으로 셉니다 —
    /// 바깥 공기를 가둔 셈이므로 평균에서 빼면 안 됩니다.
    /// 이 규칙 덕분에 넓은 야외를 새로 밀폐하면 실외 값 쪽으로 희석되고,
    /// 오염된 방을 터서 넓히면 그만큼 옅어집니다.
    /// </summary>
    private void InheritState(Room room, int[,] previousGrid, Dictionary<int, Room> previousRooms)
    {
        float outdoorTemperature = TemperatureManager.instance != null
            ? TemperatureManager.instance.OutdoorTemperature
            : 20f;

        float outdoorErosion = TerrainErosionManager.instance != null
            ? TerrainErosionManager.instance.OutdoorErosion
            : 0f;

        float temperatureSum = 0f;
        float erosionSum = 0f;
        int weight = 0;

        foreach (var cell in room.Cells)
        {
            int oldId = previousGrid != null ? previousGrid[cell.x, cell.y] : OUTDOOR_ID;

            if (oldId != OUTDOOR_ID && previousRooms.TryGetValue(oldId, out Room oldRoom))
            {
                temperatureSum += oldRoom.Temperature;
                erosionSum     += oldRoom.Erosion;
            }
            else
            {
                // 실외였거나 고체를 파낸 칸 — 바깥 값이 들어온 것으로 본다
                temperatureSum += outdoorTemperature;
                erosionSum     += outdoorErosion;
            }

            weight++;
        }

        if (weight == 0) return;

        room.Temperature = temperatureSum / weight;
        room.Erosion     = erosionSum / weight;
        room.TemperatureInitialized = true;
    }

    /// <summary>
    /// 문마다 좌우로 이어진 두 공간의 방 번호를 기록합니다.
    /// 측면뷰라 문은 좌우를 잇습니다. 한쪽이 실외면 <see cref="OUTDOOR_ID"/>가 들어갑니다.
    /// </summary>
    private void RebuildDoorLinks()
    {
        var manager = DoorManager.instance;
        if (manager == null) return;

        foreach (var pair in manager.AllDoors)
        {
            Vector2Int tile = pair.Key;

            int left  = GetRoomId(tile.x - 1, tile.y);
            int right = GetRoomId(tile.x + 1, tile.y);

            // 양쪽이 모두 고체면 이어주는 것이 없다 (문 옆이 벽으로 막힌 경우)
            bool leftOpen  = IsOpenCell(tile.x - 1, tile.y);
            bool rightOpen = IsOpenCell(tile.x + 1, tile.y);
            if (!leftOpen && !rightOpen) continue;

            doorLinks[tile] = (left, right);

            if (left != OUTDOOR_ID && rooms.TryGetValue(left, out Room leftRoom))
                leftRoom.DoorLinks.Add(right);

            if (right != OUTDOOR_ID && rooms.TryGetValue(right, out Room rightRoom))
                rightRoom.DoorLinks.Add(left);
        }
    }

    #endregion

    #region ISaveModule

    /// <summary>지형(10)·건물(30) 복원 이후. 실제 매칭은 PostRestore에서 합니다.</summary>
    public int SaveOrder => 35;

    /// <summary>
    /// 방의 온도·침식을 대표 좌표와 함께 저장합니다.
    /// 방 번호는 실행마다 달라지므로 저장하지 않습니다.
    /// </summary>
    public void Capture(SaveData data)
    {
        var save = new RoomSystemSaveData();

        foreach (var pair in rooms)
        {
            Room room = pair.Value;
            save.rooms.Add(new RoomStateSaveData
            {
                representative = room.Representative,
                temperature = room.Temperature,
                erosion = room.Erosion
            });
        }

        data.roomSystem = save;
    }

    public void Restore(SaveData data) { }

    /// <summary>
    /// 모든 모듈 복원이 끝난 뒤 방을 다시 계산하고 저장된 상태를 되돌립니다.
    ///
    /// 지형과 건물이 모두 제자리에 온 다음이라야 방 구조가 맞습니다.
    /// 대표 좌표로 방을 찾으므로 지형이 같으면 정확히 같은 방에 값이 돌아갑니다.
    /// </summary>
    public void PostRestore(SaveData data)
    {
        Rebuild();

        if (data.roomSystem?.rooms == null) return;

        int matched = 0;
        foreach (var state in data.roomSystem.rooms)
        {
            Room room = GetRoom(state.representative);
            if (room == null) continue;

            room.Temperature = state.temperature;
            room.Erosion = state.erosion;
            room.TemperatureInitialized = true;
            matched++;
        }

        if (showDebugLogs)
            Debug.Log($"[RoomManager] 방 상태 복원: {matched}/{data.roomSystem.rooms.Count}개 매칭");
    }

    #endregion

    #region 칸 판정

    /// <summary>
    /// 공기가 통하는 칸인지 — 방에 포함될 수 있는 칸인지 판정합니다.
    /// 기준은 침식 BFS(<c>CanBFSPassThrough</c>)와 동일하게 맞춰 두 시스템이 갈라지지 않게 합니다.
    /// </summary>
    private bool IsOpenCell(int x, int y)
    {
        if (!IsInBounds(x, y)) return false;
        if (!gameMap.IsPassableTile(x, y)) return false;        // 지형이 있으면 막힘 (AIR·사다리만 통과)
        if (gameMap.DoesTileBlockMovement(x, y)) return false;   // 차단 건물·문
        return true;
    }

    private static bool IsInBounds(int x, int y)
        => x >= 0 && x < GameMap.MAP_WIDTH && y >= 0 && y < GameMap.MAP_HEIGHT;

    private static bool IsMapEdge(int x, int y)
        => x == 0 || x == GameMap.MAP_WIDTH - 1 || y == 0 || y == GameMap.MAP_HEIGHT - 1;

    #endregion
}
