using UnityEngine;

/// <summary>
/// 스피터 행동 컴포넌트 (IXenopsBehavior 구현).
/// 제일 기본적인 원거리 개체형 제놉스.
///
/// 동작:
///   - 가장 가까운 직원을 사거리(6블럭) 안에서 공격
///   - 사거리보다 가까우면 후퇴 (최소 유지거리 = attackRange * 0.6)
///   - 사거리보다 멀면 접근
///   - 공격 적중 시: DoT 적용 + 침식 수치 증가 (피격 침식)
///
/// 필요 컴포넌트 (프리팹):
///   Xenops, XenopsHealth, HostileErosionAura, Rigidbody2D, Collider2D, SpitterData SO
/// </summary>
[RequireComponent(typeof(Xenops))]
[RequireComponent(typeof(Rigidbody2D))]
public class SpitterBehavior : MonoBehaviour, IXenopsBehavior
{
    // ─── IXenopsBehavior ──────────────────────
    public XenopsType BehaviorType => XenopsType.Hostile;
    public bool IsActive => _isActive;

    // ─── 상태 ─────────────────────────────────
    private enum State { Idle, Positioning, Firing, Cooldown }

    private Xenops      _xenops;
    private SpitterData _spitterData;
    private Rigidbody2D _rb;
    private bool        _isActive;

    // 스탯 (Start에서 읽음)
    private float _moveSpeed;
    private float _attackDamage;
    private float _attackRange;
    private float _attackInterval;
    private float _minRange;          // 최소 유지 거리 = attackRange * 0.6

    // 스피터 전용
    private float _hitErosionAmount;
    private float _dotDps;
    private float _dotDuration;

    // 런타임
    private State    _state = State.Idle;
    private Employee _target;
    private float    _cooldownTimer;
    private float    _aimTimer;   // 조준 진행 — 거리를 벗어나면 처음부터

    // ─────────────────────────────────────────
    #region 초기화

    private void Awake()
    {
        _xenops = GetComponent<Xenops>();
        _rb     = GetComponent<Rigidbody2D>();
        _nav    = GetComponent<EnemyNavigator>() ?? gameObject.AddComponent<EnemyNavigator>();
    }

    private EnemyNavigator _nav;

    /// <summary>같은 높이로 볼 세로 차이 — 이보다 떨어진 직원은 길을 따라 먼저 내려가거나 올라간다</summary>
    private const float SAME_LEVEL_DY = 1.5f;

    private void Start()
    {
        _spitterData = _xenops?.Data as SpitterData;

        if (_xenops?.Data?.hostileStats == null) return;
        var s = _xenops.Data.hostileStats;

        _moveSpeed      = s.moveSpeed;
        _attackDamage   = s.attackDamage;
        _attackRange    = s.attackRange;
        _attackInterval = s.attackSpeed > 0f ? 1f / s.attackSpeed : 1f;
        _minRange       = _attackRange * 0.6f;

        if (_spitterData != null)
        {
            _hitErosionAmount = _spitterData.hitErosionAmount;
            _dotDps           = _spitterData.dotDamagePerSecond;
            _dotDuration      = _spitterData.dotDuration;
        }
    }

    #endregion

    // ─────────────────────────────────────────
    #region IXenopsBehavior

    public void OnActivated()
    {
        _isActive = true;
        _state    = State.Idle;
    }

    public void OnDeactivated()
    {
        _isActive = false;
        _rb.linearVelocity = Vector2.zero;
    }

    public void UpdateBehavior()
    {
        if (!_isActive) return;

        switch (_state)
        {
            case State.Idle:
            case State.Positioning:
                UpdatePositioning();
                break;

            case State.Firing:
                Fire();
                break;

            case State.Cooldown:
                UpdateCooldown();
                break;
        }
    }

    #endregion

    // ─────────────────────────────────────────
    #region AI

