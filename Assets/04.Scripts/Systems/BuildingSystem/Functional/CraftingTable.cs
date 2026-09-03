using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using System.Linq;
using Cysharp.Threading.Tasks;

[RequireComponent(typeof(Collider2D))]
public class CraftingTable : MonoBehaviour, IBuildingFunction, IMaterialReceiver, IBuildingExtraSerializable
{
    [Header("제작 설정")]
    [Tooltip("이 작업대에서 만들 수 있는 레시피 목록")]
    [SerializeField] private List<CraftingRecipe> availableRecipes;

    [Header("자재 운반 설정")]
    [Tooltip("자재 운반 작업물의 최대 동시 작업자 수")]
    [SerializeField] private int withdrawMaxWorkers = 3;
    [Tooltip("자재 운반 작업물 우선순위 (낮을수록 우선)")]
    [SerializeField] private int withdrawPriority = 3;
    [Tooltip("직원이 자재를 인계할 때 사용할 오프셋 (작업대 좌측 하단 기준)")]
    [SerializeField] private Vector2 deliveryOffset = new Vector2(0.5f, 0f);

    [Header("제작 진행 표시")]
    [Tooltip("제작 진행 바 (선택사항)")]
    [SerializeField] private GameObject progressBarPrefab;
    [SerializeField] private Vector3 progressBarOffset = new Vector3(0, 1.5f, 0);

    [Header("오디오 (선택사항)")]
    [SerializeField] private AudioClip craftingStartSound;
    [SerializeField] private AudioClip craftingCompleteSound;

    private bool _isCrafting = false;
    private CraftingRecipe _currentRecipe;
    private float _craftingProgress = 0f;
    private GameObject _progressBarInstance;
    /// <summary>진행 중인 제작 작업의 취소원 (null이면 제작 중 아님)</summary>
    private CancellationTokenSource _craftingCts;

    // ── 자재 운반(출고) 상태 ──────────────────────────────────────────────
    /// <summary>InventoryManager 예약 ID (자재 잠금, 다른 작업이 가져가지 못하게)</summary>
    private int _reservationId = -1;
    /// <summary>아직 도착하지 않은 자재 (자재 도착 시 차감 → 0이면 제작 시작)</summary>
    private readonly Dictionary<ItemData, int> _pendingMaterials = new Dictionary<ItemData, int>();
    /// <summary>이미 도착한 자재 (취소 시 환불 드롭으로 사용)</summary>
    private readonly Dictionary<ItemData, int> _deliveredMaterials = new Dictionary<ItemData, int>();
    /// <summary>자재 운반 작업물 (모든 자재 도착 후 정리)</summary>
    private WorkOrder _withdrawWorkOrder;
    /// <summary>현재 자재 운반 단계인지 여부</summary>
    private bool _isAwaitingMaterials = false;

    // IBuildingFunction 구현
    public bool IsOperating => _isCrafting || _isAwaitingMaterials;
    
    void Start()
    {
        ValidateRecipes();
    }
    
    void OnDestroy()
    {
        // 자재 운반 대기 단계라면 예약/작업 정리
        if (_isAwaitingMaterials)
        {
            CancelMaterialRequest("작업대 파괴");
        }

        // 진행 중인 제작 취소
        if (_craftingCts != null)
        {
            CancelCraftingTask();
            RefundMaterials();
        }

        if (_progressBarInstance != null)
        {
            Destroy(_progressBarInstance);
        }
    }
    
    private void ValidateRecipes()
    {
        // null 레시피 제거
        availableRecipes = availableRecipes?.Where(r => r != null).ToList() ?? new List<CraftingRecipe>();
        
        if (availableRecipes.Count == 0)
        {
            Debug.LogWarning($"[CraftingTable] {name}에 사용 가능한 레시피가 없습니다!");
        }
    }
    
    private void OnMouseDown()
    {
        // Building이 비활성화 상태인지 확인
        Building building = GetComponent<Building>();
        if (building != null && !building.IsFunctional)
        {
            Debug.LogWarning("[CraftingTable] 건물이 비활성화 상태입니다.");
            ShowDisabledMessage();
            return;
        }
        
        if (_isCrafting)
        {
            ShowCraftingProgress();
            return;
        }

        // UI가 있다면 UI 열기, 없으면 첫 번째 레시피 제작
        if (CraftingUIManager.instance != null)
        {
            CraftingUIManager.instance.OpenCraftingUI(this);
        }
        else if (availableRecipes.Count > 0)
        {
            // 테스트용: 첫 번째 레시피 자동 제작
            var firstAvailable = availableRecipes.FirstOrDefault(r => r.IsAvailable());
            if (firstAvailable != null) TryStartCrafting(firstAvailable);
        }
    }
    
