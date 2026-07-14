using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 적이 추적·공격할 대상 종류. ErosionShooterBehavior 인스펙터에서 설정합니다.
/// [Flags]라 복수 선택이 가능합니다 (예: 직원 + 건설물 동시 추적).
/// </summary>
[System.Flags]
public enum TrackTargetType
{
    None     = 0,
    /// <summary>직원</summary>
    Employee = 1 << 0,
    /// <summary>건설물</summary>
    Building = 1 << 1,
}

/// <summary>
/// 침식 투사체 발사형 적 행동 컴포넌트 (IXenopsBehavior 구현).
///
/// 동작:
///   1. 가장 가까운 공격 대상(직원/건설물)을 주기적으로 선정해 그쪽으로 전진
///   2. 박스 사정거리(좌우 attackRangeX·상하 attackRangeY) 안 + 시야 확보 시 정지 후 발사
///   3. 진행 경로에 넘을 수 없는 벽이 사정거리 안에 들어오면, 부딪히기 전에 멈춰서
///      그 벽을 공격해 파괴한다 (지형 타일·건설물 모두 파괴)
///   4. 수직 장애물은 최대 3칸까지 도약 돌파 (비행·사다리 불가)
///   5. 일정 시간 어떤 공격도 못 하면 대상을 '도달 불가'로 일시 제외하고 다음 대상으로 전환
///   6. 범위 침식 오라는 HostileErosionAura가 독립적으로 처리
///
/// 필요 컴포넌트 (프리팹):
///   Xenops, XenopsHealth, HostileErosionAura, Rigidbody2D, CircleCollider2D
///   ErosionShooterData SO (xenopsType = Hostile)
/// </summary>
[RequireComponent(typeof(Xenops))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(HostileErosionAura))]
public class ErosionShooterBehavior : MonoBehaviour, IXenopsBehavior
{
    // ─── 인스펙터 설정 ────────────────────────────
    [Header("추적 설정")]
    [Tooltip("이 적이 추적·공격할 대상 종류 (복수 선택 가능)")]
    [SerializeField] private TrackTargetType trackingTargets = TrackTargetType.Building;

    [Header("공격 사정거리 (박스)")]
    [Tooltip("좌우(가로) 사정거리 — 칸 수")]
    [SerializeField] private float attackRangeX = 4f;
    [Tooltip("상하(세로) 사정거리 — 칸 수")]
    [SerializeField] private float attackRangeY = 3f;

    [Header("디버그")]
    [Tooltip("AI 동작 디버그 로그 출력 (1초 간격)")]
    [SerializeField] private bool debugLog = true;

    // ─── IXenopsBehavior ──────────────────────────
    public XenopsType BehaviorType => XenopsType.Hostile;
    public bool IsActive => _isActive;

    // ─── 컴포넌트 참조 ───────────────────────────
    private Xenops             _xenops;
    private ErosionShooterData _data;
    private Rigidbody2D        _rb;
    private Animator           _animator;
    private SpriteRenderer     _sr;
    private CircleCollider2D   _collider;
    private bool               _isActive;

    // ─── 애니메이터 파라미터 해시 ────────────────
    private static readonly int AnimAttack = Animator.StringToHash("Attack");

    // ─── 캐시된 스탯 ────────────────────────────
    private float _moveSpeed;
    private float _attackInterval;
    private float _projectileSpeed;
    private float _projectileLifetime;
    private float _erosionOnHit;
    private float _healthDamageOnHit;
    private float _unreachableTimeout;

    // ─── 타겟 시스템 ─────────────────────────────
    /// <summary>현재 공격 대상 (Employee 또는 Building)</summary>
    private Component _lockedTarget;

    /// <summary>현재 타겟 교전 경과 시간 — 발사 성공 시 0으로 리셋</summary>
    private float _engageTimer;

    /// <summary>재타겟 주기 타이머</summary>
    private float _retargetTimer;

    /// <summary>'도달 불가' 판정된 대상의 제외 만료 시각 (Time.time 기준)</summary>
    private readonly Dictionary<Component, float> _blacklist = new Dictionary<Component, float>();

    private const float RETARGET_INTERVAL  = 0.5f;
    private const float BLACKLIST_DURATION = 8f;

    // ─── 런타임 ──────────────────────────────────
    private float _cooldownTimer;
    private float _debugLogTimer;
    private const float DEBUG_LOG_INTERVAL = 1f;

