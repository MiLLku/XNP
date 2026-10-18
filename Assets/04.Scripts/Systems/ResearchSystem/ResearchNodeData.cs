using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewResearchNode", menuName = "Research/Research Node")]
public class ResearchNodeData : ScriptableObject
{
    [Header("기본 정보")]
    public string nodeId;
    public string nodeName;
    [TextArea(2, 4)] public string description;
    public Sprite icon;

    [Header("선행 조건")]
    [Tooltip("이 연구를 시작하기 전에 완료되어야 하는 연구 ID 목록")]
    public List<string> prerequisiteNodeIds = new List<string>();

    [Header("연구 비용")]
    [Tooltip("연구 시작 시 즉시 소비되는 자원")]
    public List<ResourceCost> resourceCosts = new List<ResourceCost>();
    [Tooltip("연구 완료까지 필요한 총 연구 포인트")]
    public float researchPointCost = 100f;

    [Header("해금 효과")]
    [Tooltip("연구 완료 시 순서대로 적용되는 효과 목록")]
    public List<ResearchUnlockEffect> unlockEffects = new List<ResearchUnlockEffect>();

    [Header("트리 배치")]
    [Tooltip("연구 트리 UI 내 위치. (0,0)=중앙, ±1 단위=노드 간격")]
    public Vector2 treePosition;
}
