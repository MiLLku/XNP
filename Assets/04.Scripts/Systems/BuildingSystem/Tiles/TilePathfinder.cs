using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 타일 기반 A* 길찾기 시스템
/// 직원은 2칸 높이를 차지하므로, 이동 시 수직 2칸 공간이 필요합니다.
///
/// 성능 최적화:
///   - openList: 이진 최소 힙(MinHeap) — 매 반복 O(log n) 추출
///   - openSet:  Dictionary — O(1) 멤버십 확인 (List.Contains 대체)
///   - closedSet: HashSet — O(1) 방문 확인
/// </summary>
public class TilePathfinder
{
    private GameMap gameMap;
    private const int EMPLOYEE_HEIGHT = 2;

    // ── 최소 힙 ─────────────────────────────────────────────────────────

    private class PathNode
    {
        public Vector2Int position;
        public PathNode parent;
        public float gCost;
        public float hCost;
        public float fCost => gCost + hCost;
        public int heapIndex; // 힙 내 위치 추적용

        public PathNode(Vector2Int pos)
        {
            position = pos;
        }
    }

    /// <summary>
    /// fCost 기준 이진 최소 힙.
    /// 동일 fCost는 hCost로 2차 정렬합니다.
    /// </summary>
    private class MinHeap
    {
        private readonly List<PathNode> _items = new List<PathNode>();

        public int Count => _items.Count;

        public void Push(PathNode node)
        {
            node.heapIndex = _items.Count;
            _items.Add(node);
            BubbleUp(_items.Count - 1);
        }

        public PathNode Pop()
        {
            PathNode top = _items[0];
            int last = _items.Count - 1;
            _items[0] = _items[last];
            _items[0].heapIndex = 0;
            _items.RemoveAt(last);
            if (_items.Count > 0) SiftDown(0);
            return top;
        }

        /// <summary>노드의 비용이 낮아졌을 때 힙 순서를 복원합니다.</summary>
        public void UpdateNode(PathNode node)
        {
            BubbleUp(node.heapIndex);
        }

        private void BubbleUp(int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (Compare(_items[i], _items[parent]) < 0)
                {
                    Swap(i, parent);
                    i = parent;
                }
                else break;
            }
        }

        private void SiftDown(int i)
        {
            int count = _items.Count;
            while (true)
            {
                int left  = 2 * i + 1;
                int right = 2 * i + 2;
                int smallest = i;

                if (left  < count && Compare(_items[left],  _items[smallest]) < 0) smallest = left;
                if (right < count && Compare(_items[right], _items[smallest]) < 0) smallest = right;

                if (smallest == i) break;
                Swap(i, smallest);
                i = smallest;
            }
        }

        private void Swap(int a, int b)
        {
            PathNode tmp = _items[a];
            _items[a] = _items[b];
            _items[b] = tmp;
            _items[a].heapIndex = a;
            _items[b].heapIndex = b;
        }

