using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 건물 배치 전 이동 공간 연결성을 검사하는 유틸리티 클래스.
///
/// 알고리즘:
///   1. 현재 GameMap에서 "걸을 수 있는 타일" 집합을 구성 (건물 footprint 제외)
///   2. BFS로 한 지점에서 도달 가능한 타일 수를 센다
///   3. 전체 걸을 수 있는 타일 수보다 적으면 → 분단 발생
///
/// 걸을 수 있는 타일의 조건 (TilePathfinder.CanStandAt과 동일):
///   - 발 타일 (foot): AIR이고 이동 차단이 아님
///   - 몸통 타일 (foot+1): AIR이고 이동 차단이 아님
///   - 바닥 타일 (foot-1): 고체 타일 or FloorTile or 건설된 바닥
/// </summary>
public static class ArticulationPointChecker
{
    private const int EMPLOYEE_HEIGHT = 2;

    // 수평 이동 시 허용하는 최대 높이 차 (TilePathfinder와 동일)
    private static readonly int[] DX = { 1, -1 };
    private static readonly int[] DY_STEP = { 0, 1, 2, -1, -2 };

    /// <summary>
    /// 건물을 배치했을 때 현재 활성 직원 중 누군가가 다른 직원과
    /// 이동 경로상 분리되는지 확인합니다.
    ///
    /// 직원이 0명이거나 1명이면 항상 false를 반환합니다.
    /// </summary>
    /// <param name="gameMap">현재 GameMap</param>
    /// <param name="origin">건물 왼쪽 아래 그리드 좌표</param>
    /// <param name="size">건물 크기 (타일 단위)</param>
    /// <returns>분단이 발생하면 true</returns>
    public static bool WouldIsolateEmployee(GameMap gameMap, Vector3Int origin, Vector2Int size)
    {
        if (gameMap == null) return false;
        if (EmployeeManager.instance == null) return false;

        var employees = EmployeeManager.instance.AllEmployees;
        if (employees == null || employees.Count <= 1) return false;

        // 건물 footprint 집합 구성
        var footprint = BuildFootprint(origin, size);

        // 걸을 수 있는 타일 집합 구성 (footprint 제외)
        var walkable = BuildWalkableSet(gameMap, footprint);
        if (walkable.Count == 0) return false;

        // 첫 번째 직원의 발 위치에서 BFS 시작점 결정
        Vector2Int? startTile = FindEmployeeStartTile(employees, walkable);
        if (!startTile.HasValue) return false;

        // BFS로 도달 가능한 타일 수 계산
        int reachable = BFS(startTile.Value, walkable);

        // 도달 가능 수 < 전체 walkable 수  →  분단 발생
        if (reachable < walkable.Count) return true;

        // 모든 직원이 walkable 집합에 포함되어 있는지 추가 확인
        // (직원 자신이 footprint 위에 있는 극단 케이스 대비)
        foreach (var emp in employees)
        {
            if (emp == null) continue;
            var movement = emp.GetComponent<EmployeeMovement>();
            if (movement == null) continue;

            Vector2Int foot = movement.GetFootTile();
            if (!walkable.Contains(foot))
                return true; // 직원이 접근 불가 구역에 있음
        }

        return false;
    }

    /// <summary>
    /// 건물을 배치했을 때 이동 가능 공간 전체가 분단되는지 확인합니다.
    /// WouldIsolateEmployee보다 엄격한 검사입니다.
    /// </summary>
    public static bool WouldDisconnect(GameMap gameMap, Vector3Int origin, Vector2Int size)
    {
        if (gameMap == null) return false;

        var footprint = BuildFootprint(origin, size);
        var walkable  = BuildWalkableSet(gameMap, footprint);
        if (walkable.Count == 0) return false;

        Vector2Int start = GetAny(walkable);
        int reachable = BFS(start, walkable);

        return reachable < walkable.Count;
    }

    // ─── 내부 헬퍼 ───────────────────────────────────────────────────────

    private static HashSet<Vector2Int> BuildFootprint(Vector3Int origin, Vector2Int size)
    {
        var set = new HashSet<Vector2Int>();
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
                set.Add(new Vector2Int(origin.x + x, origin.y + y));
        return set;
    }