    private void UpdatePositioning()
    {
        // 길을 막은 건물 — 사거리 안이면 산을 뱉어 부순다
        var wall = _nav.TargetBuilding;
        if (wall != null && EnemyNavigator.DistanceToBuilding(transform.position, wall) <= _attackRange)
        {
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            if (_cooldownTimer <= 0f)
            {
                wall.TakeDamage(Mathf.Max(1, Mathf.RoundToInt(_attackDamage)));
                _cooldownTimer = _attackInterval;
                _state = State.Cooldown;
            }
            return;
        }

        _target = _nav.TargetEmployee ?? FindNearestEmployee();

        if (_target == null)
        {
            if (!_nav.Steer(_moveSpeed)) _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            _state       = State.Idle;
            return;
        }

        // 다른 층이면 사거리 계산보다 길 따라가기가 먼저
        if (Mathf.Abs(_target.transform.position.y - transform.position.y) > SAME_LEVEL_DY && _nav.Steer(_moveSpeed))
        {
            _state = State.Positioning;
            return;
        }

        float dist = HorizontalDist(_target);

        if (dist < _minRange)
        {
            // 너무 가까우면 후퇴
            float dir = -Mathf.Sign(_target.transform.position.x - transform.position.x);
            _rb.linearVelocity = new Vector2(dir * _moveSpeed, _rb.linearVelocity.y);
            _state       = State.Positioning;
        }
        else if (dist > _attackRange)
        {
            // 사거리 밖이면 접근 — 길이 있으면 길을 따라
            if (!_nav.Steer(_moveSpeed))
            {
                float dir = Mathf.Sign(_target.transform.position.x - transform.position.x);
                _rb.linearVelocity = new Vector2(dir * _moveSpeed, _rb.linearVelocity.y);
            }
            _state       = State.Positioning;
        }
        else
        {
            // 사거리 안 → 발사 준비
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            _state       = State.Firing;
        }
    }

    private void Fire()
    {
        if (_target == null || _target.State == EmployeeState.Dead)
        {
            _state = State.Idle;
            return;
        }

        float dist = HorizontalDist(_target);

        if (dist < _minRange || dist > _attackRange)
        {
            _aimTimer = 0f;
            _state = State.Positioning;
            return;
        }

        // 조준 → 명중 판정. 빗나가면 피해 없이 다시 쿨다운
        _aimTimer += Time.deltaTime;
        if (_xenops != null && _xenops.Data != null && _aimTimer < _xenops.Data.rangedAimTime) return;
        _aimTimer = 0f;
        if (_xenops != null && _xenops.Data != null && !_xenops.Data.RollRangedHit())
        {
            _cooldownTimer = _attackInterval;
            _state         = State.Cooldown;
            return;
        }

        // ── 공격 적중 처리 ──
        // 1. 즉시 체력 피해
        _target.TakeDamage(_attackDamage);

        // 2. 지속 피해 (DoT)
        if (_dotDps > 0f && _dotDuration > 0f)
            DotEffect.Apply(_target, _dotDps, _dotDuration);

        // 3. 피격 침식 누적
        if (_hitErosionAmount > 0f)
            _target.ErosionController?.AddErosion(
                _hitErosionAmount, ErosionSource.PROJECTILE, "원거리 피격");

        _cooldownTimer = _attackInterval;
        _state         = State.Cooldown;
    }

    private void UpdateCooldown()
    {
        _cooldownTimer -= Time.deltaTime;
        if (_cooldownTimer <= 0f)
            _state = State.Positioning;
    }

    // ─────────────────────────────────────────
    private Employee FindNearestEmployee()
    {
        // 방어 태세 직원 어그로 가중 (CombatTargeting 공통 헬퍼)
        return CombatTargeting.PickEmployeeTarget(transform.position);
    }

    private float HorizontalDist(Employee emp)
        => Mathf.Abs(emp.transform.position.x - transform.position.x);

    #endregion

    // ─────────────────────────────────────────
    #region Unity Update

    // 다른 Hostile Behavior들과 동일하게 스스로 구동한다.
    // (스턴은 Xenops.Stun이 이 컴포넌트의 enabled를 꺼서 정지시킨다)
    private void Update()
    {
        if (_isActive) _nav.Tick(Time.deltaTime);
        UpdateBehavior();
    }

    #endregion
}
