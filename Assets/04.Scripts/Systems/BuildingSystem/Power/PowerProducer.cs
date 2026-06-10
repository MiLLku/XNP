using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 발전기. 전력을 생산하여 전력망에 공급합니다.
///
/// 두 가지 종류를 데이터로 지원합니다:
/// • 무한 가동형 (requiresFuel=false): 항상 outputWatts 생산 (풍력·지열 등).
/// • 연료 소비형 (requiresFuel=true): 내부 연료 버퍼가 남아있을 때만 생산하며,
///   가동 중 매 틱 연료를 소비합니다 (화력·바이오 등).
///
/// 연료 자동 보급: 버퍼가 임계치 아래로 떨어지면 IMaterialReceiver로 출고 요청을 만들어
/// 직원이 창고에서 연료 아이템을 운반해 옵니다 (CraftingTable의 자재 운반과 같은 흐름).
///
/// 연료 잔량은 IBuildingExtraSerializable로 세이브에 보존됩니다.
/// 진행 중이던 보급 요청은 저장하지 않습니다 — 로드 후 다음 체크에서 자동 재요청됩니다.
/// </summary>
[RequireComponent(typeof(Building))]
public class PowerProducer : MonoBehaviour, IPowerNode, IBuildingExtraSerializable, IMaterialReceiver
{
    [Header("발전")]
    [Tooltip("정상 가동 시 생산 전력(W).")]
    [SerializeField] private int outputWatts = 1000;

    [Header("연료 (선택)")]
    [Tooltip("연료가 필요한 발전기인지 여부. false면 무한 가동.")]
    [SerializeField] private bool requiresFuel = false;
    [Tooltip("소비할 연료 아이템 (연료 보급 연동용).")]
    [SerializeField] private ItemData fuelItem;
    [Tooltip("가동 중 초당 연료 소비량.")]
    [SerializeField] private float fuelUnitsPerSecond = 0.1f;
    [Tooltip("현재 내부 연료 잔량.")]
    [SerializeField] private float fuelBuffer = 0f;
    [Tooltip("내부 연료 버퍼 최대치.")]
    [SerializeField] private float maxFuelBuffer = 100f;

    [Header("연료 자동 보급")]
    [Tooltip("연료 부족 시 직원이 창고에서 연료를 자동 운반하도록 요청할지 여부.")]
    [SerializeField] private bool autoRefuel = true;
    [Tooltip("연료 아이템 1개당 채워지는 연료량.")]
    [SerializeField] private float fuelUnitsPerItem = 25f;
    [Tooltip("연료 비율이 이 값 아래로 떨어지면 보급을 요청합니다 (0~1).")]
    [Range(0f, 1f)]
    [SerializeField] private float refuelThreshold = 0.5f;
    [Tooltip("연료 운반 작업 우선순위 (CraftingTable 자재 운반과 동일 기본값).")]
    [SerializeField] private int refuelPriority = 3;

    private const float REFUEL_CHECK_INTERVAL = 2f;
    private const float REFUEL_FAIL_RETRY_DELAY = 10f;

    private Building _building;

    private WorkOrder _refuelWorkOrder;
    private int _refuelReservationId = -1;
    private int _incomingFuelItems;
    private float _nextRefuelCheckTime;

    public bool IsOnline => _building == null || _building.IsFunctional;
    public bool RequiresFuel => requiresFuel;
    public ItemData FuelItem => fuelItem;
    public float FuelBuffer => fuelBuffer;
    public float MaxFuelBuffer => maxFuelBuffer;

    /// <summary>현재 실제 출력(W). 기반 파괴/연료 고갈 시 0.</summary>
    public int CurrentOutput
    {
        get
        {
            if (!IsOnline) return 0;
            if (requiresFuel && fuelBuffer <= 0f) return 0;
            return outputWatts;
        }
    }

    // ── IPowerNode ──────────────────────────────────────────────
    public IEnumerable<Vector2Int> OccupiedCells => PowerUtil.FootprintCells(transform, _building);
    public PowerNodeKind Kind => PowerNodeKind.Producer;

