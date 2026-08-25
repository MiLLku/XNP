using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 연구 트리 개별 노드 UI — 프리팹 불필요, 코드 생성 방식.
///
/// ResearchTreePanel이 GO를 생성한 뒤 Initialize()를 호출합니다.
///
/// 상태별 색상:
///   Completed  — 골드
///   Available  — 초록 (클릭 가능)
///   InProgress — 파랑 + 하단 진행바
///   Locked     — 회색 + 어두운 오버레이
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ResearchNodeUI : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    // ─── 색상 / 상수 ────────────────────────────────────────────────────────

    public const float SIZE = 70f;

    private static readonly Color ColCompleted  = new Color(1.00f, 0.82f, 0.22f);
    private static readonly Color ColAvailable  = new Color(0.22f, 0.80f, 0.36f);
    private static readonly Color ColInProgress = new Color(0.20f, 0.55f, 1.00f);
    private static readonly Color ColLocked     = new Color(0.25f, 0.25f, 0.28f);
    private static readonly Color ColBorderOn   = new Color(1f, 1f, 1f, 0.85f);
    private static readonly Color ColBorderOff  = new Color(1f, 1f, 1f, 0f);
    private static readonly Color ColProgressBg = new Color(0f, 0f, 0f, 0.5f);
    private static readonly Color ColProgressFg = new Color(0.20f, 0.70f, 1.00f);

    // ─── 런타임 생성 참조 ───────────────────────────────────────────────────

    private Image           _bgImage;
    private Image           _borderImage;
    private Image           _iconImage;
    private TextMeshProUGUI _nameText;
    private Image           _lockOverlay;
    private GameObject      _completeBadge;
    private Image           _progressFill;
    private GameObject      _tooltipRoot;
    private TextMeshProUGUI _tooltipText;

    // ─── 상태 ──────────────────────────────────────────────────────────────

    private ResearchNodeData  _node;
    private ResearchNodeState _state;
    private Action<ResearchNodeData> _onSelected;

    // ─── 초기화 ─────────────────────────────────────────────────────────────

    /// <summary>노드 초기화. AddComponent 직후 호출하세요.</summary>
    public void Initialize(ResearchNodeData node, Action<ResearchNodeData> onSelected)
    {
        _node       = node;
        _onSelected = onSelected;
        BuildVisuals();
        Refresh();
    }

    // ─── 비주얼 빌드 ────────────────────────────────────────────────────────

    private void BuildVisuals()
    {
        GetComponent<RectTransform>().sizeDelta = new Vector2(SIZE, SIZE);

        // 배경
        _bgImage = gameObject.AddComponent<Image>();
        _bgImage.raycastTarget = true;

        // 테두리
        var borderGO = MakeChild("Border");
        Rect(borderGO).sizeDelta = new Vector2(SIZE + 8, SIZE + 8);
        _borderImage = borderGO.AddComponent<Image>();
        _borderImage.raycastTarget = false;
        borderGO.transform.SetAsFirstSibling();

        // 아이콘
        var iconGO = MakeChild("Icon");
        Rect(iconGO).sizeDelta = new Vector2(40, 40);
        Rect(iconGO).anchoredPosition = new Vector2(0, 5);
        _iconImage = iconGO.AddComponent<Image>();
        _iconImage.preserveAspect = true;
        _iconImage.raycastTarget = false;
        _iconImage.enabled = false;

        // 이름 텍스트
        var nameGO = MakeChild("NameText");
        Rect(nameGO).sizeDelta = new Vector2(SIZE + 30, 18);
        Rect(nameGO).anchoredPosition = new Vector2(0, -(SIZE * 0.5f + 12));
        _nameText = nameGO.AddComponent<TextMeshProUGUI>();
        _nameText.fontSize = 9;
        _nameText.alignment = TextAlignmentOptions.Center;
        _nameText.color = new Color(0.9f, 0.9f, 0.9f);
        _nameText.raycastTarget = false;
        _nameText.textWrappingMode = TextWrappingModes.NoWrap;
        _nameText.overflowMode = TextOverflowModes.Ellipsis;

        // 잠금 오버레이
        var lockGO = MakeChild("LockOverlay");
        Rect(lockGO).sizeDelta = new Vector2(SIZE, SIZE);
        _lockOverlay = lockGO.AddComponent<Image>();
        _lockOverlay.color = new Color(0f, 0f, 0f, 0.62f);
        _lockOverlay.raycastTarget = false;

        // 완료 뱃지 (✓)
        _completeBadge = MakeChild("CompleteBadge");
        Rect(_completeBadge).sizeDelta = new Vector2(20, 20);
        Rect(_completeBadge).anchoredPosition = new Vector2(SIZE * 0.5f - 4f, SIZE * 0.5f - 4f);
        var badgeImg = _completeBadge.AddComponent<Image>();
        badgeImg.color = new Color(0.1f, 0.7f, 0.2f);
        badgeImg.raycastTarget = false;
        var checkGO = MakeChildIn("Check", _completeBadge.transform);
        Stretch(Rect(checkGO));
        var checkTmp = checkGO.AddComponent<TextMeshProUGUI>();
        checkTmp.text = "✓";
        checkTmp.fontSize = 13;
        checkTmp.color = Color.white;
        checkTmp.alignment = TextAlignmentOptions.Center;
        checkTmp.raycastTarget = false;

        // 진행바 (하단 strip, InProgress 전용)
        var progressBgGO = MakeChild("ProgressBg");
        Rect(progressBgGO).sizeDelta = new Vector2(SIZE, 6);
        Rect(progressBgGO).anchoredPosition = new Vector2(0, -(SIZE * 0.5f - 3));
        var progressBg = progressBgGO.AddComponent<Image>();
        progressBg.color = ColProgressBg;
        progressBg.raycastTarget = false;

        var progressFgGO = MakeChildIn("ProgressFg", progressBgGO.transform);
        var fillRect = progressFgGO.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0, 0);
        fillRect.anchorMax = new Vector2(0, 1);
        fillRect.pivot = new Vector2(0, 0.5f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = new Vector2(0, 0);
        _progressFill = progressFgGO.AddComponent<Image>();
        _progressFill.color = ColProgressFg;
        _progressFill.raycastTarget = false;

        // 툴팁 (호버 시 비용 표시)
        _tooltipRoot = MakeChild("Tooltip");
        Rect(_tooltipRoot).sizeDelta = new Vector2(180, 60);
        Rect(_tooltipRoot).anchoredPosition = new Vector2(0, SIZE * 0.5f + 38);
        var tooltipBg = _tooltipRoot.AddComponent<Image>();
        tooltipBg.color = new Color(0.07f, 0.07f, 0.09f, 0.96f);
        tooltipBg.raycastTarget = false;
        var ttGO = MakeChildIn("TooltipText", _tooltipRoot.transform);
        Stretch(Rect(ttGO));
        _tooltipText = ttGO.AddComponent<TextMeshProUGUI>();
        _tooltipText.fontSize = 9.5f;
        _tooltipText.color = Color.white;
        _tooltipText.margin = new Vector4(7, 7, 7, 7);
        _tooltipText.raycastTarget = false;
        _tooltipRoot.SetActive(false);
    }

    // ─── 갱신 ──────────────────────────────────────────────────────────────

    public void Refresh()
    {
        if (_node == null) return;

        _state = ResearchTreeManager.instance != null
            ? ResearchTreeManager.instance.GetNodeState(_node.nodeId)
            : ResearchNodeState.Locked;

        ApplyVisuals();

        if (_nameText != null)
            _nameText.text = _node.nodeName;

        if (_iconImage != null && _node.icon != null)
        {
            _iconImage.sprite = _node.icon;
            _iconImage.enabled = true;
        }

        UpdateProgressBar();
    }

    private void ApplyVisuals()
    {
        Color bg = _state switch
        {
            ResearchNodeState.Completed  => ColCompleted,
            ResearchNodeState.Available  => ColAvailable,
            ResearchNodeState.InProgress => ColInProgress,
            _                            => ColLocked,
        };

        if (_bgImage != null) _bgImage.color = bg;

        bool borderVisible = _state is ResearchNodeState.Completed or ResearchNodeState.Available or ResearchNodeState.InProgress;
        if (_borderImage != null) _borderImage.color = borderVisible ? ColBorderOn : ColBorderOff;

        if (_lockOverlay != null)
            _lockOverlay.enabled = _state == ResearchNodeState.Locked;

        if (_completeBadge != null)
            _completeBadge.SetActive(_state == ResearchNodeState.Completed);
    }

    private void UpdateProgressBar()
    {
        if (_progressFill == null) return;

        bool showProgress = _state == ResearchNodeState.InProgress
                            && ResearchTreeManager.instance != null
                            && _node.researchPointCost > 0;

        _progressFill.transform.parent.gameObject.SetActive(showProgress);

        if (showProgress)
        {
            float ratio = ResearchTreeManager.instance.CurrentProgress / _node.researchPointCost;
            var rt = _progressFill.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(SIZE * Mathf.Clamp01(ratio), 0);
        }
    }

    // ─── 이벤트 ─────────────────────────────────────────────────────────────

    public void OnPointerClick(PointerEventData eventData)
        => _onSelected?.Invoke(_node);

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_tooltipRoot == null || _node == null) return;

        string costLine = _node.researchPointCost > 0
            ? $"{_node.researchPointCost:F0}pt 필요"
            : "포인트 불필요";
        string stateLine = _state switch
        {
            ResearchNodeState.Completed  => "<color=#FFD43B>완료</color>",
            ResearchNodeState.Available  => "<color=#3ECC5F>연구 가능</color>",
            ResearchNodeState.InProgress => "<color=#3399FF>연구 중</color>",
            _                            => "<color=#888888>잠김</color>",
        };

        if (_tooltipText != null)
            _tooltipText.text = $"<b>{_node.nodeName}</b>  {stateLine}\n{costLine}";

        _tooltipRoot.SetActive(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_tooltipRoot != null) _tooltipRoot.SetActive(false);
    }

    // ─── 내부 유틸 ──────────────────────────────────────────────────────────

    private GameObject MakeChild(string n) => MakeChildIn(n, transform);

    private static GameObject MakeChildIn(string n, Transform parent)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    private static RectTransform Rect(GameObject go) => go.GetComponent<RectTransform>();

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
    }
}
