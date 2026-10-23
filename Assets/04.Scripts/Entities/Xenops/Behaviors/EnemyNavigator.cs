using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 적 길찾기 — 직원과 같은 타일 이동 규칙(옆 걸음 ±2칸, 사다리)으로 목표까지 경로를 잡고,
/// 물리로 움직이는 적 행동(그래플러·스피터·침식 사수)에 "지금 어느 쪽으로 가라"를 알려준다.
///
/// 목표 고르는 순서:
///   1. 걸어서 닿는 가장 가까운 직원
///   2. (건물 공격 가능 개체) 닿는 직원이 없으면 — 건물을 부숴서라도 직원에게 가는 길(돌파 경로)을 잡고,
///      그 길에 처음 놓인 건물을 부술 대상으로 삼는다
///   3. 그것도 없으면 걸어서 닿는 가장 가까운 플레이어 건물
/// 닫힌 문도 그 길 위에 있으면 부술 대상이다 (적은 문을 열 수 없다).
///
/// 성능: 경로는 REPATH_INTERVAL마다(개체마다 시점을 흩뜨려) 다시 계산하고, 그 사이에는 저장된 경로를 따른다.
/// </summary>
public class EnemyNavigator : MonoBehaviour
{
    private const float REPATH_INTERVAL = 1.5f;
    private const float WAYPOINT_REACH_X = 0.2f;
    private const float CLIMB_SPEED = 2.5f;

    private static TilePathfinder _walkFinder, _breachFinder;
    private static GameMap _finderMap;

    private Xenops _xenops;
    private Rigidbody2D _rb;
    private float _repathTimer;
    private float _baseGravity = 1f;
    private bool _climbing;
    private readonly List<Vector2Int> _path = new List<Vector2Int>();

    /// <summary>노리는 직원 (걸어서 닿거나, 돌파해서 가려는 대상)</summary>
    public Employee TargetEmployee { get; private set; }

    /// <summary>지금 부술 건물 (길을 막은 건물, 또는 직원이 없을 때 노리는 건물). 없으면 null</summary>
    public Building TargetBuilding { get; private set; }

    /// <summary>따라갈 경로가 있는지</summary>
    public bool HasPath => _path.Count > 0;

    private void Awake()
    {
        _xenops = GetComponent<Xenops>();
        _rb = GetComponent<Rigidbody2D>();
        if (_rb != null) _baseGravity = _rb.gravityScale;
        _repathTimer = Random.Range(0f, REPATH_INTERVAL); // 개체마다 계산 시점을 흩뜨린다
    }

