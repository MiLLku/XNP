using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 한 칸 단위로 끊어지는 스크롤.
///
/// 드래그를 놓거나 휠을 굴린 뒤, 내용물 위치를 <see cref="step"/>의 배수로 맞춥니다 —
/// 행이 반쯤 잘린 채로 멈추지 않게 하기 위한 것입니다.
///
/// 붙이는 곳: ScrollRect와 같은 오브젝트. 관성(inertia)은 꺼두는 편이 자연스럽습니다.
/// </summary>
[RequireComponent(typeof(ScrollRect))]
public class SnapScrollRect : MonoBehaviour, IBeginDragHandler, IEndDragHandler, IScrollHandler
{
    [Tooltip("한 칸 높이. 0이면 첫 행 높이에서 자동으로 잡습니다 (행 높이가 내용에 따라 달라지는 목록)")]
    [SerializeField] private float step = 0f;

    private ScrollRect scroll;
    private bool dragging;
    private bool snapQueued;

    /// <summary>행 높이가 런타임에 달라지는 목록(구역 줄바꿈 등)에서 갱신합니다.</summary>
    public float Step { get { return step; } set { step = value; } }

    private void Awake() => scroll = GetComponent<ScrollRect>();

    public void OnBeginDrag(PointerEventData eventData) => dragging = true;

    public void OnEndDrag(PointerEventData eventData)
    {
        dragging = false;
        snapQueued = true;
    }

    // 휠은 ScrollRect가 같은 프레임에 먼저 처리할 수도, 나중에 할 수도 있으므로
    // 그 자리에서 맞추지 않고 다음 LateUpdate로 미룬다
    public void OnScroll(PointerEventData eventData) => snapQueued = true;

    private void LateUpdate()
    {
        if (!snapQueued || dragging) return;
        snapQueued = false;
        Snap();
    }

    private void Snap()
    {
        if (scroll == null || scroll.content == null) return;

        float unit = CurrentStep();
        if (unit <= 0f) return;

        var pos = scroll.content.anchoredPosition;
        pos.y = Mathf.Round(pos.y / unit) * unit;
        scroll.content.anchoredPosition = pos;
        scroll.velocity = Vector2.zero;
    }

    /// <summary>
    /// 스냅 간격. step을 비워두면 첫 행에서 뽑습니다 —
    /// 구역이 늘어 칸이 줄바꿈되면 행 높이 자체가 달라지기 때문입니다.
    /// </summary>
    private float CurrentStep()
    {
        if (step > 0f) return step;

        var group = scroll.content.GetComponent<VerticalLayoutGroup>();
        float spacing = group != null ? group.spacing : 0f;

        foreach (RectTransform child in scroll.content)
        {
            if (!child.gameObject.activeSelf) continue;
            if (child.rect.height > 0f) return child.rect.height + spacing;
        }

        return 0f;
    }
}
