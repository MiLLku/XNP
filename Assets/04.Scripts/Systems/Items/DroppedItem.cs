using UnityEngine;

/// <summary>
/// 채굴/수확으로 바닥에 떨어진 재료 아이템.
///
/// 동작:
///   - 스폰 시 DroppedItemManager에 자동 등록 → Haul WorkOrder 태스크 생성
///   - 직원이 운반을 수락하면 Claim() → IsClaimed = true (중복 할당 방지)
///   - 직원이 운반 취소 시 Unclaim()
///   - 직원이 픽업 완료 시 Remove() → 오브젝트 파괴
///
/// 비주얼:
///   - 스프라이트 없이 원형 SpriteRenderer + 아이템 아이콘 색상으로 표현
///
/// 물리:
///   - Awake에서 Rigidbody2D + CircleCollider2D를 자동 추가 (prefab에 없어도 동작)
///   - Rigidbody2D는 dynamic + gravityScale=2 로 중력 적용 → 바닥에 떨어짐
///   - DroppedItem끼리는 Physics2D.IgnoreCollision으로 충돌 무시 (DroppedItemManager에서 처리)
/// </summary>
public class DroppedItem : MonoBehaviour, IPoolable, IMaterialSource
{
    #region 상수

    /// <summary>중력 가속도 배율 (1=기본). 빠르게 떨어지도록 2로 설정.</summary>
    private const float GRAVITY_SCALE = 2f;

    /// <summary>충돌 반경 (월드 단위)</summary>
    private const float COLLIDER_RADIUS = 0.25f;

    #endregion

    #region 필드

    /// <summary>아이템 데이터</summary>
    public ItemData itemData;

    /// <summary>아이템 수량</summary>
    public int quantity = 1;

    /// <summary>현재 직원이 운반 예약 중인지 여부 (중복 할당 방지)</summary>
    public bool IsClaimed { get; private set; }

    private SpriteRenderer _spriteRenderer;
    private Rigidbody2D    _rigidbody;
    private CircleCollider2D _collider;

    #endregion

    #region 초기화

    void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        EnsurePhysicsComponents();
    }

    /// <summary>
    /// prefab에 Rigidbody2D / CircleCollider2D가 없으면 자동으로 부착하고,
    /// 중력 + 회전 잠금 + 비-트리거 충돌 설정을 적용합니다.
    /// </summary>
    private void EnsurePhysicsComponents()
    {
        // Rigidbody2D: 중력 적용
        _rigidbody = GetComponent<Rigidbody2D>();
        if (_rigidbody == null)
            _rigidbody = gameObject.AddComponent<Rigidbody2D>();

        _rigidbody.bodyType        = RigidbodyType2D.Dynamic;
        _rigidbody.gravityScale    = GRAVITY_SCALE;
        _rigidbody.freezeRotation  = true; // 회전 잠금
        _rigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        // CircleCollider2D: 바닥/벽과 충돌 (트리거 아님)
        _collider = GetComponent<CircleCollider2D>();
        if (_collider == null)
            _collider = gameObject.AddComponent<CircleCollider2D>();

        _collider.isTrigger = false;
        _collider.radius    = COLLIDER_RADIUS;
    }

    /// <summary>외부에서 접근할 수 있도록 collider를 노출 (DroppedItemManager의 충돌 무시 처리용).</summary>
    public Collider2D Collider => _collider;

