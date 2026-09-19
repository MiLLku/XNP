using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 직원 작업 우선순위 바 — 박스를 드래그해 순서를 정합니다.
///
///   ◄ 우선순위 높음                        낮음 ►  │  작업 하지 않음
///   [요양][채광][벌목][제작][원예][건설][운반]      │  [세척][연구][철거]
///
/// 왼쪽 줄에서의 <b>위치가 곧 우선순위</b>입니다 (왼쪽일수록 먼저 함).
/// 오른쪽 칸에 넣은 작업은 하지 않습니다. 직원이 타고난 결격 작업은
/// 처음부터 오른쪽 칸에 다른 색으로 들어가 있고 드래그할 수 없습니다.
///
/// 줄바꿈은 하지 않습니다 — 작업 종류가 늘면 박스를 접지 말고 패널 폭을 키우세요.
///
/// 프리팹 배선:
///   activeRow / disabledRow — HorizontalLayoutGroup을 붙인 빈 RectTransform
///   boxTemplate             — Image + 자식 TMP_Text 를 가진 비활성 오브젝트
/// </summary>
public class WorkPriorityBar : MonoBehaviour
{
    [Header("줄")]
    [Tooltip("우선순위 줄 (왼쪽 = 먼저). HorizontalLayoutGroup 필요")]
    [SerializeField] private RectTransform activeRow;

    [Tooltip("작업 하지 않음 칸. HorizontalLayoutGroup 필요")]
    [SerializeField] private RectTransform disabledRow;

    [Header("박스 템플릿 (비활성)")]
    [Tooltip("Image + 자식 TMP_Text")]
    [SerializeField] private RectTransform boxTemplate;

    [Header("색")]
    [SerializeField] private Color normalColor  = new Color(0.12f, 0.35f, 0.47f, 1f);
    [Tooltip("결격 — 옮길 수 없는 박스")]
    [SerializeField] private Color blockedColor = new Color(0.90f, 0.49f, 0.19f, 1f);

    [Header("방향 라벨 (선택)")]
    [SerializeField] private TMP_Text leftLabel;
    [SerializeField] private TMP_Text rightLabel;

    private Employee employee;
    private EmployeeWork work;
    private readonly List<GameObject> boxes = new List<GameObject>();

    // 드래그 상태
    private RectTransform dragging;
    private RectTransform placeholder;
    private CanvasGroup draggingGroup;
    private LayoutElement draggingElement;

    private void Awake()
    {
        // 좌우 방향은 보는 사람이 헷갈리는 지점이라 라벨 문구를 코드에서 박는다
        if (leftLabel  != null) leftLabel.text  = "◄ 우선순위 높음";
        if (rightLabel != null) rightLabel.text = "우선순위 낮음 ►";
    }

    /// <summary>드래그 도중 패널이 닫혀도 임시 오브젝트가 남지 않게 정리합니다.</summary>
    private void OnDisable() => CancelDrag();

    #region 그리기

    /// <summary>이 직원의 작업 순서를 다시 그립니다. 드래그 중에는 호출하지 마세요.</summary>
    public void Show(Employee emp)
    {
        employee = emp;
        work     = emp != null ? emp.GetComponent<EmployeeWork>() : null;

        CancelDrag();
        Clear();

        if (work == null || boxTemplate == null || activeRow == null || disabledRow == null) return;

        // 결격은 '작업 하지 않음' 칸에서도 항상 맨 오른쪽 — 그래서 두 번 돈다.
        // 플레이어가 끈 작업(옮길 수 있는 것)이 왼쪽에 모여 있어야 다시 꺼내기 쉽다
        foreach (WorkType type in work.GetWorkTypesInOrder())
        {
            if (work.IsPermanentlyBlocked(type)) continue;
            CreateBox(type, work.IsWorkEnabled(type) ? activeRow : disabledRow, false);
        }

        foreach (WorkType type in work.GetWorkTypesInOrder())
        {
            if (work.IsPermanentlyBlocked(type)) CreateBox(type, disabledRow, true);
        }
    }

    /// <summary>
    /// 결격 박스를 '작업 하지 않음' 칸 맨 오른쪽으로 다시 밀어냅니다.
    ///
    /// 결격은 못 옮기지만 <b>다른 박스를 결격 뒤에 떨어뜨릴 수는</b> 있으므로,
    /// 드롭이 끝날 때마다 순서를 되돌립니다.
    /// </summary>
    private void PinBlockedRight()
    {
        if (disabledRow == null) return;

        // 자식을 돌며 옮기므로 먼저 목록을 뜬다 (순회 중 SetAsLastSibling은 순서를 흔든다)
        var blocked = new List<Transform>();
        foreach (RectTransform child in disabledRow)
        {
            var box = child.GetComponent<WorkPriorityBox>();
            if (box != null && box.Blocked) blocked.Add(child);
        }

        foreach (var child in blocked) child.SetAsLastSibling();
    }

    private void CreateBox(WorkType type, RectTransform row, bool blocked)
    {
        var box = Instantiate(boxTemplate, row);
        box.gameObject.SetActive(true);

        var image = box.GetComponent<Image>();
        if (image != null) image.color = blocked ? blockedColor : normalColor;

        var label = box.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = WorkTypeCategory.GetDisplayName(type);

        box.gameObject.AddComponent<WorkPriorityBox>().Init(this, type, blocked);
        boxes.Add(box.gameObject);
    }

