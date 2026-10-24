using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 자폭체 행동 (브리칭 습격 전용 적).
///
/// 동작:
///   - EnemyNavigator가 정한 건물(벽 우선, XenopsData.prefersBreaching)로 간다
///   - 닿으면 터진다: 반경 BLAST_RADIUS 안 건물에 attackDamage, 직원에게는 그 EMPLOYEE_SHARE만큼
///   - 노릴 건물이 없으면 가장 가까운 직원에게 붙어 터진다
///
/// 필요 컴포넌트 (프리팹): Xenops, XenopsHealth, Rigidbody2D, Collider2D
/// </summary>
[RequireComponent(typeof(Xenops))]
[RequireComponent(typeof(Rigidbody2D))]
public class BomberBehavior : MonoBehaviour, IXenopsBehavior
{
    // shortcut: 임시 수치 — 밸런스 확정 시 데이터로 뺄 것
    private const float BLAST_RADIUS = 2f;
    private const float EMPLOYEE_SHARE = 0.15f;
    private const float TRIGGER_DISTANCE = 1.1f;

    public XenopsType BehaviorType => XenopsType.Hostile;
    public bool IsActive => _isActive;

    private Xenops _xenops;
    private Rigidbody2D _rb;
    private EnemyNavigator _nav;
    private bool _isActive;
    private bool _exploded;
    private float _moveSpeed = 2f;
    private float _damage = 60f;

    private void Awake()
    {
        _xenops = GetComponent<Xenops>();
        _rb = GetComponent<Rigidbody2D>();
        _nav = GetComponent<EnemyNavigator>() ?? gameObject.AddComponent<EnemyNavigator>();
    }

    private void Start()
    {
        var s = _xenops?.Data?.hostileStats;
        if (s == null) return;
        _moveSpeed = s.moveSpeed;
        _damage = s.attackDamage;
    }

    public void OnActivated() => _isActive = true;

    public void OnDeactivated()
    {
        _isActive = false;
        if (_rb != null) _rb.linearVelocity = Vector2.zero;
    }

    public void UpdateBehavior()
    {
        if (!_isActive || _exploded) return;

        var wall = _nav.TargetBuilding;
        if (wall != null && EnemyNavigator.DistanceToBuilding(transform.position, wall) <= TRIGGER_DISTANCE)
        {
            Explode();
            return;
        }

        // 노릴 건물이 없으면 직원에게
        var target = wall == null ? (_nav.TargetEmployee ?? CombatTargeting.PickEmployeeTarget(transform.position)) : null;
        if (target != null && Vector2.Distance(target.transform.position, transform.position) <= TRIGGER_DISTANCE)
        {
            Explode();
            return;
        }

        if (!_nav.Steer(_moveSpeed))
        {
            Vector3? goal = wall != null ? wall.transform.position : target != null ? target.transform.position : (Vector3?)null;
            float dir = goal.HasValue ? Mathf.Sign(goal.Value.x - transform.position.x) : 0f;
            _rb.linearVelocity = new Vector2(dir * _moveSpeed, _rb.linearVelocity.y);
        }
    }

    /// <summary>터진다 — 반경 안 건물은 크게, 직원은 작게. 자기는 사라진다</summary>
    private void Explode()
    {
        _exploded = true;
        Vector2 c = transform.position;
        int r = Mathf.CeilToInt(BLAST_RADIUS);

        var hit = new HashSet<Building>();
        for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                var cell = new Vector2Int(Mathf.FloorToInt(c.x) + dx, Mathf.FloorToInt(c.y) + dy);
                if (Vector2.Distance(c, new Vector2(cell.x + 0.5f, cell.y + 0.5f)) > BLAST_RADIUS + 0.5f) continue;
                var b = Building.GetBuildingAt(cell);
                if (b != null && hit.Add(b)) b.TakeDamage(Mathf.Max(1, Mathf.RoundToInt(_damage)));
            }

        if (EmployeeManager.instance != null)
            foreach (var e in EmployeeManager.instance.AllEmployees.ToArray())
                if (e != null && e.State != EmployeeState.Dead && Vector2.Distance(c, e.transform.position) <= BLAST_RADIUS)
                    e.TakeDamage(_damage * EMPLOYEE_SHARE);

        Debug.Log($"[BomberBehavior] 자폭 @ {c} — 건물 {hit.Count}개");
        Destroy(gameObject);
    }

    private void Update()
    {
        if (_isActive && !_exploded) _nav.Tick(Time.deltaTime);
        UpdateBehavior();
    }
}
