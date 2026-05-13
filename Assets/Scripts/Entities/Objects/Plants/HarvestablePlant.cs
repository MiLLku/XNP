using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 수확 가능한 식물.
/// 성장 단계별 스프라이트를 지원하며, 수확 후 재성장이 가능합니다.
/// IHarvestable, IWorkTarget을 구현하여 작업 시스템과 연동됩니다.
/// </summary>
public class HarvestablePlant : MonoBehaviour, IHarvestable, IWorkTarget
{
    [Header("식물 설정")]
    [SerializeField] private string plantName = "Berry Bush";
    [SerializeField] private float growthTime = 30f; // 성장 시간
    [SerializeField] private float harvestTime = 2f; // 수확 시간
    [SerializeField] private bool regrows = true; // 수확 후 다시 자라는지
    [SerializeField] private float regrowTime = 20f; // 재성장 시간
    
    [Header("수확물")]
    [SerializeField] private List<HarvestYield> yields = new List<HarvestYield>();
    
    [Header("성장 단계")]
    [SerializeField] private List<GrowthStage> growthStages = new List<GrowthStage>();
    
    private SpriteRenderer spriteRenderer;
    private float currentGrowth = 0f;
    private int currentStageIndex = 0;
    private bool isHarvestable = false;
    private bool isRegrowing = false;
    private bool isBeingHarvested = false;

    /// <summary>세이브에서 복원된 경우 true — Start()의 랜덤 초기화를 건너뜁니다.</summary>
    private bool _isRestoredFromSave = false;

    /// <summary>성장 진행도 (0~1)</summary>
    public float GrowthProgress => growthTime > 0 ? Mathf.Clamp01(currentGrowth / growthTime) : 1f;
    
    [System.Serializable]
    public class HarvestYield
    {
        public ItemData item;
        public int minAmount = 1;
        public int maxAmount = 3;
        [Range(0f, 1f)]
        public float dropChance = 1f;
    }
    
