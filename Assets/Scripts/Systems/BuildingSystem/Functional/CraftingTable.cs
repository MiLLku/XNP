using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

[RequireComponent(typeof(Collider2D))]
public class CraftingTable : MonoBehaviour, IBuildingFunction
{
    [Header("제작 설정")]
    [Tooltip("이 작업대에서 만들 수 있는 레시피 목록")]
    [SerializeField] private List<CraftingRecipe> availableRecipes;
    
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
    private Coroutine _currentCraftingCoroutine;
    
    // IBuildingFunction 구현
    public bool IsOperating => _isCrafting;
    
    void Start()
    {
        ValidateRecipes();
    }
    
    void OnDestroy()
    {
        // 진행 중인 제작 취소
        if (_currentCraftingCoroutine != null)
        {
            StopCoroutine(_currentCraftingCoroutine);
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
            TryStartCrafting(availableRecipes[0]);
        }
    }
    
    public void TryStartCrafting(CraftingRecipe recipe)
    {
        if (_isCrafting)
        {
            Debug.LogWarning("[CraftingTable] 이미 제작 중입니다.");
            return;
        }
        
        if (recipe == null)
        {
            Debug.LogError("[CraftingTable] 레시피가 null입니다.");
            return;
        }
        
        // 재료 확인
        if (!InventoryManager.instance.HasItems(recipe.requiredMaterials))
        {
            Debug.LogWarning($"[CraftingTable] 재료 부족: '{recipe.outputItem.itemName}'");
            ShowInsufficientMaterialsMessage(recipe);
            return;
        }
        
        // 재료 소모
        if (!InventoryManager.instance.RemoveItems(recipe.requiredMaterials))
        {
            Debug.LogError("[CraftingTable] 재료 소모 실패!");
            return;
        }
        
        // 제작 시작
        _currentRecipe = recipe;
        _currentCraftingCoroutine = StartCoroutine(CraftingCoroutine(recipe));
        
        // 사운드 재생
        PlaySound(craftingStartSound);
    }
    
    private IEnumerator CraftingCoroutine(CraftingRecipe recipe)
    {
        _isCrafting = true;
        _craftingProgress = 0f;
        
        Debug.Log($"[CraftingTable] '{recipe.outputItem.itemName}' 제작 시작... ({recipe.craftingTime}초)");
        
        // 진행 바 생성
        if (progressBarPrefab != null && _progressBarInstance == null)
        {
            _progressBarInstance = Instantiate(progressBarPrefab, transform.position + progressBarOffset, Quaternion.identity, transform);
        }
        
        float elapsedTime = 0f;
        while (elapsedTime < recipe.craftingTime)
        {
            elapsedTime += Time.deltaTime;
            _craftingProgress = elapsedTime / recipe.craftingTime;
            
            // 진행 바 업데이트
            UpdateProgressBar(_craftingProgress);
            
            yield return null;
        }
        
        // 제작 완료
        CompleteCrafting(recipe);
    }
    
    private void CompleteCrafting(CraftingRecipe recipe)
    {
        // 아이템 추가
        if (InventoryManager.instance != null)
        {
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
        if (!_isCrafting || _currentCraftingCoroutine == null) return;
        
        Debug.Log("[CraftingTable] 제작 취소됨");
        
        StopCoroutine(_currentCraftingCoroutine);
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
        _currentCraftingCoroutine = null;
        
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