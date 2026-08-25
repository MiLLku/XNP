using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 스킬 트리 개별 노드 UI — 프리팹 불필요, 자급자족 방식.
///
/// Initialize() 호출 시 모든 시각 요소(배경·아이콘·이름·오버레이·툴팁)를 코드로 생성합니다.
/// SkillTreePanel이 직접 GO를 생성한 뒤 AddComponent + Initialize() 순서로 사용합니다.
///
/// 상태:
///   Unlocked     — 골드 (이미 해제)
///   Available    — 초록 (선행 충족 + 결격 없음)
///   Locked       — 회색 + 어두운 오버레이 (선행 미충족)
///   Disqualified — 적색 + ✕ 뱃지 (카테고리 결격)
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SkillNodeUI : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    // ─────────────────────────────────────────────
    #region 열거형 / 상수 / 색상

    public enum NodeState { Unlocked, Available, Locked, Disqualified }

    private const float SIZE = 60f;

    private static readonly Color ColUnlocked     = new Color(1.00f, 0.82f, 0.22f);
    private static readonly Color ColAvailable    = new Color(0.22f, 0.80f, 0.36f);
    private static readonly Color ColLocked       = new Color(0.28f, 0.28f, 0.30f);
    private static readonly Color ColDisqualified = new Color(0.65f, 0.10f, 0.10f);
    private static readonly Color ColBorderOn     = new Color(1f, 1f, 1f, 0.85f);
    private static readonly Color ColBorderOff    = new Color(1f, 1f, 1f, 0f);

    #endregion

    // ─────────────────────────────────────────────
    #region 런타임 생성 참조

    private Image                _bgImage;
    private Image                _borderImage;
    private Image                _iconImage;
    private TextMeshProUGUI      _nameText;
    private Image                _lockOverlayImage;
    private GameObject           _disqualBadgeGO;
    private GameObject           _tooltipRoot;
    private TextMeshProUGUI      _tooltipText;

    #endregion

    // ─────────────────────────────────────────────
    #region 상태

    private SkillData           _skill;
    private EmployeeSkillState  _skillState;
    private NodeState           _currentState;
    private Action              _onTreeChanged;

    #endregion

    // ─────────────────────────────────────────────
    #region 초기화 (SkillTreePanel에서 호출)

    /// <summary>
    /// 노드를 초기화합니다. AddComponent&lt;SkillNodeUI&gt;() 직후 호출하세요.
    /// </summary>
    /// <param name="skill">표시할 스킬 데이터</param>
    /// <param name="skillState">직원의 스킬 상태</param>
    /// <param name="onTreeChanged">스킬 해제 후 트리 전체 갱신을 요청할 콜백</param>
    public void Initialize(SkillData skill, EmployeeSkillState skillState, Action onTreeChanged)
    {
        _skill          = skill;
        _skillState     = skillState;
        _onTreeChanged  = onTreeChanged;
        BuildVisuals();
        Refresh();
    }

    #endregion

    // ─────────────────────────────────────────────
    #region 비주얼 빌드 (코드 생성)

    private void BuildVisuals()
    {
        GetComponent<RectTransform>().sizeDelta = new Vector2(SIZE, SIZE);

        // ── 배경 (이 GO에 직접)
        _bgImage               = gameObject.AddComponent<Image>();
        _bgImage.raycastTarget = true;

        // ── 테두리 (배경보다 8px 큰, 가장 아래 sibling)
        var borderGO          = MakeChild("Border");
        Rect(borderGO).sizeDelta = new Vector2(SIZE + 8, SIZE + 8);
        _borderImage              = borderGO.AddComponent<Image>();
        _borderImage.raycastTarget = false;
        borderGO.transform.SetAsFirstSibling();

        // ── 아이콘 (노드 중앙 상단)
        var iconGO             = MakeChild("Icon");
        Rect(iconGO).sizeDelta = new Vector2(36, 36);
        Rect(iconGO).anchoredPosition = new Vector2(0, 5);
        _iconImage                 = iconGO.AddComponent<Image>();
        _iconImage.preserveAspect  = true;
        _iconImage.raycastTarget   = false;
        _iconImage.enabled         = false;

        // ── 이름 텍스트 (노드 바로 아래)
        var nameGO             = MakeChild("NameText");
        Rect(nameGO).sizeDelta = new Vector2(SIZE + 28, 16);
        Rect(nameGO).anchoredPosition = new Vector2(0, -(SIZE * 0.5f + 11));
        _nameText                  = nameGO.AddComponent<TextMeshProUGUI>();
        _nameText.fontSize         = 9;
        _nameText.alignment        = TextAlignmentOptions.Center;
        _nameText.color            = new Color(0.9f, 0.9f, 0.9f);
        _nameText.raycastTarget    = false;
        _nameText.textWrappingMode = TextWrappingModes.NoWrap;
        _nameText.overflowMode     = TextOverflowModes.Ellipsis;

        // ── 잠금 오버레이 (어두운 반투명)
        var lockGO               = MakeChild("LockOverlay");
        Rect(lockGO).sizeDelta   = new Vector2(SIZE, SIZE);
        _lockOverlayImage        = lockGO.AddComponent<Image>();
        _lockOverlayImage.color  = new Color(0f, 0f, 0f, 0.62f);
        _lockOverlayImage.raycastTarget = false;

        // ── 결격 뱃지 (우상단 ✕)
        _disqualBadgeGO          = MakeChild("DisqualBadge");
        Rect(_disqualBadgeGO).sizeDelta = new Vector2(18, 18);
        Rect(_disqualBadgeGO).anchoredPosition = new Vector2(SIZE * 0.5f - 3f, SIZE * 0.5f - 3f);
        var badgeImg             = _disqualBadgeGO.AddComponent<Image>();
        badgeImg.color           = new Color(0.9f, 0.15f, 0.12f);
        badgeImg.raycastTarget   = false;
        // ✕ 텍스트
        var xGO = MakeChildIn("X", _disqualBadgeGO.transform);
        Stretch(Rect(xGO));
        var xTmp                = xGO.AddComponent<TextMeshProUGUI>();
        xTmp.text               = "✕";
        xTmp.fontSize           = 11;
        xTmp.color              = Color.white;
        xTmp.alignment          = TextAlignmentOptions.Center;
        xTmp.raycastTarget      = false;

        // ── 툴팁 패널 (호버 시 노드 위에 표시)
        _tooltipRoot             = MakeChild("Tooltip");
        Rect(_tooltipRoot).sizeDelta = new Vector2(200, 90);
        Rect(_tooltipRoot).anchoredPosition = new Vector2(0, SIZE * 0.5f + 56);
        var tooltipBg            = _tooltipRoot.AddComponent<Image>();
        tooltipBg.color          = new Color(0.07f, 0.07f, 0.09f, 0.96f);
        tooltipBg.raycastTarget  = false;
        // 툴팁 텍스트
        var ttGO = MakeChildIn("TooltipText", _tooltipRoot.transform);
        Stretch(Rect(ttGO));
        _tooltipText             = ttGO.AddComponent<TextMeshProUGUI>();
        _tooltipText.fontSize    = 10;
        _tooltipText.color       = Color.white;
        _tooltipText.margin      = new Vector4(8, 8, 8, 8);
        _tooltipText.raycastTarget = false;
        _tooltipRoot.SetActive(false);
    }

    #endregion

    // ─────────────────────────────────────────────
    #region 갱신

    /// <summary>현재 스킬 상태에 맞게 비주얼을 갱신합니다.</summary>
    public void Refresh()
    {
        if (_skill == null) return;

        _currentState = ComputeState();
        ApplyVisuals();

        if (_nameText != null)
            _nameText.text = _skill.skillName;

        if (_iconImage != null && _skill.icon != null)
        {
            _iconImage.sprite  = _skill.icon;
            _iconImage.enabled = true;
        }
    }

    private NodeState ComputeState()
    {
        if (_skillState == null)                                        return NodeState.Locked;
        if (_skillState.IsUnlocked(_skill.skillId))                    return NodeState.Unlocked;
        if (_skillState.IsDisqualifiedForCategory(_skill.category))    return NodeState.Disqualified;
        if (_skillState.CanUnlock(_skill))                             return NodeState.Available;
        return NodeState.Locked;
    }

    private void ApplyVisuals()
    {
        Color bg = _currentState switch
        {
            NodeState.Unlocked      => ColUnlocked,
            NodeState.Available     => ColAvailable,
            NodeState.Disqualified  => ColDisqualified,
            _                       => ColLocked,
        };

        if (_bgImage     != null) _bgImage.color     = bg;
        if (_borderImage != null) _borderImage.color =
            (_currentState == NodeState.Unlocked || _currentState == NodeState.Available)
                ? ColBorderOn : ColBorderOff;

        if (_lockOverlayImage != null)
            _lockOverlayImage.enabled = (_currentState == NodeState.Locked);

        if (_disqualBadgeGO != null)
            _disqualBadgeGO.SetActive(_currentState == NodeState.Disqualified);
    }

    #endregion

    // ─────────────────────────────────────────────
    #region 이벤트 핸들러

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_currentState != NodeState.Available) return;
        _skillState.Unlock(_skill.skillId);
        _onTreeChanged?.Invoke();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_tooltipRoot == null || _skill == null) return;

        string block = _skillState?.GetBlockReason(_skill);
        string body  = string.IsNullOrEmpty(_skill.description) ? "" : _skill.description;
        if (!string.IsNullOrEmpty(block))
            body += $"\n\n<color=#FF7777>{block}</color>";

        if (_tooltipText != null)
            _tooltipText.text = $"<b>{_skill.skillName}</b>\n{body}";

        _tooltipRoot.SetActive(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_tooltipRoot != null) _tooltipRoot.SetActive(false);
    }

    #endregion

    // ─────────────────────────────────────────────
    #region 내부 유틸리티

    private GameObject MakeChild(string childName) => MakeChildIn(childName, transform);

    private static GameObject MakeChildIn(string childName, Transform parent)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    private static RectTransform Rect(GameObject go) => go.GetComponent<RectTransform>();

    private static void Stretch(RectTransform r)
    {
        r.anchorMin    = Vector2.zero;
        r.anchorMax    = Vector2.one;
        r.offsetMin    = Vector2.zero;
        r.offsetMax    = Vector2.zero;
    }

    #endregion
}
