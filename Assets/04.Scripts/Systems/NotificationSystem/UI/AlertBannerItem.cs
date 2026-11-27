using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 경고 배너 1개(HUD). AlertReadoutUI가 풀로 관리한다.
/// 클릭 시 대상(culprits[0])으로 카메라를 이동한다.
/// 펼침 목록(<see cref="AlertReport.details"/>)이 있는 경고는 누를 때마다 배너 바로 아래로 상자 목록을 펼치거나 접는다.
///
/// 계층 예:
///   AlertBannerItem [Image=background, Button]
///   ├── Label (TMP_Text)
///   └── (선택) Icon
/// 펼침 목록은 런타임에 배너의 형제(바로 다음 칸)로 만든다 — 부모의 세로 배치가 그대로 밀어 준다.
/// </summary>
public class AlertBannerItem : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text label;
    [SerializeField] private Button button;

    /// <summary>펼쳐 둔 경고 키 — 배너 풀이 재배치돼도 같은 경고는 펼친 채로</summary>
    private static readonly HashSet<string> Expanded = new HashSet<string>();

    private static readonly Color RowColor = new Color(0.16f, 0.17f, 0.22f, 0.95f);

    private AlertReport _report;
    private RectTransform _detailRoot;
    private string _shownSignature;

    private void Awake()
    {
        if (button != null) button.onClick.AddListener(OnClicked);
    }

    private void OnDestroy()
    {
        if (_detailRoot != null) Destroy(_detailRoot.gameObject);
    }

    /// <summary>배너를 활성화하고 내용을 채운다.</summary>
    public void Show(AlertReport report, Color color)
    {
        _report = report;
        if (background != null) background.color = color;
        if (label != null) label.text = report.label;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        RefreshDetails();
    }

    public void Hide()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
        _report = default;
        RefreshDetails();
    }

    private void OnClicked()
    {
        if (_report.details != null && !string.IsNullOrEmpty(_report.key))
        {
            if (!Expanded.Remove(_report.key)) Expanded.Add(_report.key);
            RefreshDetails();
            return;
        }

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

    #region 펼침 목록

    private void RefreshDetails()
    {
        bool open = gameObject.activeSelf && _report.active && _report.details != null &&
                    !string.IsNullOrEmpty(_report.key) && Expanded.Contains(_report.key);
        if (!open)
        {
            if (_detailRoot != null && _detailRoot.gameObject.activeSelf) _detailRoot.gameObject.SetActive(false);
            _shownSignature = null;
            return;
        }

        var rows = _report.details();
        EnsureRoot();
        _detailRoot.SetSiblingIndex(transform.GetSiblingIndex() + 1);
        if (!_detailRoot.gameObject.activeSelf) _detailRoot.gameObject.SetActive(true);

        // 내용이 같으면 다시 만들지 않는다 — 폴링마다 지우면 누르는 순간의 상자가 사라진다
        var sig = new System.Text.StringBuilder();
        foreach (var r in rows) sig.Append(r.label).Append('\n');
        if (sig.ToString() == _shownSignature) return;
        _shownSignature = sig.ToString();

        RuntimeUI.Clear(_detailRoot);
        foreach (var r in rows)
        {
            var row = r;
            var btn = RuntimeUI.Btn(_detailRoot, row.label, 0, () => row.onClick?.Invoke(), RowColor);
            var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (txt != null) { txt.alignment = TextAlignmentOptions.MidlineLeft; txt.margin = new Vector4(10f, 0f, 6f, 0f); }
        }
    }

    private void EnsureRoot()
    {
        if (_detailRoot != null) return;
        var go = new GameObject("AlertDetails", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement));
        go.transform.SetParent(transform.parent, false);
        _detailRoot = (RectTransform)go.transform;

        var mine = (RectTransform)transform;
        _detailRoot.anchorMin = mine.anchorMin;
        _detailRoot.anchorMax = mine.anchorMax;
        _detailRoot.pivot = mine.pivot;
        _detailRoot.sizeDelta = new Vector2(mine.sizeDelta.x, 0f);

        var v = go.GetComponent<VerticalLayoutGroup>();
        v.spacing = 2f;
        v.padding = new RectOffset(12, 0, 0, 4); // 들여 써서 배너에 딸린 목록으로 보이게
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var myLe = GetComponent<LayoutElement>();
        if (myLe != null) go.GetComponent<LayoutElement>().preferredWidth = myLe.preferredWidth;
    }

    #endregion
}
