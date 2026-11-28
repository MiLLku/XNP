using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 타일 기반 길찾기와 이동을 지원하는 직원 이동 컨트롤러.
/// TilePathfinder를 사용하여 A* 경로 탐색 후 타일 단위로 Lerp 이동합니다.
///
/// 주요 기능:
///   - 타일 기반 경로 탐색 및 이동 (MoveTo)
///   - 바닥 감지 및 자연 낙하 (CheckGroundAndFall)
///   - 고체 타일 내부 진입 시 자동 보정 (AdjustPositionIfInsideSolid)
///   - 충돌 시 자동 경로 재탐색
///
/// 좌표 규칙:
///   - 직원 피벗: Bottom Center
///   - 타일 좌표 → 월드 좌표: (tileX + 0.5f, tileY, 0)
///   - 월드 좌표 → 타일 좌표: (FloorToInt(x), FloorToInt(y))
/// </summary>
public class EmployeeMovement : MonoBehaviour
{
    #region 상수

    /// <summary>직원 높이 (타일 단위)</summary>
    private const int EMPLOYEE_HEIGHT = 2;

    /// <summary>위치 보정 시 최대 검색 높이</summary>
    private const int MAX_POSITION_ADJUST_HEIGHT = 10;

    /// <summary>스프라이트 방향 전환 무시 임계값</summary>
    private const float DIRECTION_THRESHOLD = 0.01f;

    /// <summary>높이차에 따른 속도 감소 계수</summary>
    private const float HEIGHT_SPEED_PENALTY = 0.2f;

    #endregion

    #region 필드 및 설정

    [Header("이동 설정")]
    [SerializeField] private float stoppingDistance = 0.1f;
    [SerializeField] private float tileTransitionSpeed = 5f;

    [Header("낙하 설정")]
    [SerializeField] private float fallSpeed = 8f;

    [Tooltip("이 칸 수까지는 떨어져도 다치지 않습니다")]
    [Min(0)] [SerializeField] private int safeFallTiles = 3;

    [Tooltip("안전 높이를 넘긴 한 칸마다 받는 체력 피해 (기본: 7칸 = 100 = 사실상 사망). 물에 떨어지면 피해 없음.")]
    [Min(0f)] [SerializeField] private float fallDamagePerTile = 25f;

    /// <summary>낙하를 시작한 발 높이 (피해 계산용)</summary>
    private float fallStartY;

    /// <summary>한 번이라도 착지했는지 — 스폰 직후 첫 착지는 낙하 피해를 주지 않는다</summary>
    private bool hasLanded;

    [Header("디버그")]
    [SerializeField] private bool showPath = true;
    [SerializeField] private bool showDebugLogs = true;

    /// <summary>이동 목표 월드 좌표</summary>
    private Vector3 targetPosition;

    /// <summary>현재 이동에 사용 중인 PathOptions</summary>
    private PathOptions currentPathOptions;
    private bool requireExactGoal;

    /// <summary>현재 낙하 중 여부</summary>
    private bool isFalling = false;

    /// <summary>현재 이동 중 여부</summary>
    private bool isMoving = false;

    /// <summary>
    /// 수직 이동(사다리 오르기/내리기) 중 여부.
    /// true일 때 CheckGroundAndFall의 낙하 판정을 건너뜁니다.
    /// </summary>
    private bool isClimbing = false;

    /// <summary>진행 중인 칸 이동의 출발 위치 (칸 사이에서 이동이 끊겼을 때 복귀용)</summary>
    private Vector3 stepStartPos;

    /// <summary>안개(Fog of War) 시야 마지막 중심 — 이동 시 diff 갱신용</summary>
    private Vector2Int _lastSightCenter;

    /// <summary>시야가 한 번이라도 등록되었는지 (스폰 직후 첫 호출 분기용)</summary>
    private bool _hasSightCenter = false;

    /// <summary>이동 성공 콜백</summary>
    private Action onReachDestination;

    /// <summary>이동 실패 콜백</summary>
    private Action onMoveFailed;

    /// <summary>진행 중인 이동 작업의 취소원 (null이면 이동 작업 없음)</summary>
    private CancellationTokenSource moveCts;

    /// <summary>현재 경로 (타일 좌표 목록)</summary>
    private List<Vector2Int> currentPath;

    /// <summary>현재 경로 인덱스</summary>
    private int currentPathIndex = 0;

    /// <summary>
    /// 마지막으로 확인한 GameMap.NavVersion.
    /// 이동 중 이 값이 바뀌면 다음 타일 진입 전 경로 재검증을 수행합니다.
    /// </summary>
    private int lastNavVersion = -1;

    // 컴포넌트 참조
    private Rigidbody2D rb;
    private Collider2D col;
    private Employee employee;
    private EmployeeErosionController erosionController;
    private EmployeeStatsController statsController;
    private GameMap gameMap;
    private TilePathfinder pathfinder;

    /// <summary>원래 Rigidbody 타입 (이동 중 Kinematic으로 전환)</summary>
    private RigidbodyType2D originalBodyType;

    #endregion

    #region 이벤트

    /// <summary>착지 시 발생하는 이벤트 (발 위치 타일 좌표 전달)</summary>
    public event Action<Vector2Int> OnLanded;

    #endregion

    #region 프로퍼티

    /// <summary>현재 이동 중 여부</summary>
    public bool IsMoving => isMoving;

    internal bool IsMovingFor(Action onArrival) => onReachDestination == onArrival;

