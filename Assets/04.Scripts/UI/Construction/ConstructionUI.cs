using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using MessagePipe;

/// <summary>
/// 건설 UI 메인 패널
/// 카테고리 탭과 건물 목록을 표시합니다.
///
/// 저장 위치: Assets/Scripts/UI/Construction/ConstructionUI.cs
/// </summary>
public class ConstructionUI : BasePanel
{
    [Header("패널 참조")]
    [SerializeField] private CanvasGroup canvasGroup;
    
    [Header("카테고리 탭")]
    [SerializeField] private Transform categoryTabContainer;
    [SerializeField] private GameObject categoryTabPrefab;
    
    [Header("건물 목록")]
    [SerializeField] private Transform buildingListContainer;
    [SerializeField] private GameObject buildingListItemPrefab;
    [SerializeField] private ScrollRect buildingListScrollRect;
    
    [Header("상세 정보")]
    [SerializeField] private GameObject detailPanel;
    [SerializeField] private TextMeshProUGUI buildingNameText;
    [SerializeField] private TextMeshProUGUI buildingDescriptionText;
    [SerializeField] private TextMeshProUGUI resourceCostText;
    [SerializeField] private Image buildingPreviewImage;
    
    [Header("버튼")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button buildButton;
    
    [Header("색상 설정")]
    [SerializeField] private Color selectedTabColor = new Color(0.8f, 0.9f, 1f);
    [SerializeField] private Color normalTabColor = new Color(0.6f, 0.6f, 0.6f);
    [SerializeField] private Color canAffordColor = new Color(0.8f, 1f, 0.8f);
    [SerializeField] private Color cannotAffordColor = new Color(1f, 0.7f, 0.7f);
    
    // 상태
    private bool isOpen = false;
    private BuildingCategory currentCategory = BuildingCategory.Production;

    /// <summary>배치 모드·인벤토리 변경 메시지 구독 핸들</summary>
    private IDisposable _subscriptions;
    private BuildingData selectedBuildingData;
    
    // UI 요소 캐시
    private Dictionary<BuildingCategory, Button> categoryTabButtons = new Dictionary<BuildingCategory, Button>();
    private List<BuildingListItem> buildingListItems = new List<BuildingListItem>();
    
    // 참조
    private ConstructionManager constructionManager;
    
    void Start()
    {
        constructionManager = ConstructionManager.instance;
        
        if (constructionManager == null)
        {
            Debug.LogError("[ConstructionUI] ConstructionManager를 찾을 수 없습니다!");
            return;
        }
        
        // 버튼 이벤트 연결
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(OnClose);
        }
        
        if (buildButton != null)
        {
            buildButton.onClick.AddListener(OnBuildButtonClicked);
            buildButton.interactable = false;
        }
        
        // 배치 모드 · 인벤토리 변경 구독 (자원 표시 업데이트용)
        _subscriptions = DisposableBag.Create(
            GameMessageBus.Subscribe<BuildingPlacementModeChangedMessage>(
                m => OnPlacementModeChanged(m.isPlacing, m.building)),
            GameMessageBus.Subscribe<InventoryChangedMessage>(
                m => OnInventoryChanged(m.item, m.changeAmount)));
        
        // 초기화
        InitializeCategoryTabs();
    }
    
    void OnDestroy()
    {
        _subscriptions?.Dispose();
        _subscriptions = null;
    }
    
    #region 패널 열기/닫기

    /// <summary>
    /// UI를 엽니다. (BasePanel 오버라이드)
    /// </summary>
    public override void OnOpen()
    {
        base.OnOpen();
        isOpen = true;

        // Start()보다 먼저 호출될 수 있으므로 초기화 확인
        if (constructionManager == null)
        {
            constructionManager = ConstructionManager.instance;
        }

        if (constructionManager == null)
        {
            Debug.LogError("[ConstructionUI] ConstructionManager를 찾을 수 없습니다!");
            return;
        }

        // 첫 번째 카테고리 선택
        var categories = constructionManager.GetAvailableCategories();
        if (categories.Count > 0)
        {
            SelectCategory(categories[0]);
        }
        else
        {
            // 카테고리가 없으면 기본 카테고리 선택
            SelectCategory(BuildingCategory.Production);
        }

        // 상세 정보 초기화
        ClearDetail();
    }