    [System.Serializable]
    public class GrowthStage
    {
        public string stageName;
        public Sprite sprite;
        public float growthPercent; // 0-1 사이 값
        public bool canHarvest = false;
        public Color tintColor = Color.white;
    }
    
    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }
        
        // 기본 성장 단계 설정
        if (growthStages.Count == 0)
        {
            SetupDefaultGrowthStages();
        }
    }
    
    void Start()
    {
        UpdateVisual();

        // 세이브 복원 시에는 랜덤 초기화 생략 (RestoreGrowthState에서 이미 설정됨)
        if (!_isRestoredFromSave)
        {
            // 테스트용: 30% 확률로 이미 수확 가능한 상태로 시작
            if (Random.Range(0f, 1f) < 0.3f)
            {
                currentGrowth = growthTime;
                UpdateGrowthStage();
            }
        }
    }
    
    void Update()
    {
        if (!isHarvestable || isRegrowing)
        {
            Grow(Time.deltaTime);
        }
    }
    
    private void SetupDefaultGrowthStages()
    {
        growthStages.Add(new GrowthStage { 
            stageName = "Seed", 
            growthPercent = 0f, 
            canHarvest = false 
        });
        growthStages.Add(new GrowthStage { 
            stageName = "Sprout", 
            growthPercent = 0.25f, 
            canHarvest = false 
        });
        growthStages.Add(new GrowthStage { 
            stageName = "Young", 
            growthPercent = 0.5f, 
            canHarvest = false 
        });
        growthStages.Add(new GrowthStage { 
            stageName = "Mature", 
            growthPercent = 0.75f, 
            canHarvest = false 
        });
        growthStages.Add(new GrowthStage { 
            stageName = "Harvestable", 
            growthPercent = 1f, 
            canHarvest = true,
            tintColor = new Color(1f, 1f, 0.8f) 
        });
    }
    
    private void Grow(float deltaTime)
    {
        float targetTime = isRegrowing ? regrowTime : growthTime;
        currentGrowth += deltaTime;
        
        float growthPercent = Mathf.Clamp01(currentGrowth / targetTime);
        
        UpdateGrowthStage();
        
        if (growthPercent >= 1f)
        {
            if (isRegrowing)
            {
                isRegrowing = false;
                Debug.Log($"[Plant] {plantName}이(가) 다시 자랐습니다!");
            }
            else
            {
                Debug.Log($"[Plant] {plantName}이(가) 수확 가능합니다!");
            }
        }
    }
    
    private void UpdateGrowthStage()
    {
        float growthPercent = currentGrowth / (isRegrowing ? regrowTime : growthTime);
        
        int newStageIndex = 0;
        for (int i = growthStages.Count - 1; i >= 0; i--)
        {
            if (growthPercent >= growthStages[i].growthPercent)
            {
                newStageIndex = i;
                break;
            }
        }
        
        if (newStageIndex != currentStageIndex || currentStageIndex == 0)
        {
            currentStageIndex = newStageIndex;
            var stage = growthStages[currentStageIndex];
            isHarvestable = stage.canHarvest;
            UpdateVisual();
        }
    }
    
    private void UpdateVisual()
    {
        if (currentStageIndex < 0 || currentStageIndex >= growthStages.Count) return;
        
        var stage = growthStages[currentStageIndex];
        
        if (stage.sprite != null)
        {
            spriteRenderer.sprite = stage.sprite;
        }
        
        spriteRenderer.color = stage.tintColor;
        
        // 크기 조정 (성장 표현)
        float scaleMultiplier = 0.3f + (0.7f * ((float)currentStageIndex / (growthStages.Count - 1)));
        transform.localScale = Vector3.one * scaleMultiplier;
    }
    
    #region IHarvestable 구현
    
    public bool CanHarvest()
    {
        return isHarvestable && !isBeingHarvested && !isRegrowing;
    }
    
    public void Harvest()
    {
        if (!CanHarvest()) return;
        
        Debug.Log($"[Plant] {plantName} 수확됨!");
        
        // 수확물 생성
        foreach (var yield in yields)
        {
            if (Random.Range(0f, 1f) <= yield.dropChance)
            {
                int amount = Random.Range(yield.minAmount, yield.maxAmount + 1);
                
                if (InventoryManager.instance != null && yield.item != null)
                {
                    InventoryManager.instance.AddItem(yield.item, amount);
                    Debug.Log($"[Plant] {yield.item.itemName} x{amount} 수확");
                }
            }
        }
        
        // 재성장 또는 제거
        if (regrows)
        {
            StartRegrowth();
        }
        else
        {
            // 식물 제거
            Destroy(gameObject);
        }
    }
    
    public float GetHarvestTime()
    {
        return harvestTime;
    }
    
    public WorkType GetHarvestType()
    {
        return WorkType.Gardening;
    }
    
    #endregion
    
    #region IWorkTarget 구현
    
    public Vector3 GetWorkPosition()
    {
        return transform.position + new Vector3(0, -0.3f, 0);
    }
    
    public WorkType GetWorkType()
    {
        return WorkType.Gardening;
    }
    
    public float GetWorkTime()
    {
        return harvestTime;
    }
    
    public bool IsWorkAvailable()
    {
        return CanHarvest();
    }
    
    public void CompleteWork(Employee worker)
    {
        isBeingHarvested = true;
        Harvest();
    }
    
    public void CancelWork(Employee worker)
    {
        isBeingHarvested = false;
    }
    
    #endregion
    
    /// <summary>
    /// 세이브에서 성장 상태를 복원합니다.
    /// Instantiate 직후, Start() 실행 전에 호출해야 합니다.
    /// </summary>
    /// <param name="growthProgress">저장된 성장 진행도 (0~1)</param>
    public void RestoreGrowthState(float growthProgress)
    {
        _isRestoredFromSave = true;
        currentGrowth = Mathf.Clamp01(growthProgress) * growthTime;

        // 기본 성장 단계가 없으면 먼저 설정 (Awake에서 이미 처리됐을 수 있음)
        if (growthStages.Count == 0) SetupDefaultGrowthStages();
        UpdateGrowthStage();

        // spriteRenderer는 Awake()에서 이미 할당됨
        if (spriteRenderer != null) UpdateVisual();
    }

    private void StartRegrowth()
    {
        currentGrowth = 0f;
        currentStageIndex = 0;
        isHarvestable = false;
        isRegrowing = true;
        isBeingHarvested = false;
        UpdateVisual();
        
        Debug.Log($"[Plant] {plantName}이(가) 재성장을 시작합니다. ({regrowTime}초)");
    }
    
    void OnMouseEnter()
    {
        if (CanHarvest())
        {
            spriteRenderer.color = new Color(1.5f, 1.5f, 1f);
        }
    }
    
    void OnMouseExit()
    {
        UpdateVisual(); // 원래 색상으로 복구
    }
    
    void OnDrawGizmos()
    {
        if (isHarvestable)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.3f);
        }
        else if (isRegrowing)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.3f);
        }
        else
        {
            Gizmos.color = Color.green;
            float size = 0.1f + (0.2f * (currentGrowth / growthTime));
            Gizmos.DrawWireCube(transform.position, Vector3.one * size);
        }
    }
}