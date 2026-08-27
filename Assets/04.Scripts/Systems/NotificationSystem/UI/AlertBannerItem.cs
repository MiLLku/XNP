using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 경고 배너 1개(HUD). AlertReadoutUI가 풀로 관리한다.
/// 클릭 시 대상(culprits[0])으로 카메라를 이동한다.
///
/// 계층 예:
///   AlertBannerItem [Image=background, Button]
///   ├── Label (TMP_Text)
///   └── (선택) Icon
/// </summary>
public class AlertBannerItem : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text label;
    [SerializeField] private Button button;

    private AlertReport _report;

    private void Awake()
    {
        if (button != null) button.onClick.AddListener(OnClicked);
    }

    /// <summary>배너를 활성화하고 내용을 채운다.</summary>
    public void Show(AlertReport report, Color color)
    {
        _report = report;
        if (background != null) background.color = color;
        if (label != null) label.text = report.label;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    private void OnClicked()
    {
        // 장소가 문제인 경고(침식이 심한 방 등)는 좌표를 직접 들고 온다
        if (_report.focusPosition.HasValue)
        {
            CameraController.Instance?.FocusOn(_report.focusPosition.Value);
            return;
        }

        if (_report.culprits == null || _report.culprits.Count == 0) return;

        var target = _report.culprits[0];
        if (target != null)
            CameraController.Instance?.FocusOn(target.transform.position);
    }
}
