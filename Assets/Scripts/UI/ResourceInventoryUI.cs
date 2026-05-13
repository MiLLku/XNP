using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 자원 인벤토리 UI.
/// 글로벌 인벤토리의 아이템 목록과 예약 정보를 표시합니다.
/// 인벤토리 변경 이벤트를 구독하여 실시간으로 갱신됩니다.
/// </summary>
public class ResourceInventoryUI : BasePanel
{
    #region 필드 및 설정

    [Header("UI 요소")]
    [SerializeField] private Transform contentContainer;
    [SerializeField] private GameObject itemRowPrefab;
    [SerializeField] private Button closeButton;
    [SerializeField] private TextMeshProUGUI headerText;

    [Header("설정")]
    [SerializeField] private KeyCode toggleKey = KeyCode.I;

    /// <summary>UI가 현재 열려있는지 여부</summary>
    private bool isOpen = false;

    #endregion

    #region 초기화 및 정리

    void Start()
    {
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(OnClose);
        }

        // 인벤토리 변경 이벤트 구독
        if (InventoryManager.instance != null)
        {
            InventoryManager.instance.OnInventoryChanged += OnInventoryChanged;
        }
    }

    void OnDestroy()
    {
        // 이벤트 구독 해제
        if (InventoryManager.instance != null)
        {
            InventoryManager.instance.OnInventoryChanged -= OnInventoryChanged;
        }
    }

    #endregion

    #region 입력 처리

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            if (UIManager.instance != null)
            {
                UIManager.instance.TogglePanel(UIPanelType.ResourceInventory);
            }
        }
    }

    #endregion

    #region BasePanel 오버라이드

    /// <summary>
    /// UI 열기. 인벤토리 내용을 갱신합니다.
    /// </summary>
    public override void OnOpen()
    {
        base.OnOpen();
        isOpen = true;
        RefreshUI();
    }

    /// <summary>
    /// UI 닫기.
    /// </summary>
    public override void OnClose()
    {
        base.OnClose();
        isOpen = false;
    }

    #endregion

    #region UI 갱신

    /// <summary>
    /// UI 전체를 갱신합니다.
    /// 기존 행을 모두 제거하고 인벤토리에서 다시 생성합니다.
    /// </summary>
    private void RefreshUI()
    {
        if (contentContainer == null || itemRowPrefab == null) return;

        // 기존 아이템 행 삭제
        foreach (Transform child in contentContainer)
        {
            Destroy(child.gameObject);
        }

        if (InventoryManager.instance == null) return;

        List<KeyValuePair<ItemData, int>> items = InventoryManager.instance.GetAllItems();

        // 헤더 업데이트
        if (headerText != null)
        {
            headerText.text = $"자원 목록 ({items.Count}종)";
        }

        // 아이템이 없으면 "비어있음" 표시
        if (items.Count == 0)
        {
            GameObject emptyRow = Instantiate(itemRowPrefab, contentContainer);
            TextMeshProUGUI[] texts = emptyRow.GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length > 0) texts[0].text = "비어있음";
            if (texts.Length > 1) texts[1].text = "";
            return;
        }

        // 각 아이템에 대한 행 생성
        foreach (var kvp in items)
        {
            CreateItemRow(kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    /// 아이템 행을 생성합니다.
    /// 예약된 수량이 있으면 함께 표시합니다.
    /// </summary>
    /// <param name="itemData">표시할 아이템 데이터</param>
    /// <param name="count">보유 수량</param>
    private void CreateItemRow(ItemData itemData, int count)
    {
        if (itemData == null) return;

        GameObject row = Instantiate(itemRowPrefab, contentContainer);

        int reserved = InventoryManager.instance.GetReservedAmount(itemData);

        TextMeshProUGUI[] texts = row.GetComponentsInChildren<TextMeshProUGUI>();
        if (texts.Length > 0)
        {
            texts[0].text = itemData.itemName;
        }
        if (texts.Length > 1)
        {
            if (reserved > 0)
                texts[1].text = $"x{count} ({reserved} 예약됨)";
            else
                texts[1].text = $"x{count}";
        }
    }

    #endregion

    #region 이벤트 핸들러

    /// <summary>
    /// 인벤토리 변경 이벤트 핸들러.
    /// UI가 열려있을 때만 갱신합니다.
    /// </summary>
    private void OnInventoryChanged(ItemData item, int changeAmount)
    {
        if (isOpen)
        {
            RefreshUI();
        }
    }

    #endregion
}
