using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// 건물 기본 클래스.
/// 건설 완료 후 생성되는 실제 건물 오브젝트로, 체력/기능 관리/타일 점유를 담당합니다.
///
/// 기능 스크립트 연동:
///   - IBuildingFunction 인터페이스를 구현한 컴포넌트를 자동 감지
///   - Inspector에서 수동 지정한 스크립트와 병합하여 관리
///   - 기반 파괴 시 모든 기능 스크립트를 비활성화
///
/// 상태 흐름:
///   Initialize → Functional (정상)
///                  ↓ OnFoundationDestroyed()
///               Disabled (비활성) → RepairFoundation() → Functional
///                  ↓ TakeDamage() (체력 0)
///               DestroyBuilding() → 오브젝트 제거
/// </summary>
[RequireComponent(typeof(SpriteRenderer), typeof(Collider2D))]
public class Building : MonoBehaviour
{
    #region 상수

    /// <summary>비활성 상태의 스프라이트 투명도</summary>
    private const float DISABLED_ALPHA = 0.5f;

    /// <summary>활성 상태의 스프라이트 투명도</summary>
    private const float ACTIVE_ALPHA = 1.0f;

    /// <summary>파괴 시 자원 반환 비율 (50%)</summary>
    private const int RESOURCE_RETURN_DIVISOR = 2;

    #endregion

    #region 필드 및 설정

    [Header("건물 데이터")]
    [Tooltip("이 건물의 원본 데이터 (프리팹 인스펙터에서 수동 연결)")]
    public BuildingData buildingData;

    [Header("기능 스크립트 (선택사항)")]
    [Tooltip("수동으로 지정할 기능 스크립트 (자동 감지와 병행 사용)")]
    [SerializeField] private List<MonoBehaviour> manualFunctionalScripts;

    [Header("디버그")]
    [SerializeField] private bool showDebugInfo = false;

    /// <summary>스프라이트 렌더러</summary>
    private SpriteRenderer _spriteRenderer;

    /// <summary>콜라이더 (클릭 상호작용용)</summary>
    private Collider2D _collider;

    /// <summary>현재 체력</summary>
    private int _currentHealth;

    /// <summary>건물 기능 활성화 여부</summary>
    private bool _isFunctional = true;

    /// <summary>타일 점유 해제 완료 여부 (이중 해제 방지)</summary>
    private bool _tilesReleased = false;

    /// <summary>자동으로 감지된 IBuildingFunction 컴포넌트 목록</summary>
    private List<IBuildingFunction> _buildingFunctions;

    /// <summary>모든 기능 스크립트 목록 (자동 감지 + 수동 지정)</summary>
    private List<MonoBehaviour> _allFunctionalScripts;

    /// <summary>런타임 고유 ID (세이브/로드 및 크로스레퍼런스용)</summary>
    private int _instanceId = -1;

    /// <summary>초기화 완료 여부 (Awake + SpawnBuilding 이중 초기화 방지)</summary>
    private bool _isInitialized = false;

    #endregion

    #region 프로퍼티

    /// <summary>건물 기능 활성화 여부</summary>
    public bool IsFunctional => _isFunctional;

    /// <summary>현재 체력</summary>
    public int CurrentHealth => _currentHealth;

    /// <summary>체력 비율 (0~1)</summary>
    public float HealthPercentage => (buildingData != null && buildingData.maxHealth > 0)
        ? (float)_currentHealth / buildingData.maxHealth
        : 0f;

    /// <summary>런타임 고유 ID</summary>
    public int InstanceId => _instanceId;

    #endregion

    #region 초기화

    void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _collider = GetComponent<Collider2D>();

        // IBuildingFunction을 구현한 모든 컴포넌트 자동 감지
        _buildingFunctions = GetComponents<IBuildingFunction>().ToList();

        // 모든 기능 스크립트 통합 (자동 감지 + 수동 지정)
        _allFunctionalScripts = new List<MonoBehaviour>();

