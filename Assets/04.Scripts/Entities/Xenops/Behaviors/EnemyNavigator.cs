using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

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
/// 발판 깔기(canBuildFloor): 2의 길은 "발밑이 빈 칸에 침식 발판을 깔고 지나가기"도 섞어 고른다 —
/// 넘지 못하는 낭떠러지엔 다리를, 뛰어넘지 못하는 벽 앞엔 계단을 쌓는다. 벽을 부수는 것과 비용을 견줘 싼 쪽.
/// 깔아야 할 칸 앞에 오면 멈춰 BUILD_TIME 동안 깐 뒤 지나간다. 습격당 개수 제한은 ErosionFloorField.
///
/// 굴착(canDigTerrain): 위 어느 것도 없으면(막힌 굴) 자연 지형을 파며 직원 쪽으로 간다 — 칸마다 DIG_TIME, 자원 없음.
///
/// 성능: 경로는 REPATH_INTERVAL마다(개체마다 시점을 흩뜨려) 다시 계산하고, 그 사이에는 저장된 경로를 따른다.
/// </summary>
public class EnemyNavigator : MonoBehaviour
{
    private const float REPATH_INTERVAL = 1.5f;
    private const float WAYPOINT_REACH_X = 0.2f;
    private const float CLIMB_SPEED = 2.5f;
    private const float BUILD_TIME = 2f;   // shortcut: 임시 수치 — 발판 한 칸 까는 시간
    private const float BUILD_REACH = 2.5f;
    private const float REPATH_RETRY = 0.3f;
    private const int MAX_BREACH_TARGETS = 3;
    private const float DIG_TIME = 1.5f;   // shortcut: 임시 수치 — 지형 한 칸 파는 시간
    private const float DIG_REACH = 4f;    // 뛰어오를 칸의 머리 높이까지 닿게

    // [돌파][발판] 조합별 길찾기
    private static TilePathfinder _walkFinder, _breachFinder, _buildFinder, _breachBuildFinder, _digFinder;
    private static GameMap _finderMap;

    private Xenops _xenops;
    private Rigidbody2D _rb;
    private float _repathTimer;
    private float _baseGravity = 1f;
    private bool _climbing;
    private float _buildTimer;
    private float _digTimer;
    private bool _digPath; // 지금 경로가 굴착 경로인지 (걷는 길에서는 파지 않는다)
    private readonly List<Vector2Int> _path = new List<Vector2Int>();

    /// <summary>노리는 직원 (걸어서 닿거나, 돌파해서 가려는 대상)</summary>
    public Employee TargetEmployee { get; private set; }

    /// <summary>지금 부술 건물 (길을 막은 건물, 또는 직원이 없을 때 노리는 건물). 없으면 null</summary>
    public Building TargetBuilding { get; private set; }

    /// <summary>따라갈 경로가 있는지</summary>
    public bool HasPath => _path.Count > 0;

    /// <summary>다음 경유점에 서려면 먼저 깔아야 하는 발판 칸. 없으면 null</summary>
    /// <summary>다음 경유점으로 가려면 먼저 파야 하는 지형 칸 (그 칸 기둥에서 몸이 지나갈 칸). 없으면 null</summary>
    public Vector2Int? PendingDig
    {
        get
        {
            if (!_digPath || _path.Count == 0 || _digFinder == null) return null;
            var wp = _path[0];
            var foot = Foot;
            // 경유점 칸의 몸(발판 칸은 남긴다), 옆으로 지나가거나 떨어지는 높이까지
            for (int y = wp.y; y <= Mathf.Max(foot.y, wp.y) + 1; y++)
                if (_digFinder.IsDiggable(new Vector2Int(wp.x, y))) return new Vector2Int(wp.x, y);
            // 뛰어오를 때 제 머리 위
            for (int y = foot.y + 2; y <= wp.y + 1; y++)
                if (_digFinder.IsDiggable(new Vector2Int(foot.x, y))) return new Vector2Int(foot.x, y);
            return null;
        }
    }

    public Vector2Int? PendingFloor
        => _path.Count > 0 && _buildFinder != null && _buildFinder.NeedsBuiltFloor(_path[0]) ? _path[0] + Vector2Int.down : (Vector2Int?)null;

