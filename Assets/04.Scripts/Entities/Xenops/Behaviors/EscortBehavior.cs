using UnityEngine;

/// <summary>
/// 침식 호위병 행동 컴포넌트 (IXenopsBehavior 구현).
/// 원거리 침식체(침식 사수)를 호위하는 근접 개체 — 먼저 달려들지 않는다.
///
/// 동작:
///   1. 가장 가까운 침식 사수를 호위 대상으로 삼는다
///   2. 평소엔 호위 대상 곁(사수와 사수가 노리는 쪽 사이)에 선다
///   3. 호위 대상 guardRadius 안으로 들어온 직원만 막아서서 공격한다
///      — 호위 대상에서 leashRadius 이상 멀어지면 추격을 멈추고 돌아간다
///   4. 호위 대상을 잃으면(전멸) 가장 가까운 직원에게 돌진한다
///   5. 1~3칸 단차는 도약으로 넘는다 (사수를 따라가야 하므로). 그 이상은 멈춘다 — 벽은 부수지 않는다
///
/// 필요 컴포넌트 (프리팹):
///   Xenops, XenopsHealth, HostileErosionAura, Rigidbody2D, Collider2D
///   EscortData SO (xenopsType = Hostile)
/// </summary>
[RequireComponent(typeof(Xenops))]
[RequireComponent(typeof(Rigidbody2D))]
public class EscortBehavior : MonoBehaviour, IXenopsBehavior
{
    #region 상수

    private const float RECHARGE_INTERVAL = 0.5f;   // 호위 대상 재탐색 주기
    private const float ARRIVE_EPSILON = 0.3f;      // 자리 도착 판정 (가로)
    private const int MAX_STEP_HEIGHT = 3;
    private const float STEP_UP_COOLDOWN = 0.4f;
    private const float WALL_PROBE = 0.75f;

    #endregion

    // ─── IXenopsBehavior ──────────────────────
    public XenopsType BehaviorType => XenopsType.Hostile;
    public bool IsActive => _isActive;

    #region 참조 · 스탯

    private Xenops _xenops;
    private Rigidbody2D _rb;
    private Collider2D _collider;
    private SpriteRenderer _sr;
    private bool _isActive;

    private float _moveSpeed;
    private float _attackDamage;
    private float _attackRange;
    private float _attackInterval;
    private float _gravity;

    private float _chargeSearchRadius = 25f;
    private float _followDistance = 1.5f;
    private float _guardRadius = 4f;
    private float _leashRadius = 6f;

    #endregion

    #region 런타임

    private ErosionShooterBehavior _charge;
    private float _rechargeTimer;
    private float _cooldownTimer;
    private float _stepUpCooldown;

    #endregion

    #region 초기화

    private void Awake()
    {
        _xenops   = GetComponent<Xenops>();
        _rb       = GetComponent<Rigidbody2D>();
        _collider = GetComponent<Collider2D>();
        _sr       = GetComponent<SpriteRenderer>();
    }

    private void Start()
    {
        if (_xenops?.Data?.hostileStats == null) return;
        var s = _xenops.Data.hostileStats;
        _moveSpeed      = s.moveSpeed;
        _attackDamage   = s.attackDamage;
        _attackRange    = s.attackRange;
        _attackInterval = s.attackSpeed > 0f ? 1f / s.attackSpeed : 1f;
        _gravity        = Mathf.Abs(Physics2D.gravity.y) * _rb.gravityScale;

        if (_xenops.Data is EscortData e)
        {
            _chargeSearchRadius = e.chargeSearchRadius;
            _followDistance     = e.followDistance;
            _guardRadius        = e.guardRadius;
            _leashRadius        = e.leashRadius;
        }
    }

    #endregion

    #region IXenopsBehavior

    public void OnActivated()
    {
        _isActive = true;
        _rechargeTimer = 0f;
    }

    public void OnDeactivated()
    {
        _isActive = false;
        _rb.linearVelocity = Vector2.zero;
    }

    public void UpdateBehavior()
    {
        if (!_isActive) return;

        float dt = Time.deltaTime;
        if (_cooldownTimer  > 0f) _cooldownTimer  -= dt;
        if (_stepUpCooldown > 0f) _stepUpCooldown -= dt;

        _rechargeTimer -= dt;
        if (_charge == null || !_charge.isActiveAndEnabled || _rechargeTimer <= 0f)
        {
            _charge = FindCharge();
            _rechargeTimer = RECHARGE_INTERVAL;
        }

        // 호위 대상을 잃으면 돌진
        if (_charge == null)
        {
            var target = CombatTargeting.PickEmployeeTarget(transform.position);
            if (target != null) ChaseAndAttack(target);
            else Stop();
            return;
        }

        Vector2 chargePos = _charge.transform.position;
        Employee threat = FindThreatNear(chargePos);
        bool withinLeash = Vector2.Distance(transform.position, chargePos) <= _leashRadius;

        if (threat != null && withinLeash) ChaseAndAttack(threat);
        else Follow(chargePos);
    }

    #endregion

    #region AI

