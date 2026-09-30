using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 침식 배양기 창 — 목표 방 침식 조절과 현재 상태 (냉난방기 창과 같은 구성).
/// </summary>
public class ErosionIncubatorUI : BasePanel
{
    [Header("UI 요소")]
    [SerializeField] private TextMeshProUGUI headerText;
    [SerializeField] private Slider targetSlider;
    [SerializeField] private TextMeshProUGUI targetText;
    [SerializeField] private TextMeshProUGUI roomText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Button closeButton;

    [SerializeField] private float refreshInterval = 0.5f;

    private ErosionIncubator unit;
    private float timer;
    private bool suppress;

    private void Awake()
    {
        if (closeButton != null) closeButton.onClick.AddListener(() => UIManager.instance.HidePanel(panelType));
        if (targetSlider != null) targetSlider.onValueChanged.AddListener(OnSlider);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { UIManager.instance.HidePanel(panelType); return; }
        if (unit == null) return;
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = refreshInterval;
        Refresh();
    }

    /// <summary>건물을 클릭했을 때 호출됩니다.</summary>
    public void Setup(ErosionIncubator target)
    {
        unit = target;
        if (unit == null) return;

        if (headerText != null) headerText.text = "침식 배양기";
        if (targetSlider != null)
        {
            suppress = true;
            targetSlider.minValue = ErosionIncubator.TARGET_MIN;
            targetSlider.maxValue = ErosionIncubator.TARGET_MAX;
            targetSlider.wholeNumbers = true;
            targetSlider.value = unit.TargetErosion;
            suppress = false;
        }
        timer = 0f;
        Refresh();
    }

    private void OnSlider(float value)
    {
        if (suppress || unit == null) return;
        unit.SetTargetErosion(value);
        Refresh();
    }

    private void Refresh()
    {
        if (targetText != null) targetText.text = $"목표 침식 {unit.TargetErosion:F0}";

        if (roomText != null)
        {
            var room = unit.CurrentRoom;
            roomText.text = (room != null ? $"현재 방 침식 {room.Erosion:F1}" : "현재 방 침식 —") +
                            $" · 결정체 {unit.CrystalSupply.Stock}/{unit.CrystalSupply.capacity}";
        }

        if (statusText != null) statusText.text = unit.DescribeStatus();
    }

    public override void OnClose()
    {
        unit = null;
        base.OnClose();
    }
}
