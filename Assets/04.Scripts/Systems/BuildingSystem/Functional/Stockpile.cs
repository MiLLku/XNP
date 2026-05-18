using UnityEngine;

/// <summary>
/// 창고 건물 컴포넌트 — Building과 함께 사용합니다 (IBuildingFunction 구현).
///
/// 역할:
///   - 직원이 운반한 재료를 인벤토리에 저장합니다.
///   - StockpileManager에 등록하여 가장 가까운 창고 조회를 지원합니다.
///
/// 사용 방법:
///   1. Building 컴포넌트와 함께 프리팹에 붙입니다.
///   2. BuildingData의 blocksMovement = false (직원이 입장 가능)로 설정합니다.
///   3. depositOffset으로 직원이 배달하러 오는 정확한 위치를 조정합니다.
/// </summary>
[RequireComponent(typeof(Building))]
public class Stockpile : MonoBehaviour, IBuildingFunction
{
    #region 설정

    [Header("창고 설정")]
    [Tooltip("직원이 배달하러 올 위치 오프셋 (건물 좌측 하단 기준)")]
    [SerializeField] private Vector2 depositOffset = new Vector2(0.5f, 0f);

    [Header("디버그")]
    [SerializeField] private bool showDebugInfo = false;

    #endregion

    #region 내부 상태

    private Building _building;
    private bool     _registered = false;

    #endregion

    #region 프로퍼티

    /// <summary>창고가 정상 동작 중인지 여부.</summary>
    public bool IsOperational => _building != null && _building.IsFunctional;

    // IBuildingFunction
    public bool IsOperating => IsOperational;

    #endregion

    #region 초기화

    void Awake()
    {
        _building = GetComponent<Building>();
    }

    void Start()
    {
        StockpileManager.instance?.Register(this);
        _registered = true;
    }

    void OnDestroy()
    {
        if (_registered)
            StockpileManager.instance?.Unregister(this);
    }

    #endregion

    #region IBuildingFunction

    public void OnBuildingDisabled()
    {
        if (showDebugInfo)
            Debug.Log($"[Stockpile] {gameObject.name} 파손 — 배달 비활성");
    }

    public void OnBuildingEnabled()
    {
        if (showDebugInfo)
            Debug.Log($"[Stockpile] {gameObject.name} 복구 — 배달 활성");
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 직원이 아이템을 배달하는 월드 위치를 반환합니다.
    /// </summary>
    public Vector3 GetDepositPosition()
    {
        return transform.position + new Vector3(depositOffset.x, depositOffset.y, 0f);
    }

    /// <summary>
    /// 아이템을 창고에 저장합니다 (현재는 전역 인벤토리로 전달).
    /// </summary>
    /// <param name="data">저장할 아이템 데이터</param>
    /// <param name="qty">수량</param>
    public void Deposit(ItemData data, int qty)
    {
        if (data == null || qty <= 0) return;

        if (InventoryManager.instance != null)
        {
            InventoryManager.instance.AddItem(data, qty);

            if (showDebugInfo)
                Debug.Log($"[Stockpile] {gameObject.name} 입고: {data.itemName} × {qty}");
        }
        else
        {
            Debug.LogWarning($"[Stockpile] InventoryManager가 없어 아이템을 저장할 수 없습니다: {data.itemName}");
        }
    }

    #endregion
}