    private bool InBuildReach(Vector2Int cell, float reach = BUILD_REACH)
        => Vector2.Distance(transform.position, new Vector2(cell.x + 0.5f, cell.y + 0.5f)) <= reach;

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
            _buildFinder = new TilePathfinder(map) { BuildMode = true };
            _breachBuildFinder = new TilePathfinder(map) { BreachMode = true, BuildMode = true };
            _digFinder = new TilePathfinder(map) { BreachMode = true, DigMode = true };
        }
        return true;
    }

    /// <summary>매 프레임 호출 — 주기적으로 목표·경로를 갱신하고 지나간 경유점을 지운다</summary>
    public void Tick(float dt)
    {
        _repathTimer -= dt;
        // 노리던 직원이 죽었거나 건물이 부서졌으면 바로 다시 (목표를 못 찾은 상태에선 매 프레임 다시 돌리지 않는다)
        bool targetGone = (TargetEmployee != null && TargetEmployee.State == EmployeeState.Dead)
                       || (!ReferenceEquals(TargetBuilding, null) && TargetBuilding == null);
        // 굴착 경로는 끝까지 판다 — 직원이 움직일 때마다 다시 짜면 굴이 이리저리 갈팡질팡한다
        bool keepDigging = _digPath && _path.Count > 0 && !OffPath();
        if ((_repathTimer <= 0f && !keepDigging) || targetGone)
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

        // 앞을 막은 지형은 파낸다
        var dig = PendingDig;
        if (dig.HasValue && InBuildReach(dig.Value, DIG_REACH))
        {
            _digTimer += dt;
            if (_digTimer >= DIG_TIME)
            {
                _digTimer = 0f;
                DigOut(dig.Value);
            }
            return;
        }
        _digTimer = 0f;

        // 다음 칸 발밑이 비었으면 멈춰서 발판을 깐다
        var floor = PendingFloor;
        if (floor.HasValue && InBuildReach(floor.Value))
        {
            _buildTimer += dt;
            if (_buildTimer >= BUILD_TIME)
            {
                _buildTimer = 0f;
                var field = ErosionFloorField.instance;
                if (field == null || !field.TryPlace(floor.Value)) _repathTimer = 0f; // 못 깔았으면 길을 다시
            }
        }
        else _buildTimer = 0f;
    }

    /// <summary>
    /// 경로 계산 출발 칸 — 발 위치 칸에 설 수 없으면(도약·낙하 중, 비탈) 가까운 설 수 있는 칸으로 맞춘다.
    /// 공중에서 계산하면 출발 칸이 무효라 경로가 통째로 실패했다.
    /// </summary>
    private Vector2Int? StandCell()
    {
        var foot = Foot;
        if (_walkFinder.IsValidPosition(foot)) return foot;
        for (int dy = 1; dy <= 4; dy++)
            if (_walkFinder.IsValidPosition(foot + Vector2Int.down * dy)) return foot + Vector2Int.down * dy;
        foreach (var d in new[] { Vector2Int.up, Vector2Int.left, Vector2Int.right, new Vector2Int(-1, -1), new Vector2Int(1, -1) })
            if (_walkFinder.IsValidPosition(foot + d)) return foot + d;
        return null;
    }

    private void Replan()
    {
        if (!EnsureFinders() || EmployeeManager.instance == null) return;

        // 공중이라 설 칸을 못 찾으면 지금 길을 유지하고 곧 다시
        var stand = StandCell();
        if (!stand.HasValue) { _repathTimer = REPATH_RETRY; return; }
        var foot = stand.Value;

        _path.Clear();
        _digPath = false;
        TargetEmployee = null;
        TargetBuilding = null;
        var reach = ReachabilityMap.Current;
        bool canBreach = _xenops == null || _xenops.Data == null || _xenops.Data.canBreachBuildings;
        bool canBuild = (_xenops == null || _xenops.Data == null || _xenops.Data.canBuildFloor)
                        && ErosionFloorField.instance != null && ErosionFloorField.instance.CanBuildMore;

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
        // 2. 돌파·발판 경로 — 가장 가까운 직원에게, 처음 막는 건물을 부수고 빈 발밑엔 발판을 깐다
        var finder = canBreach ? (canBuild ? _breachBuildFinder : _breachFinder) : (canBuild ? _buildFinder : null);
        // 실패한 탐색은 반복 상한까지 돌므로(발판 모드는 하늘도 칸이다) 가까운 몇 명만 본다
        for (int i = 0; finder != null && i < Mathf.Min(staff.Count, MAX_BREACH_TARGETS); i++)
        {
            var e = staff[i];
            var p = finder.FindPath(foot, FootOf(e.transform.position));
            if (p == null || !WithinFloorBudget(p)) continue;
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
        if (best != null) { TargetBuilding = best; _path.AddRange(bestPath); return; }

        // 4. 막힌 굴 — 자연 지형을 파며 가장 가까운 직원 쪽으로 (길 위의 건물은 부순다)
        bool canDig = _xenops == null || _xenops.Data == null || _xenops.Data.canDigTerrain;
        for (int i = 0; canDig && i < Mathf.Min(staff.Count, MAX_BREACH_TARGETS); i++)
        {
            var p = _digFinder.FindPath(foot, FootOf(staff[i].transform.position));
            if (p == null) continue;
            TargetEmployee = staff[i];
            _path.AddRange(p);
            _digPath = true;
            TargetBuilding = FirstBlockingBuildingOnPath();
            if (TargetBuilding != null) TrimPathBefore(TargetBuilding);
            return;
        }
    }

    /// <summary>지형 한 칸을 없앤다 (침식 사수 탄처럼 자원 없음)</summary>
    private void DigOut(Vector2Int c)
    {
        var mr = MapGenerator.instance != null ? MapGenerator.instance.MapRendererInstance : null;
        if (_finderMap == null || mr == null) return;
        if (WorkSystemManager.instance != null && WorkSystemManager.instance.IsTileUnderWork(new Vector3Int(c.x, c.y, 0))) return;
        _finderMap.SetTile(c.x, c.y, 0);
        _finderMap.UnmarkTileOccupied(c.x, c.y);
        mr.UpdateTileVisual(c.x, c.y);
    }

    /// <summary>이번 습격에 남은 발판 수로 이 길을 다 깔 수 있는지</summary>
    private bool WithinFloorBudget(List<Vector2Int> p)
    {
        int need = 0;
        foreach (var c in p) if (_buildFinder.NeedsBuiltFloor(c)) need++;
        var field = ErosionFloorField.instance;
        return need == 0 || (field != null && field.BuiltThisRaid + need <= ErosionFloorField.MAX_PER_RAID);
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

        // 지형을 파는 중 — 제자리
        var dig = PendingDig;
        if (dig.HasValue && InBuildReach(dig.Value, DIG_REACH))
        {
            StopClimb();
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            return true;
        }

        // 발판을 까는 중 — 제자리
        var floor = PendingFloor;
        if (floor.HasValue && InBuildReach(floor.Value))
        {
            StopClimb();
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            return true;
        }

        var wp = _path[0];
        var foot = Foot;
        float dx = wp.x + 0.5f - transform.position.x;

        // 바로 위인데 사다리가 없다 = 올라설 턱 밑으로 들어와 버렸다 → 옆 칸으로 빠져나와 다시 뛴다
        if (wp.x == foot.x && wp.y > foot.y && !IsClimbable(foot) && !IsClimbable(foot + Vector2Int.up))
        {
            StopClimb();
            float away = transform.position.x < wp.x + 0.5f ? -1f : 1f;
            _rb.linearVelocity = new Vector2(away * moveSpeed, _rb.linearVelocity.y);
            return true;
        }

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
        // 오를 턱 옆면·밑면에 몸을 밀어붙이면 마찰로 붙어 못 올라간다 — 턱 높이를 넘을 때까지는 제 칸 가운데서 수직으로만
        float vx = dir * moveSpeed;
        if (wp.y > foot.y && dir != 0f && transform.position.y < wp.y && _finderMap != null)
        {
            int sideX = foot.x + (int)dir;
            for (int y = Mathf.FloorToInt(transform.position.y); y < wp.y; y++)
                if (sideX >= 0 && sideX < GameMap.MAP_WIDTH && y >= 0 && y < GameMap.MAP_HEIGHT
                    && (_finderMap.TileGrid[sideX, y] != 0 || _finderMap.DoesTileBlockMovement(sideX, y)))
                {
                    vx = (foot.x + 0.5f - transform.position.x) * 4f;
                    break;
                }
        }
        _rb.linearVelocity = new Vector2(vx, vy);
        return true;
    }

    /// <summary>땅에 선 채로 다음 경유점이 한 걸음(옆 1칸·위 2칸)보다 멀다 — 떨어지거나 밀려 길에서 벗어났다</summary>
    private bool OffPath()
    {
        if (_path.Count == 0 || _rb == null || Mathf.Abs(_rb.linearVelocity.y) > 0.05f) return false;
        var wp = _path[0];
        var foot = Foot;
        return Mathf.Abs(wp.x - foot.x) > 1 || wp.y - foot.y > 2;
    }

    private static bool IsClimbable(Vector2Int c)
    {
        var ft = FloorTile.GetFloorTileAt(c);
        return (ft != null && ft.AllowsVerticalMovement()) || Building.AllowsVerticalMovementAt(c);
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

    /// <summary>
    /// 길찾기상 지나갈 수 있는 것(나무·식물·통과 건물·떨어진 아이템)에 몸이 걸리면 그 충돌을 끈다.
    /// 지형·바닥 타일·막는 건물·발판 건물·문·다른 몸(직원·적)은 그대로 부딪힌다.
    /// </summary>
    private void OnCollisionEnter2D(Collision2D c)
    {
        var other = c.collider;
        if (other is TilemapCollider2D || other is CompositeCollider2D) return;
        if (other.attachedRigidbody != null && other.attachedRigidbody.bodyType == RigidbodyType2D.Dynamic) return;
        if (other.GetComponentInParent<FloorTile>() != null || other.GetComponentInParent<Door>() != null) return;

        var b = other.GetComponentInParent<Building>();
        if (b != null && _finderMap != null)
        {
            var cell = FootOf(b.transform.position);
            if (_finderMap.DoesTileBlockMovement(cell.x, cell.y) || _finderMap.IsFloorSupport(cell.x, cell.y)) return;
        }
        Physics2D.IgnoreCollision(c.otherCollider, other);
    }
}
