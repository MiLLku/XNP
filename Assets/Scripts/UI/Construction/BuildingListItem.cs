using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// 건물 목록의 개별 아이템 UI
/// 
/// 저장 위치: Assets/Scripts/UI/Construction/BuildingListItem.cs
/// </summary>
public class BuildingListItem : MonoBehaviour
{
    [Header("UI 요소")]
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI sizeText;
    [SerializeField] private Button button;
    
    [Header("색상 설정")]
    [SerializeField] private Color normalColor = new Color(0.9f, 0.9f, 0.9f);
    [SerializeField] private Color selectedColor = new Color(0.7f, 0.85f, 1f);
    [SerializeField] private Color canAffordColor = new Color(0.8f, 1f, 0.8f);
    [SerializeField] private Color cannotAffordColor = new Color(1f, 0.75f, 0.75f);
    
    // 데이터
    private BuildingData buildingData;
    private Action<BuildingData> onClickCallback;
    private bool isSelected = false;
    private bool canAfford = true;
    
    public BuildingData BuildingData => buildingData;
    
    void Awake()
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }
        
        if (backgroundImage == null)
        {
            backgroundImage = GetComponent<Image>();
        }
    }
    
    /// <summary>
    /// 아이템을 초기화합니다.
    /// </summary>
    public void Initialize(BuildingData data, Action<BuildingData> onClick)
    {
        buildingData = data;
        onClickCallback = onClick;
        
        // UI 설정
        if (nameText != null)
        {
            nameText.text = data.buildingName;
        }
        
        if (sizeText != null)
        {
            sizeText.text = $"{data.size.x}x{data.size.y}";
        }
        
        if (iconImage != null && data.icon != null)
        {
            iconImage.sprite = data.icon;
            iconImage.gameObject.SetActive(true);
        }
        else if (iconImage != null)
        {
            iconImage.gameObject.SetActive(false);
        }
        
        // 버튼 이벤트
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
        }
        
        UpdateVisual();
    }
    
    private void OnClick()
    {
        onClickCallback?.Invoke(buildingData);
    }
    
    /// <summary>
    /// 선택 상태를 설정합니다.
    /// </summary>
    public void SetSelected(bool selected)
    {
        isSelected = selected;
        UpdateVisual();
    }
    
    /// <summary>
    /// 자원 구매 가능 여부를 업데이트합니다.
    /// </summary>
    public void UpdateAffordability(bool affordable)
    {
        canAfford = affordable;
        UpdateVisual();
    }
    
    private void UpdateVisual()
    {
        if (backgroundImage == null) return;
        
        Color baseColor;
        
        if (isSelected)
        {
            baseColor = selectedColor;
        }
        else if (!canAfford)
        {
            baseColor = cannotAffordColor;
        }
        else
        {
            baseColor = normalColor;
        }
        
        backgroundImage.color = baseColor;
        
        // 버튼 interactable은 항상 true (선택은 가능하되 건설만 불가)
        if (button != null)
        {
            button.interactable = true;
        }
        
        // 텍스트 색상 (자원 부족 시 회색 처리)
        if (nameText != null)
        {
            nameText.color = canAfford ? Color.black : Color.gray;
        }
    }
}