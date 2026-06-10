using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 침식 투사체.
/// ErosionProjectilePool에서 꺼내 사용하며, 아래 조건 중 하나로 풀에 반환됩니다.
///   - 직원 명중 → 침식 + 체력 피해 적용
///   - 건물 명중 → 건물 피해 적용 (건물 체력 시스템 연동 대기)
///   - 타일 명중 → 타일 파괴
///   - 수명 만료
///
/// 프리팹 구성:
///   SpriteRenderer / Rigidbody2D (gravityScale=0, Continuous) /
///   CircleCollider2D (isTrigger=true, radius=0.5) / ErosionProjectile
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class ErosionProjectile : MonoBehaviour, IPoolable
{
    private float _erosionAmount;
    private float _healthDamage;
    private float _structureDamage;
    private float _lifetime;
    private bool  _isReturned;

    private Rigidbody2D _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
    }

    // ─── IPoolable ─────────────────────────────
    /// <summary>풀에서 꺼낼 때 호출. Init()이 곧이어 발사 파라미터를 설정합니다.</summary>
    public void OnSpawn()
    {
        _isReturned = false;
    }

    /// <summary>풀로 반환되기 직전 호출. 다음 재사용 시 잔존 velocity 방지.</summary>
    public void OnDespawn()
    {
        if (_rb != null) _rb.linearVelocity = Vector2.zero;
    }

    // ─── 초기화 (풀에서 꺼낼 때마다 호출) ────────
    public void Init(Vector2 direction, float speed, float erosionAmount, float healthDamage,
                     float structureDamage, float lifetime)
    {
        _erosionAmount   = erosionAmount;
        _healthDamage    = healthDamage;
        _structureDamage = structureDamage;
        _lifetime        = lifetime;
        _isReturned      = false;

        _rb.linearVelocity = direction.normalized * speed;
    }

    // ─── 생명주기 ────────────────────────────────
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

        // ── 1. 직원 명중 ──────────────────────────
        var employee = other.GetComponent<Employee>();
        if (employee != null)
        {
            if (employee.State == EmployeeState.Dead) return;

            if (_healthDamage > 0f)
                employee.ModifyHealth(-_healthDamage);

            if (_erosionAmount > 0f)
                employee.SetErosion(employee.ErosionLevel + _erosionAmount);

            ReturnToPool();
            return;
        }

        // ── 2. 건물 명중 ──────────────────────────
        var building = other.GetComponent<Building>();
        if (building != null)
        {
            HandleBuildingHit(building);
            ReturnToPool();
            return;
        }

        // ── 3. 타일맵 명중 ────────────────────────
        var tilemap = other.GetComponent<Tilemap>()
                   ?? other.GetComponentInParent<Tilemap>();

        if (tilemap != null)
        {
            HandleTileHit(tilemap);
            ReturnToPool();
        }
    }

    // ─────────────────────────────────────────────
    #region 피해 처리

    private void HandleBuildingHit(Building building)
    {
        // 건설물(벽 등)에 구조물 피해를 가함. 체력이 0이 되면 Building이 스스로 파괴됨.
        if (_structureDamage > 0f)
        {
            int dmg = Mathf.CeilToInt(_structureDamage);
            building.TakeDamage(dmg);
            Debug.Log($"[ErosionProjectile] 건물 명중: {building.name} -{dmg} (남은 체력 {building.CurrentHealth})");
        }
    }

    private void HandleTileHit(Tilemap tilemap)
    {
        if (MapGenerator.instance == null) return;

        var gameMap     = MapGenerator.instance.GameMapInstance;
        var mapRenderer = MapGenerator.instance.MapRendererInstance;
        if (gameMap == null || mapRenderer == null) return;

        // 투사체 현재 위치 → 타일 좌표
        Vector3Int cellPos = tilemap.WorldToCell(transform.position);
        int x = cellPos.x;
        int y = cellPos.y;

        if (x < 0 || x >= GameMap.MAP_WIDTH || y < 0 || y >= GameMap.MAP_HEIGHT) return;

        int tileID = gameMap.TileGrid[x, y];

        // 빈 타일(0) 또는 바닥 타일(7)은 무시
        if (tileID == 0 || tileID == 7) return;

        // 이미 채광 작업 중인 타일은 건드리지 않음
        if (WorkSystemManager.instance != null &&
            WorkSystemManager.instance.IsTileUnderWork(cellPos)) return;

        // 타일 파괴 (채광 작업과 달리 자원 드랍 없음)
        gameMap.SetTile(x, y, 0);
        gameMap.UnmarkTileOccupied(x, y);
        mapRenderer.UpdateTileVisual(x, y);

        Debug.Log($"[ErosionProjectile] 타일 파괴: ({x},{y}) tileID={tileID}");
    }

    #endregion

    // ─── 풀 반환 ────────────────────────────────
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
