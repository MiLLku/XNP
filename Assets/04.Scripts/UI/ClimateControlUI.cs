using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 냉난방기 목표 온도 설정 패널.
///
/// 목표 온도만 정하는 게 아니라 <b>왜 전기를 이만큼 먹는지</b>를 같은 화면에서 보여줍니다 —
/// "공조기 없을 때 이 방은 52도가 된다 → 20도로 유지하려면 32도를 상대해야 한다 → 444W".
/// 그래서 플레이어가 목표를 낮추는 대신 단열을 하거나 더 얕은 곳에 짓는 선택을 할 수 있습니다.
/// </summary>
public class ClimateControlUI : BasePanel
{
    #region 필드 및 설정

    [Header("UI 요소 연결")]
    [SerializeField] private TextMeshProUGUI headerText;
    [SerializeField] private Slider targetSlider;
    [SerializeField] private TextMeshProUGUI targetText;
    [SerializeField] private TextMeshProUGUI roomTemperatureText;
    [SerializeField] private TextMeshProUGUI naturalText;
    [SerializeField] private TextMeshProUGUI powerText;
    [SerializeField] private Button closeButton;

    [Header("갱신")]
    [Tooltip("표시값을 다시 읽는 주기(초). 온도 틱이 1초라 그보다 촘촘할 필요가 없습니다.")]
    [SerializeField] private float refreshInterval = 0.5f;

    /// <summary>지금 편집 중인 냉난방기</summary>
    private ClimateControlUnit _unit;

    private float _refreshTimer;

    /// <summary>슬라이더를 코드로 움직일 때 콜백이 되돌아오는 것을 막는 플래그</summary>
    private bool _suppressCallback;

    #endregion

    #region 생명주기

    void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(() => UIManager.instance?.HidePanel(panelType));

        if (targetSlider != null)
            targetSlider.onValueChanged.AddListener(OnSliderChanged);
    }

    void Update()
    {
        if (_unit == null) return;

        _refreshTimer -= Time.deltaTime;
        if (_refreshTimer > 0f) return;

        _refreshTimer = refreshInterval;
        RefreshReadouts();
    }

    #endregion

    #region 설정

    /// <summary>건물을 클릭했을 때 호출됩니다.</summary>
    public void Setup(ClimateControlUnit unit)
    {
        _unit = unit;
        if (_unit == null) return;

        var config = TemperatureManager.instance != null ? TemperatureManager.instance.Config : null;
        float min = config != null ? config.climateTargetMin : -20f;
        float max = config != null ? config.climateTargetMax : 60f;

        if (headerText != null)
            headerText.text = _unit.IsHeater ? "온열기" : "냉방기";

        if (targetSlider != null)
        {
            _suppressCallback = true;
            targetSlider.minValue = min;
            targetSlider.maxValue = max;
            targetSlider.value = _unit.TargetTemperature;
            _suppressCallback = false;
        }

        _refreshTimer = 0f;
        RefreshReadouts();
    }

    #endregion

    #region 갱신

    private void OnSliderChanged(float value)
    {
        if (_suppressCallback || _unit == null) return;

        _unit.SetTargetTemperature(value);
        RefreshReadouts();
    }

    /// <summary>표시값을 모두 다시 읽습니다.</summary>
    private void RefreshReadouts()
    {
        if (_unit == null) return;

        if (targetText != null)
            targetText.text = $"목표 {_unit.TargetTemperature:F0}도";

        if (roomTemperatureText != null)
            roomTemperatureText.text = $"현재 방 온도 {_unit.RoomTemperature:F1}도";

        if (naturalText != null)
        {
            float natural = _unit.NaturalEquilibrium;
            float load = _unit.Load;

            naturalText.text = load > 0f
                ? $"이 기기가 없으면 {natural:F1}도 · 부하 {load:F1}도"
                : $"이 기기가 없어도 {natural:F1}도 — 지금은 할 일이 없습니다";
        }

        if (powerText != null)
            powerText.text = $"소비 전력 {_unit.GetPowerDraw()}W";
    }

    #endregion

    #region BasePanel

    public override void OnClose()
    {
        _unit = null;
        base.OnClose();
    }

    #endregion
}