    /// <summary>
    /// 맵 전체에서 "서 있을 수 있는" 타일을 수집합니다.
    /// footprint에 포함된 타일은 막힌 것으로 간주합니다.
    /// </summary>
    private static HashSet<Vector2Int> BuildWalkableSet(GameMap gameMap, HashSet<Vector2Int> footprint)
    {
        var walkable = new HashSet<Vector2Int>();

        // MAP_HEIGHT-EMPLOYEE_HEIGHT : 몸통 타일이 맵 밖으로 나가지 않도록
        for (int x = 0; x < GameMap.MAP_WIDTH; x++)
        {
            for (int y = 1; y < GameMap.MAP_HEIGHT - EMPLOYEE_HEIGHT; y++)
            {
                var pos = new Vector2Int(x, y);
                if (!footprint.Contains(pos) && IsWalkable(gameMap, pos, footprint))
                    walkable.Add(pos);
            }
        }

        return walkable;
    }

    /// <summary>
    /// 해당 타일에 2칸 높이 직원이 서 있을 수 있는지 확인합니다.
    /// </summary>
    private static bool IsWalkable(GameMap gameMap, Vector2Int pos, HashSet<Vector2Int> blocked)
    {
        if (blocked.Contains(pos)) return false;

        // 발 타일: AIR이고 이동 차단이 아님
        if (!IsInBounds(pos)) return false;
        if (gameMap.TileGrid[pos.x, pos.y] != 0) return false;
        if (gameMap.DoesTileBlockMovement(pos.x, pos.y)) return false;

        // 몸통 타일 (foot+1)
        var body = new Vector2Int(pos.x, pos.y + 1);
        if (blocked.Contains(body)) return false;
        if (!IsInBounds(body)) return false;
        if (gameMap.TileGrid[body.x, body.y] != 0) return false;
        if (gameMap.DoesTileBlockMovement(body.x, body.y)) return false;

        // 바닥 (foot-1): 고체 타일 or FloorTile or 건설된 바닥
        var ground = new Vector2Int(pos.x, pos.y - 1);
        if (!IsInBounds(ground)) return false;

        bool hasSolid     = gameMap.TileGrid[ground.x, ground.y] != 0;
        bool hasFloorTile = FloorTile.HasFloorTileAt(ground);
        bool hasBuiltFloor = gameMap.IsTileOccupied(ground.x, ground.y)
                          && !gameMap.DoesTileBlockMovement(ground.x, ground.y);

        if (hasSolid || hasFloorTile || hasBuiltFloor) return true;

        // 사다리가 있으면 바닥 없어도 서 있을 수 있음
        FloorTile ladder = FloorTile.GetFloorTileAt(pos);
        return ladder != null && ladder.AllowsVerticalMovement();
    }

    /// <summary>
    /// BFS로 start에서 도달 가능한 walkable 타일 수를 반환합니다.
    /// </summary>
    private static int BFS(Vector2Int start, HashSet<Vector2Int> walkable)
    {
        var visited = new HashSet<Vector2Int> { start };
        var queue   = new Queue<Vector2Int>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();

            // 수평 + 단차 이동
            foreach (int dx in DX)
            {
                foreach (int dy in DY_STEP)
                {
                    var nb = new Vector2Int(cur.x + dx, cur.y + dy);
                    if (walkable.Contains(nb) && visited.Add(nb))
                        queue.Enqueue(nb);
                }
            }

            // 수직 이동 (사다리)
            var up   = new Vector2Int(cur.x, cur.y + 1);
            var down = new Vector2Int(cur.x, cur.y - 1);
            if (walkable.Contains(up)   && visited.Add(up))   queue.Enqueue(up);
            if (walkable.Contains(down) && visited.Add(down)) queue.Enqueue(down);
        }

        return visited.Count;
    }

    /// <summary>
    /// 직원 목록에서 walkable에 포함된 첫 번째 발 위치를 반환합니다.
    /// </summary>
    private static Vector2Int? FindEmployeeStartTile(
        System.Collections.Generic.List<Employee> employees,
        HashSet<Vector2Int> walkable)
    {
        foreach (var emp in employees)
        {
            if (emp == null) continue;
            var movement = emp.GetComponent<EmployeeMovement>();
            if (movement == null) continue;

            Vector2Int foot = movement.GetFootTile();
            if (walkable.Contains(foot)) return foot;
        }

        // 직원 위치가 walkable에 없으면 walkable의 임의 타일 사용
        return walkable.Count > 0 ? GetAny(walkable) : (Vector2Int?)null;
    }

    private static bool IsInBounds(Vector2Int pos)
    {
        return pos.x >= 0 && pos.x < GameMap.MAP_WIDTH &&
               pos.y >= 0 && pos.y < GameMap.MAP_HEIGHT;
    }

    private static Vector2Int GetAny(HashSet<Vector2Int> set)
    {
        foreach (var item in set) return item;
        return Vector2Int.zero;
    }
}
