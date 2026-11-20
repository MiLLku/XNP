using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RuntimeUI;

/// <summary>
/// 화면 상단 중앙의 정화 진행 표시.
///   - 장치가 없으면 숨김
///   - 대기 중이면 [정화 가동] 버튼
///   - 가동 중이면 "N일 / 14일" + 진행 막대
/// <see cref="PurificationManager"/>가 런타임에 만듭니다 (씬 배선 없음).
/// </summary>
public class PurificationHUD : MonoBehaviour
{
    #region 상수

    private const float WIDTH = 360f;
    private const float TOP_MARGIN = 12f;
    private const float BAR_HEIGHT = 10f;
    private const int CANVAS_ORDER = 40;

    private static readonly Color BAR_FILL = new Color(0.35f, 0.8f, 0.55f, 1f);

    #endregion

    #region 참조

    private PurificationManager _manager;
    private GameObject _panel;
    private TextMeshProUGUI _statusText;
    private RectTransform _barFill;
    private GameObject _barRoot;
    private Button _activateButton;

    #endregion

    public static PurificationHUD Create(PurificationManager manager)
    {
        var canvasGo = new GameObject("PurificationHUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CANVAS_ORDER;

        var hud = canvasGo.AddComponent<PurificationHUD>();
        hud._manager = manager;
        hud.Build();
        return hud;
    }

    private void Build()
    {
        _panel = new GameObject("Panel", typeof(RectTransform));
        _panel.transform.SetParent(transform, false);
        var rt = (RectTransform)_panel.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -TOP_MARGIN);
        SetupPanel(_panel.GetComponent<RectTransform>(), WIDTH);

        var title = Text(_panel.transform, "완전 정화 장치", 16);
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;

        _statusText = Text(_panel.transform, "", 14);
        _statusText.alignment = TextAlignmentOptions.Center;

        // 진행 막대 — 배경 위에 왼쪽부터 차오르는 채움
        _barRoot = new GameObject("Bar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        _barRoot.transform.SetParent(_panel.transform, false);
        _barRoot.GetComponent<Image>().color = ROW_BG;
        _barRoot.GetComponent<Image>().raycastTarget = false;
        _barRoot.GetComponent<LayoutElement>().minHeight = BAR_HEIGHT;

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(_barRoot.transform, false);
        fill.GetComponent<Image>().color = BAR_FILL;
        fill.GetComponent<Image>().raycastTarget = false;
        _barFill = (RectTransform)fill.transform;
        _barFill.anchorMin = Vector2.zero;
        _barFill.anchorMax = new Vector2(0f, 1f);
        _barFill.offsetMin = _barFill.offsetMax = Vector2.zero;

        _activateButton = Btn(_panel.transform, "정화 가동", 0, OnActivateClicked, BTN_ON);
    }

    private void Update()
    {
        var device = _manager != null ? _manager.DisplayDevice : null;
        bool show = device != null;
        if (_panel.activeSelf != show) _panel.SetActive(show);
        if (!show) return;

        if (device.IsCompleted)
        {
            _statusText.text = "정화 완료";
            SetBar(1f);
            _activateButton.gameObject.SetActive(false);
        }
        else if (device.IsRunning)
        {
            _statusText.text = $"정화 진행 {device.ElapsedDays:F1}일 / {PurificationDevice.PURIFICATION_DAYS}일 ({device.Progress * 100f:F0}%)";
            SetBar(device.Progress);
            _activateButton.gameObject.SetActive(false);
        }
        else
        {
            _statusText.text = $"대기 중 — 가동하면 {PurificationDevice.PURIFICATION_DAYS}일간 버텨야 합니다";
            _barRoot.SetActive(false);
            _activateButton.gameObject.SetActive(true);
        }
    }

    private void SetBar(float progress)
    {
        if (!_barRoot.activeSelf) _barRoot.SetActive(true);
        _barFill.anchorMax = new Vector2(progress, 1f);
    }

    private void OnActivateClicked()
    {
        var device = _manager != null ? _manager.DisplayDevice : null;
        device?.Activate();
    }
}