    void Awake()
    {
        _building = GetComponent<Building>();
    }

    void OnEnable()
    {
        var pm = PowerManager.instance;
        if (pm != null) pm.RegisterProducer(this);
    }

    void OnDisable()
    {
        var pm = PowerManager.instance;
        if (pm != null) pm.UnregisterProducer(this);

        // 보급 대기 중 파괴되면 예약·운반 작업 정리 (운반 중 자재는 코루틴이 환불 처리)
        ClearRefuelRequest(removeOrderAsCancellation: true);
    }

    void Update()
    {
        if (!requiresFuel || !autoRefuel) return;
        if (Time.time < _nextRefuelCheckTime) return;
        _nextRefuelCheckTime = Time.time + REFUEL_CHECK_INTERVAL;
        TryRequestRefuel();
    }

    /// <summary>가동 중인 발전기의 연료를 seconds초만큼 소비합니다 (PowerManager가 호출).</summary>
    public void ConsumeFuel(float seconds)
    {
        if (!requiresFuel || fuelBuffer <= 0f) return;
        fuelBuffer = Mathf.Max(0f, fuelBuffer - fuelUnitsPerSecond * seconds);
    }

    /// <summary>연료를 보급합니다.</summary>
    public void AddFuel(float units)
    {
        fuelBuffer = Mathf.Clamp(fuelBuffer + units, 0f, maxFuelBuffer);
    }

    // ── 연료 자동 보급 ──────────────────────────────────────────
    /// <summary>
    /// 연료가 임계치 아래면 창고 출고(Withdraw) 요청을 생성합니다.
    /// 흐름은 CraftingTable.TryStartCrafting의 자재 운반과 동일:
    /// TryReserve → MaterialRequest/WithdrawOrder 등록 → 직원 운반 → OnMaterialDelivered.
    /// </summary>
    private void TryRequestRefuel()
    {
        if (_refuelWorkOrder != null) return; // 이미 요청 진행 중
        if (fuelItem == null || maxFuelBuffer <= 0f) return;
        if (!IsOnline) return;
        if (fuelBuffer / maxFuelBuffer >= refuelThreshold) return;

        if (InventoryManager.instance == null || WorkSystemManager.instance == null) return;

        // 버퍼 빈 공간만큼 요청 (최소 1개), 창고 가용 수량으로 제한
        float space = maxFuelBuffer - fuelBuffer;
        int want = Mathf.FloorToInt(space / Mathf.Max(fuelUnitsPerItem, 0.01f));
        if (want < 1) return;

        int available = InventoryManager.instance.GetAvailableAmount(fuelItem);
        int amount = Mathf.Min(want, available);
        if (amount < 1) return; // 창고에 연료 없음 — 다음 체크 때 재시도

        var costs = new List<ResourceCost> { new ResourceCost { item = fuelItem, amount = amount } };
        int reservationId = InventoryManager.instance.TryReserve(costs);
        if (reservationId < 0) return;

        _refuelReservationId = reservationId;
        _incomingFuelItems = amount;

        string displayName = _building != null && _building.buildingData != null
            ? _building.buildingData.buildingName
            : name;
        _refuelWorkOrder = WorkSystemManager.instance.CreateWorkOrder(
            $"연료 보급: {displayName}",
            WorkType.Hauling,
            maxWorkers: 1,
            priority:   refuelPriority
        );
        _refuelWorkOrder.AddTarget(new WithdrawOrder(new MaterialRequest(fuelItem, amount, this, reservationId)));

        Debug.Log($"[PowerProducer] 연료 보급 요청: {displayName} ← {fuelItem.itemName}×{amount}");
    }

    /// <summary>
    /// 보급 요청 상태를 정리합니다.
    /// 예약 잠금 해제 — 자재 차감은 직원이 출고 시점에 이미 수행하므로
    /// 성공 경로에서도 ConsumeReservation이 아닌 CancelReservation을 사용합니다 (CraftingTable과 동일).
    /// </summary>
    private void ClearRefuelRequest(bool removeOrderAsCancellation)
    {
        if (_refuelReservationId >= 0)
        {
            InventoryManager.instance?.CancelReservation(_refuelReservationId);
            _refuelReservationId = -1;
        }
        if (_refuelWorkOrder != null)
        {
            WorkSystemManager.instance?.RemoveWorkOrder(_refuelWorkOrder, isCancellation: removeOrderAsCancellation);
            _refuelWorkOrder = null;
        }
        _incomingFuelItems = 0;
    }

