using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

/// <summary>
/// 생산 건물 UI.
/// 제작 가능한 레시피 리스트를 표시하고 수량 조절 후 제작 주문을 생성합니다.
/// RecipeItem 프리팹이 각자의 수량 조절 버튼과 제작 버튼을 가집니다.
/// </summary>
public class ProductionUI : BasePanel
{
    #region 상수

    /// <summary>제작 수량 최소값</summary>
    private const int MIN_CRAFT_AMOUNT = 1;

    /// <summary>제작 수량 최대값</summary>
    private const int MAX_CRAFT_AMOUNT = 99;

    #endregion

    #region 필드 및 설정

    [Header("UI 요소 연결")]
    [SerializeField] private TextMeshProUGUI headerText;
    [SerializeField] private Transform recipeListContainer;
    [SerializeField] private GameObject recipeItemPrefab;
    [SerializeField] private Button closeButton;

    /// <summary>사용 가능한 레시피 목록</summary>
    private List<CraftingRecipe> _availableRecipes;

    /// <summary>제작 시작 콜백 (레시피, 수량, 직원)</summary>
    private Action<CraftingRecipe, int, Employee> _onStartProductionCallback;

    /// <summary>UI를 호출한 건물 참조</summary>
    private MonoBehaviour _sourceBuilding;

    /// <summary>레시피별 제작 수량 (RecipeItem별 독립적)</summary>
    private Dictionary<CraftingRecipe, int> _recipeCraftAmounts = new Dictionary<CraftingRecipe, int>();

    /// <summary>레시피별 UI 요소 참조</summary>
    private Dictionary<CraftingRecipe, RecipeItemUI> _recipeItemUIs = new Dictionary<CraftingRecipe, RecipeItemUI>();

    #endregion

    #region 초기화 및 정리

