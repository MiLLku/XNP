using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 연구 트리 전체 상태를 관리하는 싱글톤.
///
/// 흐름:
///   1. StartResearch(nodeId) → 선행 연구 + 자원 검사 → 자원 즉시 소비 → InProgress
///   2. ResearchWorkbench.OnResearchTick() → AddProgress(points) → 포인트 누적
///   3. 목표 포인트 달성 → CompleteResearch() → 효과 적용 → 자식 노드 Available 전환
/// </summary>
public class ResearchTreeManager : DestroySingleton<ResearchTreeManager>, ISaveModule
{
    [Header("설정")]
    [SerializeField] private ResearchTreeConfig treeConfig;

    // ─── 런타임 상태 ───────────────────────────────────────────────────────────

    private Dictionary<string, ResearchNodeState> _nodeStates = new();
    private string _activeNodeId;
    private float _currentProgress;

    private HashSet<int> _unlockedBuildingIds = new();
    private HashSet<int> _unlockedRecipeIds = new();
    private Dictionary<ResearchStatType, float> _statBonuses = new();

    // ─── 프로퍼티 ──────────────────────────────────────────────────────────────

    public ResearchTreeConfig TreeConfig => treeConfig;
    public string ActiveNodeId => _activeNodeId;
    public float CurrentProgress => _currentProgress;
    public ResearchNodeData ActiveNode => treeConfig?.GetNode(_activeNodeId);
    public bool IsResearchActive => !string.IsNullOrEmpty(_activeNodeId);