    // ─── 높이 이동 (수직 최대 3칸 돌파) ───────────
    /// <summary>넘을 수 있는 최대 수직 높이 (타일)</summary>
    private const int MAX_STEP_HEIGHT = 3;
    private float _gravity;
    private float _stepUpCooldown;
    private const float STEP_UP_COOLDOWN = 0.4f;

    /// <summary>TryStepUp 결과 — 장애물없음 / 도약함 / 가로막힘(4칸+·천장) / 처리불가(쿨다운·공중)</summary>
    private enum StepResult { NoObstacle, Stepped, Blocked, Busy }

    // ─────────────────────────────────────────────
    #region 초기화

    private void Awake()
    {
        _xenops   = GetComponent<Xenops>();
        _rb       = GetComponent<Rigidbody2D>();
        _animator = GetComponent<Animator>();
        _sr       = GetComponent<SpriteRenderer>();
        _collider = GetComponent<CircleCollider2D>();
    }

    private void Start()
    {
        _data = _xenops?.Data as ErosionShooterData;

        if (debugLog)
            Debug.Log($"[ErosionShooter:{name}] Start — xenops={(_xenops != null)}, " +
                      $"xenopsData={(_xenops != null && _xenops.Data != null ? _xenops.Data.GetType().Name : "NULL")}, " +
                      $"ErosionShooterData 캐스트={(_data != null)}");

        if (_data == null)
        {
            Debug.LogError($"[ErosionShooterBehavior] {name}: ErosionShooterData가 할당되지 않았습니다.");
            enabled = false;
            return;
        }
        if (_data.projectilePrefab == null)
        {
            Debug.LogError($"[ErosionShooterBehavior] {name}: projectilePrefab이 비어 있습니다.");
            enabled = false;
            return;
        }

        // 풀 초기화 — PoolManager에 prefab을 사전 등록 (prewarm·max 명시)
        PoolManager.instance?.RegisterPool(_data.projectilePrefab, prewarm: 20, maxSize: 100);

        // 스탯 캐시
        var s           = _data.hostileStats;
        _moveSpeed      = s.moveSpeed;
        _attackInterval = s.attackSpeed > 0f ? 1f / s.attackSpeed : 2f;

        _projectileSpeed    = _data.projectileSpeed;
        _projectileLifetime = _data.projectileLifetime;
        _erosionOnHit       = _data.erosionOnHit;
        _healthDamageOnHit  = _data.healthDamageOnHit;

        // 도달 불가 판정 시간 — 공격 간격의 3배 또는 최소 5초
        _unreachableTimeout = Mathf.Max(5f, _attackInterval * 3f);

        // 높이 이동용 중력 캐시 (도약 속도는 돌파 높이별로 계산)
        _gravity = Mathf.Abs(Physics2D.gravity.y) * _rb.gravityScale;

        // ── OnStateChanged 구독으로 활성화 자동 처리 ──
        _xenops.OnStateChanged += HandleXenopsStateChanged;

        // 이미 Active 상태로 스폰된 경우 (저장 데이터 복원 등)
        if (_xenops.State == XenopsState.Active)
            OnActivated();

        if (debugLog)
            Debug.Log($"[ErosionShooter:{name}] Start 완료 — isActive={_isActive}, " +
                      $"moveSpeed={_moveSpeed}, 사정거리=({attackRangeX}x{attackRangeY}), " +
                      $"건물피해(침식+체력)={_erosionOnHit + _healthDamageOnHit}, xenopsState={_xenops.State}");
    }

    private void OnDestroy()
    {
        if (_xenops != null)
            _xenops.OnStateChanged -= HandleXenopsStateChanged;
    }

    #endregion

    // ─────────────────────────────────────────────
    #region IXenopsBehavior

    public void OnActivated()
    {
        if (_isActive) return;
        _isActive = true;
        if (debugLog) Debug.Log($"[ErosionShooter:{name}] OnActivated → isActive=true");
    }

    public void OnDeactivated()
    {
        if (!_isActive) return;
        _isActive = false;
        _rb.linearVelocity = Vector2.zero;
        _lockedTarget = null;
        if (debugLog) Debug.Log($"[ErosionShooter:{name}] OnDeactivated → isActive=false");
    }

    public void UpdateBehavior() { }

    #endregion

