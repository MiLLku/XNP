using UnityEngine;

/// <summary>
/// 클릭 가능한 월드 아이템.
/// 마우스 호버 시 하이라이트를 표시하고, 클릭 시 인벤토리에 추가 후 파괴됩니다.
/// </summary>
[RequireComponent(typeof(Collider2D), typeof(Rigidbody2D))]
public class ClickableItem : MonoBehaviour
{
    [Header("아이템 정보")]
    [Tooltip("이 아이템의 데이터(ID, 이름 등) ScriptableObject")]
    [SerializeField] private ItemData itemData;

    [Header("시각 효과")]
    [Tooltip("마우스를 올렸을 때 켜질 하이라이트 오브젝트 (자식 오브젝트)")]
    [SerializeField] private GameObject highlightObject;

    private void Start()
    {
        if (highlightObject != null)
        {
            highlightObject.SetActive(false);
        }
    }

    /// <summary>
    /// 이 아이템의 데이터를 반환합니다.
    /// </summary>
    public ItemData GetItemData()
    {
        return itemData;
    }

    #region 마우스 상호작용

    private void OnMouseEnter()
    {
        if (highlightObject != null)
        {
            highlightObject.SetActive(true);
        }
    }

    private void OnMouseExit()
    {
        if (highlightObject != null)
        {
            highlightObject.SetActive(false);
        }
    }

    private void OnMouseDown()
    {
        if (itemData == null)
        {
            Debug.LogError($"아이템 {name}에 ItemData가 없습니다!", this.gameObject);
            return;
        }

        if (InventoryManager.instance != null)
        {
            InventoryManager.instance.AddItem(itemData);
            Destroy(this.gameObject);
        }
        else
        {
            Debug.LogError("씬에 InventoryManager가 없습니다! 아이템을 주울 수 없습니다.");
        }
    }

    #endregion
}