        foreach (var func in _buildingFunctions)
        {
            if (func is MonoBehaviour script)
            {
                _allFunctionalScripts.Add(script);
            }
        }

        // 수동으로 지정한 스크립트 추가 (중복 제거)
        if (manualFunctionalScripts != null)
        {
            foreach (var script in manualFunctionalScripts)
            {
                if (script != null && !_allFunctionalScripts.Contains(script))
                {
                    _allFunctionalScripts.Add(script);
                }
            }
        }

        if (buildingData != null)
        {
            Initialize(buildingData);
        }
        else
        {
            // 프리팹에 buildingData가 미연결된 경우 SpawnBuilding()에서 Initialize()를 직접 호출합니다.
            // (정상 흐름 — 에러가 아닙니다)
        }

        if (showDebugInfo)
        {
            Debug.Log($"[Building] {name}: 자동 감지된 기능 {_buildingFunctions.Count}개, " +
                     $"수동 지정된 기능 {manualFunctionalScripts?.Count ?? 0}개");
        }
    }

    /// <summary>
    /// 건물을 초기화합니다.
    /// BuildingData를 설정하고 체력을 최대로 채운 뒤 타일을 점유합니다.
    ///
    /// 호출 경로:
    ///   A) 프리팹에 buildingData가 연결된 경우 → Awake()에서 자동 호출
    ///   B) 프리팹에 buildingData가 없는 경우  → SpawnBuilding()에서 직접 호출
    ///   두 경로 모두 안전하게 처리됩니다 (_isInitialized 가드).
    /// </summary>
    /// <param name="data">건물 데이터</param>
    public void Initialize(BuildingData data)
    {
        if (_isInitialized) return; // 이중 초기화 방지 (Awake + SpawnBuilding 중복 호출 시)
        _isInitialized = true;

        buildingData = data;
        _currentHealth = data.maxHealth;
        _isFunctional = true;
        this.name = $"{data.buildingName} ({this.gameObject.GetInstanceID()})";

        // ── 콜라이더 트리거 설정 ───────────────────────────────────────────────
        // 통행 가능 건물(바닥 타일, 사다리, 다리 등)은 트리거 콜라이더를 사용합니다.
        // 이유: Solid 콜라이더이면 직원이 이동 중(Kinematic) 타일에 닿을 때
        //       OnCollisionEnter2D가 발생 → StopMoving() 호출 → 이동 중단 버그.
        //       트리거로 설정하면 물리 충돌이 없어 이동이 끊기지 않습니다.
        //       (마우스 클릭 이벤트는 트리거 콜라이더에서도 정상 동작합니다.)
        if (_collider != null)
            _collider.isTrigger = !data.blocksMovement;

        RegisterToGameMap();
    }

    void Start()
    {
        // 새로 생성된 건물에 instanceId 부여 (복원된 건물은 이미 설정됨)
        if (_instanceId < 0 && SaveManager.instance != null)
        {
            _instanceId = SaveManager.instance.GenerateInstanceId();
        }
        if (_instanceId >= 0 && RuntimeIDRegistry.instance != null)
        {
            RuntimeIDRegistry.instance.Register(_instanceId, this);
        }
    }

    #endregion

    #region 저장/복원

    /// <summary>
    /// 현재 상태를 저장 데이터로 변환합니다.
    /// </summary>
    public BuildingSaveData CreateSaveData()
    {
        return new BuildingSaveData
        {
            instanceId = _instanceId,
            dataId = buildingData.buildingID,
            gridX = Mathf.FloorToInt(transform.position.x),
            gridY = Mathf.FloorToInt(transform.position.y),
            currentHealth = _currentHealth,
            isFunctional = _isFunctional,
            extraData = ""
        };
    }

    /// <summary>
    /// 저장된 데이터로 상태를 복원합니다.
    /// Instantiate + Awake(Initialize) 이후에 호출됩니다.
    /// </summary>
    public void RestoreState(BuildingSaveData saveData)
    {
        _instanceId = saveData.instanceId;
        _currentHealth = saveData.currentHealth;
        _isFunctional = saveData.isFunctional;

        if (!_isFunctional)
        {
            SetVisualState(false);
            _collider.enabled = false;
            DisableAllFunctions();
        }

        if (_instanceId >= 0 && RuntimeIDRegistry.instance != null)
        {
            RuntimeIDRegistry.instance.Register(_instanceId, this);
        }
    }

    #endregion

    #region 기반 파괴/복구

    /// <summary>
    /// 건물 기반이 파괴되었을 때 호출됩니다.
    /// 시각적 피드백, 콜라이더 비활성화, 기능 스크립트 비활성화를 수행합니다.
    /// </summary>
    public void OnFoundationDestroyed()
    {
        if (!_isFunctional) return;

        _isFunctional = false;
        Debug.Log($"[Building] {buildingData.buildingName}의 기반이 파괴되어 사용 불가능 상태가 됩니다.");

        SetVisualState(false);
        _collider.enabled = false;
        DisableAllFunctions();
    }

    /// <summary>
    /// 건물 기반을 복구합니다.
    /// 시각적 피드백 복구, 콜라이더 활성화, 기능 스크립트 활성화를 수행합니다.
    /// </summary>
    public void RepairFoundation()
    {
        if (_isFunctional) return;

        _isFunctional = true;
        Debug.Log($"[Building] {buildingData.buildingName}의 기반이 복구되어 다시 사용 가능합니다.");

        SetVisualState(true);
        _collider.enabled = true;
        EnableAllFunctions();
    }

    #endregion

    #region 피해/파괴

    /// <summary>
    /// 건물에 피해를 입힙니다.
    /// 체력이 0 이하가 되면 건물이 파괴됩니다.
    /// </summary>
    /// <param name="amount">피해량</param>
    public void TakeDamage(int amount)
    {
        if (!_isFunctional || amount <= 0) return;

        _currentHealth -= amount;
        Debug.Log($"[Building] {buildingData.buildingName}이(가) {amount}의 피해를 받았습니다. (남은 체력: {_currentHealth}/{buildingData.maxHealth})");

        if (_currentHealth <= 0)
        {
            DestroyBuilding();
        }
    }

    /// <summary>
    /// 건물을 파괴합니다.
    /// 타일 점유 해제, 일부 자원 반환 후 오브젝트를 제거합니다.
    /// </summary>
    private void DestroyBuilding()
    {
        Debug.Log($"[Building] {buildingData.buildingName}이(가) 파괴되었습니다!");

        ReleaseOccupiedTiles();

        // TODO [이펙트]: 파괴 이펙트 추가
        // Instantiate(destructionEffect, transform.position, Quaternion.identity);

        ReturnPartialResources();

        Destroy(gameObject);
    }

    /// <summary>
    /// 건설 비용의 일부를 반환합니다 (50%).
    /// </summary>
    private void ReturnPartialResources()
    {
        if (InventoryManager.instance != null && buildingData != null)
        {
            foreach (var cost in buildingData.requiredResources)
            {
                int returnAmount = Mathf.Max(1, cost.amount / RESOURCE_RETURN_DIVISOR);
                InventoryManager.instance.AddItem(cost.item, returnAmount);
            }
        }
    }

    #endregion

    #region 시각 효과

    /// <summary>
    /// 건물의 시각적 활성/비활성 상태를 설정합니다.
    /// </summary>
    /// <param name="isActive">활성 여부 (true: 불투명, false: 반투명)</param>
    private void SetVisualState(bool isActive)
    {
        Color color = _spriteRenderer.color;
        color.a = isActive ? ACTIVE_ALPHA : DISABLED_ALPHA;
        _spriteRenderer.color = color;
    }

    #endregion

    #region 기능 스크립트 관리

    /// <summary>
    /// 모든 기능 스크립트를 비활성화합니다.
    /// IBuildingFunction.OnBuildingDisabled()를 호출한 뒤 스크립트를 비활성화합니다.
    /// </summary>
    private void DisableAllFunctions()
    {
        foreach (var function in _buildingFunctions)
        {
            function.OnBuildingDisabled();
        }

        Debug.Log($"[Building] {_allFunctionalScripts.Count}개의 기능 스크립트를 비활성화합니다.");
        foreach (var script in _allFunctionalScripts)
        {
            if (script != null)
            {
                script.enabled = false;
            }
        }
    }

    /// <summary>
    /// 모든 기능 스크립트를 활성화합니다.
    /// 스크립트를 활성화한 뒤 IBuildingFunction.OnBuildingEnabled()를 호출합니다.
    /// </summary>
    private void EnableAllFunctions()
    {
        Debug.Log($"[Building] {_allFunctionalScripts.Count}개의 기능 스크립트를 활성화합니다.");
        foreach (var script in _allFunctionalScripts)
        {
            if (script != null)
            {
                script.enabled = true;
            }
        }

        foreach (var function in _buildingFunctions)
        {
            function.OnBuildingEnabled();
        }
    }

    #endregion

    #region 타일 점유

    /// <summary>
    /// 건물 영역의 타일을 GameMap에 점유 등록합니다.
    /// </summary>
    private void RegisterToGameMap()
    {
        if (buildingData == null || MapGenerator.instance == null) return;

        GameMap gameMap = MapGenerator.instance.GameMapInstance;
        Vector2Int basePos = new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y)
        );

        for (int x = 0; x < buildingData.size.x; x++)
        {
            for (int y = 0; y < buildingData.size.y; y++)
            {
                int tx = basePos.x + x;
                int ty = basePos.y + y;
                gameMap.MarkTileOccupied(tx, ty, buildingData.blocksMovement);
                // 통행 가능 건물(바닥 타일, 다리 등)은 그 위에 서거나 건물을 올릴 수 있도록
                // FloorSupportGrid에 등록합니다. 건설 예정지는 여기에 포함되지 않습니다.
                if (!buildingData.blocksMovement)
                    gameMap.MarkFloorSupport(tx, ty, true);
            }
        }

        // Fog of War — 건물 풋프린트 + 주변 buildingRadius 영역의 안개를 제거합니다.
        // 건물이 파괴되기 전까지는 영구적으로 비춤 (참조 카운터 +1).
        FogOfWarManager.instance?.AddBuildingSight(basePos, buildingData.size);
    }

    /// <summary>
    /// 건물 영역의 타일 점유를 GameMap에서 해제합니다.
    /// </summary>
    private void UnregisterFromGameMap()
    {
        if (buildingData == null || MapGenerator.instance == null) return;

        GameMap gameMap = MapGenerator.instance.GameMapInstance;
        Vector2Int basePos = new Vector2Int(
            Mathf.FloorToInt(transform.position.x),
            Mathf.FloorToInt(transform.position.y)
        );

        for (int x = 0; x < buildingData.size.x; x++)
        {
            for (int y = 0; y < buildingData.size.y; y++)
            {
                gameMap.UnmarkTileOccupied(basePos.x + x, basePos.y + y);
            }
        }

        // Fog of War — 건물 시야 카운터 -1 (다른 건물/직원이 같은 타일을 비추면 그대로 유지됨)
        FogOfWarManager.instance?.RemoveBuildingSight(basePos, buildingData.size);
    }

    /// <summary>
    /// 건물이 차지하던 타일 점유를 해제합니다.
    /// </summary>
    private void ReleaseOccupiedTiles()
    {
        if (_tilesReleased) return;
        _tilesReleased = true;
        UnregisterFromGameMap();
    }

    #endregion

    #region 생명주기

    void OnDestroy()
    {
        if (_instanceId >= 0 && RuntimeIDRegistry.instance != null)
        {
            RuntimeIDRegistry.instance.Unregister(_instanceId);
        }
        ReleaseOccupiedTiles();
    }

    #endregion
}