    // ─────────────────────────────────────────────
    #region Xenops 상태 핸들러

    private void HandleXenopsStateChanged(XenopsState state)
    {
        if (state == XenopsState.Active)
            OnActivated();
        else if (_isActive)
            OnDeactivated();
    }

    #endregion

    // ─────────────────────────────────────────────
    #region AI 루프

    private void Update()
    {
        bool doLog = false;
        if (debugLog)
        {
            _debugLogTimer -= Time.deltaTime;
            if (_debugLogTimer <= 0f) { _debugLogTimer = DEBUG_LOG_INTERVAL; doLog = true; }
        }

        if (!_isActive)
        {
            if (doLog)
                Debug.Log($"[ErosionShooter:{name}] INACTIVE — Update 중단 " +
                          $"(xenopsState={(_xenops != null ? _xenops.State.ToString() : "?")}, enabled={enabled})");
            return;
        }
        if (_xenops != null && _xenops.State == XenopsState.Subdued)
        {
            if (doLog) Debug.Log($"[ErosionShooter:{name}] SUBDUED — Update 중단");
            return;
        }

        float dt = Time.deltaTime;
        if (_stepUpCooldown > 0f) _stepUpCooldown -= dt;
        if (_cooldownTimer  > 0f) _cooldownTimer  -= dt;

        // ── 1. 타겟 선정 — 가장 가까운 공격 대상 (주기적 재평가) ──
        _retargetTimer -= dt;
        if (_lockedTarget == null || !IsTargetAlive(_lockedTarget) || _retargetTimer <= 0f)
        {
            AcquireNearestTarget();
            _retargetTimer = RETARGET_INTERVAL;
        }

        // ── 2. 타겟 없음 → 정지 ──
        if (_lockedTarget == null)
        {
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            if (doLog)
                Debug.Log($"[ErosionShooter:{name}] 타겟 없음 — 정지 " +
                          $"(employees={(EmployeeManager.instance != null ? EmployeeManager.instance.AllEmployees.Count.ToString() : "EmployeeManager=null")}, " +
                          $"mask={trackingTargets})");
            return;
        }

        // ── 3. 거리·시야 평가 (박스 사정거리) ──
        Vector2 myPos   = transform.position;
        Vector2 tgtPos  = _lockedTarget.transform.position;
        float   dx      = Mathf.Abs(tgtPos.x - myPos.x);
        float   dy      = Mathf.Abs(tgtPos.y - myPos.y);
        bool    inRange = dx <= attackRangeX && dy <= attackRangeY;
        bool    los     = inRange && HasLineOfSight(_lockedTarget);

        _engageTimer += dt;

        if (doLog)
            Debug.Log($"[ErosionShooter:{name}] target={_lockedTarget.name}, dx={dx:F2}, dy={dy:F2}, " +
                      $"사정거리=({attackRangeX}x{attackRangeY}), inRange={inRange}, los={los}, " +
                      $"vel={_rb.linearVelocity}, pos={myPos}, engage={_engageTimer:F1}");

        if (inRange && los)
        {
            // ── 4a. 공격 — 정지 후 타겟에 발사 ──
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            if (_cooldownTimer <= 0f)
                FireProjectile(tgtPos);
        }
        else
        {
            // ── 4b. 이동 / 벽 공격 — 사거리 밖이거나 시야가 막힘 ──
            float dir = Mathf.Sign(tgtPos.x - myPos.x);
            if (Mathf.Approximately(dir, 0f))
                dir = (_sr != null && _sr.flipX) ? -1f : 1f; // 수직으로만 떨어진 경우

            if (ScanBlockingWall(dir, out Vector2 wallPoint))
            {
                // 사정거리 안에 넘을 수 없는 벽 → 부딪히기 전에 정지하고 벽을 공격
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
                if (_cooldownTimer <= 0f)
                {
                    FireProjectile(wallPoint);
                    if (doLog) Debug.Log($"[ErosionShooter:{name}] 가로막힌 벽 → 사거리에서 정지·공격 @ {wallPoint}");
                }
            }
            else
            {
                // 가까운 1~3칸 장애물은 도약 돌파, 그 외에는 타겟 쪽으로 전진
                StepResult step = TryStepUp(dir, doLog, out Vector2 stepWall);
                if (step == StepResult.Blocked)
                {
                    // 폴백 — 바로 앞 4칸+ 벽 (스캔이 못 잡은 근접 케이스) → 정지·공격
                    _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
                    if (_cooldownTimer <= 0f)
                        FireProjectile(stepWall);
                }
                else
                {
                    _rb.linearVelocity = new Vector2(dir * _moveSpeed, _rb.linearVelocity.y);
                }
            }

            // ── 4c. 도달 불가 판정 — 일정 시간 아무 공격도 못하면 타겟 교체 ──
            if (_engageTimer >= _unreachableTimeout)
            {
                _blacklist[_lockedTarget] = Time.time + BLACKLIST_DURATION;
                if (debugLog)
                    Debug.Log($"[ErosionShooter:{name}] 도달 불가 → {_lockedTarget.name} 일시 제외, 재타겟");
                _lockedTarget = null;
                AcquireNearestTarget();
            }
        }

        // ── 5. 타겟 방향으로 스프라이트 정렬 ──
        if (_sr != null && _lockedTarget != null)
            _sr.flipX = _lockedTarget.transform.position.x < transform.position.x;
    }