    /// <summary>현재 낙하 중 여부</summary>
    public bool IsFalling => isFalling;

    /// <summary>이동 목표 월드 좌표</summary>
    public Vector3 TargetPosition => targetPosition;

    /// <summary>목표까지의 거리</summary>
    public float DistanceToTarget => Vector3.Distance(transform.position, targetPosition);

    /// <summary>현재 경로</summary>
    public List<Vector2Int> CurrentPath => currentPath;

    /// <summary>
    /// 이 직원이 쓰는 길찾기 인스턴스 (Start에서 1회 생성).
    /// 경로 질의가 필요한 다른 컴포넌트(EmployeeWork 등)는 새로 만들지 말고 이것을 재사용하세요.
    /// 맵 생성 전에는 null입니다.
    /// </summary>
    public TilePathfinder Pathfinder => pathfinder;

    #endregion

    #region 초기화

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        employee = GetComponent<Employee>();
        erosionController = GetComponent<EmployeeErosionController>();
        statsController = GetComponent<EmployeeStatsController>();

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            originalBodyType = rb.bodyType;
        }

        if (col == null)
        {
            Debug.LogWarning("[EmployeeMovement] Collider2D가 없습니다.");
        }
        else
        {
            foreach (var other in AllColliders)
                if (other != null) Physics2D.IgnoreCollision(col, other);
            AllColliders.Add(col);
        }
    }

    /// <summary>직원 충돌체 전체 — 새 직원이 기존 직원 전부와 충돌을 끄는 데 씀</summary>
    private static readonly List<Collider2D> AllColliders = new List<Collider2D>();

    void Start()
    {
        if (MapGenerator.instance != null)
        {
            gameMap = MapGenerator.instance.GameMapInstance;
            pathfinder = new TilePathfinder(gameMap);

            // 초기 NavVersion 동기화 (맵 생성 중 누적된 버전 무시)
            lastNavVersion = gameMap?.NavVersion ?? 0;

            // 피벗 변경 후 위치 보정: 직원이 고체 안에 있으면 위로 밀어냄
            AdjustPositionIfInsideSolid();

            // 스폰 위치 주변 안개 제거 (참조 카운터 등록)
            var spawnFoot = GetFootTile();
            FogOfWarManager.instance?.AddEmployeeSight(spawnFoot);
            _lastSightCenter = spawnFoot;
            _hasSightCenter  = true;
        }
        else
        {
            Debug.LogError("[EmployeeMovement] MapGenerator를 찾을 수 없습니다!");
        }
    }

    /// <summary>
    /// 직원 제거 시 등록한 시야를 해제하고 진행 중인 이동 작업을 정리합니다.
    /// </summary>
    private void OnDestroy()
    {
        CancelMoveTask();
        AllColliders.Remove(col);

        if (_hasSightCenter)
            FogOfWarManager.instance?.RemoveEmployeeSight(_lastSightCenter);
    }

    /// <summary>고체 안에서 발견된 직원을 지지면이 있는 위치로 보정한다.</summary>
    private void AdjustPositionIfInsideSolid()
    {
        Vector2Int foot = GetFootTile();
        if (pathfinder == null || pathfinder.HasBodyClearance(foot)) return;

        if (hasSafePosition && CanRestAt(WorldToFootTile(lastSafePosition)))
        {
            transform.position = lastSafePosition;
            return;
        }

        for (int dy = 0; dy <= MAX_POSITION_ADJUST_HEIGHT; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                var candidate = new Vector2Int(foot.x + dx, foot.y + dy);
                if (!CanRestAt(candidate)) continue;
                transform.position = TileToWorld(candidate);
                return;
            }
        }
    }

    private bool CanRestAt(Vector2Int foot) =>
        pathfinder != null && pathfinder.HasFooting(foot) && pathfinder.HasBodyClearance(foot);

    internal bool HasStableFooting => !isFalling && CanRestAt(GetFootTile());

    /// <summary>해당 작업 칸에 안정적으로 서 있는지를 확인한다.</summary>
    public bool IsAtWorkPosition(Vector3 workPosition)
    {
        Vector2Int foot = GetFootTile();
        return !isMoving && !isFalling && foot == WorldToFootTile(workPosition) && CanRestAt(foot);
    }

    internal bool CanReachExactTile(Vector2Int goal, PathOptions options = null)
    {
        if (pathfinder == null || isFalling || !pathfinder.IsValidPosition(goal)) return false;
        Vector2Int from = GetFootTile();
        return from == goal ||
            (ReachabilityMap.Current?.IsReachable(from, goal, options) != false &&
             pathfinder.FindPath(from, goal, options, allowGoalFallback: false, logFailure: false) != null);
    }

    #endregion

    #region 업데이트

    void Update()
    {
        CheckGroundAndFall();
    }

    #endregion

    #region 낙하 처리

    /// <summary>
    /// 매 프레임 바닥을 확인하고 낙하를 처리합니다.
    /// </summary>
    private void CheckGroundAndFall()
    {
        if (pathfinder == null || isClimbing) return;

        Vector2Int foot = GetFootTile();
        if (!pathfinder.HasBodyClearance(foot))
        {
            if (isMoving) StopMovingForFall();
            AdjustPositionIfInsideSolid();
            foot = GetFootTile();
            if (!pathfinder.HasBodyClearance(foot)) return;
            if (isFalling && CanRestAt(foot))
            {
                // 고체 안에서 안전한 자리로 보정한 거리는 낙하 거리에 포함하지 않는다.
                isFalling = false;
                hasLanded = true;
                NotifyLanded(foot);
                return;
            }
        }

        if (pathfinder.HasFooting(foot))
        {
            if (isFalling) LandOnGround(foot);
            else if (!isMoving)
            {
                hasLanded = true;
                RememberSafePosition(foot);
            }
            return;
        }

        if (!isFalling)
        {
            isFalling = true;
            fallStartY = transform.position.y;
            if (isMoving) StopMovingForFall();
            if (showDebugLogs) Debug.Log($"[EmployeeMovement] 바닥 없음! 낙하 시작: 발={foot}");
        }

        AdvanceFall(Time.deltaTime);
    }

    private void AdvanceFall(float deltaTime)
    {
        Vector3 start = transform.position;
        Vector2Int foot = GetFootTile();
        float endY = Mathf.Max(1f, start.y - fallSpeed * deltaTime);
        int endTileY = Mathf.FloorToInt(endY);

        // 고배속이나 프레임 지연에도 통과한 첫 발판에서 착지한다.
        for (int y = foot.y - 1; y >= endTileY; y--)
        {
            var crossed = new Vector2Int(foot.x, y);
            if (!pathfinder.HasBodyClearance(crossed)) return;
            if (!pathfinder.HasFooting(crossed)) continue;
            LandOnGround(crossed);
            return;
        }

        transform.position = new Vector3(start.x, endY, start.z);
    }

    /// <summary>
    /// 낙하로 인해 이동을 중단합니다.
    /// 이동 실패 콜백을 호출합니다.
    /// </summary>
    private void StopMovingForFall()
    {
        CancelMoveTask();

        isMoving   = false;
        isClimbing = false;
        currentPath = null;
        currentPathIndex = 0;

        var failCallback = onMoveFailed;
        onReachDestination = null;
        onMoveFailed = null;

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }

        Debug.Log("[EmployeeMovement] 낙하로 인해 이동 중단");

        failCallback?.Invoke();
    }

    /// <summary>
    /// 낙하 피해 — 안전 높이(safeFallTiles)를 넘긴 칸마다 fallDamagePerTile.
    /// 착지한 칸에 물이 반 이상 차 있으면 피해가 없습니다(대신 익사 위험).
    /// </summary>
    private void ApplyFallDamage(Vector2Int foot)
    {
        if (!hasLanded) { hasLanded = true; return; } // 스폰·로드 직후 자리 잡기
        int tiles = Mathf.FloorToInt(fallStartY - foot.y + 0.01f);
        if (tiles <= safeFallTiles || fallDamagePerTile <= 0f || statsController == null) return;
        if (DebugManager.IsBlocked(DebugFlag.Damage)) return;

        var fluids = FluidManager.instance;
        if (fluids != null && fluids.GetFluid(foot.x, foot.y) == FluidType.Water
            && fluids.GetAmount(foot.x, foot.y) >= 0.5f) return;

        float damage = (tiles - safeFallTiles) * fallDamagePerTile;
        statsController.ModifyHealth(-damage);
        Debug.Log($"[EmployeeMovement] {employee?.DisplayName} 낙하 {tiles}칸 — 체력 -{damage:F0}");
    }

    private void LandOnGround(Vector2Int foot)
    {
        isFalling = false;
        transform.position = TileToWorld(foot);
        RememberSafePosition(foot);
        EnablePhysicsForMovement(!pathfinder.IsLadder(foot));
        if (rb != null) rb.linearVelocity = Vector2.zero;
        ApplyFallDamage(foot);

        if (showDebugLogs)
            Debug.Log($"[EmployeeMovement] 착지 완료: 발={foot}, 최종위치={transform.position}");

        if (employee == null || employee.State != EmployeeState.Dead) NotifyLanded(foot);
    }

    private void RememberSafePosition(Vector2Int foot)
    {
        lastSafePosition = TileToWorld(foot);
        hasSafePosition = true;
    }

    private void NotifyLanded(Vector2Int footTile)
    {
        // 낙하 착지 후 시야 갱신 (직원이 이동 작업 없이 떨어진 경우 커버)
        var fog = FogOfWarManager.instance;
        if (fog != null)
        {
            if (_hasSightCenter) fog.UpdateEmployeeSight(_lastSightCenter, footTile);
            else                 fog.AddEmployeeSight(footTile);
            _lastSightCenter = footTile;
            _hasSightCenter  = true;
        }

        OnLanded?.Invoke(footTile);
    }

    #endregion

    #region 이동 시스템

    /// <summary>
    /// PathOptions를 지정하여 이동합니다 (구역 제한 등).
    /// options가 null이면 기본 정책(<see cref="PathOptions.Default"/> — 제한구역만 차단)이 적용됩니다.
    /// </summary>
    /// <param name="worldDestination">목표 월드 좌표</param>
    /// <param name="options">길찾기 정책 (null이면 기본 정책)</param>
    /// <param name="onComplete">이동 성공 시 콜백</param>
    /// <param name="onFailed">이동 실패 시 콜백 (경로 없음 등)</param>
    public void MoveTo(Vector3 worldDestination, PathOptions options, Action onComplete = null, Action onFailed = null)
    {
        MoveToInternal(worldDestination, options ?? PathOptions.Default, onComplete, onFailed);
    }

    /// <summary>
    /// 제한 없이(맵 전체) 이동합니다.
    /// 직원의 배정 구역 안으로 제한하려면 EmployeeZoneAssignment.GetPathOptions()를 넘기세요.
    /// </summary>
    /// <param name="worldDestination">목표 월드 좌표</param>
    /// <param name="onComplete">이동 성공 시 콜백</param>
    /// <param name="onFailed">이동 실패 시 콜백 (경로 없음 등)</param>
    public void MoveTo(Vector3 worldDestination, Action onComplete = null, Action onFailed = null)
    {
        MoveToInternal(worldDestination, PathOptions.Default, onComplete, onFailed);
    }

    public void MoveToExactTile(Vector3 worldDestination, PathOptions options = null,
        Action onComplete = null, Action onFailed = null)
    {
        MoveToInternal(worldDestination, options ?? PathOptions.Default, onComplete, onFailed, exactGoal: true);
    }

    /// <summary>
    /// 모든 MoveTo 오버로드의 단일 진입점.
    /// options는 여기서만 설정되며, 이동 중 재탐색(NavVersion 변화)에도 동일하게 재사용됩니다.
    /// </summary>
    private void MoveToInternal(Vector3 worldDestination, PathOptions options, Action onComplete, Action onFailed,
        bool exactGoal = false)
    {
        // 스폰한 프레임(Start 전)에 이동 명령이 오면 경로 탐색기가 아직 없다 — 그 자리에서 만든다
        if (pathfinder == null && MapGenerator.instance != null && MapGenerator.instance.GameMapInstance != null)
        {
            gameMap = MapGenerator.instance.GameMapInstance;
            pathfinder = new TilePathfinder(gameMap);
            lastNavVersion = gameMap.NavVersion;
        }

        if (pathfinder == null)
        {
            Debug.LogError("[EmployeeMovement] Pathfinder가 초기화되지 않았습니다!");
            onFailed?.Invoke();
            return;
        }

        if (isFalling)
        {
            onFailed?.Invoke();
            return;
        }

        StopMoving();

        // StopMoving 이후에 설정한다 — 재탐색 시에도 같은 정책을 쓰기 위해 유지되어야 한다.
        currentPathOptions = options;

        Vector2Int currentTile = GetFootTile();
        Vector2Int goalTile = WorldToFootTile(worldDestination);
        bool ladderGoal = pathfinder.IsLadder(goalTile);
        requireExactGoal = exactGoal || ladderGoal;
        if (requireExactGoal) worldDestination = TileToWorld(goalTile);

        if (showDebugLogs)
        {
            Debug.Log($"[EmployeeMovement] 이동 요청 - 현재: {currentTile}, 목표: {goalTile}");
        }

        if (currentTile == goalTile)
        {
            if (!CanRestAt(goalTile) || (requireExactGoal && !pathfinder.IsValidPosition(goalTile)))
            {
                onFailed?.Invoke();
                return;
            }
            if (ladderGoal)
            {
                transform.position = worldDestination;
                EnablePhysicsForMovement(false);
            }
            if (showDebugLogs)
            {
                Debug.Log($"[EmployeeMovement] 이미 목표 위치에 있음");
            }
            onComplete?.Invoke();
            return;
        }

        // 연결 성분이 다르면 경로가 존재할 수 없다 — A*를 돌리지 않고 즉시 실패시킨다.
        // (도달 불가 목적지에 대한 반복 탐색 낭비 방지. 판정 불가 시에는 true를 주므로 통과된다.)
        var reachability = ReachabilityMap.Current;
        if (reachability != null && !reachability.IsReachable(currentTile, goalTile, options))
        {
            if (showDebugLogs)
                Debug.Log($"[EmployeeMovement] 도달 불가 (연결 성분 불일치): {currentTile} -> {goalTile}");

            onFailed?.Invoke();
            return;
        }

        currentPath = pathfinder.FindPath(currentTile, goalTile, currentPathOptions, allowGoalFallback: !requireExactGoal);

        if (currentPath == null || currentPath.Count == 0)
        {
            Debug.LogWarning($"[EmployeeMovement] 경로를 찾을 수 없습니다: {currentTile} -> {goalTile} " +
                             $"(허용구역={(options?.allowedZoneIds == null ? "전체" : string.Join(",", options.allowedZoneIds))})");
            onFailed?.Invoke();
            return;
        }

        targetPosition = worldDestination;
        onReachDestination = onComplete;
        onMoveFailed = onFailed;
        currentPathIndex = 0;
        isMoving = true;

        if (showDebugLogs)
        {
            Debug.Log($"[EmployeeMovement] 경로 발견: {currentPath.Count}개 타일");
            for (int i = 0; i < Mathf.Min(currentPath.Count, 5); i++)
            {
                Debug.Log($"  경로[{i}]: {currentPath[i]}");
            }
        }

        FollowPathAsync(RestartMoveTask()).Forget();
    }

    /// <summary>
    /// 진행 중인 이동을 끊고 새 취소 토큰을 발급합니다.
    /// 직원이 파괴되면 함께 취소되도록 파괴 토큰에 묶습니다.
    /// </summary>
    private CancellationToken RestartMoveTask()
    {
        CancelMoveTask();
        moveCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        return moveCts.Token;
    }

    /// <summary>진행 중인 이동 작업을 취소합니다.</summary>
    private void CancelMoveTask()
    {
        if (moveCts == null) return;

        moveCts.Cancel();
        moveCts.Dispose();
        moveCts = null;
    }

    /// <summary>
    /// 경로를 따라 이동합니다.
    /// 타일 진입 전마다 GameMap.NavVersion을 확인해 지형 변화 시 즉시 재탐색합니다.
    /// </summary>
    private async UniTaskVoid FollowPathAsync(CancellationToken ct)
    {
        EnablePhysicsForMovement(false);
        lastNavVersion = gameMap?.NavVersion ?? lastNavVersion;

        while (isMoving && currentPathIndex < currentPath.Count)
        {
            Vector2Int nextTile = currentPath[currentPathIndex];

            // ── 경로 무효화 체크 ──────────────────────────────────────────────
            // 지형이 변했고 다음 타일이 더 이상 서 있을 수 없는 곳이라면 재탐색.
            if (gameMap != null && lastNavVersion != gameMap.NavVersion)
            {
                lastNavVersion = gameMap.NavVersion;

                // 다음 칸만 보면 더 앞쪽이 막힌 변화는 여기서 소비되고, 나중에 그 칸에 와서는 검사 없이 벽을 통과한다
                bool blockedAhead = false;
                for (int i = currentPathIndex; i < currentPath.Count && !blockedAhead; i++)
                    blockedAhead = !pathfinder.IsValidPosition(currentPath[i]);

                if (blockedAhead)
                {
                    Vector2Int currentFoot = GetFootTile();
                    Vector2Int goalTile    = new Vector2Int(
                        Mathf.FloorToInt(targetPosition.x),
                        Mathf.FloorToInt(targetPosition.y)
                    );

                    List<Vector2Int> newPath = pathfinder.FindPath(currentFoot, goalTile, currentPathOptions,
                        allowGoalFallback: !requireExactGoal);

                    if (newPath == null || (newPath.Count == 0 && currentFoot != goalTile))
                    {
                        if (showDebugLogs)
                            Debug.LogWarning($"[EmployeeMovement] 지형 변화 → 재탐색 실패: {currentFoot} -> {goalTile}");

                        EnablePhysicsForMovement(true);

                        var failCb = onMoveFailed;
                        isMoving           = false;
                        currentPath        = null;
                        currentPathIndex   = 0;
                        onReachDestination = null;
                        onMoveFailed       = null;

                        failCb?.Invoke();
                        return;
                    }

                    currentPath      = newPath;
                    currentPathIndex = 0;

                    if (showDebugLogs)
                        Debug.Log($"[EmployeeMovement] 지형 변화 감지 → 재탐색 완료 ({newPath.Count}타일)");

                    continue; // 새 경로의 첫 타일부터 재시작
                }
            }
            // ─────────────────────────────────────────────────────────────────

            Vector2Int prevTile = (currentPathIndex > 0) ? currentPath[currentPathIndex - 1] : GetFootTile();
            int heightDiff = nextTile.y - prevTile.y;

            if (showDebugLogs)
            {
                Debug.Log($"[EmployeeMovement] 타일 이동: {prevTile} -> {nextTile} (높이차: {heightDiff})");
            }

            // ── 문(Door) 처리 ────────────────────────────────────────────────────
            Door doorAtNext = DoorManager.instance?.GetDoorAt(nextTile);
            if (doorAtNext != null)
            {
                if (!doorAtNext.InteractionAllowed && !doorAtNext.IsBroken)
                {
                    // 잠긴 문이고 파손되지 않음 → 이동 실패
                    if (showDebugLogs)
                        Debug.Log($"[EmployeeMovement] 문이 잠겨 이동 불가: {nextTile}");

                    EnablePhysicsForMovement(true);
                    var failCb     = onMoveFailed;
                    isMoving       = false;
                    currentPath    = null;
                    currentPathIndex   = 0;
                    onReachDestination = null;
                    onMoveFailed       = null;
                    failCb?.Invoke();
                    return;
                }

                if (!doorAtNext.IsPassable)
                {
                    // 아직 닫혀 있음 → 열기 요청 후 완전히 열릴 때까지 대기
                    doorAtNext.RequestOpen();
                    if (showDebugLogs)
                        Debug.Log($"[EmployeeMovement] 문 열리는 중 대기: {nextTile}");

                    await UniTask.WaitUntil(() => doorAtNext.IsPassable || !isMoving, GameLoop.Frame, ct);

                    if (!isMoving) return; // 대기 중 이동이 외부에서 중단된 경우
                }
            }
            // ─────────────────────────────────────────────────────────────────────

            await MoveToTileAsync(nextTile, heightDiff, ct);
            if (GetFootTile() == nextTile && CanRestAt(nextTile)) RememberSafePosition(nextTile);

            // 문 통과 완료 → 요청 카운터 감소
            doorAtNext?.ReleaseOpen();

            currentPathIndex++;
        }

        // 마무리 직선 이동은 목표 칸에 실제로 도착했을 때만 — 목표가 막혀 있으면 길찾기는 가장 가까운 칸까지만
        // 경로를 주는데, 여기서 원래 좌표로 직선 이동하면 벽·바위를 뚫고 들어가 갇힌다.
        bool reachedGoalTile = currentPath == null || currentPath.Count == 0 ||
                               currentPath[currentPath.Count - 1] == WorldToFootTile(targetPosition);
        if (isMoving && reachedGoalTile && Vector3.Distance(transform.position, targetPosition) > stoppingDistance)
        {
            await MoveToPositionAsync(targetPosition, ct);
        }

        EnablePhysicsForMovement(true);

        ReachDestination();
    }

    /// <summary>
    /// 단일 타일로 Lerp 이동합니다.
    /// 높이차가 클수록 이동 속도가 감소합니다.
    /// </summary>
    /// <param name="targetTile">목표 타일</param>
    /// <param name="heightDiff">높이차</param>
    /// <param name="ct">이동 취소 토큰</param>
    private async UniTask MoveToTileAsync(Vector2Int targetTile, int heightDiff, CancellationToken ct)
    {
        Vector3 startPos = transform.position;
        stepStartPos = startPos;
        Vector3 endPos = TileToWorld(targetTile);
        float journeyLength = Vector3.Distance(startPos, endPos);
        float startTime = Time.time;

        // 수직/사다리 이동 시 낙하 판정 비활성화 (controlled vertical movement)
        // - 위로 올라가기 (heightDiff > 0) — 계단/사다리 모두 포함
        // - 사다리/수직 이동 가능 타일 위에서 내려가기 — 추락이 아닌 의도된 controlled descent
        // 이유: Lerp 도중 발 아래 타일이 없는 순간(계단 중간)에 오탐 낙하가 발생하기 때문
        Vector2Int currentFootTile = GetFootTile();
        bool isLadderMove = pathfinder.IsLadder(currentFootTile) || pathfinder.IsLadder(targetTile);
        // 내려가기도 길찾기가 허용한 계단 단차 — 대각선 직선 이동이 모서리 블록을 스치는 순간 낙하로 오판해
        // 출발 칸으로 되돌아가는 무한 반복(창고에 못 가 굶어 죽음)이 있었다
        if (heightDiff != 0 || isLadderMove) isClimbing = true;

        if (showDebugLogs)
        {
            Debug.Log($"[EmployeeMovement] Lerp 이동 시작: {startPos} -> {endPos}, TileToWorld({targetTile}) = {endPos}");
        }

        float speedModifier = 1f + Mathf.Abs(heightDiff) * HEIGHT_SPEED_PENALTY;
        float tileSpeedMult = GetTileSpeedMultiplier(targetTile);
        var equipment = employee != null ? employee.Equipment : null;
        if (FluidManager.instance != null) // 물속 감속 — 방수 장화는 물만 무시(용암은 그대로)
        {
            bool skip = equipment != null && equipment.IgnoresWaterSlow()
                     && FluidManager.instance.GetFluid(targetTile.x, targetTile.y) == FluidType.Water;
            if (!skip) tileSpeedMult *= FluidManager.instance.GetSpeedMultiplierAt(targetTile.x, targetTile.y);
        }
        float erosionMoveModifier = erosionController != null ? erosionController.MoveSpeedModifier : 1f;
        float traitMoveModifier   = statsController   != null ? statsController.CachedMoveSpeedMult : 1f;
        float equipMoveModifier   = equipment != null ? Mathf.Max(0.1f, 1f + equipment.GetTotalMoveSpeedModifier() / 100f) : 1f;
        float actualSpeed = tileTransitionSpeed * tileSpeedMult / speedModifier * erosionMoveModifier * traitMoveModifier * equipMoveModifier;

        while (Vector3.Distance(transform.position, endPos) > stoppingDistance)
        {
            if (!isMoving) return;

            float distCovered = (Time.time - startTime) * actualSpeed;
            float fractionOfJourney = distCovered / journeyLength;

            Vector3 newPos = Vector3.Lerp(startPos, endPos, fractionOfJourney);
            transform.position = newPos;
            UpdateSpriteDirection(endPos.x - startPos.x);

            await UniTask.Yield(GameLoop.Frame, ct);
        }

        transform.position = endPos;
        isClimbing = false; // 타일 이동 완료 → 낙하 판정 복원

        // 새 타일 도달 시 시야 갱신 (이전 위치 → 현재 위치 차집합만 업데이트)
        var fogMgr = FogOfWarManager.instance;
        if (fogMgr != null)
        {
            if (_hasSightCenter) fogMgr.UpdateEmployeeSight(_lastSightCenter, targetTile);
            else                 fogMgr.AddEmployeeSight(targetTile);
            _lastSightCenter = targetTile;
            _hasSightCenter  = true;
        }

        // 환경 침식은 더 이상 타일 진입 시점에 적용하지 않습니다.
        // EmployeeErosionController가 머무는 동안 주기적으로 '방 침식 + 발밑 타일 침식'을 읽어 적용합니다.

        if (showDebugLogs)
        {
            Debug.Log($"[EmployeeMovement] Lerp 이동 완료: 최종위치={transform.position}");
        }
    }

    /// <summary>
    /// 특정 월드 좌표로 직접 Lerp 이동합니다.
    /// 경로의 마지막 지점에서 정확한 목표 위치까지 미세 조정할 때 사용합니다.
    /// </summary>
    /// <param name="targetPos">목표 월드 좌표</param>
    /// <param name="ct">이동 취소 토큰</param>
    private async UniTask MoveToPositionAsync(Vector3 targetPos, CancellationToken ct)
    {
        Vector3 startPos = transform.position;
        float journeyLength = Vector3.Distance(startPos, targetPos);
        float startTime = Time.time;

        while (Vector3.Distance(transform.position, targetPos) > stoppingDistance)
        {
            if (!isMoving) return;

            float distCovered = (Time.time - startTime) * tileTransitionSpeed;
            float fractionOfJourney = distCovered / journeyLength;

            transform.position = Vector3.Lerp(startPos, targetPos, fractionOfJourney);
            UpdateSpriteDirection(targetPos.x - startPos.x);

            await UniTask.Yield(GameLoop.Frame, ct);
        }

        transform.position = targetPos;
    }

    /// <summary>
    /// 이동 중 물리 시뮬레이션을 활성화/비활성화합니다.
    /// 이동 중에는 Kinematic으로 전환하여 물리 간섭을 방지합니다.
    /// </summary>
    /// <param name="enable">활성화 여부</param>
    private void EnablePhysicsForMovement(bool enable)
    {
        if (rb != null)
        {
            if (enable)
            {
                Vector2Int foot = GetFootTile();
                rb.bodyType = pathfinder != null && pathfinder.IsLadder(foot) && CanRestAt(foot)
                    ? RigidbodyType2D.Kinematic
                    : originalBodyType;
            }
            else
            {
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.linearVelocity = Vector2.zero;
            }
        }
    }

    /// <summary>
    /// 이동 방향에 따라 스프라이트를 좌우 반전합니다.
    /// </summary>
    /// <param name="xDirection">X축 이동 방향</param>
    private void UpdateSpriteDirection(float xDirection)
    {
        if (Mathf.Abs(xDirection) < DIRECTION_THRESHOLD) return;

        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * Mathf.Sign(xDirection);
        transform.localScale = scale;
    }

    /// <summary>
    /// 목적지 도착 처리.
    /// 이동 상태를 초기화하고 성공 콜백을 호출합니다.
    /// </summary>
    private void ReachDestination()
    {
        ValidateAndFixPosition();
        Vector2Int foot = GetFootTile();
        bool arrived = CanRestAt(foot) && !isFalling &&
            (!requireExactGoal || (foot == WorldToFootTile(targetPosition) && pathfinder.IsValidPosition(foot)));
        isMoving = false;
        currentPath = null;
        currentPathIndex = 0;

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }

        var callback = arrived ? onReachDestination : onMoveFailed;
        onReachDestination = null;
        onMoveFailed = null;

        callback?.Invoke();
    }

    /// <summary>
    /// 이동을 즉시 중지합니다.
    /// 이동 작업 취소, 상태 초기화, 물리 복원을 수행합니다.
    /// </summary>
    public void StopMoving()
    {
        CancelMoveTask();
        if (isClimbing)
        {
            Vector2Int foot = GetFootTile();
            // 이미 사다리 칸에 진입했다면 그 칸에서 멈출 수 있다.
            transform.position = pathfinder.IsLadder(foot) && CanRestAt(foot) ? TileToWorld(foot) : stepStartPos;
        }
        ValidateAndFixPosition();

        isMoving  = false;
        isClimbing = false;
        currentPath = null;
        currentPathIndex = 0;
        onReachDestination = null;
        onMoveFailed = null;

        EnablePhysicsForMovement(true);

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }
    }

    #endregion

    #region 좌표 변환

    /// <summary>
    /// 직원의 현재 발 위치 타일 좌표를 반환합니다.
    /// 피벗이 Bottom Center이므로 FloorToInt를 사용합니다.
    /// </summary>
    /// <returns>발 위치 타일 좌표</returns>
    public Vector2Int GetFootTile()
    {
        return new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y)
        );
    }

    /// <summary>
    /// 월드 좌표를 발 위치 타일 좌표로 변환합니다.
    /// </summary>
    /// <param name="worldPos">변환할 월드 좌표</param>
    /// <returns>타일 좌표</returns>
    private Vector2Int WorldToFootTile(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x),
            Mathf.FloorToInt(worldPos.y)
        );
    }

    /// <summary>
    /// 타일 좌표를 직원 월드 좌표로 변환합니다.
    /// 피벗이 Bottom Center이므로 타일 중앙(+0.5f)으로 변환합니다.
    /// </summary>
    /// <param name="tilePos">변환할 타일 좌표 (발 위치)</param>
    /// <returns>월드 좌표</returns>
    private Vector3 TileToWorld(Vector2Int tilePos)
    {
        return new Vector3(tilePos.x + 0.5f, tilePos.y, 0);
    }

    /// <summary>
    /// 타일 위치의 이동 속도 배율을 반환합니다.
    /// FloorTile이 있으면 해당 속도 배율, 없으면 기본 1.0을 반환합니다.
    /// </summary>
    /// <param name="tilePos">조회할 타일 좌표</param>
    /// <returns>이동 속도 배율 (기본 1.0)</returns>
    private float GetTileSpeedMultiplier(Vector2Int tilePos)
    {
        // 발 아래 타일(groundY)에 FloorTile이 있는지 확인
        Vector2Int groundPos = new Vector2Int(tilePos.x, tilePos.y - 1);
        FloorTile floorTile = FloorTile.GetFloorTileAt(groundPos);

        if (floorTile != null)
        {
            return floorTile.GetMovementSpeedMultiplier();
        }

        // 현재 타일 위치에도 확인 (사다리 등)
        FloorTile currentTile = FloorTile.GetFloorTileAt(tilePos);
        if (currentTile != null)
        {
            return currentTile.GetMovementSpeedMultiplier();
        }

        // FloorTile이 없으면 일반 건물(작업대·울타리 등)의 이동속도 배율 — 발 아래 칸 우선, 없으면 현재 칸
        float groundBuildingMult = Building.GetSpeedMultiplierAt(groundPos);
        if (groundBuildingMult != 1.0f) return groundBuildingMult;
        return Building.GetSpeedMultiplierAt(tilePos);
    }

    #endregion

    #region 충돌 처리

    /// <summary>
    /// 이동 중 충돌 시 경로를 재탐색합니다.
    /// </summary>
    void OnCollisionEnter2D(Collision2D collision)
    {
        if (isMoving)
        {
            if (showDebugLogs)
            {
                Debug.Log($"[EmployeeMovement] 충돌 감지, 경로 재탐색");
            }

            Vector3 currentTarget = targetPosition;
            Action currentCallback = onReachDestination;
            Action currentFailCallback = onMoveFailed;

            MoveToInternal(currentTarget, currentPathOptions, currentCallback, currentFailCallback, requireExactGoal);
        }
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 건설이 완료된 건물의 footprint 안에 직원이 갇힌 경우 외부로 밀어냅니다.
    /// ConstructionSite.CompleteConstruction() 직후 호출됩니다.
    /// </summary>
    /// <param name="buildingOrigin">건물 왼쪽 아래 그리드 좌표</param>
    /// <param name="buildingSize">건물 크기 (타일 단위)</param>
    public void SnapOutOfBuilding(Vector3Int buildingOrigin, Vector2Int buildingSize)
    {
        if (gameMap == null || pathfinder == null) return;

        Vector2Int foot = GetFootTile();
        bool footInside = IsInsideRect(foot,                              buildingOrigin, buildingSize);
        bool bodyInside = IsInsideRect(new Vector2Int(foot.x, foot.y + 1), buildingOrigin, buildingSize);

        if (!footInside && !bodyInside) return;

        // 건물 왼쪽 / 오른쪽 중 현재 발에서 더 가까운 쪽부터 시도
        int leftX  = buildingOrigin.x - 1;
        int rightX = buildingOrigin.x + buildingSize.x;

        int[] xCandidates = Mathf.Abs(foot.x - leftX) <= Mathf.Abs(foot.x - rightX)
            ? new[] { leftX, rightX }
            : new[] { rightX, leftX };

        int searchBottom = buildingOrigin.y - 1;
        int searchTop    = buildingOrigin.y + buildingSize.y + 1;

        foreach (int x in xCandidates)
        {
            if (x < 0 || x >= GameMap.MAP_WIDTH) continue;

            for (int y = foot.y; y >= searchBottom; y--)
            {
                if (pathfinder.IsValidPosition(new Vector2Int(x, y)))
                {
                    StopMoving();
                    transform.position = new Vector3(x + 0.5f, y, 0);
                    Debug.Log($"[EmployeeMovement] 건설 완료 위치 보정: {gameObject.name} {foot} → ({x},{y})");
                    return;
                }
            }

            for (int y = foot.y + 1; y <= searchTop; y++)
            {
                if (pathfinder.IsValidPosition(new Vector2Int(x, y)))
                {
                    StopMoving();
                    transform.position = new Vector3(x + 0.5f, y, 0);
                    Debug.Log($"[EmployeeMovement] 건설 완료 위치 보정(위): {gameObject.name} {foot} → ({x},{y})");
                    return;
                }
            }
        }

        Debug.LogWarning($"[EmployeeMovement] 건설 완료 위치 보정 실패: {gameObject.name} at {foot}");
    }

    /// <summary>타일 좌표가 직사각형 영역 안에 있는지 확인합니다.</summary>
    private bool IsInsideRect(Vector2Int pos, Vector3Int origin, Vector2Int size)
    {
        return pos.x >= origin.x && pos.x < origin.x + size.x &&
               pos.y >= origin.y && pos.y < origin.y + size.y;
    }

    /// <summary>
    /// 현재 위치가 유효하지 않으면 (고체 안에 있으면) 위치를 보정합니다.
    /// 주의: 일반적인 채광 후에는 자연 낙하(CheckGroundAndFall)를 사용하세요.
    /// 이 함수는 텔레포트 등 특수한 상황에서만 사용합니다.
    /// </summary>
    public void ValidateAndFixPosition()
    {
        AdjustPositionIfInsideSolid();
    }

    /// <summary>마지막으로 밟은 경로 칸의 위치 (고체에 박혔을 때 복귀용)</summary>
    private Vector3 lastSafePosition;
    private bool hasSafePosition;

    #endregion

    #region 디버그

    void OnDrawGizmos()
    {
        if (!showPath || currentPath == null || currentPath.Count == 0)
            return;

        Gizmos.color = Color.yellow;

        Vector3 currentPos = transform.position;
        if (currentPathIndex < currentPath.Count)
        {
            Vector3 firstPathPoint = TileToWorld(currentPath[currentPathIndex]);
            Gizmos.DrawLine(currentPos, firstPathPoint);
            currentPos = firstPathPoint;
        }

        for (int i = currentPathIndex; i < currentPath.Count - 1; i++)
        {
            Vector3 from = TileToWorld(currentPath[i]);
            Vector3 to = TileToWorld(currentPath[i + 1]);
            Gizmos.DrawLine(from, to);
        }

        Gizmos.color = Color.green;
        foreach (var tile in currentPath)
        {
            Vector3 pos = TileToWorld(tile);
            Gizmos.DrawWireCube(pos, Vector3.one * 0.3f);
        }

        if (currentPath.Count > 0)
        {
            Gizmos.color = Color.red;
            Vector3 goalPos = TileToWorld(currentPath[currentPath.Count - 1]);
            Gizmos.DrawWireSphere(goalPos, 0.5f);
        }
    }

    #endregion
}
