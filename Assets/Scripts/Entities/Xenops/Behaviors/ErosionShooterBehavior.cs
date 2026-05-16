using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 침식 투사체 발사형 적 행동 컴포넌트 (IXenopsBehavior 구현).
///
/// 동작:
///   1. 탐지 반경(= attackRange)에 가장 먼저 진입한 직원을 타겟으로 고정
///   2. 타겟이 사거리 밖이면 수평 접근, 너무 가까우면 후퇴
///   3. 사거리 안 + 시야 확보 시 투사체 발사 (ErosionProjectilePool 사용)
///   4. 타겟이 죽거나 탐지 범위를 벗어나면 다음 후보로 전환
///   5. 범위 침식 오라는 HostileErosionAura가 독립적으로 처리
///
/// 필요 컴포넌트 (프리팹):
///   Xenops, XenopsHealth, HostileErosionAura, Rigidbody2D, Collider2D
///   ErosionShooterData SO (xenopsType = Hostile)
/// </summary>
[RequireComponent(typeof(Xenops))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(HostileErosionAura))]
public class ErosionShooterBehavior : MonoBehaviour, IXenopsBehavior
{
    // ─── IXenopsBehavior ──────────────────────────
    public XenopsType BehaviorType => XenopsType.Hostile;
    public bool IsActive => _isActive;

    // ─── 상태 머신 ────────────────────────────────
    private enum State { Idle, Positioning, Shooting, Cooldown }

    // ─── 컴포넌트 참조 ───────────────────────────
    private Xenops             _xenops;
    private ErosionShooterData _data;
    private Rigidbody2D        _rb;
    private bool               _isActive;

    // ─── 캐시된 스탯 ────────────────────────────
    private float _moveSpeed;
    private float _attackRange;
    private float _minRange;
    private float _attackInterval;
    private float _projectileSpeed;
    private float _projectileLifetime;
    private float _erosionOnHit;
    private float _healthDamageOnHit;

    // ─── 타겟 시스템 ─────────────────────────────
    /// <summary>현재 고정 타겟</summary>
    private Employee _lockedTarget;

    /// <summary>탐지 범위에 들어온 순서대로 보관되는 후보 목록</summary>
    private readonly List<Employee> _candidates = new List<Employee>();

    // ─── 런타임 ──────────────────────────────────
    private State _state = State.Idle;
    private float _cooldownTimer;

    // ─── 높이 이동 (높이차 1 돌파) ───────────────
    private float _stepUpSpeed;
    private float _stepUpCooldown;
    private const float STEP_UP_COOLDOWN = 0.4f;

    // ─────────────────────────────────────────────
    #region 초기화

    private void Awake()
    {
        _xenops = GetComponent<Xenops>();
        _rb     = GetComponent<Rigidbody2D>();
    }

    private void Start()
    {
        _data = _xenops?.Data as ErosionShooterData;

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

        // 풀 초기화
        if (ErosionProjectilePool.instance == null)
        {
            var poolGO = new GameObject("ErosionProjectilePool");
            poolGO.AddComponent<ErosionProjectilePool>();
        }
        ErosionProjectilePool.instance.EnsureInitialized(_data.projectilePrefab);

        // 스탯 캐시
        var s           = _data.hostileStats;
        _moveSpeed      = s.moveSpeed;
        _attackRange    = s.attackRange;
        _minRange       = _attackRange * 0.5f;
        _attackInterval = s.attackSpeed > 0f ? 1f / s.attackSpeed : 2f;

        _projectileSpeed    = _data.projectileSpeed;
        _projectileLifetime = _data.projectileLifetime;
        _erosionOnHit       = _data.erosionOnHit;
        _healthDamageOnHit  = _data.healthDamageOnHit;

        // 높이 이동 속도 = 높이 1.3 타일을 넘기기 위한 초기 수직 속도
        float g = Mathf.Abs(Physics2D.gravity.y) * _rb.gravityScale;
        _stepUpSpeed = Mathf.Sqrt(2f * g * 1.3f);

        // 탐지 존 생성 (자식 오브젝트 트리거)
        SetupDetectionZone();

        // ── Fix #5: OnStateChanged 구독으로 활성화 자동 처리 ──
        _xenops.OnStateChanged += HandleXenopsStateChanged;

        // 이미 Active 상태로 스폰된 경우 (저장 데이터 복원 등)
        if (_xenops.State == XenopsState.Active)
            OnActivated();
    }