    /// <summary>
    /// 제작을 시작합니다.
    /// 출고 흐름:
    ///   1. 사용 가능한 자재(예약 미반영분)가 충분한지 확인
    ///   2. InventoryManager.TryReserve로 자재 잠금
    ///   3. 각 자재마다 WithdrawOrder 생성 → 직원이 창고에서 운반
    ///   4. 모든 자재가 도착하면(OnMaterialDelivered) CancelReservation + 실제 제작 시작
    /// </summary>
    public void TryStartCrafting(CraftingRecipe recipe)
    {
        // 연구로 잠긴 레시피 방어 (UI 필터를 우회한 호출 대비)
        if (recipe != null && !recipe.IsAvailable())
        {
            Debug.LogWarning($"[CraftingTable] 연구 미해금 레시피: {recipe.outputItem?.itemName}");
            return;
        }

        if (_isCrafting || _isAwaitingMaterials)
        {
            Debug.LogWarning("[CraftingTable] 이미 제작 또는 자재 대기 중입니다.");
            return;
        }

        if (recipe == null)
        {
            Debug.LogError("[CraftingTable] 레시피가 null입니다.");
            return;
        }

        if (InventoryManager.instance == null)
        {
            Debug.LogError("[CraftingTable] InventoryManager가 없습니다.");
            return;
        }

        // 사용 가능 자재 체크 (예약된 자재 제외)
        if (!InventoryManager.instance.HasAvailableItems(recipe.requiredMaterials))
        {
            Debug.LogWarning($"[CraftingTable] 자재 부족(예약 고려): '{recipe.outputItem.itemName}'");
            ShowInsufficientMaterialsMessage(recipe);
            return;
        }

        // 자재 예약 (다른 작업이 가져가지 못하도록 잠금)
        int reservationId = InventoryManager.instance.TryReserve(recipe.requiredMaterials);
        if (reservationId < 0)
        {
            Debug.LogWarning("[CraftingTable] 자재 예약 실패");
            return;
        }

        _currentRecipe = recipe;
        _reservationId = reservationId;
        _isAwaitingMaterials = true;

        // 도착 대기 자재 카운트 + 누적 도착 자재 초기화
        _pendingMaterials.Clear();
        _deliveredMaterials.Clear();
        foreach (var cost in recipe.requiredMaterials)
        {
            if (cost.item == null || cost.amount <= 0) continue;
            _pendingMaterials[cost.item] = cost.amount;
        }

        // 즉시 자재 없는 레시피라면 바로 제작 시작
        if (_pendingMaterials.Count == 0)
        {
            StartActualCrafting();
            return;
        }

        // WithdrawOrder들을 묶을 WorkOrder 생성
        if (WorkSystemManager.instance == null)
        {
            Debug.LogError("[CraftingTable] WorkSystemManager가 없어 자재 운반을 요청할 수 없습니다.");
            InventoryManager.instance.CancelReservation(_reservationId);
            _reservationId = -1;
            _currentRecipe = null;
            _isAwaitingMaterials = false;
            _pendingMaterials.Clear();
            return;
        }

        _withdrawWorkOrder = WorkSystemManager.instance.CreateWorkOrder(
            $"제작 자재 운반: {recipe.outputItem.itemName}",
            WorkType.Hauling,
            maxWorkers: withdrawMaxWorkers,
            priority:   withdrawPriority
        );

        foreach (var cost in recipe.requiredMaterials)
        {
            if (cost.item == null || cost.amount <= 0) continue;
            var request = new MaterialRequest(cost.item, cost.amount, this, _reservationId);
            var task    = new WithdrawOrder(request);
            _withdrawWorkOrder.AddTarget(task);
        }

        Debug.Log($"[CraftingTable] 자재 운반 요청 생성: {recipe.outputItem.itemName} ({_pendingMaterials.Count}종)");
    }