    /// <summary>
    /// UI를 닫습니다. (BasePanel 오버라이드)
    /// </summary>
    public override void OnClose()
    {
        base.OnClose();
        isOpen = false;

        // 배치 모드도 종료
        if (constructionManager != null && constructionManager.IsPlacementMode)
        {
            constructionManager.ExitPlacementMode();
        }
    }

    #endregion
    
    #region 카테고리 탭
    
    private void InitializeCategoryTabs()
    {
        if (categoryTabContainer == null || categoryTabPrefab == null) return;
        
        // 기존 탭 삭제
        foreach (Transform child in categoryTabContainer)
        {
            Destroy(child.gameObject);
        }
        categoryTabButtons.Clear();
        
        // 모든 카테고리 탭 생성
        foreach (BuildingCategory category in System.Enum.GetValues(typeof(BuildingCategory)))
        {
            CreateCategoryTab(category);
        }
    }
    
    private void CreateCategoryTab(BuildingCategory category)
    {
        GameObject tabObj = Instantiate(categoryTabPrefab, categoryTabContainer);
        tabObj.name = $"Tab_{category}";
        
        // 텍스트 설정
        TextMeshProUGUI tabText = tabObj.GetComponentInChildren<TextMeshProUGUI>();
        if (tabText != null)
        {
            tabText.text = GetCategoryDisplayName(category);
        }
        
        // 버튼 이벤트
        Button tabButton = tabObj.GetComponent<Button>();
        if (tabButton != null)
        {
            BuildingCategory capturedCategory = category;
            tabButton.onClick.AddListener(() => SelectCategory(capturedCategory));
            categoryTabButtons[category] = tabButton;
        }
    }
    
    private string GetCategoryDisplayName(BuildingCategory category)
    {
        switch (category)
        {
            case BuildingCategory.Storage: return "보관";
            case BuildingCategory.Production: return "생산";
            case BuildingCategory.Furniture: return "가구";
            case BuildingCategory.Infrastructure: return "기반시설";
            case BuildingCategory.Special: return "특수";
            case BuildingCategory.Power: return "전력시설";
            default: return category.ToString();
        }
    }
    
    /// <summary>
    /// 카테고리를 선택합니다.
    /// </summary>
    public void SelectCategory(BuildingCategory category)
    {
        currentCategory = category;
        
        // 탭 시각 업데이트
        UpdateTabVisuals();
        
        // 건물 목록 업데이트
        RefreshBuildingList();
        
        // 상세 정보 초기화
        ClearDetail();
    }
    
    private void UpdateTabVisuals()
    {
        foreach (var kvp in categoryTabButtons)
        {
            Image tabImage = kvp.Value.GetComponent<Image>();
            if (tabImage != null)
            {
                tabImage.color = (kvp.Key == currentCategory) ? selectedTabColor : normalTabColor;
            }
        }
    }
    
    #endregion
    
    #region 건물 목록
    
    private void RefreshBuildingList()
    {
        if (buildingListContainer == null) return;
        
        // 기존 목록 삭제
        foreach (Transform child in buildingListContainer)
        {
            Destroy(child.gameObject);
        }
        buildingListItems.Clear();
        
        if (constructionManager == null) return;
        
        // 현재 카테고리의 건물 목록 가져오기
        List<BuildingData> buildings = constructionManager.GetBuildingsByCategory(currentCategory);
        
        foreach (var buildingData in buildings)
        {
            CreateBuildingListItem(buildingData);
        }
        
        // 스크롤 위치 초기화
        if (buildingListScrollRect != null && buildingListScrollRect.content != null)
        {
            // 즉시 실행하면 Destroy가 완료되지 않아서 문제 발생
            // StartCoroutine 또는 다음 프레임에서 실행
            StartCoroutine(ResetScrollPosition());
        }
    }
    private System.Collections.IEnumerator ResetScrollPosition()
    {
        // 한 프레임 대기 (Destroy 완료 대기)
        yield return null;
    
        if (buildingListScrollRect != null && buildingListScrollRect.content != null)
        {
            buildingListScrollRect.verticalNormalizedPosition = 1f;
        }
    }
    
