using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전력 시스템 중앙 매니저.
/// 발전기·축전기·소비건물·전선을 등록받아, 4방향 인접 기반으로 전력망(PowerNetwork)을
/// 재구성하고 매 틱 전력을 분배합니다.
///
/// 전력망 그래프는 세이브하지 않고, 로드 후 위치 기반으로 재계산합니다(_dirty).
/// </summary>
public class PowerManager : DestroySingleton<PowerManager>
{
    private static readonly Vector2Int[] Dirs4 =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    [Tooltip("전력 분배 틱 간격(초).")]
    [SerializeField] private float tickInterval = 0.2f;

    private readonly List<PowerProducer> _producers = new List<PowerProducer>();
    private readonly List<PowerBattery> _batteries = new List<PowerBattery>();
    private readonly List<PowerConsumer> _consumers = new List<PowerConsumer>();

    private readonly List<PowerNetwork> _networks = new List<PowerNetwork>();
    private bool _dirty = true;
    private float _tickTimer = 0f;

    public IReadOnlyList<PowerNetwork> Networks => _networks;

    #region 생명주기

    void Start()
    {
        // 매니저보다 먼저 생성된 노드들을 수집 (등록 누락 방지)
        CollectExistingNodes();
        _dirty = true;
    }

    void Update()
    {
        if (_dirty)
        {
            RebuildNetworks();
            _dirty = false;
        }

        _tickTimer += Time.deltaTime;
        if (_tickTimer >= tickInterval)
        {
            foreach (var net in _networks) net.Tick(_tickTimer);
            _tickTimer = 0f;
        }
    }

    #endregion

    #region 등록/해제

    public void RegisterProducer(PowerProducer p) { if (p != null && !_producers.Contains(p)) { _producers.Add(p); _dirty = true; } }
    public void UnregisterProducer(PowerProducer p) { if (_producers.Remove(p)) _dirty = true; }
    public void RegisterBattery(PowerBattery b) { if (b != null && !_batteries.Contains(b)) { _batteries.Add(b); _dirty = true; } }
    public void UnregisterBattery(PowerBattery b) { if (_batteries.Remove(b)) _dirty = true; }
    public void RegisterConsumer(PowerConsumer c) { if (c != null && !_consumers.Contains(c)) { _consumers.Add(c); _dirty = true; } }
    public void UnregisterConsumer(PowerConsumer c) { if (_consumers.Remove(c)) _dirty = true; }

    /// <summary>전선 추가/제거 등 위상 변화 시 전력망 재계산을 요청합니다.</summary>
    public void MarkDirty() => _dirty = true;

    #endregion

    #region 전력망 재구성

    private void CollectExistingNodes()
    {
        foreach (var p in FindObjectsByType<PowerProducer>(FindObjectsSortMode.None)) RegisterProducer(p);
        foreach (var b in FindObjectsByType<PowerBattery>(FindObjectsSortMode.None)) RegisterBattery(b);
        foreach (var c in FindObjectsByType<PowerConsumer>(FindObjectsSortMode.None)) RegisterConsumer(c);
    }

    private void RebuildNetworks()
    {
        _networks.Clear();

        _producers.RemoveAll(n => n == null);
        _batteries.RemoveAll(n => n == null);
        _consumers.RemoveAll(n => n == null);

        // 모든 노드 수집 (발전기·축전기·소비자 + 전선)
        var nodes = new List<IPowerNode>();
        nodes.AddRange(_producers);
        nodes.AddRange(_batteries);
        nodes.AddRange(_consumers);
        foreach (var w in PowerWire.All) if (w != null) nodes.Add(w);

        // 재계산 전 기본 정전 처리 (어느 네트워크에도 속하지 못한 소비자 대비)
        foreach (var c in _consumers) c.SetPowered(false);

        // 셀 → 그 셀을 점유한 노드 목록
        var cellToNodes = new Dictionary<Vector2Int, List<IPowerNode>>();
        foreach (var n in nodes)
            foreach (var cell in n.OccupiedCells)
            {
                if (!cellToNodes.TryGetValue(cell, out var list))
                {
                    list = new List<IPowerNode>();
                    cellToNodes[cell] = list;
                }
                list.Add(n);
            }

        // BFS로 연결 컴포넌트(= 전력망) 분리
        var visited = new HashSet<IPowerNode>();
        var queue = new Queue<IPowerNode>();
        foreach (var start in nodes)
        {
            if (visited.Contains(start)) continue;

            var network = new PowerNetwork();
            queue.Clear();
            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                AddNodeToNetwork(network, node);

                foreach (var cell in node.OccupiedCells)
                {
                    // 같은 셀(겹침: 전선↔건물) + 4방향 이웃 셀의 노드들과 연결
                    EnqueueCell(cellToNodes, cell, visited, queue);
                    foreach (var d in Dirs4)
                        EnqueueCell(cellToNodes, new Vector2Int(cell.x + d.x, cell.y + d.y), visited, queue);
                }
            }
            _networks.Add(network);
        }
    }

    private static void EnqueueCell(Dictionary<Vector2Int, List<IPowerNode>> map, Vector2Int cell,
        HashSet<IPowerNode> visited, Queue<IPowerNode> queue)
    {
        if (!map.TryGetValue(cell, out var list)) return;
        foreach (var n in list)
            if (visited.Add(n)) queue.Enqueue(n);
    }

    private static void AddNodeToNetwork(PowerNetwork net, IPowerNode node)
    {
        switch (node)
        {
            case PowerProducer p: net.Producers.Add(p); break;
            case PowerBattery b: net.Batteries.Add(b); break;
            case PowerConsumer c: net.Consumers.Add(c); break;
            case PowerWire w: net.Wires.Add(w); break;
        }
    }

    #endregion
}
