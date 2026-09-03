using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using MessagePipe;

/// <summary>
/// 연구 트리 패널.
///
/// 계층 구조:
///   ResearchTreePanel
///   ├── Header
///   │   ├── TitleLabel
///   │   └── CloseBtn
///   ├── TreeViewport  [Mask]
///   │   └── ContentRoot  ← 드래그 패닝
///   └── Sidebar
///       ├── NodeName
///       ├── NodeDesc
///       ├── CostText
///       ├── EffectsText
///       ├── StartBtn       ← Available 노드 선택 시 활성
///       ├── Divider
///       ├── ActiveLabel
///       ├── ProgressBar    ← Slider 또는 RawImage fill
///       ├── ProgressText
///       └── CancelBtn
///
/// 자동 연결: [ContextMenu "씬 참조 자동 연결"] 또는 Awake 시 이름 기반으로 연결합니다.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ResearchTreePanel : BasePanel, IBeginDragHandler, IDragHandler
{
    // ─── 직렬화 필드 ────────────────────────────────────────────────────────

    [Header("데이터")]
    [SerializeField] private ResearchTreeConfig treeConfig;

    [Header("씬 참조 (인스펙터 연결 or 자동 연결)")]
    [SerializeField] private RectTransform contentRoot;
    [SerializeField] private Button closeButton;

    [Header("사이드바 - 선택 노드 정보")]
    [SerializeField] private TextMeshProUGUI nodeNameText;
    [SerializeField] private TextMeshProUGUI nodeDescText;
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private TextMeshProUGUI effectsText;
    [SerializeField] private Button startButton;
    [SerializeField] private TextMeshProUGUI startButtonText;

    [Header("사이드바 - 진행 중 연구")]
    [SerializeField] private TextMeshProUGUI activeNodeNameText;
    [SerializeField] private Image progressFill;   // ProgressBar/Fill
    [SerializeField] private TextMeshProUGUI progressText;
    [SerializeField] private Button cancelButton;
    [SerializeField] private GameObject noActiveResearchLabel;

    [Header("레이아웃")]
    [SerializeField] private float nodeSpacing = 140f;

    [Header("드래그 패닝")]
    [SerializeField] private float dragSensitivity = 1f;
    [SerializeField] private Vector2 panMin = new Vector2(-1200f, -900f);
    [SerializeField] private Vector2 panMax = new Vector2(1200f, 900f);

    // ─── 내부 상태 ──────────────────────────────────────────────────────────

    private ResearchNodeData _selectedNode;

    /// <summary>연구 진행 메시지 구독 핸들</summary>
    private IDisposable _subscriptions;
    private readonly Dictionary<string, ResearchNodeUI> _nodeMap = new();
    private readonly List<(ResearchNodeData from, ResearchNodeData to, SkillTreeEdgeUI edge)> _edges = new();

    // ─── 초기화 ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        panelType = UIPanelType.ResearchTreeUI;
        AutoWireReferences();
        gameObject.SetActive(false);

        if (closeButton != null)  closeButton.onClick.AddListener(OnClose);
        if (startButton != null)  startButton.onClick.AddListener(OnStartClicked);
        if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelClicked);
    }

    private void OnEnable()
    {
        _subscriptions = DisposableBag.Create(
            GameMessageBus.Subscribe<ResearchStartedMessage>(_ => RefreshAll()),
            GameMessageBus.Subscribe<ResearchCompletedMessage>(_ => RefreshAll()),
            GameMessageBus.Subscribe<ResearchCancelledMessage>(_ => RefreshAll()),
            GameMessageBus.Subscribe<ResearchNodeStateChangedMessage>(_ => RefreshAll()),
            GameMessageBus.Subscribe<ResearchProgressChangedMessage>(m => OnProgressChanged(m.current, m.required)));
    }

    private void OnDisable()
    {
        _subscriptions?.Dispose();
        _subscriptions = null;
    }

    [ContextMenu("씬 참조 자동 연결")]
    private void AutoWireReferences()
    {
        TryWire(ref contentRoot,        "TreeViewport/ContentRoot");
        TryWire(ref closeButton,        "Header/CloseBtn");
        TryWire(ref nodeNameText,       "Sidebar/NodeName");
        TryWire(ref nodeDescText,       "Sidebar/NodeDesc");
        TryWire(ref costText,           "Sidebar/CostText");
        TryWire(ref effectsText,        "Sidebar/EffectsText");
        TryWire(ref startButton,        "Sidebar/StartBtn");
        TryWire(ref startButtonText,    "Sidebar/StartBtn/Text");
        TryWire(ref activeNodeNameText, "Sidebar/Progress/ActiveLabel");
        TryWire(ref progressFill,       "Sidebar/Progress/ProgressBar/Fill");
        TryWire(ref progressText,       "Sidebar/Progress/ProgressText");
        TryWire(ref cancelButton,       "Sidebar/Progress/CancelBtn");
        TryWire(ref noActiveResearchLabel, "Sidebar/Progress/NoActiveLabel");
    }

    // ─── 공개 API ────────────────────────────────────────────────────────────

    public override void OnOpen()
    {
        base.OnOpen();
        BuildTree();
        SelectNode(null);
        RefreshProgressSection();
    }

    public override void OnClose()
    {
        base.OnClose();
    }

    // ─── 트리 빌드 ──────────────────────────────────────────────────────────

    private void BuildTree()
    {
        ClearTree();

        var config = treeConfig ?? ResearchTreeManager.instance?.TreeConfig;
        if (config == null)
        {
            Debug.LogWarning("[ResearchTreePanel] ResearchTreeConfig가 연결되지 않았습니다.");
            return;
        }
        if (contentRoot == null)
        {
            Debug.LogError("[ResearchTreePanel] ContentRoot가 연결되지 않았습니다.");
            return;
        }

        // 엣지 먼저 (노드보다 아래 레이어)
        foreach (var node in config.nodes)
        {
            if (node == null) continue;
            foreach (string prereqId in node.prerequisiteNodeIds)
            {
                var prereq = config.GetNode(prereqId);
                if (prereq == null) continue;
                SpawnEdge(prereq, node);
            }
        }

        // 노드
        foreach (var node in config.nodes)
        {
            if (node == null) continue;
            SpawnNode(node);
        }
    }

    private void SpawnNode(ResearchNodeData node)
    {
        var go = new GameObject($"Node_{node.nodeId}");
        go.transform.SetParent(contentRoot, false);
        go.AddComponent<RectTransform>().anchoredPosition = node.treePosition * nodeSpacing;

        var nodeUI = go.AddComponent<ResearchNodeUI>();
        nodeUI.Initialize(node, OnNodeSelected);
        _nodeMap[node.nodeId] = nodeUI;
    }

    private void SpawnEdge(ResearchNodeData from, ResearchNodeData to)
    {
        var go = new GameObject($"Edge_{from.nodeId}→{to.nodeId}");
        go.transform.SetParent(contentRoot, false);
        go.transform.SetAsFirstSibling();
        go.AddComponent<RectTransform>();
        go.AddComponent<Image>().raycastTarget = false;

        var edge = go.AddComponent<SkillTreeEdgeUI>();
        bool active = ResearchTreeManager.instance != null &&
                      ResearchTreeManager.instance.GetNodeState(from.nodeId) == ResearchNodeState.Completed;
        edge.SetLine(from.treePosition * nodeSpacing, to.treePosition * nodeSpacing, active);

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

    // ─── 노드 선택 ──────────────────────────────────────────────────────────

    private void OnNodeSelected(ResearchNodeData node)
    {
        SelectNode(node);
    }

    private void SelectNode(ResearchNodeData node)
    {
        _selectedNode = node;
        RefreshDetailSection();
    }

    private void RefreshDetailSection()
    {
        if (_selectedNode == null)
        {
            SetText(nodeNameText, "노드를 선택하세요");
            SetText(nodeDescText, "");
            SetText(costText, "");
            SetText(effectsText, "");
            if (startButton != null) startButton.gameObject.SetActive(false);
            return;
        }

        var mgr = ResearchTreeManager.instance;
        var state = mgr != null ? mgr.GetNodeState(_selectedNode.nodeId) : ResearchNodeState.Locked;

        SetText(nodeNameText, _selectedNode.nodeName);
        SetText(nodeDescText, _selectedNode.description);
        SetText(costText, $"연구 포인트: {_selectedNode.researchPointCost:F0}pt");

        // 효과 목록
        if (_selectedNode.unlockEffects.Count > 0)
        {
            var sb = new System.Text.StringBuilder("효과:\n");
            foreach (var effect in _selectedNode.unlockEffects)
                if (effect != null) sb.AppendLine($"  • {effect.GetDescription()}");
            SetText(effectsText, sb.ToString().TrimEnd());
        }
        else
        {
            SetText(effectsText, "효과: (미설정)");
        }

        // 시작 버튼
        if (startButton != null)
        {
            bool canStart = mgr != null && mgr.CanStartResearch(_selectedNode.nodeId);
            startButton.gameObject.SetActive(state == ResearchNodeState.Available || state == ResearchNodeState.Locked);
            startButton.interactable = canStart;

            string btnLabel = state switch
            {
                ResearchNodeState.Completed  => "완료됨",
                ResearchNodeState.InProgress => "연구 중",
                ResearchNodeState.Available  => canStart ? "연구 시작" : (mgr?.IsResearchActive == true ? "다른 연구 진행 중" : "연구 시작"),
                _                            => "잠김",
            };
            SetText(startButtonText, btnLabel);
        }
    }

    // ─── 진행 중 섹션 ────────────────────────────────────────────────────────

    private void RefreshProgressSection()
    {
        var mgr = ResearchTreeManager.instance;
        bool hasActive = mgr != null && mgr.IsResearchActive;
        var activeNode = mgr?.ActiveNode;

        if (noActiveResearchLabel != null)
            noActiveResearchLabel.SetActive(!hasActive);

        if (activeNodeNameText != null)
        {
            activeNodeNameText.gameObject.SetActive(hasActive);
            if (hasActive) activeNodeNameText.text = $"연구 중: {activeNode?.nodeName}";
        }

        if (progressFill != null)
        {
            progressFill.transform.parent.gameObject.SetActive(hasActive);
            if (hasActive && activeNode != null && activeNode.researchPointCost > 0)
                progressFill.fillAmount = mgr.CurrentProgress / activeNode.researchPointCost;
        }

        if (progressText != null)
        {
            progressText.gameObject.SetActive(hasActive);
            if (hasActive && activeNode != null)
            {
                progressText.text = $"{mgr.CurrentProgress:F0} / {activeNode.researchPointCost:F0} pt";
            }
        }

        if (cancelButton != null)
            cancelButton.gameObject.SetActive(hasActive);
    }

    private void OnProgressChanged(float current, float max)
    {
        if (progressFill != null && max > 0)
            progressFill.fillAmount = current / max;

        if (progressText != null)
            progressText.text = $"{current:F0} / {max:F0} pt";

        // 현재 InProgress 노드의 진행바도 갱신
        var mgr = ResearchTreeManager.instance;
        if (mgr != null && !string.IsNullOrEmpty(mgr.ActiveNodeId) &&
            _nodeMap.TryGetValue(mgr.ActiveNodeId, out var nodeUI))
        {
            nodeUI.Refresh();
        }
    }

    // ─── 전체 갱신 ──────────────────────────────────────────────────────────

    private void RefreshAll()
    {
        foreach (var kv in _nodeMap)
            kv.Value.Refresh();

        foreach (var (from, _, edge) in _edges)
        {
            bool active = ResearchTreeManager.instance != null &&
                          ResearchTreeManager.instance.GetNodeState(from.nodeId) == ResearchNodeState.Completed;
            edge.SetActive(active);
        }

        RefreshDetailSection();
        RefreshProgressSection();
    }

    // ─── 버튼 핸들러 ────────────────────────────────────────────────────────

    private void OnStartClicked()
    {
        if (_selectedNode == null) return;
        ResearchTreeManager.instance?.StartResearch(_selectedNode.nodeId);
    }

    private void OnCancelClicked()
    {
        ResearchTreeManager.instance?.CancelResearch();
    }

    // ─── 드래그 패닝 ────────────────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData eventData) { }

    public void OnDrag(PointerEventData eventData)
    {
        if (contentRoot == null) return;
        Vector2 np = contentRoot.anchoredPosition + eventData.delta * dragSensitivity;
        np.x = Mathf.Clamp(np.x, panMin.x, panMax.x);
        np.y = Mathf.Clamp(np.y, panMin.y, panMax.y);
        contentRoot.anchoredPosition = np;
    }

    // ─── 유틸 ───────────────────────────────────────────────────────────────

    private static void SetText(TextMeshProUGUI tmp, string text)
    {
        if (tmp != null) tmp.text = text;
    }

    private void TryWire<T>(ref T field, string path) where T : Component
    {
        if (field != null) return;
        var t = transform.Find(path);
        if (t != null) field = t.GetComponent<T>();
    }

    private void TryWire(ref GameObject field, string path)
    {
        if (field != null) return;
        var t = transform.Find(path);
        if (t != null) field = t.gameObject;
    }
}