    /// <summary>
    /// 가장 가까운 공격 대상을 선정합니다.
    /// '도달 불가' 목록은 우선 제외하되, 모두 제외되면 그중 최근접을 사용해 계속 전진합니다.
    /// </summary>
    private void AcquireNearestTarget()
    {
        Component next = FindNearestTarget(excludeBlacklisted: true)
                      ?? FindNearestTarget(excludeBlacklisted: false);

        if (next != _lockedTarget)
        {
            if (debugLog)
                Debug.Log($"[ErosionShooter:{name}] 타겟 변경 → {(next != null ? next.name : "NULL")}");
            _lockedTarget = next;
            _engageTimer  = 0f;
        }
    }

    /// <summary>
    /// trackingTargets 마스크에 해당하는 가장 가까운 대상을 찾습니다.
    /// 직원은 방어 태세 어그로 가중 유효 거리(CombatTargeting)로 비교합니다.
    /// </summary>
    private Component FindNearestTarget(bool excludeBlacklisted)
    {
        Component nearest  = null;
        float     bestDist = float.MaxValue;
        Vector2   myPos    = transform.position;

        if ((trackingTargets & TrackTargetType.Employee) != 0 && EmployeeManager.instance != null)
        {
            foreach (var emp in EmployeeManager.instance.AllEmployees)
            {
                if (emp == null || emp.State == EmployeeState.Dead) continue;
                if (excludeBlacklisted && IsBlacklisted(emp)) continue;
                float d = CombatTargeting.EffectiveDistance(myPos, emp);
                if (d < bestDist) { bestDist = d; nearest = emp; }
            }
        }
        if ((trackingTargets & TrackTargetType.Building) != 0)
        {
            var buildings = UnityEngine.Object.FindObjectsByType<Building>(FindObjectsSortMode.None);
            foreach (var b in buildings)
            {
                if (b == null) continue;
                if (excludeBlacklisted && IsBlacklisted(b)) continue;
                float d = Vector2.Distance((Vector2)b.transform.position, myPos);
                if (d < bestDist) { bestDist = d; nearest = b; }
            }
        }
        return nearest;
    }

    /// <summary>대상이 '도달 불가'로 일시 제외 중인지 확인합니다 (만료 시 자동 정리).</summary>
    private bool IsBlacklisted(Component target)
    {
        if (!_blacklist.TryGetValue(target, out float expiry)) return false;
        if (Time.time >= expiry)
        {
            _blacklist.Remove(target);
            return false;
        }
        return true;
    }

    /// <summary>대상이 아직 유효한가 (직원: 생존, 건설물: 미파괴).</summary>
    private static bool IsTargetAlive(Component target)
    {
        if (target == null) return false;
        if (target is Employee emp) return emp.State != EmployeeState.Dead;
        return true;
    }

    /// <summary>지정 위치를 향해 침식 투사체를 발사합니다 (타겟·벽 공통).</summary>
    private void FireProjectile(Vector2 targetPos)
    {
        // 공격 애니메이션 트리거 (이동→공격, 종료 후 자동 복귀)
        if (_animator != null)
            _animator.SetTrigger(AnimAttack);

        Vector2 direction = (targetPos - (Vector2)transform.position).normalized;
        var proj = PoolManager.instance.Spawn<ErosionProjectile>(_data.projectilePrefab, transform.position);
        proj.Init(direction, _projectileSpeed, _erosionOnHit, _healthDamageOnHit, _erosionOnHit + _healthDamageOnHit, _projectileLifetime);

        _cooldownTimer = _attackInterval;
        _engageTimer   = 0f; // 공격(타겟·벽) = 진행 중 → 도달 불가 타이머 리셋
    }

