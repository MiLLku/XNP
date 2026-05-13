using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 직원 스킬 트리 패널.
///
/// 씬에 미리 구성된 UI 계층구조를 참조합니다 (런타임 생성 없음).
/// 인스펙터에서 contentRoot, employeeNameText, closeButton을 연결하세요.
///
/// 계층 구조:
///   SkillTreePanel   [RectTransform, Image, SkillTreePanel]
///   ├── Header       [Image]
///   │   ├── TitleLabel        [TMP]
///   │   ├── EmployeeNameLabel [TMP]  ← employeeNameText
///   │   └── CloseBtn          [Button] ← closeButton
///   └── TreeViewport [Image, Mask]
///       └── ContentRoot [RectTransform] ← contentRoot  (드래그로 이동)
///           ├── Edge_N  [SkillTreeEdgeUI]
///           └── Node_N  [SkillNodeUI]
///
/// 드래그 패닝:
///   SkillTreePanel에 IBeginDragHandler / IDragHandler가 구현되어 있습니다.
///   SkillNodeUI는 드래그 핸들러를 구현하지 않으므로 이벤트가 이 컴포넌트까지 버블업됩니다.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SkillTreePanel : BasePanel, IBeginDragHandler, IDragHandler
{
    // ─────────────────────────────────────────────
    #region 직렬화 필드

    [Header("스킬 트리 데이터")]
    [Tooltip("SkillSampleCreator 메뉴로 생성한 SkillTreeConfig")]
    [SerializeField] private SkillTreeConfig skillTreeConfig;

    [Header("씬 UI 참조 (인스펙터 연결 필수)")]
    [Tooltip("노드·엣지가 생성되는 컨테이너. 드래그로 이동함")]
    [SerializeField] private RectTransform contentRoot;

    [Tooltip("직원 이름을 표시하는 TMP 텍스트")]
    [SerializeField] private TextMeshProUGUI employeeNameText;

    [Tooltip("패널을 닫는 버튼")]
    [SerializeField] private Button closeButton;

    [Header("레이아웃")]
    [Tooltip("treePosition 1단위 = 이 픽셀 간격")]
    [SerializeField] private float nodeSpacing = 130f;

    [Header("드래그 패닝")]
    [SerializeField] private float dragSensitivity = 1f;
    [SerializeField] private Vector2 panMin = new Vector2(-950f, -750f);
    [SerializeField] private Vector2 panMax = new Vector2( 950f,  750f);

    #endregion

    // ─────────────────────────────────────────────
    #region 내부 상태

    private Employee           _employee;
    private EmployeeSkillState _skillState;

    private readonly Dictionary<int, SkillNodeUI> _nodeMap
        = new Dictionary<int, SkillNodeUI>();

    private readonly List<(SkillData from, SkillData to, SkillTreeEdgeUI edge)> _edges
        = new List<(SkillData, SkillData, SkillTreeEdgeUI)>();

    #endregion

    // ─────────────────────────────────────────────
    #region Unity 생명주기

    private void Awake()
    {
        panelType = UIPanelType.SkillTreeUI;

        // 인스펙터 연결이 없으면 계층 이름으로 자동 탐색
        AutoWireReferences();

        gameObject.SetActive(false);

        if (closeButton != null)
            closeButton.onClick.AddListener(OnClose);
    }

    /// <summary>
    /// [SerializeField] 참조가 비어 있을 때 자식 이름으로 자동 연결합니다.
    /// 우클릭 메뉴에서도 수동 실행 가능합니다.
    /// </summary>
    [ContextMenu("씬 참조 자동 연결")]
    private void AutoWireReferences()
    {
        if (contentRoot == null)
        {
            var t = transform.Find("TreeViewport/ContentRoot");
            if (t != null) contentRoot = t as RectTransform ?? t.GetComponent<RectTransform>();
        }
        if (employeeNameText == null)
        {
            var t = transform.Find("Header/EmployeeNameLabel");
            if (t != null) employeeNameText = t.GetComponent<TextMeshProUGUI>();
        }
        if (closeButton == null)
        {
            var t = transform.Find("Header/CloseBtn");
            if (t != null) closeButton = t.GetComponent<Button>();
        }
    }

    #endregion

    // ─────────────────────────────────────────────
    #region 공개 API

    /// <summary>직원과 연결하고 패널을 엽니다.</summary>
    public void Setup(Employee employee)
    {
        _employee   = employee;
        _skillState = employee != null ? employee.GetComponent<EmployeeSkillState>() : null;

        var cfg = _skillState?.Config ?? skillTreeConfig;

        if (employeeNameText != null)
            employeeNameText.text = employee != null ? employee.DisplayName : "—";

        BuildTree(cfg);
        OnOpen();
    }

    public override void OnOpen()
    {
        base.OnOpen();
        RefreshAll();
    }

    public override void OnClose()
    {
        base.OnClose();
    }

    /// <summary>
    /// 직원 없이 SkillTreeConfig 기반으로 패널을 미리보기 엽니다.
    /// 에디터 테스트용.
    /// </summary>
    public void OpenPreview()
    {
        _employee   = null;
        _skillState = null;

        if (employeeNameText != null)
            employeeNameText.text = "[미리보기]";

        BuildTree(skillTreeConfig);
        OnOpen();
    }

    #endregion

    // ─────────────────────────────────────────────
    #region 트리 빌드

    private void BuildTree(SkillTreeConfig config)
    {
        ClearTree();

        if (config == null)
        {
            Debug.LogWarning("[SkillTreePanel] SkillTreeConfig가 연결되지 않았습니다.");
            return;
        }
        if (contentRoot == null)
        {
            Debug.LogError("[SkillTreePanel] contentRoot가 연결되지 않았습니다. 인스펙터를 확인하세요.");
            return;
        }

        // 1단계: 엣지 (먼저 생성 → SetAsFirstSibling으로 노드 아래 위치)
        foreach (var skill in config.skills)
        {
            if (skill == null) continue;
            foreach (int prereqId in skill.prerequisiteSkillIds)
            {
                var prereq = config.GetSkill(prereqId);
                if (prereq == null) continue;
                SpawnEdge(prereq, skill);
            }
        }

        // 2단계: 노드
        foreach (var skill in config.skills)
        {
            if (skill == null) continue;
            SpawnNode(skill);
        }
    }

    private void SpawnNode(SkillData skill)
    {
        var go        = new GameObject($"Node_{skill.skillId}_{skill.skillName}");
        go.transform.SetParent(contentRoot, false);
        go.AddComponent<RectTransform>().anchoredPosition = skill.treePosition * nodeSpacing;

        var nodeUI = go.AddComponent<SkillNodeUI>();
        nodeUI.Initialize(skill, _skillState, RefreshAll);

        _nodeMap[skill.skillId] = nodeUI;
    }

    private void SpawnEdge(SkillData from, SkillData to)
    {
        var go = new GameObject($"Edge_{from.skillId}→{to.skillId}");
        go.transform.SetParent(contentRoot, false);
        go.transform.SetAsFirstSibling();
        go.AddComponent<RectTransform>();
        go.AddComponent<Image>().raycastTarget = false;

        var edge    = go.AddComponent<SkillTreeEdgeUI>();
        Vector2 fp  = from.treePosition * nodeSpacing;
        Vector2 tp  = to.treePosition   * nodeSpacing;
        bool active = _skillState != null && _skillState.IsUnlocked(from.skillId);
        edge.SetLine(fp, tp, active);

        _edges.Add((from, to, edge));
    }

    private void ClearTree()
    {
        _nodeMap.Clear();
        _edges.Clear();

        if (contentRoot == null) return;
        for (int i = contentRoot.childCount - 1; i >= 0; i--)
            Destroy(contentRoot.GetChild(i).gameObject);
    }

    #endregion

    // ─────────────────────────────────────────────
    #region 갱신

    /// <summary>노드 해제 후 콜백으로 호출 — 모든 노드·엣지 비주얼 갱신.</summary>
    public void RefreshAll()
    {
        foreach (var kv in _nodeMap)
            kv.Value.Refresh();

        foreach (var (from, _, edge) in _edges)
        {
            bool active = _skillState != null && _skillState.IsUnlocked(from.skillId);
            edge.SetActive(active);
        }
    }

    #endregion

    // ─────────────────────────────────────────────
    #region 드래그 패닝

    public void OnBeginDrag(PointerEventData eventData) { }

    public void OnDrag(PointerEventData eventData)
    {
        if (contentRoot == null) return;

        Vector2 np = contentRoot.anchoredPosition + eventData.delta * dragSensitivity;
        np.x = Mathf.Clamp(np.x, panMin.x, panMax.x);
        np.y = Mathf.Clamp(np.y, panMin.y, panMax.y);
        contentRoot.anchoredPosition = np;
    }

    #endregion
}