    private void OnDestroy()
    {
        if (_xenops != null)
            _xenops.OnStateChanged -= HandleXenopsStateChanged;
    }

    /// <summary>
    /// 탐지 반경 트리거를 가진 자식 오브젝트를 생성합니다.
    /// </summary>
    private void SetupDetectionZone()
    {
        var zoneGO = new GameObject("DetectionZone");
        zoneGO.transform.SetParent(transform);
        zoneGO.transform.localPosition = Vector3.zero;
        zoneGO.AddComponent<CircleCollider2D>(); // Initialize() 전에 추가
        var detector = zoneGO.AddComponent<ErosionShooterDetectionZone>();
        detector.Initialize(this, _attackRange);
    }

    #endregion

    // ─────────────────────────────────────────────
    #region IXenopsBehavior

    public void OnActivated()
    {
        if (_isActive) return;
        _isActive = true;
        _state    = State.Idle;
    }

    public void OnDeactivated()
    {
        if (!_isActive) return;
        _isActive = false;
        _rb.linearVelocity = Vector2.zero;
        _lockedTarget = null;
        _candidates.Clear();
    }

    public void UpdateBehavior() { }

    #endregion

    // ─────────────────────────────────────────────
    #region Xenops 상태 핸들러 (Fix #5)

    private void HandleXenopsStateChanged(XenopsState state)
    {
        if (state == XenopsState.Active)
            OnActivated();
        else if (_isActive)
            OnDeactivated();
    }

    #endregion

    // ─────────────────────────────────────────────
    #region 타겟 탐지 (Detection Zone 콜백)

    /// <summary>직원이 탐지 범위에 진입했을 때 DetectionZone이 호출합니다.</summary>
    public void OnEmployeeEnterRange(Employee emp)
    {
        if (_candidates.Contains(emp)) return;
        _candidates.Add(emp);
        TryAcquireTarget();
    }

    /// <summary>직원이 탐지 범위를 벗어났을 때 DetectionZone이 호출합니다.</summary>
    public void OnEmployeeExitRange(Employee emp)
    {
        _candidates.Remove(emp);

        if (_lockedTarget == emp)
        {
            _lockedTarget = null;
            _state = State.Idle;
            TryAcquireTarget();
        }
    }

    /// <summary>후보 목록에서 첫 번째 생존자를 타겟으로 삼습니다.</summary>
    private void TryAcquireTarget()
    {
        if (_lockedTarget != null) return;

        // 죽은 후보 정리 후 첫 번째 생존 직원 선택
        _candidates.RemoveAll(e => e == null || e.State == EmployeeState.Dead);
        if (_candidates.Count > 0)
        {
            _lockedTarget = _candidates[0];
            _state = State.Positioning;
        }
    }

    #endregion

    // ─────────────────────────────────────────────
    #region AI 루프

    private void Update()
    {
        if (!_isActive) return;
        if (_xenops != null && _xenops.State == XenopsState.Subdued) return;

        if (_stepUpCooldown > 0f) _stepUpCooldown -= Time.deltaTime;

        // 타겟이 죽었으면 해제
        if (_lockedTarget != null && _lockedTarget.State == EmployeeState.Dead)
        {
            _candidates.Remove(_lockedTarget);
            _lockedTarget = null;
            _state = State.Idle;
            TryAcquireTarget();
        }

        switch (_state)
        {
            case State.Idle:
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
                break;

            case State.Positioning:
                UpdatePositioning();
                break;

            case State.Shooting:
                FireProjectile();
                break;

            case State.Cooldown:
                _cooldownTimer -= Time.deltaTime;
                if (_cooldownTimer <= 0f)
                    _state = State.Positioning;
                break;
        }
    }

