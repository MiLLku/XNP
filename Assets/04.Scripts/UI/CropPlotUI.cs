using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 재배 창 — 밭·수경재배기의 칸별 작물 선택과 상태.
///
/// 칸마다 한 줄: [칸 번호] [작물 버튼 — 누를 때마다 비움 → 작물1 → 작물2 … 순환] [상태 문구].
/// 수경재배기면 침식 결정체 재고도 보여 줍니다.
/// </summary>
public class CropPlotUI : BasePanel
{
    [Header("UI 요소")]
    [SerializeField] private TextMeshProUGUI headerText;
    [SerializeField] private TextMeshProUGUI supplyText;
    [SerializeField] private Transform rowContainer;
    [Tooltip("칸 한 줄 템플릿 (비활성). 자식: Label(TMP), CropButton(Button + 자식 TMP), Status(TMP)")]
    [SerializeField] private GameObject rowTemplate;
    [SerializeField] private Button closeButton;

    [SerializeField] private float refreshInterval = 0.5f;

    private CropPlot plot;
    private readonly List<(TextMeshProUGUI crop, TextMeshProUGUI status)> rows = new List<(TextMeshProUGUI, TextMeshProUGUI)>();
    private float timer;

    private void Awake()
    {
        if (closeButton != null) closeButton.onClick.AddListener(() => UIManager.instance.HidePanel(panelType));
        if (rowTemplate != null) rowTemplate.SetActive(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { UIManager.instance.HidePanel(panelType); return; }
        if (plot == null) return;
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = refreshInterval;
        Refresh();
    }

    /// <summary>건물을 클릭했을 때 호출됩니다.</summary>
    public void Setup(CropPlot target)
    {
        plot = target;
        if (plot == null) return;

        if (headerText != null) headerText.text = plot.PlotName;

        foreach (Transform child in rowContainer)
            if (child.gameObject != rowTemplate) Destroy(child.gameObject);
        rows.Clear();

        for (int i = 0; i < plot.Slots.Count; i++)
        {
            int index = i;
            var row = Instantiate(rowTemplate, rowContainer);
            row.SetActive(true);
            row.transform.Find("Label").GetComponent<TextMeshProUGUI>().text = $"칸 {i + 1}";
            var button = row.transform.Find("CropButton").GetComponent<Button>();
            button.onClick.AddListener(() => CycleCrop(index));
            rows.Add((button.GetComponentInChildren<TextMeshProUGUI>(),
                      row.transform.Find("Status").GetComponent<TextMeshProUGUI>()));
        }

        timer = 0f;
        Refresh();
    }

    /// <summary>비움 → 허용 작물들 → 비움 순으로 바꿉니다.</summary>
    private void CycleCrop(int index)
    {
        if (plot == null) return;
        var seeds = plot.AllowedSeeds;
        var current = plot.Slots[index].SelectedSeed;
        int pos = current == null ? -1 : IndexOf(seeds, current);
        int next = pos + 1;
        plot.SelectSeed(index, next < seeds.Count ? seeds[next] : null);
        Refresh();
    }

    private static int IndexOf(IReadOnlyList<ItemData> list, ItemData item)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == item) return i;
        return -1;
    }

    private void Refresh()
    {
        for (int i = 0; i < rows.Count && i < plot.Slots.Count; i++)
        {
            var slot = plot.Slots[i];
            rows[i].crop.text = slot.SelectedSeed != null ? CropName(slot.SelectedSeed) : "비움";
            rows[i].status.text = slot.Describe();
        }

        if (supplyText != null)
        {
            supplyText.gameObject.SetActive(plot.IsHydroponic);
            if (plot.IsHydroponic)
                supplyText.text = $"침식 결정체 {plot.CrystalSupply.Stock}/{plot.CrystalSupply.capacity}" +
                                  (plot.IsPowered ? "" : " · 전력 없음");
        }
    }

    /// <summary>씨앗 이름에서 작물 이름으로 ("쌀 씨앗" → 개체 이름 "쌀")</summary>
    private static string CropName(ItemData seed)
        => seed.plantEntity != null ? seed.plantEntity.Label : seed.itemName;

    public override void OnClose()
    {
        plot = null;
        base.OnClose();
    }
}