    /// <summary>모든 자재가 도착한 후 호출되어 실제 제작을 시작합니다.</summary>
    private void StartActualCrafting()
    {
        if (_currentRecipe == null) return;

        // 자재는 이미 직원이 Withdraw로 InventoryManager에서 차감했음.
        // ConsumeReservation은 RemoveItem을 또 호출하므로 사용하면 안 됨.
        // 예약 잠금만 해제 (자재 차감은 이미 됨).
        if (_reservationId >= 0 && InventoryManager.instance != null)
        {
            InventoryManager.instance.CancelReservation(_reservationId);
            _reservationId = -1;
        }

        // 자재 운반 작업물은 이미 모든 태스크가 완료되어 있을 것이지만, 안전하게 제거
        if (_withdrawWorkOrder != null && WorkSystemManager.instance != null)
        {
            WorkSystemManager.instance.RemoveWorkOrder(_withdrawWorkOrder, isCancellation: false);
            _withdrawWorkOrder = null;
        }

        _isAwaitingMaterials = false;
        _deliveredMaterials.Clear(); // 정상 제작 진행 → 도착 자재는 "소비"된 것으로 간주, 환불 불필요

        // 실제 제작 시작
        Debug.Log($"[CraftingTable] '{_currentRecipe.outputItem.itemName}' 제작 시작... ({_currentRecipe.craftingTime}초)");
        CraftAsync(_currentRecipe, 0f, RestartCraftingTask()).Forget();

        PlaySound(craftingStartSound);
    }

    /// <summary>자재 요청을 취소하고 이미 도착한 자재는 작업대 위치에 DroppedItem으로 환불합니다.</summary>
    private void CancelMaterialRequest(string reason)
    {
        if (_reservationId >= 0 && InventoryManager.instance != null)
        {
            InventoryManager.instance.CancelReservation(_reservationId);
            _reservationId = -1;
        }
        if (_withdrawWorkOrder != null && WorkSystemManager.instance != null)
        {
            WorkSystemManager.instance.RemoveWorkOrder(_withdrawWorkOrder, isCancellation: true);
            _withdrawWorkOrder = null;
        }

        // 도착한 자재 환불 (DroppedItem 떨어뜨림 → 직원이 가까운 창고로 다시 운반)
        if (_deliveredMaterials.Count > 0)
        {
            ItemRefundHelper.SpawnRefunds(_deliveredMaterials, transform.position, Vector2Int.one);
            _deliveredMaterials.Clear();
        }

        _pendingMaterials.Clear();
        _isAwaitingMaterials = false;
        _currentRecipe = null;

        Debug.Log($"[CraftingTable] 자재 요청 취소: {reason}");
    }

    // ─── IMaterialReceiver ──────────────────────────────────────────────────