    /// <summary>가장 가까운 활성 침식 사수</summary>
    private ErosionShooterBehavior FindCharge()
    {
        ErosionShooterBehavior best = null;
        float bestDist = _chargeSearchRadius;
        Vector2 myPos = transform.position;

        foreach (var s in FindObjectsByType<ErosionShooterBehavior>())
        {
            if (s == null || !s.IsActive) continue;
            float d = Vector2.Distance(myPos, s.transform.position);
            if (d <= bestDist) { bestDist = d; best = s; }
        }
        return best;
    }

    /// <summary>호위 대상 guardRadius 안에 들어온 직원 중 대상에 가장 가까운 직원</summary>
    private Employee FindThreatNear(Vector2 chargePos)
    {
        if (EmployeeManager.instance == null) return null;

        Employee best = null;
        float bestDist = _guardRadius;
        foreach (var emp in EmployeeManager.instance.AllEmployees)
        {
            if (emp == null || emp.State == EmployeeState.Dead) continue;
            float d = Vector2.Distance(chargePos, emp.transform.position);
            if (d <= bestDist) { bestDist = d; best = emp; }
        }
        return best;
    }

    /// <summary>호위 대상 곁 — 대상과 대상이 노리는 쪽 사이에 선다</summary>
    private void Follow(Vector2 chargePos)
    {
        float side;
        var aim = _charge.CurrentTarget;
        if (aim != null) side = Mathf.Sign(aim.transform.position.x - chargePos.x);
        else side = Mathf.Sign(transform.position.x - chargePos.x);
        if (Mathf.Approximately(side, 0f)) side = 1f;

        float desiredX = chargePos.x + side * _followDistance;
        float dx = desiredX - transform.position.x;

        if (Mathf.Abs(dx) <= ARRIVE_EPSILON) Stop();
        else Move(Mathf.Sign(dx));
    }

    private void ChaseAndAttack(Employee target)
    {
        if (Vector2.Distance(transform.position, target.transform.position) <= _attackRange)
        {
            Stop();
            FaceTowards(target.transform.position.x);
            if (_cooldownTimer <= 0f)
            {
                target.TakeDamage(_attackDamage);
                _cooldownTimer = _attackInterval;
            }
            return;
        }

        Move(Mathf.Sign(target.transform.position.x - transform.position.x));
    }

    private void Move(float dir)
    {
        FaceTowards(transform.position.x + dir);
        if (!TryStepUp(dir))
        {
            Stop(); // 4칸 이상 벽 — 호위병은 벽을 부수지 않는다
            return;
        }
        _rb.linearVelocity = new Vector2(dir * _moveSpeed, _rb.linearVelocity.y);
    }

    private void Stop() => _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);

    private void FaceTowards(float x)
    {
        if (_sr != null) _sr.flipX = x < transform.position.x;
    }

    /// <summary>
    /// 앞을 막는 1~3칸 단차는 도약으로 넘는다. 넘을 수 없으면(4칸+·천장) false.
    /// 장애물이 없거나 도약 중·쿨다운이면 true (그대로 전진).
    /// </summary>
    private bool TryStepUp(float dir)
    {
        if (_stepUpCooldown > 0f || Mathf.Abs(_rb.linearVelocity.y) > 1.5f) return true;

        Vector2 center  = _rb.position;
        Vector2 forward = new Vector2(Mathf.Sign(dir), 0f);
        float skin = (_collider != null ? _collider.bounds.extents.x : 0.3f) + 0.05f;

        var wallHit = Physics2D.Raycast(center + forward * skin, forward, WALL_PROBE);
        if (!IsSolid(wallHit.collider)) return true;

        int clearHeight = 0;
        for (int h = 1; h <= MAX_STEP_HEIGHT; h++)
        {
            var sideHit = Physics2D.Raycast(center + Vector2.up * (h + 0.1f), forward, WALL_PROBE);
            if (!IsSolid(sideHit.collider)) { clearHeight = h; break; }
        }
        if (clearHeight == 0) return false;

        var ceilHit = Physics2D.Raycast(center + Vector2.up * skin, Vector2.up, clearHeight + 0.4f);
        if (IsSolid(ceilHit.collider)) return false;

        _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, Mathf.Sqrt(2f * _gravity * (clearHeight + 0.4f)));
        _stepUpCooldown = STEP_UP_COOLDOWN;
        return true;
    }

    /// <summary>솔리드 장애물인지 — 트리거·자신·직원·침식체는 제외</summary>
    private bool IsSolid(Collider2D col)
    {
        if (col == null || col.isTrigger) return false;
        if (col.gameObject == gameObject) return false;
        if (col.GetComponentInParent<Employee>() != null) return false;
        if (col.GetComponentInParent<Xenops>() != null) return false;
        return true;
    }

    #endregion

    // 다른 Hostile Behavior들과 동일하게 스스로 구동한다.
    // (스턴은 Xenops.Stun이 이 컴포넌트의 enabled를 꺼서 정지시킨다)
    private void Update()
    {
        UpdateBehavior();
    }
}
