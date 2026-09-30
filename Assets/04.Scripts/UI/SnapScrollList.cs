using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 스냅 스크롤 목록 — 휠 한 칸·화살표 한 번에 항목 하나씩, 항목 경계에 딱 맞게 멈춥니다.
///
/// ScrollRect와 같은 오브젝트에 붙입니다. ScrollRect의 자체 휠 스크롤은 끄고(scrollSensitivity 0)
/// 이 컴포넌트가 항목 단위로 옮깁니다. 드래그로 옮기면 놓는 순간 가장 가까운 항목으로 붙습니다.
///
/// 위·아래 화살표는 그 방향으로 더 갈 수 있으면 흰색, 끝이면 회색입니다.
/// 항목 높이는 콘텐츠의 첫 활성 자식 높이 + VerticalLayoutGroup 간격으로 계산합니다 (항목 높이가 같다고 가정).
/// </summary>
[RequireComponent(typeof(ScrollRect))]
public class SnapScrollList : MonoBehaviour, IScrollHandler, IBeginDragHandler, IEndDragHandler
{
    [Header("화살표 (선택)")]
    [SerializeField] private Button upButton;
    [SerializeField] private Button downButton;

    [SerializeField] private Color enabledColor = Color.white;
    [SerializeField] private Color disabledColor = new Color(0.45f, 0.45f, 0.48f, 1f);

    [Tooltip("스냅 이동 속도 (클수록 빠름)")]
    [SerializeField, Min(1f)] private float snapSpeed = 18f;

    private ScrollRect scroll;
    private int index;
    private bool dragging;

    private RectTransform Content => scroll.content;
    private RectTransform Viewport => scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;

    private void Awake()
    {
        scroll = GetComponent<ScrollRect>();
        scroll.scrollSensitivity = 0f;   // 휠은 여기서 항목 단위로 처리
        scroll.inertia = false;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        // 색은 여기서 직접 칠한다 — 버튼 기본 비활성 틴트가 곱해지면 회색이 두 번 어두워진다
        if (upButton != null) { upButton.transition = Selectable.Transition.None; upButton.onClick.AddListener(() => Step(-1)); }
        if (downButton != null) { downButton.transition = Selectable.Transition.None; downButton.onClick.AddListener(() => Step(+1)); }
    }

    /// <summary>한 항목의 세로 간격 (항목 높이 + 간격)</summary>
    private float StepHeight
    {
        get
        {
            float spacing = Content.TryGetComponent(out VerticalLayoutGroup v) ? v.spacing : 0f;
            for (int i = 0; i < Content.childCount; i++)
            {
                var c = (RectTransform)Content.GetChild(i);
                if (c.gameObject.activeSelf) return Mathf.Max(1f, c.rect.height + spacing);
            }
            return 1f;
        }
    }

    /// <summary>보이는 영역 밖으로 넘친 높이</summary>
    private float Overflow => Mathf.Max(0f, Content.rect.height - Viewport.rect.height);

    private int MaxIndex => Mathf.CeilToInt(Overflow / StepHeight - 0.01f);

    /// <summary>항목 수만큼 이동합니다 (음수 = 위).</summary>
    public void Step(int delta) => index = Mathf.Clamp(index + delta, 0, MaxIndex);

    /// <summary>특정 항목이 맨 위에 오도록 이동합니다.</summary>
    public void ScrollTo(int itemIndex) => index = Mathf.Clamp(itemIndex, 0, MaxIndex);

    public void OnScroll(PointerEventData e)
    {
        if (Mathf.Abs(e.scrollDelta.y) > 0.01f) Step(e.scrollDelta.y > 0 ? -1 : +1);
    }

    public void OnBeginDrag(PointerEventData e) => dragging = true;

    public void OnEndDrag(PointerEventData e)
    {
        dragging = false;
        index = Mathf.Clamp(Mathf.RoundToInt(Content.anchoredPosition.y / StepHeight), 0, MaxIndex);
    }

    private void LateUpdate()
    {
        int max = MaxIndex;
        if (index > max) index = max;

        if (!dragging)
        {
            var pos = Content.anchoredPosition;
            float target = Mathf.Min(index * StepHeight, Overflow);
            pos.y = Mathf.Abs(pos.y - target) < 0.5f ? target : Mathf.Lerp(pos.y, target, 1f - Mathf.Exp(-snapSpeed * Time.unscaledDeltaTime));
            Content.anchoredPosition = pos;
        }

        SetArrow(upButton, index > 0);
        SetArrow(downButton, index < max);
    }

    private void SetArrow(Button button, bool canMove)
    {
        if (button == null) return;
        button.interactable = canMove;
        if (button.targetGraphic != null) button.targetGraphic.color = canMove ? enabledColor : disabledColor;
    }
}