    private static Vector2Int FootOf(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));

    private Vector2Int Foot => FootOf(transform.position);

    private static bool EnsureFinders()
    {
        var map = MapGenerator.instance != null ? MapGenerator.instance.GameMapInstance : null;
        if (map == null) return false;
        if (_finderMap != map)
        {
            _finderMap = map;
            _walkFinder = new TilePathfinder(map);
            _breachFinder = new TilePathfinder(map) { BreachMode = true };
        }
        return true;
    }

    /// <summary>매 프레임 호출 — 주기적으로 목표·경로를 갱신하고 지나간 경유점을 지운다</summary>
    public void Tick(float dt)
    {
        _repathTimer -= dt;
        bool targetGone = (TargetEmployee != null && TargetEmployee.State == EmployeeState.Dead)
                       || (TargetBuilding == null && TargetEmployee == null);
        if (_repathTimer <= 0f || targetGone)
        {
            _repathTimer = REPATH_INTERVAL;
            Replan();
        }

        // 지나간 경유점 제거
        var foot = Foot;
        while (_path.Count > 0)
        {
            var wp = _path[0];
            bool reachedX = Mathf.Abs(wp.x + 0.5f - transform.position.x) <= WAYPOINT_REACH_X;
            if (wp == foot || (reachedX && wp.y <= foot.y)) _path.RemoveAt(0);
            else break;
        }
    }

    private void Replan()
    {
        _path.Clear();
        TargetEmployee = null;
        TargetBuilding = null;
        if (!EnsureFinders() || EmployeeManager.instance == null) return;

        var foot = Foot;
        var reach = ReachabilityMap.Current;
        bool canBreach = _xenops == null || _xenops.Data == null || _xenops.Data.canBreachBuildings;

        // 직원을 가까운 순으로
        var staff = new List<Employee>();
        foreach (var e in EmployeeManager.instance.AllEmployees)
            if (e != null && e.State != EmployeeState.Dead) staff.Add(e);
        staff.Sort((a, b) => Vector2.Distance(transform.position, a.transform.position)
                              .CompareTo(Vector2.Distance(transform.position, b.transform.position)));

        // 1. 걸어서 닿는 직원
        foreach (var e in staff)
        {
            var ef = FootOf(e.transform.position);
            if (reach != null && !reach.IsReachable(foot, ef)) continue;
            var p = _walkFinder.FindPath(foot, ef);
            if (p == null) continue;
            TargetEmployee = e;
            _path.AddRange(p);
            TargetBuilding = FirstClosedDoorOnPath(); // 길 위의 닫힌 문은 부수고 지나간다
            return;
        }
        if (!canBreach) return;

        // 2. 돌파 경로 — 가장 가까운 직원에게, 처음 막는 건물을 부순다
        foreach (var e in staff)
        {
            var p = _breachFinder.FindPath(foot, FootOf(e.transform.position));
            if (p == null) continue;
            TargetEmployee = e;
            _path.AddRange(p);
            TargetBuilding = FirstBlockingBuildingOnPath();
            if (TargetBuilding != null) TrimPathBefore(TargetBuilding);
            return;
        }

        // 3. 걸어서 닿는 가장 가까운 플레이어 건물
        Building best = null; float bestD = float.MaxValue; List<Vector2Int> bestPath = null;
        foreach (var b in UnityEngine.Object.FindObjectsByType<Building>(FindObjectsSortMode.None))
        {
            if (b == null || b.buildingData == null || !b.IsFunctional || b.buildingData.maxHealth <= 0) continue;
            float d = Vector2.Distance(transform.position, b.transform.position);
            if (d >= bestD) continue;
            var bf = FootOf(b.transform.position);
            if (reach != null && !reach.IsReachable(foot, bf) && !reach.IsReachable(foot, bf + Vector2Int.up)) continue;
            var p = _walkFinder.FindPath(foot, bf);
            if (p == null) continue;
            best = b; bestD = d; bestPath = p;
        }
        if (best != null) { TargetBuilding = best; _path.AddRange(bestPath); }
    }

    private Building FirstClosedDoorOnPath()
    {
        foreach (var c in _path)
        {
            var b = Building.GetBuildingAt(c);
            if (b == null) continue;
            var door = b.GetComponent<Door>();
            if (door != null && !door.IsPassable) return b;
        }
        return null;
    }

    private Building FirstBlockingBuildingOnPath()
    {
        var map = _finderMap;
        foreach (var c in _path)
        {
            for (int dy = 0; dy <= 1; dy++)
            {
                var cell = c + new Vector2Int(0, dy);
                if (!TilePathfinder.IsBreachable(cell)) continue;
                var b = Building.GetBuildingAt(cell);
                bool blocks = map.DoesTileBlockMovement(cell.x, cell.y) || map.TileGrid[cell.x, cell.y] != 0
                              || (b.GetComponent<Door>() != null && !b.GetComponent<Door>().IsPassable);
                if (blocks) return b;
            }
        }
        return null;
    }

    /// <summary>부술 건물 앞에서 멈추도록 그 건물 칸부터 뒤는 지운다</summary>
    private void TrimPathBefore(Building b)
    {
        for (int i = 0; i < _path.Count; i++)
            if (Building.GetBuildingAt(_path[i]) == b || Building.GetBuildingAt(_path[i] + Vector2Int.up) == b)
            {
                _path.RemoveRange(i, _path.Count - i);
                return;
            }
    }

    /// <summary>
    /// 경로를 따라 몸을 움직인다 — 가로 속도, 높은 경유점이면 도약, 바로 위면 사다리 오르기.
    /// 경로가 없으면 false (호출한 쪽이 예전 방식대로 움직인다).
    /// </summary>
    public bool Steer(float moveSpeed)
    {
        if (_rb == null || _path.Count == 0) { StopClimb(); return false; }

        var wp = _path[0];
        var foot = Foot;
        float dx = wp.x + 0.5f - transform.position.x;

        // 바로 위 (사다리) — 중력을 끄고 올라간다
        if (wp.x == foot.x && wp.y > foot.y)
        {
            _climbing = true;
            _rb.gravityScale = 0f;
            _rb.linearVelocity = new Vector2(dx * 4f, CLIMB_SPEED);
            return true;
        }
        StopClimb();

        float dir = Mathf.Abs(dx) > 0.05f ? Mathf.Sign(dx) : 0f;
        float vy = _rb.linearVelocity.y;
        bool grounded = Mathf.Abs(vy) < 0.05f;
        if (wp.y > foot.y && grounded)
        {
            float h = wp.y - foot.y + 0.4f;
            float g = Mathf.Abs(Physics2D.gravity.y * _rb.gravityScale);
            vy = Mathf.Sqrt(2f * g * h);
        }
        _rb.linearVelocity = new Vector2(dir * moveSpeed, vy);
        return true;
    }

    private void StopClimb()
    {
        if (!_climbing || _rb == null) return;
        _climbing = false;
        _rb.gravityScale = _baseGravity;
    }

    /// <summary>건물까지의 거리 (가장 가까운 칸 기준)</summary>
    public static float DistanceToBuilding(Vector3 from, Building b)
    {
        if (b == null) return float.MaxValue;
        var size = b.buildingData != null ? b.buildingData.size : Vector2Int.one;
        Vector3 p = b.transform.position;
        float cx = Mathf.Clamp(from.x, p.x, p.x + size.x);
        float cy = Mathf.Clamp(from.y, p.y, p.y + size.y);
        return Vector2.Distance(from, new Vector2(cx, cy));
    }

    private void OnDisable() => StopClimb();
}
