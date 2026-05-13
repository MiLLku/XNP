using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(fileName = "ResearchTreeConfig", menuName = "Research/Research Tree Config")]
public class ResearchTreeConfig : ScriptableObject
{
    [Header("연구 트리 노드 목록")]
    public List<ResearchNodeData> nodes = new List<ResearchNodeData>();

    public ResearchNodeData GetNode(string id)
        => nodes.FirstOrDefault(n => n != null && n.nodeId == id);

    public List<ResearchNodeData> GetPrerequisites(ResearchNodeData node)
    {
        if (node == null) return new List<ResearchNodeData>();
        return node.prerequisiteNodeIds
            .Select(GetNode)
            .Where(n => n != null)
            .ToList();
    }

    public List<ResearchNodeData> GetChildren(ResearchNodeData node)
    {
        if (node == null) return new List<ResearchNodeData>();
        return nodes
            .Where(n => n != null && n.prerequisiteNodeIds.Contains(node.nodeId))
            .ToList();
    }

    public List<ResearchNodeData> GetRoots()
        => nodes.Where(n => n != null && n.prerequisiteNodeIds.Count == 0).ToList();
}