    private void CreateBuildingListItem(BuildingData buildingData)
    {
        if (buildingListItemPrefab == null)
        {
            // 프리팹이 없으면 기본 UI 생성
            CreateDefaultBuildingListItem(buildingData);
            return;
        }
        
        GameObject itemObj = Instantiate(buildingListItemPrefab, buildingListContainer);
        itemObj.name = $"BuildingItem_{buildingData.buildingName}";
        
        BuildingListItem listItem = itemObj.GetComponent<BuildingListItem>();
        if (listItem != null)
        {
            listItem.Initialize(buildingData, OnBuildingSelected);
            listItem.UpdateAffordability(constructionManager.HasRequiredResources(buildingData));
            buildingListItems.Add(listItem);
        }
        else
        {
            // BuildingListItem 컴포넌트가 없으면 기본 설정
            SetupDefaultListItem(itemObj, buildingData);
        }
    }
    
    private void CreateDefaultBuildingListItem(BuildingData buildingData)
    {
        GameObject itemObj = new GameObject($"BuildingItem_{buildingData.buildingName}");
        itemObj.transform.SetParent(buildingListContainer, false);
        
        // RectTransform 설정
        RectTransform rt = itemObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(200, 40);
        
        // 배경 Image
        Image bg = itemObj.AddComponent<Image>();
        bg.color = Color.white;
        
        // 버튼
        Button btn = itemObj.AddComponent<Button>();
        BuildingData captured = buildingData;
        btn.onClick.AddListener(() => OnBuildingSelected(captured));
        
        // 텍스트 (자식 오브젝트)
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(itemObj.transform, false);
        
        RectTransform textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.sizeDelta = Vector2.zero;
        
        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = buildingData.buildingName;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.black;
        tmp.fontSize = 14;
    }
    
    private void SetupDefaultListItem(GameObject itemObj, BuildingData buildingData)
    {
        TextMeshProUGUI nameText = itemObj.GetComponentInChildren<TextMeshProUGUI>();
        if (nameText != null)
        {
            nameText.text = buildingData.buildingName;
        }
        
        Button button = itemObj.GetComponent<Button>();
        if (button != null)
        {
            BuildingData captured = buildingData;
            button.onClick.AddListener(() => OnBuildingSelected(captured));
        }
    }
    
    /// <summary>
    /// 건물이 선택되었을 때 호출됩니다.
    /// </summary>
    private void OnBuildingSelected(BuildingData buildingData)
    {
        selectedBuildingData = buildingData;
        
        // 상세 정보 표시
        ShowBuildingDetail(buildingData);
        
        // 목록 아이템 선택 상태 업데이트
        foreach (var item in buildingListItems)
        {
            item.SetSelected(item.BuildingData == buildingData);
        }
    }
    
    #endregion
    
    #region 상세 정보
    
    private void ShowBuildingDetail(BuildingData buildingData)
    {
        if (detailPanel != null)
        {
            detailPanel.SetActive(true);
        }
        
        // 이름
        if (buildingNameText != null)
        {
            buildingNameText.text = buildingData.buildingName;
        }
        
        // 설명
        if (buildingDescriptionText != null)
        {
            buildingDescriptionText.text = buildingData.description;
        }
        
        // 자원 비용
        if (resourceCostText != null)
        {
            resourceCostText.text = GetResourceCostString(buildingData);
        }
        
        // 미리보기 이미지
        if (buildingPreviewImage != null && buildingData.icon != null)
        {
            buildingPreviewImage.sprite = buildingData.icon;
            buildingPreviewImage.gameObject.SetActive(true);
        }
        else if (buildingPreviewImage != null)
        {
            buildingPreviewImage.gameObject.SetActive(false);
        }
        
        // 건설 버튼 활성화 여부
        UpdateBuildButton();
    }
    