    private void UpdatePositioning()
    {
        if (_lockedTarget == null)
        {
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            _state = State.Idle;
            return;
        }

        float dist = Vector2.Distance(transform.position, _lockedTarget.transform.position);

        if (dist < _minRange)
        {
            // 너무 가까우면 수평 후퇴
            float dir = -Mathf.Sign(_lockedTarget.transform.position.x - transform.position.x);
            _rb.linearVelocity = new Vector2(dir * _moveSpeed, _rb.linearVelocity.y);
            TryStepUp(dir);
        }
        else if (dist > _attackRange)
        {
            // 사거리 밖이면 수평 접근
            float dir = Mathf.Sign(_lockedTarget.transform.position.x - transform.position.x);
            _rb.linearVelocity = new Vector2(dir * _moveSpeed, _rb.linearVelocity.y);
            TryStepUp(dir);
        }
        else
        {
            // 사거리 안 → 발사 준비
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            _state = State.Shooting;
        }
    }

    private void FireProjectile()
    {
        if (_lockedTarget == null || _lockedTarget.State == EmployeeState.Dead)
        {
            _state = State.Idle;
            return;
        }

        float dist = Vector2.Distance(transform.position, _lockedTarget.transform.position);
        if (dist < _minRange || dist > _attackRange)
        {
            _state = State.Positioning;
            return;
        }

        // ── Fix #4: 시야 판정 ──
        if (!HasLineOfSight(_lockedTarget))
        {
            // 시야가 막혔으면 접근 시도
            _state = State.Positioning;
            return;
        }

        Vector2 direction = (_lockedTarget.transform.position - transform.position).normalized;
        var proj = ErosionProjectilePool.instance.Rent(transform.position);
        proj.Init(direction, _projectileSpeed, _erosionOnHit, _healthDamageOnHit, _projectileLifetime);

        _cooldownTimer = _attackInterval;
        _state         = State.Cooldown;
    }

    #endregion

    // ─────────────────────────────────────────────
    #region 높이차 1 돌파 (Step-Up)

    /// <summary>
    /// 이동 방향에 높이 1짜리 장애물이 있으면 수직 충격량을 가해 올라섭니다.
    /// - 이미 공중이거나 쿨다운 중이면 무시
    /// - 전방 장애물: solid 콜라이더 (트리거·직원·제노프스 제외)
    /// - 상단 공간: 1.1타일 위 + 천장 확인
    /// </summary>
    private void TryStepUp(float moveDir)
    {
        if (_stepUpCooldown > 0f) return;
        // 수직 속도가 크면 이미 점프/낙하 중
        if (Mathf.Abs(_rb.linearVelocity.y) > 1.5f) return;

        Vector2 center  = _rb.position;
        Vector2 forward = new Vector2(Mathf.Sign(moveDir), 0f);

        // ── 전방 장애물 확인 (중심 높이) ──────────────
        var wallHit = Physics2D.Raycast(center, forward, 0.75f);
        if (wallHit.collider == null)               return;
        if (wallHit.collider.isTrigger)             return;
        if (wallHit.collider.gameObject == gameObject) return;
        if (wallHit.collider.GetComponent<Employee>() != null) return;
        if (wallHit.collider.GetComponent<Xenops>()   != null) return;

        // ── 1타일 위 전방이 열려 있는지 확인 ──────────
        var topHit = Physics2D.Raycast(center + Vector2.up * 1.1f, forward, 0.75f);
        if (topHit.collider != null && !topHit.collider.isTrigger) return;

        // ── 천장 확인 (위로 올라갈 공간) ──────────────
        var ceilHit = Physics2D.Raycast(center, Vector2.up, 1.2f);
        if (ceilHit.collider != null && !ceilHit.collider.isTrigger) return;

        // ── 수직 충격량 적용 ───────────────────────────
        _rb.linearVelocity    = new Vector2(_rb.linearVelocity.x, _stepUpSpeed);
        _stepUpCooldown = STEP_UP_COOLDOWN;
    }

    #endregion

    // ─────────────────────────────────────────────
    #region 시야 판정 (Fix #4)

    /// <summary>
    /// 타겟까지 직선 경로에 장애물이 없는지 확인합니다.
    /// Rigidbody2D가 있는 콜라이더(건물, 타일맵 등)가 먼저 감지되면 false.
    /// </summary>
    private bool HasLineOfSight(Employee target)
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
            if (hit.collider.GetComponent<Employee>() == target) return true; // 타겟 도달 = LOS OK
            return false; // 다른 물체 먼저 감지 = 시야 차단
        }

        return true; // 아무것도 없으면 LOS OK
    }

    #endregion
}
