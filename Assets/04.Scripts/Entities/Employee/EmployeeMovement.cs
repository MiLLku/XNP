using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

    /// <summary>착지 시 최대 Y 보정 높이</summary>
    private const int MAX_LANDING_ADJUST_HEIGHT = 5;

    /// <summary>스프라이트 방향 전환 무시 임계값</summary>
    private const float DIRECTION_THRESHOLD = 0.01f;

    /// <summary>높이차에 따른 속도 감소 계수</summary>
    private const float HEIGHT_SPEED_PENALTY = 0.2f;

    #endregion

    #region 필드 및 설정

    [Header("이동 설정")]
    [SerializeField] private float baseSpeed = 3f;
    [SerializeField] private float stoppingDistance = 0.1f;
    [SerializeField] private float tileTransitionSpeed = 5f;

    [Header("낙하 설정")]
    [SerializeField] private float fallSpeed = 8f;

    [Header("디버그")]
    [SerializeField] private bool showPath = true;
    [SerializeField] private bool showDebugLogs = true;

    /// <summary>이동 목표 월드 좌표</summary>
    private Vector3 targetPosition;

    /// <summary>현재 이동에 사용 중인 PathOptions</summary>
    private PathOptions currentPathOptions;

    /// <summary>현재 낙하 중 여부</summary>
    private bool isFalling = false;

    /// <summary>현재 이동 중 여부</summary>
    private bool isMoving = false;

    /// <summary>
    /// 수직 이동(사다리 오르기/내리기) 중 여부.
    /// true일 때 CheckGroundAndFall의 낙하 판정을 건너뜁니다.
    /// </summary>
    private bool isClimbing = false;

    /// <summary>안개(Fog of War) 시야 마지막 중심 — 이동 시 diff 갱신용</summary>
    private Vector2Int _lastSightCenter;

    /// <summary>시야가 한 번이라도 등록되었는지 (스폰 직후 첫 호출 분기용)</summary>
    private bool _hasSightCenter = false;

    /// <summary>이동 성공 콜백</summary>
    private Action onReachDestination;

    /// <summary>이동 실패 콜백</summary>
    private Action onMoveFailed;

    /// <summary>이동 코루틴 참조</summary>
    private Coroutine moveCoroutine;

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

    /// <summary>현재 낙하 중 여부</summary>
    public bool IsFalling => isFalling;

    /// <summary>이동 목표 월드 좌표</summary>
    public Vector3 TargetPosition => targetPosition;

    /// <summary>목표까지의 거리</summary>
    public float DistanceToTarget => Vector3.Distance(transform.position, targetPosition);

    /// <summary>현재 경로</summary>
    public List<Vector2Int> CurrentPath => currentPath;

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
    }

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
    /// 직원 제거 시 등록한 시야를 해제합니다 (안개 복구).
    /// </summary>
    private void OnDestroy()
    {
        if (_hasSightCenter)
            FogOfWarManager.instance?.RemoveEmployeeSight(_lastSightCenter);
    }

    /// <summary>
    /// 직원이 고체 타일 안에 있으면 유효한 위치로 밀어냅니다.
    /// 피벗 변경 등으로 인해 잘못된 위치에 있을 때 보정합니다.
    /// 이 함수는 Start()에서만 호출됩니다 - 런타임 중에는 자연 낙하를 사용합니다.
    /// </summary>
    private void AdjustPositionIfInsideSolid()
    {
        if (gameMap == null) return;

        Vector2Int footTile = GetFootTile();

        if (footTile.x >= 0 && footTile.x < GameMap.MAP_WIDTH &&
            footTile.y >= 0 && footTile.y < GameMap.MAP_HEIGHT)
        {
            int tileId = gameMap.TileGrid[footTile.x, footTile.y];
            if (tileId != 0)
            {
                int[] xCandidates = { footTile.x, footTile.x - 1, footTile.x + 1 };

                foreach (int checkX in xCandidates)
                {
                    if (checkX < 0 || checkX >= GameMap.MAP_WIDTH) continue;

                    for (int dy = 0; dy <= MAX_POSITION_ADJUST_HEIGHT; dy++)
                    {
                        int checkY = footTile.y + dy;
                        if (checkY >= GameMap.MAP_HEIGHT) break;

                        int checkTileId = gameMap.TileGrid[checkX, checkY];
                        if (checkTileId == 0)
                        {
                            int groundY = checkY - 1;
                            bool hasGround = groundY >= 0 && (
                                gameMap.TileGrid[checkX, groundY] != 0 ||
                                FloorTile.HasFloorTileAt(new Vector2Int(checkX, groundY)) ||
                                gameMap.IsFloorSupport(checkX, groundY)
                            );

                            bool bodySpaceClear = checkY + 1 >= GameMap.MAP_HEIGHT ||
                                                  gameMap.TileGrid[checkX, checkY + 1] == 0;

                            if (hasGround && bodySpaceClear)
                            {
                                Vector3 newPos = new Vector3(checkX + 0.5f, checkY, transform.position.z);
                                Debug.Log($"[EmployeeMovement] 위치 보정: {transform.position} -> {newPos}");
                                transform.position = newPos;
                                return;
                            }
                        }
                    }
                }

                Debug.LogWarning($"[EmployeeMovement] 위치 보정 실패: 직원이 고체 안에 갇혀있음 {footTile}");
            }
        }
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
        if (gameMap == null)    return;
        if (isClimbing)         return; // 사다리 이동 중 낙하 판정 스킵

        Vector2Int footTile = GetFootTile();
        Vector2Int groundTile = new Vector2Int(footTile.x, footTile.y - 1);

        if (!HasGroundAt(groundTile))
        {
            if (!isFalling)
            {
                isFalling = true;

                if (isMoving)
                {
                    StopMovingForFall();
                }

                if (showDebugLogs)
                {
                    Debug.Log($"[EmployeeMovement] 바닥 없음! 낙하 시작: 발={footTile}, 바닥체크={groundTile}");
                }
            }

            transform.position += Vector3.down * fallSpeed * Time.deltaTime;

            Vector2Int newFootTile = GetFootTile();
            Vector2Int newGroundTile = new Vector2Int(newFootTile.x, newFootTile.y - 1);

            if (HasGroundAt(newGroundTile))
            {
                LandOnGround(newGroundTile);
            }
        }
        else if (isFalling)
        {
            LandOnGround(groundTile);
        }
    }

    /// <summary>
    /// 낙하로 인해 이동을 중단합니다.
    /// 이동 실패 콜백을 호출합니다.
    /// </summary>
    private void StopMovingForFall()
    {
        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
            moveCoroutine = null;
        }

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
    /// 지정한 타일에 바닥이 있는지 확인합니다.
    /// 고체 타일, FloorTile, 건설된 바닥 타일을 모두 확인합니다.
    /// </summary>
    /// <param name="tilePos">확인할 타일 좌표</param>
    /// <returns>바닥 존재 여부</returns>
    private bool HasGroundAt(Vector2Int tilePos)
    {
        if (tilePos.x < 0 || tilePos.x >= GameMap.MAP_WIDTH ||
            tilePos.y < 0 || tilePos.y >= GameMap.MAP_HEIGHT)
        {
            return false;
        }

        int tileId = gameMap.TileGrid[tilePos.x, tilePos.y];
        if (tileId != 0)
        {
            return true;
        }

        if (FloorTile.HasFloorTileAt(tilePos))
        {
            return true;
        }

        // 완공된 바닥 건물만 true — 건설 예정지(blueprint)는 포함되지 않음
        // (IsFloorSupport는 Building.RegisterToGameMap에서 blocksMovement=false일 때만 설정)
        if (gameMap.IsFloorSupport(tilePos.x, tilePos.y))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 해당 타일이 수직 이동(사다리 등)을 지원하는지 확인합니다.
    /// 사다리 위에서는 fall 모드 대신 controlled descent를 사용합니다.
    /// </summary>
    private bool IsVerticalMovementTile(Vector2Int tile)
    {
        var floor = FloorTile.GetFloorTileAt(tile);
        return floor != null && floor.AllowsVerticalMovement();
    }

    /// <summary>
    /// 바닥에 착지합니다.
    /// X/Y 좌표 보정, 고체 안 진입 보정을 수행한 뒤 OnLanded 이벤트를 발생시킵니다.
    /// </summary>
    /// <param name="groundTile">밟고 있는 바닥 타일 좌표</param>
    private void LandOnGround(Vector2Int groundTile)
    {
        isFalling = false;

        int landingY = groundTile.y + 1;

        float currentX = transform.position.x;
        int floorX = Mathf.FloorToInt(currentX);
        int ceilX = Mathf.CeilToInt(currentX);

        int landingTileX = floorX;

        // 착지 위치 유효성 검증 로컬 함수
        bool IsValidLandingX(int x, int y)
        {
            if (x < 0 || x >= GameMap.MAP_WIDTH) return false;
            if (y < 0 || y >= GameMap.MAP_HEIGHT) return false;

            if (gameMap.TileGrid[x, y] != 0) return false;

            if (y + 1 < GameMap.MAP_HEIGHT && gameMap.TileGrid[x, y + 1] != 0) return false;

            int groundY = y - 1;
            if (groundY < 0) return false;

            bool hasGround = gameMap.TileGrid[x, groundY] != 0 ||
                             FloorTile.HasFloorTileAt(new Vector2Int(x, groundY)) ||
                             gameMap.IsFloorSupport(x, groundY);

            return hasGround;
        }

        if (!IsValidLandingX(floorX, landingY))
        {
            if (IsValidLandingX(ceilX, landingY))
            {
                landingTileX = ceilX;
                if (showDebugLogs)
                    Debug.Log($"[EmployeeMovement] X 보정: {floorX} -> {ceilX} (Ceil)");
            }
            else
            {
                Debug.LogWarning($"[EmployeeMovement] 유효한 착지 X 좌표 없음! 현재 X={currentX}, 착지 Y={landingY}");
            }
        }

        // 착지 위치가 고체인 경우 위로 올려서 빈 공간 찾기
        if (landingY < GameMap.MAP_HEIGHT && gameMap.TileGrid[landingTileX, landingY] != 0)
        {
            for (int dy = 1; dy <= MAX_LANDING_ADJUST_HEIGHT; dy++)
            {
                int checkY = landingY + dy;
                if (checkY >= GameMap.MAP_HEIGHT) break;

                if (gameMap.TileGrid[landingTileX, checkY] == 0)
                {
                    landingY = checkY;
                    Debug.Log($"[EmployeeMovement] 착지 Y 보정: {groundTile.y + 1} -> {landingY}");
                    break;
                }
            }
        }

        Vector3 landingPos = new Vector3(landingTileX + 0.5f, landingY, 0);
        transform.position = landingPos;

        if (showDebugLogs)
        {
            Debug.Log($"[EmployeeMovement] 착지 완료: 바닥타일={groundTile}, 최종위치={transform.position}");
        }

        Vector2Int footTile = GetFootTile();

        // 낙하 착지 후 시야 갱신 (직원이 코루틴 없이 떨어진 경우 커버)
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
    /// 목표 월드 좌표로 이동합니다 (자동 길찾기).
    /// </summary>
    /// <param name="worldDestination">목표 월드 좌표</param>
    /// <param name="onComplete">이동 성공 시 콜백</param>
    /// <param name="onFailed">이동 실패 시 콜백 (경로 없음 등)</param>
    /// <summary>
    /// PathOptions를 지정하여 이동합니다 (구역 제한 등).
    /// </summary>
    public void MoveTo(Vector3 worldDestination, PathOptions options, Action onComplete = null, Action onFailed = null)
    {
        currentPathOptions = options;
        MoveTo(worldDestination, onComplete, onFailed);
    }

    public void MoveTo(Vector3 worldDestination, Action onComplete = null, Action onFailed = null)
    {
        currentPathOptions = null;
        if (pathfinder == null)
        {
            Debug.LogError("[EmployeeMovement] Pathfinder가 초기화되지 않았습니다!");
            onFailed?.Invoke();
            return;
        }

        StopMoving();

        Vector2Int currentTile = GetFootTile();
        Vector2Int goalTile = WorldToFootTile(worldDestination);

        if (showDebugLogs)
        {
            Debug.Log($"[EmployeeMovement] 이동 요청 - 현재: {currentTile}, 목표: {goalTile}");
        }

        if (currentTile == goalTile)
        {
            if (showDebugLogs)
            {
                Debug.Log($"[EmployeeMovement] 이미 목표 위치에 있음");
            }
            onComplete?.Invoke();
            return;
        }

        currentPath = pathfinder.FindPath(currentTile, goalTile, currentPathOptions);

        if (currentPath == null || currentPath.Count == 0)
        {
            Debug.LogWarning($"[EmployeeMovement] 경로를 찾을 수 없습니다: {currentTile} -> {goalTile}");
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

        moveCoroutine = StartCoroutine(FollowPathCoroutine());
    }

    /// <summary>
    /// 경로를 따라 이동하는 코루틴.
    /// 타일 진입 전마다 GameMap.NavVersion을 확인해 지형 변화 시 즉시 재탐색합니다.
    /// </summary>
    private IEnumerator FollowPathCoroutine()
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

                if (!pathfinder.IsValidPosition(nextTile))
                {
                    Vector2Int currentFoot = GetFootTile();
                    Vector2Int goalTile    = new Vector2Int(
                        Mathf.FloorToInt(targetPosition.x),
                        Mathf.FloorToInt(targetPosition.y)
                    );

                    List<Vector2Int> newPath = pathfinder.FindPath(currentFoot, goalTile, currentPathOptions);

                    if (newPath == null || newPath.Count == 0)
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
                        yield break;
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
                    yield break;
                }

                if (!doorAtNext.IsPassable)
                {
                    // 아직 닫혀 있음 → 열기 요청 후 완전히 열릴 때까지 대기
                    doorAtNext.RequestOpen();
                    if (showDebugLogs)
                        Debug.Log($"[EmployeeMovement] 문 열리는 중 대기: {nextTile}");

                    yield return new WaitUntil(() => doorAtNext.IsPassable || !isMoving);

                    if (!isMoving) yield break; // 대기 중 이동이 외부에서 중단된 경우
                }
            }
            // ─────────────────────────────────────────────────────────────────────

            yield return MoveToTileCoroutine(nextTile, heightDiff);

            // 문 통과 완료 → 요청 카운터 감소
            doorAtNext?.ReleaseOpen();

            currentPathIndex++;
        }

        if (isMoving && Vector3.Distance(transform.position, targetPosition) > stoppingDistance)
        {
            yield return MoveToPositionCoroutine(targetPosition);
        }

        EnablePhysicsForMovement(true);

        ReachDestination();
    }

    /// <summary>
    /// 단일 타일로 Lerp 이동하는 코루틴.
    /// 높이차가 클수록 이동 속도가 감소합니다.
    /// </summary>
    /// <param name="targetTile">목표 타일</param>
    /// <param name="heightDiff">높이차</param>
    private IEnumerator MoveToTileCoroutine(Vector2Int targetTile, int heightDiff)
    {
        Vector3 startPos = transform.position;
        Vector3 endPos = TileToWorld(targetTile);
        float journeyLength = Vector3.Distance(startPos, endPos);
        float startTime = Time.time;

        // 수직/사다리 이동 시 낙하 판정 비활성화 (controlled vertical movement)
        // - 위로 올라가기 (heightDiff > 0) — 계단/사다리 모두 포함
        // - 사다리/수직 이동 가능 타일 위에서 내려가기 — 추락이 아닌 의도된 controlled descent
        // 이유: Lerp 도중 발 아래 타일이 없는 순간(계단 중간)에 오탐 낙하가 발생하기 때문
        Vector2Int currentFootTile = GetFootTile();
        bool isLadderMove = IsVerticalMovementTile(currentFootTile) || IsVerticalMovementTile(targetTile);
        if (heightDiff > 0 || isLadderMove) isClimbing = true;

        if (showDebugLogs)
        {
            Debug.Log($"[EmployeeMovement] Lerp 이동 시작: {startPos} -> {endPos}, TileToWorld({targetTile}) = {endPos}");
        }

        float speedModifier = 1f + Mathf.Abs(heightDiff) * HEIGHT_SPEED_PENALTY;
        float tileSpeedMult = GetTileSpeedMultiplier(targetTile);
        float erosionMoveModifier = erosionController != null ? erosionController.MoveSpeedModifier : 1f;
        float traitMoveModifier   = statsController   != null ? statsController.CachedMoveSpeedModifier : 1f;
        float actualSpeed = tileTransitionSpeed * tileSpeedMult / speedModifier * erosionMoveModifier * traitMoveModifier;

        while (Vector3.Distance(transform.position, endPos) > stoppingDistance)
        {
            if (!isMoving) yield break;

            float distCovered = (Time.time - startTime) * actualSpeed;
            float fractionOfJourney = distCovered / journeyLength;

            Vector3 newPos = Vector3.Lerp(startPos, endPos, fractionOfJourney);
            transform.position = newPos;
            UpdateSpriteDirection(endPos.x - startPos.x);

            yield return null;
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

        // 자연 침식 영향력 적용 (워터마크 방식)
        if (NaturalErosionManager.instance != null && erosionController != null)
        {
            float influence = NaturalErosionManager.instance.GetInfluenceAt(targetTile);
            if (influence > 0f)
                erosionController.ApplyNaturalErosion(influence);
        }

        if (showDebugLogs)
        {
            Debug.Log($"[EmployeeMovement] Lerp 이동 완료: 최종위치={transform.position}");
        }
    }

    /// <summary>
    /// 특정 월드 좌표로 직접 Lerp 이동하는 코루틴.
    /// 경로의 마지막 지점에서 정확한 목표 위치까지 미세 조정할 때 사용합니다.
    /// </summary>
    /// <param name="targetPos">목표 월드 좌표</param>
    private IEnumerator MoveToPositionCoroutine(Vector3 targetPos)
    {
        Vector3 startPos = transform.position;
        float journeyLength = Vector3.Distance(startPos, targetPos);
        float startTime = Time.time;

        while (Vector3.Distance(transform.position, targetPos) > stoppingDistance)
        {
            if (!isMoving) yield break;

            float distCovered = (Time.time - startTime) * tileTransitionSpeed;
            float fractionOfJourney = distCovered / journeyLength;

            transform.position = Vector3.Lerp(startPos, targetPos, fractionOfJourney);
            UpdateSpriteDirection(targetPos.x - startPos.x);

            yield return null;
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
                rb.bodyType = originalBodyType;
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
        isMoving = false;
        currentPath = null;
        currentPathIndex = 0;

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }

        var callback = onReachDestination;
        onReachDestination = null;
        onMoveFailed = null;

        callback?.Invoke();
    }

    /// <summary>
    /// 이동을 즉시 중지합니다.
    /// 코루틴 중지, 상태 초기화, 물리 복원을 수행합니다.
    /// </summary>
    public void StopMoving()
    {
        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
            moveCoroutine = null;
        }

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

            StopMoving();
            MoveTo(currentTarget, currentCallback, currentFailCallback);
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
        if (gameMap == null) return;

        Vector2Int footTile = GetFootTile();

        if (footTile.x >= 0 && footTile.x < GameMap.MAP_WIDTH &&
            footTile.y >= 0 && footTile.y < GameMap.MAP_HEIGHT)
        {
            int tileId = gameMap.TileGrid[footTile.x, footTile.y];
            if (tileId != 0)
            {
                Debug.LogWarning($"[EmployeeMovement] 직원이 고체 안에 있음! 위치 보정 시도: {footTile}");
                AdjustPositionIfInsideSolid();
            }
        }
    }

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
