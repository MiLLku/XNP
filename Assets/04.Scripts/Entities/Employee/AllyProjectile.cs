using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 아군(직원) 원거리 공격 투사체. ErosionProjectile과 동일한 풀 패턴.
///   - 적대(Hostile) 제노프스 명중 → XenopsHealth 피해 + onHit 콜백(OnHit 장비능력) 후 반환
///   - 직원/아군·적 투사체는 통과, 건물·타일 명중/수명 만료 → 피해 없이 반환
///
/// 프리팹 구성: SpriteRenderer / Rigidbody2D(gravityScale=0, Continuous) /
///             CircleCollider2D(isTrigger) / AllyProjectile
/// 참조: CombatConfig.allyProjectilePrefab — 발사는 EmployeeCombat(원거리 무기).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class AllyProjectile : MonoBehaviour, IPoolable
{
    private float _damage;
    private float _penetration;
    private float _lifetime;
    private bool  _isReturned;
    private System.Action<Xenops> _onHit;

    private Rigidbody2D _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
    }

    // ─── IPoolable ─────────────────────────────
    public void OnSpawn()
    {
        _isReturned = false;
    }

    public void OnDespawn()
    {
        if (_rb != null) _rb.linearVelocity = Vector2.zero;
        _onHit = null;
    }

    // ─── 초기화 (풀에서 꺼낼 때마다 호출) ────────
    /// <param name="damage">피해량. 0이면 빗나간 사격이라 명중해도 피해를 주지 않고 사라집니다.</param>
    /// <param name="penetration">방어 관통력 (0~1)</param>
    public void Init(Vector2 direction, float speed, float damage, float penetration, float lifetime,
                     System.Action<Xenops> onHit)
    {
        _damage      = damage;
        _penetration = penetration;
        _lifetime    = lifetime;
        _onHit       = onHit;
        _isReturned  = false;

        _rb.linearVelocity = direction.normalized * speed;
    }

    private void Update()
    {
        _lifetime -= Time.deltaTime;
        if (_lifetime <= 0f)
            ReturnToPool();
    }

    // ─── 충돌 처리 ──────────────────────────────
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_isReturned) return;

        // ── 1. 제노프스 명중 ──────────────────────
        var xenops = other.GetComponent<Xenops>() ?? other.GetComponentInParent<Xenops>();
        if (xenops != null)
        {
            // 적대 개체만 피해 — 나머지(환경/장비형 등)는 통과
            if (xenops.Type != XenopsType.Hostile || xenops.State == XenopsState.Subdued) return;

            var health = xenops.GetComponent<XenopsHealth>();
            if (health == null || health.IsDead) return;

            // 빗나간 사격(_damage = 0)도 명중한 것처럼 투사체는 사라지되 피해는 없다
            health.TakeDamage(_damage, _penetration);

            if (_damage > 0f)
                Debug.Log($"[AllyProjectile] {xenops.DisplayName} 명중 -{_damage:F1} (남은 HP {health.CurrentHealth:F0})");
            else
                Debug.Log($"[AllyProjectile] {xenops.DisplayName} 빗나감");

            _onHit?.Invoke(xenops);
            ReturnToPool();
            return;
        }

        // ── 2. 통과 대상: 직원, 다른 투사체, 건물 ──
        // 건물 통과 이유: 발사자가 서 있는 발판 건물(Stone Floor 등, blocksMovement=솔리드)에
        // 스폰 즉시 명중해 소멸하는 문제. 아군 투사체는 자기 기지 구조물과 상호작용하지 않는다.
        // (알려진 한계: 건설된 벽도 통과 — 지형 타일은 여전히 차단)
        if (other.GetComponent<Employee>() != null) return;
        if (other.GetComponent<AllyProjectile>() != null) return;
        if (other.GetComponent<ErosionProjectile>() != null) return;
        if (other.GetComponent<Building>() != null) return;

        // ── 3. 지형 타일 명중 → 피해 없이 소멸 ────
        var tilemap = other.GetComponent<Tilemap>() ?? other.GetComponentInParent<Tilemap>();
        if (tilemap != null)
        {
            Debug.Log($"[AllyProjectile] 타일 명중 소멸: {other.name} @{transform.position}");
            ReturnToPool();
        }
    }

    private void ReturnToPool()
    {
        if (_isReturned) return;
        _isReturned = true;

        if (PoolManager.instance != null)
            PoolManager.instance.Despawn(gameObject);
        else
            Destroy(gameObject);
    }
}