    void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseClicked);
    }

    void OnEnable()
    {
        _recipeCraftAmounts.Clear();
        _recipeItemUIs.Clear();
    }

    void OnDisable()
    {
        ClearRecipeList();
        _recipeCraftAmounts.Clear();
        _recipeItemUIs.Clear();
    }

    #endregion

    #region 공개 API

    /// <summary>
    /// 생산 건물에서 호출하여 UI를 초기화합니다.
    /// </summary>
    /// <param name="recipes">표시할 레시피 목록</param>
    /// <param name="onStartProduction">제작 시작 시 호출될 콜백</param>
    /// <param name="sourceBuilding">호출한 건물 (헤더 표시용)</param>
    public void Setup(List<CraftingRecipe> recipes, Action<CraftingRecipe, int, Employee> onStartProduction, MonoBehaviour sourceBuilding = null)
    {
        _availableRecipes = recipes;
        _onStartProductionCallback = onStartProduction;
        _sourceBuilding = sourceBuilding;

        if (headerText != null)
        {
            string buildingName = sourceBuilding != null ? sourceBuilding.name : "생산 건물";
            headerText.text = $"{buildingName} - 제작";
        }

        // 레시피별 초기 수량 설정
        _recipeCraftAmounts.Clear();
        _recipeItemUIs.Clear();
        foreach (var recipe in recipes)
        {
            if (recipe != null)
                _recipeCraftAmounts[recipe] = MIN_CRAFT_AMOUNT;
        }

        UpdateRecipeList();
    }

    /// <summary>
    /// UI 강제 새로고침 (인벤토리 변경 시 등).
    /// 모든 레시피의 제작 버튼 상태를 다시 확인합니다.
    /// </summary>
    public void RefreshUI()
    {
        foreach (var kvp in _recipeItemUIs)
        {
            UpdateMakeOrderButton(kvp.Key, kvp.Value);
        }
    }

    #endregion

    #region 레시피 목록 관리

    /// <summary>
    /// 레시피 리스트를 갱신합니다 (전체 재생성).
    /// </summary>
    private void UpdateRecipeList()
    {
        ClearRecipeList();

        if (_availableRecipes == null || _availableRecipes.Count == 0)
        {
            Debug.LogWarning("[ProductionUI] 사용 가능한 레시피가 없습니다.");
            return;
        }

        foreach (var recipe in _availableRecipes)
        {
            if (recipe == null) continue;
            CreateRecipeItem(recipe);
        }
    }

    /// <summary>
    /// 기존 레시피 목록의 UI 오브젝트를 모두 제거합니다.
    /// </summary>
    private void ClearRecipeList()
    {
        if (recipeListContainer == null) return;

        foreach (Transform child in recipeListContainer)
        {
            Destroy(child.gameObject);
        }

        _recipeItemUIs.Clear();
    }

    /// <summary>
    /// 단일 레시피에 대한 UI 행을 생성합니다.
    /// 프리팹 내의 UI 요소를 찾아 이벤트를 연결합니다.
    /// </summary>
    /// <param name="recipe">표시할 레시피</param>
    private void CreateRecipeItem(CraftingRecipe recipe)
    {
        GameObject item = Instantiate(recipeItemPrefab, recipeListContainer);

        RecipeItemUI itemUI = new RecipeItemUI();

        // UI 요소 찾기
        itemUI.nameText = item.transform.Find("NameText")?.GetComponent<TextMeshProUGUI>();
        itemUI.iconImage = item.transform.Find("Image")?.GetComponent<Image>();
        itemUI.minusButton = item.transform.Find("MinusButton")?.GetComponent<Button>();
        itemUI.plusButton = item.transform.Find("PlusButton")?.GetComponent<Button>();
        itemUI.makeOrderButton = item.transform.Find("MakeProductionOrder")?.GetComponent<Button>();
        itemUI.amountText = item.transform.Find("MinusButton")?.parent?.Find("AmountText")?.GetComponent<TextMeshProUGUI>();

        // AmountText가 다른 위치에 있을 수 있으므로 추가 검색
        if (itemUI.amountText == null)
        {
            var allTexts = item.GetComponentsInChildren<TextMeshProUGUI>();
            foreach (var text in allTexts)
            {
                if (text != itemUI.nameText)
                {
                    itemUI.amountText = text;
                    break;
                }
            }
        }

        // 레시피 정보 설정
        if (itemUI.nameText != null)
            itemUI.nameText.text = recipe.outputItem.itemName;

        if (itemUI.iconImage != null && recipe.outputItem.itemIcon != null)
            itemUI.iconImage.sprite = recipe.outputItem.itemIcon;

        // 초기 수량 표시
        int currentAmount = _recipeCraftAmounts.ContainsKey(recipe) ? _recipeCraftAmounts[recipe] : MIN_CRAFT_AMOUNT;
        if (itemUI.amountText != null)
            itemUI.amountText.text = currentAmount.ToString();

        // 버튼 이벤트 연결
        if (itemUI.minusButton != null)
            itemUI.minusButton.onClick.AddListener(() => OnMinusClicked(recipe, itemUI));

        if (itemUI.plusButton != null)
            itemUI.plusButton.onClick.AddListener(() => OnPlusClicked(recipe, itemUI));

        if (itemUI.makeOrderButton != null)
        {
            itemUI.makeOrderButton.onClick.AddListener(() => OnMakeProductionOrderClicked(recipe));
            UpdateMakeOrderButton(recipe, itemUI);
        }

        _recipeItemUIs[recipe] = itemUI;
    }

    #endregion

    #region 버튼 이벤트

    /// <summary>
    /// 수량 감소 버튼 클릭.
    /// </summary>
    private void OnMinusClicked(CraftingRecipe recipe, RecipeItemUI itemUI)
    {
        if (!_recipeCraftAmounts.ContainsKey(recipe)) return;

        int currentAmount = _recipeCraftAmounts[recipe];
        if (currentAmount > MIN_CRAFT_AMOUNT)
        {
            currentAmount--;
            _recipeCraftAmounts[recipe] = currentAmount;

            if (itemUI.amountText != null)
                itemUI.amountText.text = currentAmount.ToString();

            UpdateMakeOrderButton(recipe, itemUI);
        }
    }

    /// <summary>
    /// 수량 증가 버튼 클릭.
    /// </summary>
    private void OnPlusClicked(CraftingRecipe recipe, RecipeItemUI itemUI)
    {
        if (!_recipeCraftAmounts.ContainsKey(recipe)) return;

        int currentAmount = _recipeCraftAmounts[recipe];
        if (currentAmount < MAX_CRAFT_AMOUNT)
        {
            currentAmount++;
            _recipeCraftAmounts[recipe] = currentAmount;

            if (itemUI.amountText != null)
                itemUI.amountText.text = currentAmount.ToString();

            UpdateMakeOrderButton(recipe, itemUI);
        }
    }

    /// <summary>
    /// 생산 주문 생성 버튼 클릭.
    /// 재료가 충분하면 콜백을 호출하고 UI를 닫습니다.
    /// </summary>
    private void OnMakeProductionOrderClicked(CraftingRecipe recipe)
    {
        if (recipe == null)
        {
            Debug.LogWarning("[ProductionUI] 레시피가 null입니다.");
            return;
        }

        int amount = _recipeCraftAmounts.ContainsKey(recipe) ? _recipeCraftAmounts[recipe] : MIN_CRAFT_AMOUNT;

        if (!CheckMaterials(recipe, amount))
        {
            Debug.LogWarning($"[ProductionUI] 재료가 부족합니다!");
            ShowInsufficientMaterialsMessage(recipe, amount);
            return;
        }

        _onStartProductionCallback?.Invoke(recipe, amount, null);

        UIManager.instance.HidePanel(UIPanelType.ProductionUI);

        Debug.Log($"[ProductionUI] {recipe.outputItem.itemName} x{amount} Order가 생성되었습니다. 건물을 다시 클릭하여 직원을 할당하세요.");
    }

    /// <summary>
    /// 닫기 버튼 클릭.
    /// </summary>
    private void OnCloseClicked()
    {
        UIManager.instance.HidePanel(UIPanelType.ProductionUI);
    }

    #endregion

    #region 재료 확인

    /// <summary>
    /// 재료가 충분한지 확인합니다 (예약분 제외한 사용 가능 수량 기준).
    /// </summary>
    /// <param name="recipe">확인할 레시피</param>
    /// <param name="amount">제작 수량</param>
    /// <returns>모든 재료가 충분한지 여부</returns>
    private bool CheckMaterials(CraftingRecipe recipe, int amount)
    {
        if (recipe == null || recipe.requiredMaterials == null) return false;

        foreach (var cost in recipe.requiredMaterials)
        {
            int required = cost.amount * amount;
            int available = InventoryManager.instance.GetAvailableAmount(cost.item);

            if (available < required)
                return false;
        }

        return true;
    }

    /// <summary>
    /// 제작 버튼의 활성화 상태와 텍스트를 업데이트합니다.
    /// </summary>
    /// <param name="recipe">해당 레시피</param>
    /// <param name="itemUI">UI 요소 참조</param>
    private void UpdateMakeOrderButton(CraftingRecipe recipe, RecipeItemUI itemUI)
    {
        if (itemUI.makeOrderButton == null) return;

        int amount = _recipeCraftAmounts.ContainsKey(recipe) ? _recipeCraftAmounts[recipe] : MIN_CRAFT_AMOUNT;
        bool canCraft = CheckMaterials(recipe, amount);

        itemUI.makeOrderButton.interactable = canCraft;

        var buttonText = itemUI.makeOrderButton.GetComponentInChildren<TextMeshProUGUI>();
        if (buttonText != null)
        {
            buttonText.text = canCraft ? "제작 시작" : "재료 부족";
        }
    }

    /// <summary>
    /// 재료 부족 메시지를 로그로 출력합니다.
    /// </summary>
    /// <param name="recipe">확인할 레시피</param>
    /// <param name="amount">제작 수량</param>
    private void ShowInsufficientMaterialsMessage(CraftingRecipe recipe, int amount)
    {
        string message = "재료 부족:\n";
        foreach (var cost in recipe.requiredMaterials)
        {
            int required = cost.amount * amount;
            int available = InventoryManager.instance.GetAvailableAmount(cost.item);
            int total = InventoryManager.instance.GetItemCount(cost.item);
            int reserved = total - available;

            if (available < required)
            {
                string reservedInfo = reserved > 0 ? $" ({reserved}개 예약됨)" : "";
                message += $"- {cost.item.itemName}: {available}/{required} 사용가능{reservedInfo}\n";
            }
        }

        Debug.Log(message);
        // TODO [UI]: 팝업 메시지로 표시
    }

    #endregion

    #region 내부 클래스

    /// <summary>
    /// RecipeItem 프리팹 내부의 UI 요소 참조를 담는 헬퍼 클래스
    /// </summary>
    private class RecipeItemUI
    {
        public TextMeshProUGUI nameText;
        public TextMeshProUGUI amountText;
        public Image iconImage;
        public Button minusButton;
        public Button plusButton;
        public Button makeOrderButton;
    }

    #endregion
}