void Start()
    {
        // 풀링 흐름에서는 SpawnItem이 명시적으로 Register/ApplyVisual을 호출하므로
        // 여기서는 자동 등록을 하지 않습니다. 씬에 직접 배치된 DroppedItem만
        // 한 번 등록하기 위한 폴백입니다.
        if (DroppedItemManager.instance != null && !DroppedItemManager.instance.IsRegistered(this))
        {
            ApplyVisual();
            DroppedItemManager.instance.Register(this);
        }
    }

    void OnDestroy()
    {
        DroppedItemManager.instance?.Unregister(this);
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 아이템을 초기화합니다 (SpawnItem에서 호출).
    /// </summary>
public void Initialize(ItemData data, int qty = 1)
    {
        itemData = data;
        quantity = qty;
        IsClaimed = false; // 풀 재사용 시 예약 상태 리셋

        if (_spriteRenderer == null)
            _spriteRenderer = GetComponent<SpriteRenderer>();

        ApplyVisual();

        // 바닥 더미도 제작·건설의 자재 공급원이다.
        // 데이터가 채워진 뒤에 등록해야 GetStoredAmount가 올바른 값을 낸다.
        MaterialSourceRegistry.instance?.Register(this);
    }

    /// <summary>직원이 이 아이템의 운반을 예약합니다.</summary>
    public void Claim()   => IsClaimed = true;

    /// <summary>운반 예약을 해제합니다 (취소 시).</summary>
    public void Unclaim() => IsClaimed = false;

    #region IMaterialSource
    // 바닥에 떨어진 더미도 제작·건설의 자재 공급원이 됩니다 —
    // 창고로 한 번 옮겨진 뒤에야 쓸 수 있던 제약을 없앱니다.
    //
    // 운반 예약(IsClaimed)된 더미는 이미 다른 직원이 창고로 가져가는 중이므로
    // 자재 공급원에서는 제외합니다 (같은 물건을 두 번 세지 않도록).

    public bool IsSourceAvailable => IsAvailable && itemData != null && quantity > 0;

    public Vector3 GetWithdrawPosition() => transform.position;

    public int GetStoredAmount(ItemData item)
        => item != null && item == itemData ? quantity : 0;

    /// <summary>요청량을 전부 댈 수 있을 때만 꺼냅니다 (반쪽 출고 금지).</summary>
    public bool Withdraw(ItemData item, int amount)
    {
        if (item == null || item != itemData || amount <= 0) return false;
        if (quantity < amount) return false;

        quantity -= amount;
        if (quantity <= 0) Remove();
        return true;
    }

    #endregion

    /// <summary>픽업 완료 후 오브젝트를 파괴합니다.</summary>
public void Remove()
    {
        var mgr = DroppedItemManager.instance;
        if (mgr != null)
            mgr.Despawn(this);
        else
            Destroy(gameObject);
    }

// ─── IPoolable ────────────────────────────────────────────────────────
    /// <summary>풀에서 꺼낼 때 호출. SpawnItem이 곧이어 Initialize로 데이터를 채웁니다.</summary>
    public void OnSpawn()
    {
        IsClaimed = false;
    }

    /// <summary>풀로 반환되기 직전 호출. 다음 재사용 시 잔존 데이터로 인한 오작동 방지.</summary>
    public void OnDespawn()
    {
        MaterialSourceRegistry.instance?.Unregister(this);

        IsClaimed = false;
        itemData  = null;
        quantity  = 0;
    }


    /// <summary>운반 가능한 상태인지 여부.</summary>
    public bool IsAvailable => !IsClaimed && this != null && gameObject.activeSelf;

    #endregion

    #region 비주얼

    private void ApplyVisual()
    {
        if (_spriteRenderer == null) return;

        // dropPrefab이 자체 sprite를 갖고 있으면 그대로 사용 (정상 흐름).
        // ItemData.itemIcon은 UI 표시용이므로 덮어쓰지 않습니다.
        //
        // 폴백: prefab 없이 코드로 생성된 경우(_spriteRenderer.sprite == null)
        // 또는 dropPrefab의 SpriteRenderer가 비어있는 경우에 한해 itemIcon으로 채웁니다.
        if (_spriteRenderer.sprite == null && itemData?.itemIcon != null)
            _spriteRenderer.sprite = itemData.itemIcon;

        // 색상은 prefab이 가진 값을 유지 (덮어쓰지 않음)
    }

    #endregion
}