    #endregion

    // ─────────────────────────────────────────────
    #region 전방 벽 스캔

    /// <summary>
    /// 이동 방향 attackRangeX 칸 안에 '넘을 수 없는'(4칸 이상) 벽이 있으면 true.
    /// 부딪히기 전에 사정거리에서 멈춰 벽을 공격하기 위한 판정입니다.
    /// </summary>
    /// <param name="wallPoint">가로막은 벽의 충돌 지점 (공격 목표)</param>
    private bool ScanBlockingWall(float dir, out Vector2 wallPoint)
    {
        wallPoint = default;

        Vector2 center  = _rb.position;
        Vector2 forward = new Vector2(Mathf.Sign(dir), 0f);
        float   skin    = (_collider != null ? _collider.radius : 0.3f) + 0.05f;

        // 몸 높이 전방 — 사정거리(attackRangeX) 안의 솔리드 벽 탐지
        var bodyHit = Physics2D.Raycast(center + forward * skin, forward, attackRangeX);
        if (!IsSolidWall(bodyHit.collider)) return false;

        // 스텝 한계 위 — 같은 거리까지 막혀 있으면 4칸+ (도약으로 못 넘음)
        Vector2 highOrigin = new Vector2(center.x + forward.x * skin, center.y + MAX_STEP_HEIGHT + 0.5f);
        var highHit = Physics2D.Raycast(highOrigin, forward, bodyHit.distance + 0.5f);
        if (!IsSolidWall(highHit.collider)) return false; // 위쪽이 열림 → 넘을 수 있는 벽 → 통과 시도

        wallPoint = bodyHit.point;
        return true;
    }

    /// <summary>콜라이더가 솔리드 벽(지형·건설물)인지 — 트리거·자신·직원·제노프스는 제외.</summary>
    private bool IsSolidWall(Collider2D col)
    {
        if (col == null) return false;
        if (col.isTrigger) return false;
        if (col.gameObject == gameObject) return false;
        if (col.GetComponentInParent<Employee>() != null) return false;
        if (col.GetComponentInParent<Xenops>()   != null) return false;
        return true;
    }

    #endregion

    // ─────────────────────────────────────────────
    #region 높이 이동 (Step-Up, 최대 3칸)

