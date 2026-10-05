using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>보관함 창이 한 칸에 보여 줄 내용.</summary>
public struct StorageSlot
{
    public ItemData item;
    /// <summary>수량. 1 이하면 숫자를 표시하지 않습니다 (장비 등).</summary>
    public int count;
}

/// <summary>
/// 보관함 창(<see cref="StoragePanel"/>)에 자기 내용을 보여 줄 수 있는 건물 — 창고, 장비 보관소.
/// </summary>
public interface IStorageView
{
    /// <summary>보관 칸 수</summary>
    int SlotCount { get; }

    /// <summary>
    /// 이 보관함에 들어 있는 것을 칸 순서대로 채웁니다.
    /// 칸 수를 넘겨도 됩니다 — 넘친 만큼은 창이 '칸 초과'로 표시합니다.
    /// </summary>
    void CollectSlots(List<StorageSlot> into);
}

/// <summary>
/// 보관함 창 — 창고나 장비 보관소를 클릭하면 열립니다.
///
/// 4×4 칸으로 그 보관함에 든 것을 보여 줍니다(스택 수량 포함). 칸이 16개를 넘는 큰 보관함은
/// 좌우 버튼이나 마우스 휠로 쪽을 넘깁니다. 이름은 창 위에 붙는 건물 이름 바(BuildingNameBar)에서 바꿉니다.
/// 칸을 누르면 그 아이템의 정보 창이 옆에 뜹니다.
/// </summary>
public class StoragePanel : BasePanel
{
    private const int GRID_SIZE = 16;
    private const float REFRESH_INTERVAL = 0.5f;

    [SerializeField] private Button closeButton;
    [SerializeField] private StorageSlotCell[] cells = new StorageSlotCell[0];
    [SerializeField] private Button prevPageButton;
    [SerializeField] private Button nextPageButton;
    [SerializeField] private TMP_Text pageText;
    [SerializeField] private TMP_Text hintText;

    private readonly List<StorageSlot> slots = new List<StorageSlot>();
    private IStorageView source;
    private int page;
    private int pages = 1;
    private float timer;

    /// <summary>보관함 창을 그 보관함 내용으로 엽니다.</summary>
    public static void Show(IStorageView view)
    {
        if (view == null || UIManager.instance == null) return;

        var panel = UIManager.instance.GetPanel<StoragePanel>(UIPanelType.StorageUI);
        if (panel == null)
        {
            Debug.LogWarning("[StoragePanel] UIManager에 StorageUI 패널이 등록되지 않았습니다.");
            return;
        }

        panel.source = view;
        panel.page = 0;
        UIManager.instance.ShowPanel(UIPanelType.StorageUI);
        panel.Refresh();
    }

    private void Awake()
    {
        if (closeButton != null) closeButton.onClick.AddListener(() => UIManager.instance?.HidePanel(UIPanelType.StorageUI));
        if (prevPageButton != null) prevPageButton.onClick.AddListener(() => TurnPage(-1));
        if (nextPageButton != null) nextPageButton.onClick.AddListener(() => TurnPage(1));
    }

    private void Update()
    {
        // 보관함이 철거되면 닫는다 (Unity 객체는 파괴되면 == null)
        if (source == null || (source is UnityEngine.Object o && o == null))
        {
            source = null;
            UIManager.instance?.HidePanel(UIPanelType.StorageUI);
            return;
        }

        // 창 위에서 휠을 굴리면 쪽을 넘긴다
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0f
            && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, Input.mousePosition))
            TurnPage(scroll > 0f ? -1 : 1);

        timer += Time.unscaledDeltaTime;
        if (timer < REFRESH_INTERVAL) return;
        timer = 0f;
        Refresh();
    }

    private void TurnPage(int delta)
    {
        int next = Mathf.Clamp(page + delta, 0, pages - 1);
        if (next == page) return;
        page = next;
        Refresh();
    }

    private void Refresh()
    {
        if (source == null) return;

        slots.Clear();
        source.CollectSlots(slots);

        int capacity = Mathf.Max(0, source.SlotCount);
        int shown = Mathf.Max(capacity, slots.Count);
        pages = Mathf.Max(1, Mathf.CeilToInt(shown / (float)GRID_SIZE));
        page = Mathf.Clamp(page, 0, pages - 1);

        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] == null) continue;

            int index = page * GRID_SIZE + i;
            bool has = index < slots.Count;
            ItemData item = has ? slots[index].item : null;
            StorageSlotCell cell = cells[i];
            cells[i].Bind(item, has ? slots[index].count : 0, exists: index < shown, overflow: has && index >= capacity,
                onClick: () => { if (cell.Item != null) ItemInfoPopup.ShowForRect(cell.Item, (RectTransform)cell.transform); });
        }

        if (pageText != null) pageText.text = $"{page + 1}/{pages}";
        if (prevPageButton != null) prevPageButton.interactable = page > 0;
        if (nextPageButton != null) nextPageButton.interactable = page < pages - 1;

        if (hintText != null)
        {
            int used = Mathf.Min(slots.Count, capacity);
            hintText.text = slots.Count > capacity
                ? $"{used}/{capacity}칸 · 칸 초과 {slots.Count - capacity}개 (임시 보관)"
                : $"{used}/{capacity}칸";
        }
    }
}
