using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 디버그 패널의 자원 지급 한 줄.
/// 아이템 이름·현재 보유량과 지급 버튼 3개(+1 / +10 / +100)로 구성됩니다.
/// </summary>
public class DebugResourceRow : MonoBehaviour
{
    [Header("UI 요소")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI countText;
    [SerializeField] private Button grant1Button;
    [SerializeField] private Button grant10Button;
    [SerializeField] private Button grant100Button;

    private ItemData item;

    /// <summary>지급 대상 아이템으로 초기화합니다.</summary>
    public void Setup(ItemData targetItem)
    {
        item = targetItem;
        if (item == null) return;

        if (nameText != null)
            nameText.text = string.IsNullOrEmpty(item.itemName) ? item.name : item.itemName;

        if (iconImage != null)
        {
            iconImage.sprite = item.itemIcon;
            iconImage.enabled = item.itemIcon != null;
        }

        Bind(grant1Button, 1);
        Bind(grant10Button, 10);
        Bind(grant100Button, 100);

        Refresh();
    }

    /// <summary>현재 보유량 표시를 갱신합니다.</summary>
    public void Refresh()
    {
        if (countText == null) return;

        int count = InventoryManager.instance != null && item != null
            ? InventoryManager.instance.GetItemCount(item)
            : 0;
        countText.text = count.ToString();
    }

    private void Bind(Button button, int amount)
    {
        if (button == null) return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() =>
        {
            DebugManager.instance?.GrantItem(item, amount);
            Refresh();
        });
    }
}