    private string GetResourceCostString(BuildingData buildingData)
    {
        if (buildingData.requiredResources == null || buildingData.requiredResources.Count == 0)
        {
            return "필요 자원: 없음";
        }
        
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("필요 자원:");
        
        foreach (var cost in buildingData.requiredResources)
        {
            int current = InventoryManager.instance != null ? InventoryManager.instance.GetItemCount(cost.item) : 0;
            bool canAfford = current >= cost.amount;
            
            string colorTag = canAfford ? "green" : "red";
            sb.AppendLine($"  <color={colorTag}>{cost.item.itemName}: {current}/{cost.amount}</color>");
        }
        
        return sb.ToString();
    }
    
    private void ClearDetail()
    {
        selectedBuildingData = null;
        
        if (detailPanel != null)
        {
            detailPanel.SetActive(false);
        }
        
        if (buildButton != null)
        {
            buildButton.interactable = false;
        }
    }
    
    private void UpdateBuildButton()
    {
        if (buildButton == null || selectedBuildingData == null) return;

        // 건물이 선택된 경우 버튼은 항상 활성화 (자원 여부와 무관)
        // 자원 부족은 배치 모드에서 노란 고스트로 시각적으로 표시
        buildButton.interactable = true;

        bool canAfford = constructionManager.HasRequiredResources(selectedBuildingData);

        // 버튼 색상으로 자원 여부 표시
        Image buttonImage = buildButton.GetComponent<Image>();
        if (buttonImage != null)
        {
            buttonImage.color = canAfford ? canAffordColor : cannotAffordColor;
        }
    }
    
    #endregion
    
    #region 버튼 이벤트
    
    private void OnBuildButtonClicked()
    {
        if (selectedBuildingData == null) return;

        // 배치할 건물 데이터를 먼저 저장 (HidePanel 이후 selectedBuildingData가 null이 될 수 있음)
        BuildingData dataToPlace = selectedBuildingData;

        // 패널 먼저 닫기 — isPlacementMode가 아직 false이므로
        // OnClose()에서 ExitPlacementMode()가 호출되지 않음
        if (UIManager.instance != null)
        {
            UIManager.instance.HidePanel(UIPanelType.ConstructionUI);
        }

        // 패널이 닫힌 후 배치 모드 진입 (고스트 생성)
        bool success = constructionManager.EnterPlacementMode(dataToPlace);

        if (success)
        {
            Debug.Log($"[ConstructionUI] 배치 모드 진입 성공: {dataToPlace.buildingName}");
        }
        else
        {
            Debug.LogWarning($"[ConstructionUI] 건설 모드 진입 실패: {dataToPlace.buildingName}");
        }
    }
    
    #endregion
    
    #region 이벤트 핸들러
    
    private void OnPlacementModeChanged(bool isActive, BuildingData buildingData)
    {
        // 배치 모드 종료 시 - UI는 다시 표시하지 않음
        // (플레이어가 B키나 ESC로 건설 모드를 종료하면 일반 모드로 전환됨)
        if (!isActive)
        {
            Debug.Log("[ConstructionUI] 배치 모드 종료");

            // 자원이 변경되었을 수 있으므로 UI 갱신 (UI가 열려있을 때만)
            if (isOpen && selectedBuildingData != null)
            {
                UpdateBuildButton();

                if (resourceCostText != null)
                {
                    resourceCostText.text = GetResourceCostString(selectedBuildingData);
                }
            }
        }
    }
    
    private void OnInventoryChanged(ItemData item, int changeAmount)
    {
        // 자원이 변경되면 UI 업데이트
        if (!isOpen) return;
        
        // 건물 목록 affordability 업데이트
        foreach (var listItem in buildingListItems)
        {
            listItem.UpdateAffordability(constructionManager.HasRequiredResources(listItem.BuildingData));
        }
        
        // 상세 정보 업데이트
        if (selectedBuildingData != null)
        {
            if (resourceCostText != null)
            {
                resourceCostText.text = GetResourceCostString(selectedBuildingData);
            }
            UpdateBuildButton();
        }
    }
    
    #endregion
    
    #region Public 프로퍼티
    
    public bool IsOpen => isOpen;
    public BuildingCategory CurrentCategory => currentCategory;
    public BuildingData SelectedBuildingData => selectedBuildingData;
    
    #endregion
}