    /// <summary>
    /// 이동 방향의 장애물을 평가하고, 넘을 수 있으면(1~3칸) 수직 도약합니다.
    /// - 장애물 없음/트리거/직원/제노프스 → NoObstacle
    /// - 1~3칸 장애물 + 천장 여유 → 도약 후 Stepped
    /// - 4칸 이상이거나 천장이 막힘 → Blocked (wallPoint에 벽 충돌 지점 반환)
    /// - 쿨다운/공중 → Busy
    /// </summary>
    /// <param name="wallPoint">결과가 Blocked일 때 가로막은 벽의 충돌 지점</param>
    private StepResult TryStepUp(float moveDir, bool log, out Vector2 wallPoint)
    {
        wallPoint = default;

        if (_stepUpCooldown > 0f)
        {
            if (log) Debug.Log($"[ErosionShooter:{name}] StepUp: 쿨다운 ({_stepUpCooldown:F2}s)");
            return StepResult.Busy;
        }
        // 수직 속도가 크면 이미 점프/낙하 중
        if (Mathf.Abs(_rb.linearVelocity.y) > 1.5f)
        {
            if (log) Debug.Log($"[ErosionShooter:{name}] StepUp: 공중/점프 중 (vel.y={_rb.linearVelocity.y:F2})");
            return StepResult.Busy;
        }

        Vector2 center  = _rb.position;
        Vector2 forward = new Vector2(Mathf.Sign(moveDir), 0f);

        // 레이는 자기 콜라이더 '밖'에서 시작한다.
        // (queriesStartInColliders=true 환경에서 _rb.position은 자기 콜라이더 내부라
        //  레이가 자기 자신을 거리 0으로 먼저 맞아 StepUp이 항상 중단됨)
        float   skin       = (_collider != null ? _collider.radius : 0.3f) + 0.05f;
        Vector2 wallOrigin = center + forward * skin;

        // ── 전방 장애물 확인 (중심 높이, 자기 콜라이더 밖에서 시작) ──
        var wallHit = Physics2D.Raycast(wallOrigin, forward, 0.75f);
        if (wallHit.collider == null)                          return StepResult.NoObstacle;
        if (wallHit.collider.isTrigger)                        return StepResult.NoObstacle;
        if (wallHit.collider.gameObject == gameObject)         return StepResult.NoObstacle;
        if (wallHit.collider.GetComponent<Employee>() != null) return StepResult.NoObstacle;
        if (wallHit.collider.GetComponent<Xenops>()   != null) return StepResult.NoObstacle;

        // ── 넘을 수 있는 가장 낮은 높이(1~MAX_STEP_HEIGHT) 탐색 ──
        int clearHeight = 0;
        for (int h = 1; h <= MAX_STEP_HEIGHT; h++)
        {
            var sideHit = Physics2D.Raycast(center + Vector2.up * (h + 0.1f), forward, 0.75f);
            if (sideHit.collider == null || sideHit.collider.isTrigger)
            {
                clearHeight = h;
                break;
            }
        }
        if (clearHeight == 0)
        {
            // 4칸 이상 — 넘지 못함 → 가로막힘
            wallPoint = wallHit.point;
            if (log) Debug.Log($"[ErosionShooter:{name}] StepUp: 4칸+ 장애물 → 가로막힘 ({wallHit.collider.name})");
            return StepResult.Blocked;
        }

        // ── 천장 확인 (도약에 필요한 수직 공간, 자기 콜라이더 밖에서 시작) ──
        var ceilHit = Physics2D.Raycast(center + Vector2.up * skin, Vector2.up, clearHeight + 0.4f);
        if (ceilHit.collider != null && !ceilHit.collider.isTrigger)
        {
            wallPoint = wallHit.point;
            if (log) Debug.Log($"[ErosionShooter:{name}] StepUp: 천장 막힘 ({ceilHit.collider.name}) → 가로막힘");
            return StepResult.Blocked;
        }

        // ── 수직 충격량 적용 (clearHeight 만큼 도약) ────
        // v = sqrt(2 g h), 타일 윗면을 확실히 넘도록 0.4 여유를 더한다.
        float jumpSpeed = Mathf.Sqrt(2f * _gravity * (clearHeight + 0.4f));
        _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, jumpSpeed);
        _stepUpCooldown    = STEP_UP_COOLDOWN;
        if (log) Debug.Log($"[ErosionShooter:{name}] StepUp 도약! clearHeight={clearHeight}, jumpSpeed={jumpSpeed:F2}");
        return StepResult.Stepped;
    }

    #endregion

    // ─────────────────────────────────────────────
    #region 시야 판정

    /// <summary>
    /// 타겟까지 직선 경로에 장애물이 없는지 확인합니다.
    /// solid 콜라이더(건물, 타일맵 등)가 먼저 감지되면 false.
    /// </summary>
    private bool HasLineOfSight(Component target)
    {
        Vector2 origin = transform.position;
        Vector2 targetPos = target.transform.position;
        Vector2 dir = targetPos - origin;
        float   dist = dir.magnitude;

        // 자신 콜라이더를 무시하기 위해 약간 앞에서 시작
        Vector2 start = origin + dir.normalized * 0.6f;

        RaycastHit2D[] hits = Physics2D.RaycastAll(start, dir.normalized, dist);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider.gameObject == gameObject) continue;         // 자신
            if (hit.collider.isTrigger) continue;                        // 트리거 무시
            if (IsColliderOf(hit.collider, target)) return true;         // 타겟 도달 = LOS OK
            return false; // 다른 물체 먼저 감지 = 시야 차단
        }

        return true; // 아무것도 없으면 LOS OK
    }

    /// <summary>콜라이더가 지정 타겟(Employee/Building)에 속하는지 확인합니다.</summary>
    private static bool IsColliderOf(Collider2D col, Component target)
    {
        if (target == null) return false;
        if (target is Employee)
            return col.GetComponentInParent<Employee>() == target;
        if (target is Building)
            return col.GetComponentInParent<Building>() == target;
        return false;
    }

    #endregion
}