        private static int Compare(PathNode a, PathNode b)
        {
            int c = a.fCost.CompareTo(b.fCost);
            return c != 0 ? c : a.hCost.CompareTo(b.hCost);
        }
    }

    // ────────────────────────────────────────────────────────────────────

    public TilePathfinder(GameMap map)
    {
        gameMap = map;
    }

    /// <summary>PathOptions 없이 탐색 (기존 호환용).</summary>
    public List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal)
    {
        return FindPath(start, goal, null);
    }

    /// <summary>
    /// PathOptions를 적용하여 A* 탐색합니다.
    /// options가 null이면 제한 없는 전역 탐색을 수행합니다.
    /// </summary>
    public List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal, PathOptions options)
    {
        if (!IsValidPosition(goal))
        {
            Vector2Int? nearestValid = FindNearestValidPosition(goal);
            if (nearestValid.HasValue)
                goal = nearestValid.Value;
            else
                return null;
        }

        if (!IsValidPosition(start))
        {
            // 건설된 타일 위에 서 있을 수 있음 — 한 칸 위로 보정 시도
            Vector2Int startAbove = new Vector2Int(start.x, start.y + 1);
            if (IsValidPosition(startAbove))
            {
                Debug.LogWarning($"[Pathfinder] 시작 위치 보정: {start} → {startAbove}");
                start = startAbove;
            }
            else
            {
                Debug.LogWarning($"[Pathfinder] 시작 위치가 유효하지 않음: {start}");
                return null;
            }
        }

        var openHeap = new MinHeap();
        var openSet  = new Dictionary<Vector2Int, PathNode>();  // O(1) 멤버십
        var closedSet = new HashSet<Vector2Int>();
        var allNodes  = new Dictionary<Vector2Int, PathNode>();

        PathNode startNode = new PathNode(start) { gCost = 0, hCost = GetHeuristic(start, goal) };
        openHeap.Push(startNode);
        openSet[start]  = startNode;
        allNodes[start] = startNode;

        int iterations  = 0;
        const int MAX   = 10000;

        while (openHeap.Count > 0 && iterations < MAX)
        {
            iterations++;
            PathNode current = openHeap.Pop();
            openSet.Remove(current.position);

            if (current.position == goal)
                return ReconstructPath(current);

            closedSet.Add(current.position);

            foreach (Vector2Int neighbor in GetNeighbors(current.position))
            {
                if (closedSet.Contains(neighbor)) continue;

                float moveCost = GetMovementCost(current.position, neighbor, options);
                if (moveCost >= float.MaxValue) continue; // 완전 차단 타일 건너뜀

                float tentativeG = current.gCost + moveCost;

                PathNode neighborNode;
                if (!allNodes.TryGetValue(neighbor, out neighborNode))
                {
                    neighborNode = new PathNode(neighbor);
                    allNodes[neighbor] = neighborNode;
                }

                if (tentativeG >= neighborNode.gCost && openSet.ContainsKey(neighbor))
                    continue;

                neighborNode.parent = current;
                neighborNode.gCost  = tentativeG;
                neighborNode.hCost  = GetHeuristic(neighbor, goal);

                if (openSet.ContainsKey(neighbor))
                {
                    openHeap.UpdateNode(neighborNode);
                }
                else
                {
                    openHeap.Push(neighborNode);
                    openSet[neighbor] = neighborNode;
                }
            }
        }

        if (iterations >= MAX)
            Debug.LogWarning($"[Pathfinder] 최대 반복 횟수 초과: {start} -> {goal}");
        else
            Debug.LogWarning($"[Pathfinder] 경로 없음: {start} -> {goal}");

        return null;
    }

    private Vector2Int? FindNearestValidPosition(Vector2Int target)
    {
        for (int radius = 1; radius <= 3; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (Mathf.Abs(dx) != radius && Mathf.Abs(dy) != radius) continue;

                    Vector2Int candidate = target + new Vector2Int(dx, dy);
                    if (IsValidPosition(candidate))
                        return candidate;
                }
            }
        }
        return null;
    }

    private List<Vector2Int> ReconstructPath(PathNode endNode)
    {
        var path = new List<Vector2Int>();
        PathNode current = endNode;
        while (current != null)
        {
            path.Add(current.position);
            current = current.parent;
        }
        path.Reverse();
        if (path.Count > 0) path.RemoveAt(0);
        return path;
    }

    private float GetHeuristic(Vector2Int from, Vector2Int to)
    {
        return Mathf.Abs(to.x - from.x) + Mathf.Abs(to.y - from.y);
    }

    private List<Vector2Int> GetNeighbors(Vector2Int pos)
    {
        var neighbors = new List<Vector2Int>();

        Vector2Int[] horizontalDirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0) };

        foreach (var dir in horizontalDirs)
        {
            TryAdd(neighbors, pos, pos + dir);
            TryAdd(neighbors, pos, pos + dir + new Vector2Int(0,  1));
            TryAdd(neighbors, pos, pos + dir + new Vector2Int(0,  2));
            TryAdd(neighbors, pos, pos + dir + new Vector2Int(0, -1));
            TryAdd(neighbors, pos, pos + dir + new Vector2Int(0, -2));
        }

        TryAdd(neighbors, pos, pos + new Vector2Int(0,  1));
        TryAdd(neighbors, pos, pos + new Vector2Int(0, -1));

        return neighbors;
    }

    private void TryAdd(List<Vector2Int> list, Vector2Int from, Vector2Int to)
    {
        if (CanMoveTo(from, to)) list.Add(to);
    }

    private bool CanMoveTo(Vector2Int from, Vector2Int to)
    {
        if (!IsInBounds(to))       return false;
        if (!IsValidPosition(to))  return false;

        int heightDiff     = to.y - from.y;
        int horizontalDiff = Mathf.Abs(to.x - from.x);

        if (heightDiff == 0 && horizontalDiff == 1)
            return CanWalkHorizontally(from, to);

        if (horizontalDiff == 1 && Mathf.Abs(heightDiff) <= 2)
            return CanMoveDiagonal(from, to, heightDiff);

        if (horizontalDiff == 0 && Mathf.Abs(heightDiff) == 1)
            return heightDiff > 0 ? CanClimbUp(from, to) : CanClimbDown(from, to);

        return false;
    }

    private bool CanMoveDiagonal(Vector2Int from, Vector2Int to, int heightDiff)
    {
        if (!IsValidPosition(to))    return false;
        if (Mathf.Abs(heightDiff) > 2) return false;

        if (heightDiff > 0)
        {
            for (int y = from.y + 1; y <= to.y; y++)
                if (!IsSpaceClear(to.x, y + EMPLOYEE_HEIGHT - 1)) return false;
        }

        return true;
    }

    private bool CanWalkHorizontally(Vector2Int from, Vector2Int to)
        => IsValidPosition(to);

    private bool CanClimbUp(Vector2Int from, Vector2Int to)
    {
        FloorTile fromLadder = FloorTile.GetFloorTileAt(new Vector2Int(from.x, from.y + 1));
        FloorTile toLadder   = FloorTile.GetFloorTileAt(to);

        bool hasLadder = (fromLadder != null && fromLadder.AllowsVerticalMovement()) ||
                         (toLadder   != null && toLadder.AllowsVerticalMovement());
        if (!hasLadder) return false;

        for (int i = 1; i <= EMPLOYEE_HEIGHT; i++)
            if (!IsSpaceClear(to.x, to.y + i)) return false;

        return true;
    }

    private bool CanClimbDown(Vector2Int from, Vector2Int to)
    {
        FloorTile currentLadder = FloorTile.GetFloorTileAt(new Vector2Int(from.x, from.y + 1));
        FloorTile toLadder      = FloorTile.GetFloorTileAt(to);

        bool hasLadder = (currentLadder != null && currentLadder.AllowsVerticalMovement()) ||
                         (toLadder      != null && toLadder.AllowsVerticalMovement());

        if (hasLadder)        return IsValidPosition(to);
        if (from.y - to.y == 1) return IsValidPosition(to);
        return false;
    }

    private bool IsSpaceClear(int x, int y)
    {
        if (!IsInBounds(new Vector2Int(x, y))) return false;

        int tileId = gameMap.TileGrid[x, y];
        if (tileId == 0)
        {
            if (gameMap.DoesTileBlockMovement(x, y)) return false;
            return true;
        }

        FloorTile floorTile = FloorTile.GetFloorTileAt(new Vector2Int(x, y));
        if (floorTile != null && floorTile.IsPassable()) return true;

        return false;
    }

    private bool CanStandAt(int x, int y)
    {
        if (!IsInBounds(new Vector2Int(x, y))) return false;

        int groundY = y - 1;
        if (groundY < 0) return false;

        int groundTileId = gameMap.TileGrid[x, groundY];
        bool hasFloorTile     = FloorTile.HasFloorTileAt(new Vector2Int(x, groundY));
        // IsFloorSupport: 완성된 바닥 건물만 true. 건설 예정지(ConstructionSite)는 false.
        // → 직원이 건설 예정지 위로 올라가려는 오류 방지
        bool hasConstructed   = gameMap.IsFloorSupport(x, groundY);
        bool hasGround        = groundTileId != 0 || hasFloorTile || hasConstructed;

        if (!hasGround)
        {
            FloorTile ladder = FloorTile.GetFloorTileAt(new Vector2Int(x, groundY));
            if (ladder == null || !ladder.AllowsVerticalMovement())
            {
                FloorTile currentLadder = FloorTile.GetFloorTileAt(new Vector2Int(x, y));
                if (currentLadder == null || !currentLadder.AllowsVerticalMovement())
                    return false;
            }
        }

        for (int i = 0; i < EMPLOYEE_HEIGHT; i++)
        {
            int checkY = y + i;
            if (!IsInBounds(new Vector2Int(x, checkY))) return false;
            if (!IsSpaceClear(x, checkY))               return false;
        }

        return true;
    }

    public bool IsValidPosition(Vector2Int pos) => CanStandAt(pos.x, pos.y);

    private float GetMovementCost(Vector2Int from, Vector2Int to, PathOptions options = null)
    {
        float baseCost = 1f;

        FloorTile floorTile = FloorTile.GetFloorTileAt(to);
        if (floorTile != null)
        {
            float speedMult = floorTile.GetMovementSpeedMultiplier();
            if (speedMult > 0) baseCost /= speedMult;
        }

        int heightDifference = Mathf.Abs(to.y - from.y);
        if (heightDifference > 0) baseCost += heightDifference * 1f;

        if (options != null && ZoneManager.instance != null)
        {
            int  toZoneId = ZoneManager.instance.GetZoneIdAt(to);
            Zone toZone   = ZoneManager.instance.GetZone(toZoneId);

            // Restricted 구역 완전 차단
            if (options.blockRestrictedZones &&
                toZone != null && toZone.zoneType == ZoneType.Restricted)
            {
                return float.MaxValue;
            }

            // 허용 구역 외 타일 완전 차단
            // toZoneId == -1 (구역 미지정 = 중립 타일)은 항상 통과 허용
            if (options.allowedZoneIds != null && toZoneId >= 0 &&
                !options.allowedZoneIds.Contains(toZoneId))
            {
                return float.MaxValue;
            }
        }

        return baseCost;
    }

    private bool IsInBounds(Vector2Int pos)
    {
        return pos.x >= 0 && pos.x < GameMap.MAP_WIDTH &&
               pos.y >= 0 && pos.y < GameMap.MAP_HEIGHT;
    }
}
