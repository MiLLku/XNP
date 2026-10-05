using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>보관함 창의 한 칸 — 아이템 그림(없으면 이름)과 수량.</summary>
public class StorageSlotCell : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private Button button;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text countLabel;

    private static readonly Color SLOT = new Color(0.16f, 0.17f, 0.22f, 1f);
    private static readonly Color SLOT_NONE = new Color(0.10f, 0.10f, 0.13f, 0.5f);   // 보관 칸이 아닌 자리
    private static readonly Color SLOT_OVER = new Color(0.36f, 0.20f, 0.14f, 1f);     // 칸을 넘겨 임시로 쌓인 것

    private Action onClick;

    public ItemData Item { get; private set; }

    private void Awake()
    {
        if (button != null) button.onClick.AddListener(() => onClick?.Invoke());
    }

    /// <param name="exists">보관 칸으로 존재하는 자리인지 (마지막 쪽의 남는 자리는 false)</param>
    /// <param name="overflow">칸 수를 넘겨 임시로 쌓인 것인지</param>
    public void Bind(ItemData item, int count, bool exists, bool overflow, Action onClick)
    {
        Item = item;
        this.onClick = onClick;

        bool has = item != null;
        if (background != null) background.color = overflow ? SLOT_OVER : exists ? SLOT : SLOT_NONE;
        if (button != null) button.interactable = has;

        Sprite sprite = has ? item.itemIcon : null;
        if (icon != null)
        {
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }
        if (nameLabel != null)
        {
            nameLabel.text = has ? item.itemName : "";
            nameLabel.gameObject.SetActive(has);
        }
        if (countLabel != null)
        {
            countLabel.text = has && count > 1 ? count.ToString() : "";
            countLabel.gameObject.SetActive(has && count > 1);
        }
    }
}
