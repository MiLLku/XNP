using UnityEngine;

/// <summary>
/// 그러플러 행동 컴포넌트 (IXenopsBehavior 구현).
/// 제일 기본적인 근접 개체형 제놉스.
///
/// 동작:
///   - 가장 가까운 직원을 X축으로 추적
///   - attackRange(1블럭) 이내에 들어오면 공격 (체력 피해만)
///   - 직원이 없으면 Idle
///
/// 필요 컴포넌트 (프리팹):
///   Xenops, XenopsHealth, HostileErosionAura, Rigidbody2D, Collider2D
/// </summary>
[RequireComponent(typeof(Xenops))]
[RequireComponent(typeof(Rigidbody2D))]
public class GrapplerBehavior : MonoBehaviour, IXenopsBehavior
{
    // ─── IXenopsBehavior ──────────────────────
    public XenopsType BehaviorType => XenopsType.Hostile;
    public bool IsActive => _isActive;

    // ─── 상태 ─────────────────────────────────
    private enum State { Idle, Chasing, Attacking, Cooldown }

    private Xenops    _xenops;
    private Rigidbody2D _rb;
    private bool      _isActive;

    // 스탯 (Start에서 XenopsData로부터 읽음)
    private float _moveSpeed;
    private float _attackDamage;
    private float _attackRange;
    private float _attackInterval; // 1 / attackSpeed

    // 런타임
    private State    _state      = State.Idle;
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
        if (_xenops?.Data?.hostileStats == null) return;
        var s = _xenops.Data.hostileStats;
        _moveSpeed      = s.moveSpeed;
        _attackDamage   = s.attackDamage;
        _attackRange    = s.attackRange;
        _attackInterval = s.attackSpeed > 0f ? 1f / s.attackSpeed : 1f;
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
            case State.Chasing:
                UpdateChase();
                break;

            case State.Attacking:
                Attack();
                break;

            case State.Cooldown:
                UpdateCooldown();
                break;
        }
    }

    #endregion

    // ─────────────────────────────────────────
    #region AI

    private void UpdateChase()
    {
        _target = FindNearestEmployee();

        if (_target == null)
        {
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            _state       = State.Idle;
            return;
        }

        float dist = HorizontalDist(_target);

        if (dist <= _attackRange)
        {
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            _state       = State.Attacking;
        }
        else
        {
            float dir = Mathf.Sign(_target.transform.position.x - transform.position.x);
            _rb.linearVelocity = new Vector2(dir * _moveSpeed, _rb.linearVelocity.y);
            _state       = State.Chasing;
        }
    }

    private void Attack()
    {
        if (_target == null || _target.State == EmployeeState.Dead)
        {
            _state = State.Idle;
            return;
        }

        if (HorizontalDist(_target) > _attackRange)
        {
            _state = State.Chasing;
            return;
        }

        _target.TakeDamage(_attackDamage);
        _cooldownTimer = _attackInterval;
        _state         = State.Cooldown;
    }

    private void UpdateCooldown()
    {
        _cooldownTimer -= Time.deltaTime;
        if (_cooldownTimer <= 0f)
            _state = State.Chasing;
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
    #region Unity Update (UpdateBehavior 호출)

    // Xenops.cs의 Update()에서 behavior.UpdateBehavior()를 호출하므로
    // 이 컴포넌트의 Update()는 비워둡니다.

    #endregion
}