    // ── IMaterialReceiver (연료 인계) ───────────────────────────
    /// <summary>발전기 footprint 주변의 서 있을 수 있는 칸을 인계 위치로 반환합니다.</summary>
    public Vector3 GetDeliveryPosition()
    {
        Vector3 basePos = transform.position;
        var gameMap = MapGenerator.instance != null ? MapGenerator.instance.GameMapInstance : null;
        if (gameMap == null) return basePos;

        Vector2Int size = _building != null && _building.buildingData != null
            ? _building.buildingData.size
            : Vector2Int.one;
        if (size.x < 1) size.x = 1;

        int bx = Mathf.FloorToInt(basePos.x);
        int by = Mathf.FloorToInt(basePos.y);

        // 좌/우 인접 칸 우선, 그 다음 footprint 바닥 행 (발전기는 보통 blocksMovement라 자연히 걸러짐)
        var candidates = new List<Vector2Int> { new Vector2Int(bx - 1, by), new Vector2Int(bx + size.x, by) };
        for (int dx = 0; dx < size.x; dx++) candidates.Add(new Vector2Int(bx + dx, by));

        foreach (var pos in candidates)
        {
            if (pos.x < 0 || pos.x >= GameMap.MAP_WIDTH) continue;
            if (pos.y < 0 || pos.y >= GameMap.MAP_HEIGHT) continue;

            bool walkable = gameMap.TileGrid[pos.x, pos.y] == 0 && !gameMap.DoesTileBlockMovement(pos.x, pos.y);
            if (!walkable) continue;

            int gy = pos.y - 1;
            bool hasGround = gy >= 0 && (gameMap.IsSolidGround(pos.x, gy) || FloorTile.HasFloorTileAt(new Vector2Int(pos.x, gy)));
            if (!hasGround) continue;

            return new Vector3(pos.x + 0.5f, pos.y, 0f);
        }
        return basePos;
    }

    public bool IsRequestStillValid()
    {
        if (this == null) return false;
        if (!isActiveAndEnabled) return false;
        if (!IsOnline) return false;
        return _refuelWorkOrder != null;
    }

    public void OnMaterialDelivered(ItemData itemData, int amount)
    {
        if (itemData == null || itemData != fuelItem || amount <= 0) return;

        AddFuel(amount * fuelUnitsPerItem);
        _incomingFuelItems -= amount;

        Debug.Log($"[PowerProducer] 연료 도착: {itemData.itemName}×{amount} → 버퍼 {fuelBuffer:F0}/{maxFuelBuffer:F0}");

        if (_incomingFuelItems <= 0)
        {
            ClearRefuelRequest(removeOrderAsCancellation: false);
        }
    }

    public void OnMaterialRequestFailed(ItemData itemData, int amount)
    {
        Debug.LogWarning($"[PowerProducer] 연료 운반 실패: {itemData?.itemName} × {amount} — {REFUEL_FAIL_RETRY_DELAY}초 후 재시도");
        ClearRefuelRequest(removeOrderAsCancellation: true);
        _nextRefuelCheckTime = Time.time + REFUEL_FAIL_RETRY_DELAY;
    }

    // ── IBuildingExtraSerializable (연료 잔량 저장) ────────────────
    [System.Serializable]
    private class ExtraData { public float fuelBuffer; }

    public string SerializeExtra()
    {
        if (!requiresFuel) return "";
        return JsonUtility.ToJson(new ExtraData { fuelBuffer = fuelBuffer });
    }

    public void DeserializeExtra(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var d = JsonUtility.FromJson<ExtraData>(json);
            if (d != null) fuelBuffer = Mathf.Clamp(d.fuelBuffer, 0f, maxFuelBuffer);
        }
        catch { }
    }
}