    public Vector3 GetDeliveryPosition()
    {
        // 기본: 작업대 좌측 하단 + deliveryOffset.
        // 작업대 footprint 자체가 직원의 발판이 안 되면 인접 발판 우선 (ConstructionSite와 동일 정책).
        Vector3 basePos = transform.position + new Vector3(deliveryOffset.x, deliveryOffset.y, 0f);
        var gameMap = MapGenerator.instance?.GameMapInstance;
        if (gameMap == null) return basePos;

        int bx = Mathf.FloorToInt(basePos.x);
        int by = Mathf.FloorToInt(basePos.y);

        var candidates = new System.Collections.Generic.List<Vector2Int>
        {
            new Vector2Int(bx,     by),
            new Vector2Int(bx - 1, by),
            new Vector2Int(bx + 1, by),
            new Vector2Int(bx,     by - 1),
        };

        foreach (var pos in candidates)
        {
            if (pos.x < 0 || pos.x >= GameMap.MAP_WIDTH) continue;
            if (pos.y < 0 || pos.y >= GameMap.MAP_HEIGHT) continue;

            int tileId = gameMap.TileGrid[pos.x, pos.y];
            bool walkable = tileId == 0 && !gameMap.DoesTileBlockMovement(pos.x, pos.y);
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
        // 작업대 자체 + 제작 단계 검증
        if (this == null) return false;
        Building b = GetComponent<Building>();
        if (b != null && !b.IsFunctional) return false;
        return _isAwaitingMaterials && _currentRecipe != null;
    }

    public void OnMaterialDelivered(ItemData itemData, int amount)
    {
        if (itemData == null || amount <= 0) return;

        // 도착 자재 누적 기록 (취소 시 환불용)
        if (_deliveredMaterials.TryGetValue(itemData, out int already))
            _deliveredMaterials[itemData] = already + amount;
        else
            _deliveredMaterials[itemData] = amount;

        if (_pendingMaterials.TryGetValue(itemData, out int remaining))
        {
            remaining -= amount;
            if (remaining <= 0) _pendingMaterials.Remove(itemData);
            else                _pendingMaterials[itemData] = remaining;

            Debug.Log($"[CraftingTable] 자재 도착: {itemData.itemName}×{amount} (남은 종류: {_pendingMaterials.Count})");
        }

        if (_pendingMaterials.Count == 0)
        {
            StartActualCrafting();
        }
    }

    public void OnMaterialRequestFailed(ItemData itemData, int amount)
    {
        // 자재 운반 실패는 일시적일 수 있으니(직원 낙하·경로 차단·창고 일시 부족 등) 제작을 취소하지 않습니다.
        // 예약·WithdrawOrder·대기 상태를 유지하면 해당 태스크가 큐로 복귀해 다른 직원이 재시도합니다
        // (ConstructionSite와 동일 정책). 명시적 취소는 CancelMaterialRequest가 처리합니다.
        Debug.LogWarning($"[CraftingTable] 자재 운반 실패: {itemData?.itemName}×{amount} — 재시도 대기");
    }

    // ─── IBuildingExtraSerializable (저장/복원) ────────────────────────────

    [System.Serializable]
    private class CraftingTableExtraData
    {
        public int recipeId = -1;
        public int reservationId = -1;
        public bool isAwaitingMaterials;
        public bool isCrafting;
        public float craftingProgress;
        public List<ItemStackSaveData> pendingMaterials = new List<ItemStackSaveData>();
        public List<ItemStackSaveData> deliveredMaterials = new List<ItemStackSaveData>();
    }

    public string SerializeExtra()
    {
        // 활성 상태(자재 대기 OR 제작 중)일 때만 저장
        if (!_isAwaitingMaterials && !_isCrafting) return "";

        var data = new CraftingTableExtraData
        {
            recipeId            = _currentRecipe != null ? _currentRecipe.recipeID : -1,
            reservationId       = _reservationId,
            isAwaitingMaterials = _isAwaitingMaterials,
            isCrafting          = _isCrafting,
            craftingProgress    = _craftingProgress
        };

        foreach (var kv in _pendingMaterials)
        {
            if (kv.Key == null || kv.Value <= 0) continue;
            data.pendingMaterials.Add(new ItemStackSaveData { itemId = kv.Key.itemID, amount = kv.Value });
        }
        foreach (var kv in _deliveredMaterials)
        {
            if (kv.Key == null || kv.Value <= 0) continue;
            data.deliveredMaterials.Add(new ItemStackSaveData { itemId = kv.Key.itemID, amount = kv.Value });
        }

        return JsonUtility.ToJson(data);
    }

    public void DeserializeExtra(string json)
    {
        if (string.IsNullOrEmpty(json)) return;

        CraftingTableExtraData data;
        try
        {
            data = JsonUtility.FromJson<CraftingTableExtraData>(json);
        }
        catch
        {
            return;
        }
        if (data == null) return;

        var db = GameDatabase.Instance;
        if (db == null) return;

        // 레시피 복원
        var recipe = db.GetRecipeData(data.recipeId);
        if (recipe == null) return;
        _currentRecipe = recipe;
        _reservationId = data.reservationId;

        _pendingMaterials.Clear();
        _deliveredMaterials.Clear();
        foreach (var s in data.pendingMaterials)
        {
            var item = db.GetItemData(s.itemId);
            if (item != null && s.amount > 0) _pendingMaterials[item] = s.amount;
        }
        foreach (var s in data.deliveredMaterials)
        {
            var item = db.GetItemData(s.itemId);
            if (item != null && s.amount > 0) _deliveredMaterials[item] = s.amount;
        }

        if (data.isCrafting)
        {
            // 제작 진행 중이었음 → 진행도 이어서 재시작
            _isCrafting = true;
            _craftingProgress = data.craftingProgress;
            _isAwaitingMaterials = false;
            CraftAsync(recipe, data.craftingProgress, RestartCraftingTask()).Forget();
        }
        else if (data.isAwaitingMaterials)
        {
            _isAwaitingMaterials = true;

            if (_pendingMaterials.Count == 0)
            {
                // 모든 자재가 도착해 있던 상태 → 바로 제작 시작
                StartActualCrafting();
            }
            else
            {
                // 미도착 자재 있음 → WithdrawOrder 재생성
                RecreateWithdrawOrdersFromPending();
            }
        }
    }

    /// <summary>현재 _pendingMaterials 기준으로 WithdrawOrder들을 다시 만들어 운반 재개합니다.</summary>
    private void RecreateWithdrawOrdersFromPending()
    {
        if (_pendingMaterials.Count == 0) return;
        if (WorkSystemManager.instance == null || _currentRecipe == null) return;

        _withdrawWorkOrder = WorkSystemManager.instance.CreateWorkOrder(
            $"제작 자재 운반(재개): {_currentRecipe.outputItem.itemName}",
            WorkType.Hauling,
            maxWorkers: withdrawMaxWorkers,
            priority:   withdrawPriority
        );

        foreach (var kv in _pendingMaterials)
        {
            if (kv.Key == null || kv.Value <= 0) continue;
            var request = new MaterialRequest(kv.Key, kv.Value, this, _reservationId);
            _withdrawWorkOrder.AddTarget(new WithdrawOrder(request));
        }

        Debug.Log($"[CraftingTable] 자재 운반 재개 ({_pendingMaterials.Count}종)");
    }

    /// <summary>
    /// 진행 중인 제작을 끊고 새 취소 토큰을 발급합니다.
    /// 작업대가 파괴되면 함께 취소되도록 파괴 토큰에 묶습니다.
    /// </summary>
    private CancellationToken RestartCraftingTask()
    {
        CancelCraftingTask();
        _craftingCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        return _craftingCts.Token;
    }

    /// <summary>진행 중인 제작 작업을 취소합니다.</summary>
    private void CancelCraftingTask()
    {
        if (_craftingCts == null) return;

        _craftingCts.Cancel();
        _craftingCts.Dispose();
        _craftingCts = null;
    }

    /// <summary>
    /// 진행도 <paramref name="startProgress"/>(0~1)부터 제작을 진행합니다.
    /// 세이브 복원 시 중간 진행도부터 이어가는 경로도 이 메서드를 씁니다.
    /// 정전 중에는 진행이 멈추고, 전력이 복구되면 이어서 진행합니다.
    /// </summary>
    private async UniTaskVoid CraftAsync(CraftingRecipe recipe, float startProgress, CancellationToken ct)
    {
        _isCrafting = true;
        _craftingProgress = Mathf.Clamp01(startProgress);

        // 진행 바 생성
        if (progressBarPrefab != null && _progressBarInstance == null)
        {
            _progressBarInstance = Instantiate(progressBarPrefab, transform.position + progressBarOffset, Quaternion.identity, transform);
        }

        float elapsedTime = recipe.craftingTime * _craftingProgress;
        var powerConsumer = GetComponent<PowerConsumer>();
        while (elapsedTime < recipe.craftingTime)
        {
            // 정전 시 진행 정지 (전력 복구 시 이어서 진행)
            if (powerConsumer != null && !powerConsumer.IsPowered)
            {
                await UniTask.Yield(GameLoop.Frame, ct);
                continue;
            }

            elapsedTime += Time.deltaTime;
            _craftingProgress = elapsedTime / recipe.craftingTime;

            // 진행 바 업데이트
            UpdateProgressBar(_craftingProgress);

            await UniTask.Yield(GameLoop.Frame, ct);
        }

        // 제작 완료
        CompleteCrafting(recipe);
    }
    
    private void CompleteCrafting(CraftingRecipe recipe)
    {
        // 아이템 추가
        if (InventoryManager.instance != null)
        {
            // 산출물이 장비면 인벤토리 대신 장비 보관소 풀에 인스턴스로 입고
            if (!EquipmentStorageManager.TryAddCraftOutput(recipe.outputItem, recipe.outputAmount))
                InventoryManager.instance.AddItem(recipe.outputItem, recipe.outputAmount);
            Debug.Log($"[CraftingTable] '{recipe.outputItem.itemName}' x{recipe.outputAmount} 제작 완료!");
        }
        
        // 사운드 재생
        PlaySound(craftingCompleteSound);
        
        // 상태 리셋
        ResetCraftingState();
    }
    
    public void CancelCrafting()
    {
        // 자재 운반 대기 단계 취소
        if (_isAwaitingMaterials)
        {
            CancelMaterialRequest("사용자 취소");
            return;
        }

        if (!_isCrafting || _craftingCts == null) return;

        Debug.Log("[CraftingTable] 제작 취소됨");

        CancelCraftingTask();
        RefundMaterials();
        ResetCraftingState();
    }
    
    private void RefundMaterials()
    {
        if (_currentRecipe == null) return;
        
        // 재료 환불
        foreach (var cost in _currentRecipe.requiredMaterials)
        {
            // 진행도에 따른 부분 환불 (선택사항)
            int refundAmount = Mathf.CeilToInt(cost.amount * (1f - _craftingProgress * 0.5f));
            InventoryManager.instance.AddItem(cost.item, refundAmount);
        }
    }
    
    private void ResetCraftingState()
    {
        _isCrafting = false;
        _currentRecipe = null;
        _craftingProgress = 0f;
        CancelCraftingTask();
        
        // 진행 바 제거
        if (_progressBarInstance != null)
        {
            Destroy(_progressBarInstance);
            _progressBarInstance = null;
        }
    }
    
    private void UpdateProgressBar(float progress)
    {
        if (_progressBarInstance == null) return;
        
        // 진행 바 업데이트 로직 (스케일 또는 fill 변경)
        Transform fill = _progressBarInstance.transform.Find("Fill");
        if (fill != null)
        {
            fill.localScale = new Vector3(progress, 1, 1);
        }
    }
    
    // IBuildingFunction 인터페이스 구현
    public void OnBuildingDisabled()
    {
        // 자재 운반 대기 단계도 취소
        if (_isAwaitingMaterials)
        {
            CancelMaterialRequest("건물 비활성화");
        }

        // 제작 중이었다면 취소하고 재료 환불
        if (_isCrafting)
        {
            CancelCrafting();
            Debug.Log("[CraftingTable] 건물이 비활성화되어 제작이 취소되었습니다.");
        }
    }
    
    public void OnBuildingEnabled()
    {
        Debug.Log("[CraftingTable] 건물이 다시 활성화되었습니다.");
    }
    
    // UI 관련 헬퍼 메서드들
    private void ShowDisabledMessage()
    {
        // UI 메시지 표시 (나중에 구현)
        Debug.Log("이 건물은 현재 사용할 수 없습니다. 기반을 복구하세요.");
    }
    
    private void ShowCraftingProgress()
    {
        Debug.Log($"제작 진행 중: {(_craftingProgress * 100):F0}% 완료");
    }
    
    private void ShowInsufficientMaterialsMessage(CraftingRecipe recipe)
    {
        string missing = "";
        foreach (var cost in recipe.requiredMaterials)
        {
            int current = InventoryManager.instance.GetItemCount(cost.item);
            if (current < cost.amount)
            {
                missing += $"\n- {cost.item.itemName}: {current}/{cost.amount}";
            }
        }
        Debug.Log($"재료 부족:{missing}");
    }
    
    private void PlaySound(AudioClip clip)
    {
        if (clip != null && AudioManager.instance != null)
        {
            // AudioManager.instance.PlaySFX(clip);
        }
    }
    
    // Public 프로퍼티
    public List<CraftingRecipe> AvailableRecipes => availableRecipes;
    public bool IsCrafting => _isCrafting;
    public float CraftingProgress => _craftingProgress;
    public CraftingRecipe CurrentRecipe => _currentRecipe;
}

// 임시 UI 매니저 인터페이스 (나중에 실제 구현 필요)
public class CraftingUIManager : MonoBehaviour
{
    public static CraftingUIManager instance;
    
    public void OpenCraftingUI(CraftingTable table)
    {
        // UI 열기 로직
    }
}

// 임시 오디오 매니저 인터페이스 (나중에 실제 구현 필요)
public class AudioManager : MonoBehaviour
{
    public static AudioManager instance;
}