    /// <summary>
    /// 박스를 치웁니다. Destroy는 프레임 끝에야 실제로 지워지므로 그 사이의
    /// 드래그 입력을 막기 위해 먼저 비활성화합니다
    /// (EmployeeManagePanel.ClearPoolRows와 같은 이유).
    /// </summary>
    private void Clear()
    {
        foreach (var go in boxes)
        {
            if (go == null) continue;
            go.SetActive(false);
            Destroy(go);
        }
        boxes.Clear();
    }

    #endregion

    #region 드래그

    internal void BeginDrag(WorkPriorityBox box)
    {
        if (box == null || dragging != null) return;

        dragging = (RectTransform)box.transform;

        // 끌려다니는 동안 자리를 대신할 빈 칸 — 이게 없으면 어디에 떨어질지 보이지 않는다
        placeholder = new GameObject("_slot", typeof(RectTransform), typeof(LayoutElement))
            .GetComponent<RectTransform>();
        var element = placeholder.GetComponent<LayoutElement>();
        element.preferredWidth  = dragging.rect.width;
        element.preferredHeight = dragging.rect.height;
        placeholder.SetParent(dragging.parent, false);
        placeholder.SetSiblingIndex(dragging.GetSiblingIndex());

        // 포인터가 박스 자신이 아니라 아래의 줄에 닿아야 드롭 위치를 계산할 수 있다
        draggingGroup = dragging.GetComponent<CanvasGroup>();
        if (draggingGroup == null) draggingGroup = dragging.gameObject.AddComponent<CanvasGroup>();
        draggingGroup.blocksRaycasts = false;

        // 바 루트가 레이아웃 그룹을 갖고 있어도 끌리는 박스는 제자리에 박히면 안 된다
        draggingElement = dragging.GetComponent<LayoutElement>();
        if (draggingElement == null) draggingElement = dragging.gameObject.AddComponent<LayoutElement>();
        draggingElement.ignoreLayout = true;

        dragging.SetParent(transform, true);   // 다른 박스들 위로 올린다
        dragging.SetAsLastSibling();
    }

    internal void Drag(PointerEventData eventData)
    {
        if (dragging == null || placeholder == null) return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)transform, eventData.position, eventData.pressEventCamera,
                out Vector2 local))
            dragging.localPosition = local;

        RectTransform row = RectTransformUtility.RectangleContainsScreenPoint(
            disabledRow, eventData.position, eventData.pressEventCamera) ? disabledRow : activeRow;

        placeholder.SetParent(row, false);
        placeholder.SetSiblingIndex(DropIndex(row, eventData));
    }

    internal void EndDrag()
    {
        if (dragging == null || placeholder == null) { CancelDrag(); return; }

        if (draggingElement != null) draggingElement.ignoreLayout = false;

        dragging.SetParent(placeholder.parent, false);
        dragging.SetSiblingIndex(placeholder.GetSiblingIndex());
        dragging.localScale = Vector3.one;

        Destroy(placeholder.gameObject);
        placeholder = null;

        if (draggingGroup != null) draggingGroup.blocksRaycasts = true;
        draggingGroup   = null;
        draggingElement = null;
        dragging        = null;

        PinBlockedRight();
        Commit();
    }

    /// <summary>포인터보다 왼쪽에 있는 박스의 수 = 끼워 넣을 자리.</summary>
    private int DropIndex(RectTransform row, PointerEventData eventData)
    {
        int index = 0;
        int movable = 0;   // 결격 앞자리까지만 들어갈 수 있다

        foreach (RectTransform child in row)
        {
            if (child == placeholder) continue;

            var box = child.GetComponent<WorkPriorityBox>();
            if (box == null || !box.Blocked) movable++;

            Vector2 screen = RectTransformUtility.WorldToScreenPoint(
                eventData.pressEventCamera, child.position);
            if (screen.x < eventData.position.x) index++;
        }

        // 결격 뒤로는 미리보기도 가지 않게 막는다 (어차피 드롭하면 앞으로 되돌아온다)
        return Mathf.Min(index, movable);
    }

    /// <summary>드래그 상태만 털어냅니다 (박스 자체는 Clear가 지웁니다).</summary>
    private void CancelDrag()
    {
        if (placeholder != null) Destroy(placeholder.gameObject);
        if (draggingGroup != null) draggingGroup.blocksRaycasts = true;
        if (draggingElement != null) draggingElement.ignoreLayout = false;

        placeholder     = null;
        dragging        = null;
        draggingGroup   = null;
        draggingElement = null;
    }

    /// <summary>지금 화면에 놓인 순서를 직원에게 씁니다.</summary>
    private void Commit()
    {
        if (work == null || activeRow == null) return;

        var active = new List<WorkType>();
        foreach (RectTransform child in activeRow)
        {
            var box = child.GetComponent<WorkPriorityBox>();
            if (box != null) active.Add(box.Type);
        }

        work.SetWorkOrder(active);

        // 설정이 바뀌면 지금 하던 일을 다시 판단해야 한다 (구역 변경과 같은 이유)
        if (employee != null) employee.GetComponent<EmployeeAI>()?.ForceReevaluate();
    }

    #endregion
}

/// <summary>
/// 박스 한 개의 드래그 핸들. WorkPriorityBar가 런타임에 붙이므로
/// 인스펙터의 Add Component 목록에는 나오지 않습니다.
/// </summary>
public class WorkPriorityBox : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private WorkPriorityBar bar;

    public WorkType Type { get; private set; }

    /// <summary>결격 — 옮길 수 없는 박스</summary>
    public bool Blocked { get; private set; }

    public void Init(WorkPriorityBar owner, WorkType type, bool blocked)
    {
        bar     = owner;
        Type    = type;
        Blocked = blocked;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!Blocked) bar?.BeginDrag(this);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!Blocked) bar?.Drag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!Blocked) bar?.EndDrag();
    }
}
