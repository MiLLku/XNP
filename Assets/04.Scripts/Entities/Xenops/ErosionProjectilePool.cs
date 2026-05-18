using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// 침식 투사체 오브젝트 풀.
/// 씬에 자동으로 생성되며, ErosionShooterBehavior가 첫 초기화 시 EnsureInitialized()를 호출합니다.
/// </summary>
public class ErosionProjectilePool : DestroySingleton<ErosionProjectilePool>
{
    [SerializeField] private int initialCapacity = 20;
    [SerializeField] private int maxCapacity = 100;

    private ObjectPool<ErosionProjectile> _pool;
    private GameObject _prefab;

    // ─── 초기화 ──────────────────────────────────
    /// <summary>
    /// 풀이 아직 준비되지 않은 경우 초기화합니다.
    /// 여러 ErosionShooter가 호출해도 첫 번째만 실행됩니다.
    /// </summary>
    public void EnsureInitialized(GameObject prefab)
    {
        if (_pool != null) return;

        _prefab = prefab;
        _pool = new ObjectPool<ErosionProjectile>(
            createFunc:      CreateProjectile,
            actionOnGet:     proj => proj.gameObject.SetActive(true),
            actionOnRelease: proj => proj.gameObject.SetActive(false),
            actionOnDestroy: proj => Destroy(proj.gameObject),
            collectionCheck: false,
            defaultCapacity: initialCapacity,
            maxSize:         maxCapacity
        );

        // 초기 워밍업 — 풀 생성 시 미리 할당
        var warmup = new ErosionProjectile[initialCapacity];
        for (int i = 0; i < initialCapacity; i++)
            warmup[i] = _pool.Get();
        for (int i = 0; i < initialCapacity; i++)
            _pool.Release(warmup[i]);
    }

    // ─── 공개 API ────────────────────────────────
    public ErosionProjectile Rent(Vector3 position)
    {
        var proj = _pool.Get();
        proj.transform.position = position;
        return proj;
    }

    public void Return(ErosionProjectile proj) => _pool.Release(proj);

    // ─── 내부 ────────────────────────────────────
    private ErosionProjectile CreateProjectile()
    {
        var obj  = Instantiate(_prefab, Vector3.zero, Quaternion.identity, transform);
        var proj = obj.GetComponent<ErosionProjectile>();
        proj.SetPool(this);
        return proj;
    }
}
