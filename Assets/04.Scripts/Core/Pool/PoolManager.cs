using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 범용 오브젝트 풀 매니저 (싱글톤).
///
/// 특징:
///   - Prefab 키 기반: Spawn(prefab, pos) / Despawn(instance)
///   - 자동 풀 등록: 처음 보는 prefab을 Spawn하면 풀이 자동 생성됨
///   - 사전 등록: Inspector의 registeredPools 또는 RegisterPool(prefab, prewarm, max) 호출로 워밍업
///   - IPoolable 콜백: Spawn/Despawn 시 자동 호출 (구현 안 했으면 SetActive만)
///   - 풀별 부모: 인스턴스를 풀 이름의 GameObject 하위에 정리
///   - 최대 크기 초과: 풀이 가득 차면 진짜 Destroy
///
/// 사용 예:
///   var item = PoolManager.instance.Spawn&lt;DroppedItem&gt;(prefab, worldPos);
///   PoolManager.instance.Despawn(item.gameObject);
/// </summary>
public class PoolManager : DestroySingleton<PoolManager>
{
    #region Inspector 설정

    /// <summary>Inspector에서 사전 등록할 풀 항목.</summary>
    [System.Serializable]
    public struct PoolConfig
    {
        [Tooltip("풀링할 프리팹")]
        public GameObject prefab;

        [Tooltip("게임 시작 시 미리 만들어둘 인스턴스 수")]
        public int prewarm;

        [Tooltip("이 풀에 보관할 최대 비활성 인스턴스 수 (초과분은 Destroy)")]
        public int maxSize;
    }

    [Header("사전 등록 풀 (선택)")]
    [Tooltip("게임 시작 시 자동으로 RegisterPool을 호출할 풀 목록")]
    [SerializeField] private List<PoolConfig> registeredPools = new List<PoolConfig>();

    [Header("기본값")]
    [Tooltip("RegisterPool 호출 없이 자동 등록되는 풀의 최대 크기 기본값")]
    [SerializeField] private int defaultMaxSize = 256;

    [Header("디버그")]
    [SerializeField] private bool showDebugInfo = false;

    #endregion

    #region 내부 상태

    private class Pool
    {
        public GameObject prefab;
        public Queue<GameObject> inactive = new Queue<GameObject>();
        public int maxSize;
        public Transform parent;
    }

    /// <summary>prefab 키 → Pool</summary>
    private readonly Dictionary<GameObject, Pool> _poolsByPrefab = new Dictionary<GameObject, Pool>();

    /// <summary>instance 키 → 원본 prefab (Despawn 시 어느 풀로 돌려보낼지 추적)</summary>
    private readonly Dictionary<GameObject, GameObject> _instanceToPrefab = new Dictionary<GameObject, GameObject>();

    #endregion

    #region 초기화

    protected override void Awake()
    {
        base.Awake();

        if (registeredPools != null)
        {
            foreach (var cfg in registeredPools)
            {
                if (cfg.prefab == null) continue;
                int max = cfg.maxSize > 0 ? cfg.maxSize : defaultMaxSize;
                RegisterPool(cfg.prefab, cfg.prewarm, max);
            }
        }
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 프리팹의 풀을 사전 등록합니다. prewarm 개수만큼 즉시 생성해 풀에 적재합니다.
    /// 이미 등록된 풀이면 maxSize만 갱신하고 추가 prewarm은 무시합니다.
    /// </summary>
    public void RegisterPool(GameObject prefab, int prewarm = 0, int maxSize = -1)
    {
        if (prefab == null) return;
        int max = maxSize > 0 ? maxSize : defaultMaxSize;

        Pool pool = GetOrCreatePool(prefab, max);
        pool.maxSize = max;

        // Prewarm — 풀이 비어있을 때만 추가 생성
        int toCreate = Mathf.Max(0, prewarm - pool.inactive.Count);
        for (int i = 0; i < toCreate; i++)
        {
            var obj = Instantiate(prefab, pool.parent);
            obj.SetActive(false);
            pool.inactive.Enqueue(obj);
            _instanceToPrefab[obj] = prefab;
        }

        if (showDebugInfo)
            Debug.Log($"[PoolManager] '{prefab.name}' 풀 등록 (prewarm={toCreate}, max={max})");
    }

    /// <summary>
    /// 풀에서 인스턴스를 가져옵니다. 풀이 비었으면 새로 생성합니다.
    /// 처음 보는 prefab이면 풀이 자동으로 생성됩니다 (maxSize = defaultMaxSize).
    /// </summary>
    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return null;
        Pool pool = GetOrCreatePool(prefab, defaultMaxSize);

        GameObject obj = null;
        // 풀에서 유효한 인스턴스 꺼내기
        while (pool.inactive.Count > 0 && obj == null)
        {
            var candidate = pool.inactive.Dequeue();
            if (candidate == null)
            {
                // 외부에서 Destroy된 경우 — 추적 맵에서도 제거
                continue;
            }
            obj = candidate;
            obj.transform.SetPositionAndRotation(position, rotation);
            obj.SetActive(true);
        }

        // 풀이 비었으면 새로 생성
        if (obj == null)
        {
            obj = Instantiate(prefab, position, rotation, pool.parent);
            _instanceToPrefab[obj] = prefab;
        }

        // IPoolable 콜백
        var poolables = obj.GetComponents<IPoolable>();
        for (int i = 0; i < poolables.Length; i++)
            poolables[i].OnSpawn();

        return obj;
    }