    // ─── 초기화 ────────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        InitializeNodeStates();
    }

    private void InitializeNodeStates()
    {
        if (treeConfig == null) return;
        foreach (var node in treeConfig.nodes)
        {
            if (node == null || _nodeStates.ContainsKey(node.nodeId)) continue;
            _nodeStates[node.nodeId] = node.prerequisiteNodeIds.Count == 0
                ? ResearchNodeState.Available
                : ResearchNodeState.Locked;
        }
    }

    // ─── 상태 조회 ─────────────────────────────────────────────────────────────

    public ResearchNodeState GetNodeState(string nodeId)
        => _nodeStates.TryGetValue(nodeId, out var state) ? state : ResearchNodeState.Locked;

    public bool CanStartResearch(string nodeId)
    {
        if (treeConfig == null || IsResearchActive) return false;

        var node = treeConfig.GetNode(nodeId);
        if (node == null) return false;
        if (GetNodeState(nodeId) != ResearchNodeState.Available) return false;

        if (node.prerequisiteNodeIds.Any(id => GetNodeState(id) != ResearchNodeState.Completed))
            return false;

        if (node.resourceCosts.Count > 0 &&
            (InventoryManager.instance == null || !InventoryManager.instance.HasItems(node.resourceCosts)))
            return false;

        return true;
    }

    // ─── 연구 제어 ─────────────────────────────────────────────────────────────

    /// <summary>연구를 시작합니다. 자원을 즉시 소비하고 InProgress 상태로 전환합니다.</summary>
    public bool StartResearch(string nodeId)
    {
        if (!CanStartResearch(nodeId))
        {
            Debug.LogWarning($"[ResearchTreeManager] 연구 시작 불가: {nodeId}");
            return false;
        }

        var node = treeConfig.GetNode(nodeId);

        if (node.resourceCosts.Count > 0 && !InventoryManager.instance.RemoveItems(node.resourceCosts))
        {
            Debug.LogWarning($"[ResearchTreeManager] 자원 소비 실패: {node.nodeName}");
            return false;
        }

        _activeNodeId = nodeId;
        _currentProgress = 0f;
        SetNodeState(nodeId, ResearchNodeState.InProgress);

        GameMessageBus.Publish(new ResearchStartedMessage(node));
        Debug.Log($"[ResearchTreeManager] 연구 시작: {node.nodeName} (목표: {node.researchPointCost}pt)");
        return true;
    }

    /// <summary>ResearchWorkbench 틱에서 호출됩니다. 활성 연구에 포인트를 누적합니다.</summary>
    public void AddProgress(float points)
    {
        if (!IsResearchActive || treeConfig == null) return;

        var node = treeConfig.GetNode(_activeNodeId);
        if (node == null) return;

        _currentProgress += points;
        GameMessageBus.Publish(new ResearchProgressChangedMessage(_currentProgress, node.researchPointCost));

        if (_currentProgress >= node.researchPointCost)
            CompleteResearch(node);
    }

    /// <summary>진행 중인 연구를 취소합니다. 소비된 자원은 반환되지 않습니다.</summary>
    public void CancelResearch()
    {
        if (!IsResearchActive) return;

        var node = treeConfig.GetNode(_activeNodeId);
        if (node == null) return;

        SetNodeState(_activeNodeId, ResearchNodeState.Available);
        _activeNodeId = null;
        _currentProgress = 0f;

        GameMessageBus.Publish(new ResearchCancelledMessage(node));
        Debug.Log($"[ResearchTreeManager] 연구 취소: {node?.nodeName}");
    }

    private void CompleteResearch(ResearchNodeData node)
    {
        SetNodeState(node.nodeId, ResearchNodeState.Completed);
        _activeNodeId = null;
        _currentProgress = 0f;

        foreach (var effect in node.unlockEffects)
            effect?.Apply();

        foreach (var child in treeConfig.GetChildren(node))
        {
            if (GetNodeState(child.nodeId) == ResearchNodeState.Locked && ArePrerequisitesMet(child))
                SetNodeState(child.nodeId, ResearchNodeState.Available);
        }

        GameMessageBus.Publish(new ResearchCompletedMessage(node));
        Debug.Log($"[ResearchTreeManager] 연구 완료: {node.nodeName}");
    }

    private bool ArePrerequisitesMet(ResearchNodeData node)
        => node.prerequisiteNodeIds.All(id => GetNodeState(id) == ResearchNodeState.Completed);

    private void SetNodeState(string nodeId, ResearchNodeState state)
    {
        _nodeStates[nodeId] = state;
        GameMessageBus.Publish(new ResearchNodeStateChangedMessage(nodeId, state));
    }

    // ─── Effect 콜백 ───────────────────────────────────────────────────────────

    public void RegisterBuildingUnlock(BuildingData building)
    {
        if (building == null) return;
        _unlockedBuildingIds.Add(building.buildingID);
        GameMessageBus.Publish(new BuildingUnlockedMessage(building));
        Debug.Log($"[ResearchTreeManager] 건물 해금: {building.buildingName}");
    }

    public void RegisterRecipeUnlock(CraftingRecipe recipe)
    {
        if (recipe == null) return;
        _unlockedRecipeIds.Add(recipe.recipeID);
        GameMessageBus.Publish(new RecipeUnlockedMessage(recipe));
        Debug.Log($"[ResearchTreeManager] 레시피 해금: {recipe.outputItem?.itemName}");
    }

    public void ApplyStatBonus(ResearchStatType statType, float value)
    {
        _statBonuses.TryGetValue(statType, out float current);
        _statBonuses[statType] = current + value;
        Debug.Log($"[ResearchTreeManager] 스탯 보너스: {statType} +{value} (누적: {_statBonuses[statType]})");
    }

    // ─── 외부 조회 ─────────────────────────────────────────────────────────────

    public bool IsBuildingUnlocked(BuildingData building)
        => building != null && _unlockedBuildingIds.Contains(building.buildingID);

    public bool IsRecipeUnlocked(CraftingRecipe recipe)
        => recipe != null && _unlockedRecipeIds.Contains(recipe.recipeID);

    public float GetStatBonus(ResearchStatType statType)
        => _statBonuses.TryGetValue(statType, out float val) ? val : 0f;

    // ─── ISaveModule ────────────────────────────────────────────────────────────

    public int SaveOrder => 85;

    public void Capture(SaveData data)
    {
        var save = new ResearchTreeSaveData
        {
            activeNodeId    = _activeNodeId,
            currentProgress = _currentProgress
        };

        foreach (var kv in _nodeStates)
        {
            save.nodeStates.Add(new ResearchNodeSaveEntry
            {
                nodeId = kv.Key,
                state  = (int)kv.Value
            });
        }

        data.researchTree = save;
    }

    public void Restore(SaveData data)
    {
        // 파생 상태 초기화
        _nodeStates.Clear();
        _unlockedBuildingIds.Clear();
        _unlockedRecipeIds.Clear();
        _statBonuses.Clear();
        _activeNodeId    = null;
        _currentProgress = 0f;

        // 저장 데이터 없으면 초기 상태로 시작
        if (data.researchTree == null)
        {
            InitializeNodeStates();
            return;
        }

        var save = data.researchTree;

        // 노드 상태 복원
        foreach (var entry in save.nodeStates)
        {
            if (!string.IsNullOrEmpty(entry.nodeId))
                _nodeStates[entry.nodeId] = (ResearchNodeState)entry.state;
        }

        // 저장에 없는 노드(새 콘텐츠 추가 등)는 초기 상태로 보완
        if (treeConfig != null)
        {
            foreach (var node in treeConfig.nodes)
            {
                if (node == null || _nodeStates.ContainsKey(node.nodeId)) continue;
                _nodeStates[node.nodeId] = node.prerequisiteNodeIds.Count == 0
                    ? ResearchNodeState.Available
                    : ResearchNodeState.Locked;
            }

            // 완료된 노드의 해금 효과 재적용 → _unlockedBuildingIds 등 파생 상태 재건
            foreach (var node in treeConfig.nodes)
            {
                if (node == null) continue;
                if (GetNodeState(node.nodeId) != ResearchNodeState.Completed) continue;

                foreach (var effect in node.unlockEffects)
                    effect?.Apply();
            }
        }

        // 진행 중 연구 복원
        _activeNodeId    = save.activeNodeId;
        _currentProgress = save.currentProgress;

        Debug.Log($"[ResearchTreeManager] 연구 트리 복원 완료 " +
                  $"(노드 {_nodeStates.Count}개, 진행 중: {_activeNodeId ?? "없음"})");
    }

    public void PostRestore(SaveData data) { }
}
