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

    // ─────────────────────────────────────────
    #region 초기화

    private void Awake()
    {
        _xenops = GetComponent<Xenops>();
        _rb     = GetComponent<Rigidbody2D>();
    }

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
        _target = FindNearestEmployee();

        if (_target == null)
        {
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            _state       = State.Idle;
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
            // 사거리 밖이면 접근
            float dir = Mathf.Sign(_target.transform.position.x - transform.position.x);
            _rb.linearVelocity = new Vector2(dir * _moveSpeed, _rb.linearVelocity.y);
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
            _state = State.Positioning;
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
            _target.SetErosion(_target.ErosionLevel + _hitErosionAmount);

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
        if (EmployeeManager.instance == null) return null;

        Employee nearest = null;
        float    minDist = float.MaxValue;

        foreach (var emp in EmployeeManager.instance.AllEmployees)
        {
            if (emp == null || emp.State == EmployeeState.Dead) continue;
            float d = Vector2.Distance(transform.position, emp.transform.position);
            if (d < minDist) { minDist = d; nearest = emp; }
        }

        return nearest;
    }

    private float HorizontalDist(Employee emp)
        => Mathf.Abs(emp.transform.position.x - transform.position.x);

    #endregion
}
