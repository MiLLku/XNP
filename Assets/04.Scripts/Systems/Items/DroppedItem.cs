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
/// </summary>
public class DroppedItem : MonoBehaviour, IPoolable
{
    #region 필드

    /// <summary>아이템 데이터</summary>
    public ItemData itemData;

    /// <summary>아이템 수량</summary>
    public int quantity = 1;

    /// <summary>현재 직원이 운반 예약 중인지 여부 (중복 할당 방지)</summary>
    public bool IsClaimed { get; private set; }

    private SpriteRenderer _spriteRenderer;

    #endregion

    #region 초기화

    void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }

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
    }

    /// <summary>직원이 이 아이템의 운반을 예약합니다.</summary>
    public void Claim()   => IsClaimed = true;

    /// <summary>운반 예약을 해제합니다 (취소 시).</summary>
    public void Unclaim() => IsClaimed = false;

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

        // 아이콘 스프라이트가 있으면 사용, 없으면 Unity 기본 Sprite(흰 원형)
        if (itemData?.itemIcon != null)
            _spriteRenderer.sprite = itemData.itemIcon;

        // 색상: 아이템 아이콘 색 or 기본 흰색
        _spriteRenderer.color = Color.white;
    }

    #endregion
}