    /// <summary>회전을 생략한 Spawn 오버로드. (Quaternion.identity)</summary>
    public GameObject Spawn(GameObject prefab, Vector3 position) =>
        Spawn(prefab, position, Quaternion.identity);

    /// <summary>제네릭 헬퍼: GetComponent&lt;T&gt;()를 같이 호출합니다.</summary>
    public T Spawn<T>(GameObject prefab, Vector3 position, Quaternion rotation) where T : Component
    {
        var go = Spawn(prefab, position, rotation);
        return go != null ? go.GetComponent<T>() : null;
    }

    /// <summary>제네릭 헬퍼 (회전 생략).</summary>
    public T Spawn<T>(GameObject prefab, Vector3 position) where T : Component =>
        Spawn<T>(prefab, position, Quaternion.identity);

    /// <summary>
    /// 인스턴스를 풀로 반환합니다.
    /// 풀이 가득 차면 진짜 Destroy합니다.
    /// 풀에 추적되지 않는 인스턴스(다른 경로로 생성)이면 Destroy합니다.
    /// </summary>
    public void Despawn(GameObject instance)
    {
        if (instance == null) return;

        // IPoolable 콜백 (SetActive 전)
        var poolables = instance.GetComponents<IPoolable>();
        for (int i = 0; i < poolables.Length; i++)
            poolables[i].OnDespawn();

        if (!_instanceToPrefab.TryGetValue(instance, out var prefab))
        {
            // 풀 추적 안 됨 — 외부 인스턴스이므로 그냥 파괴
            Destroy(instance);
            return;
        }

        if (!_poolsByPrefab.TryGetValue(prefab, out var pool))
        {
            _instanceToPrefab.Remove(instance);
            Destroy(instance);
            return;
        }

        // 풀이 가득 차면 진짜 Destroy
        if (pool.inactive.Count >= pool.maxSize)
        {
            _instanceToPrefab.Remove(instance);
            Destroy(instance);
            return;
        }

        instance.SetActive(false);
        pool.inactive.Enqueue(instance);
    }

    /// <summary>Component를 넘기는 편의 오버로드.</summary>
    public void Despawn(Component instance)
    {
        if (instance != null) Despawn(instance.gameObject);
    }

    /// <summary>특정 prefab 풀의 현재 비활성 인스턴스 수를 반환합니다 (디버그·통계용).</summary>
    public int GetInactiveCount(GameObject prefab)
    {
        if (prefab == null) return 0;
        return _poolsByPrefab.TryGetValue(prefab, out var pool) ? pool.inactive.Count : 0;
    }

    #endregion

    #region 내부 헬퍼

    private Pool GetOrCreatePool(GameObject prefab, int maxSize)
    {
        if (!_poolsByPrefab.TryGetValue(prefab, out var pool))
        {
            pool = new Pool
            {
                prefab  = prefab,
                maxSize = maxSize,
            };
            var parentGO = new GameObject($"Pool_{prefab.name}");
            parentGO.transform.SetParent(transform);
            pool.parent = parentGO.transform;
            _poolsByPrefab[prefab] = pool;
        }
        return pool;
    }

    #endregion

    #region 디버그

    [ContextMenu("Print Pool Status")]
    public void PrintPoolStatus()
    {
        Debug.Log($"[PoolManager] 등록된 풀 {_poolsByPrefab.Count}개:");
        foreach (var kv in _poolsByPrefab)
        {
            Debug.Log($"  - {kv.Key.name}: 비활성 {kv.Value.inactive.Count}/{kv.Value.maxSize}");
        }
    }

    #endregion
}
