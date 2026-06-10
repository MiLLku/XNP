using System;
using System.Collections.Generic;

/// <summary>
/// 연구 트리 전체 상태 저장 데이터.
/// ResearchTreeManager의 ISaveModule.Capture/Restore에서 사용합니다.
/// </summary>
[Serializable]
public class ResearchTreeSaveData
{
    /// <summary>각 노드의 상태 목록</summary>
    public List<ResearchNodeSaveEntry> nodeStates = new List<ResearchNodeSaveEntry>();

    /// <summary>현재 진행 중인 연구 노드 ID (없으면 null/empty)</summary>
    public string activeNodeId;

    /// <summary>현재 연구 진행 포인트</summary>
    public float currentProgress;
}

/// <summary>
/// 개별 연구 노드의 저장 데이터.
/// </summary>
[Serializable]
public class ResearchNodeSaveEntry
{
    /// <summary>노드 식별자 (ResearchNodeData.nodeId)</summary>
    public string nodeId;

    /// <summary>노드 상태 (ResearchNodeState를 int로 직렬화)</summary>
    public int state;
}
