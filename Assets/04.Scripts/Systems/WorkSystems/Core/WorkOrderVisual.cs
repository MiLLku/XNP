using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 작업 명령의 시각적 표현 (외곽선 + 라벨).
/// 작업 대상 타일들의 외곽선 메시를 생성하고, 진행 상황 라벨을 표시합니다.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider2D))]
public class WorkOrderVisual : MonoBehaviour
{
    #region 상수

    private const int OUTLINE_SORTING_ORDER = 100;
    private const float TILE_HALF_SIZE = 0.5f;
    private const float LABEL_Y_OFFSET = 1.0f;

    #endregion

    #region 필드

    [Header("데이터")]
    [SerializeField] private WorkOrder workOrder;
    [SerializeField] private List<Vector3Int> tilePositions = new List<Vector3Int>();

    [Header("외곽선 설정")]
    [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color selectedColor = new Color(1f, 0.8f, 0.2f, 1f);
    [SerializeField] private Color pendingColor = new Color(0.2f, 0.8f, 1f, 0.8f);
    [SerializeField] private float lineWidth = 0.05f;

    [Header("UI 연결")]
    [SerializeField] private GameObject labelPrefab;

    private MeshRenderer meshRenderer;
    private MeshFilter meshFilter;
    private BoxCollider2D boxCollider;
    private GameObject labelInstance;
    private TMPro.TextMeshProUGUI labelText;

    private bool isSelected = false;
    private bool isPendingMode = false;

    #endregion

    #region 프로퍼티

    /// <summary>연결된 작업 명령</summary>
    public WorkOrder WorkOrder => workOrder;

    #endregion

    #region 초기화

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        boxCollider = GetComponent<BoxCollider2D>();
        boxCollider.isTrigger = true;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            meshRenderer.material = new Material(shader);
        }
        else
        {
            Debug.LogError("[WorkOrderVisual] Sprites/Default shader not found!");
        }
    }

    void OnDestroy()
    {
        if (meshRenderer != null && meshRenderer.material != null)
        {
            Destroy(meshRenderer.material);
        }
        if (meshFilter != null && meshFilter.mesh != null)
        {
            Destroy(meshFilter.mesh);
        }
        if (labelInstance != null)
        {
            Destroy(labelInstance);
        }
    }

    /// <summary>
    /// 정식 작업물로 초기화합니다.
    /// </summary>
    /// <param name="order">연결할 작업 명령</param>
    /// <param name="tiles">작업 대상 타일 좌표 목록</param>
    public void Initialize(WorkOrder order, List<Vector3Int> tiles)
    {
        workOrder = order;
        tilePositions = new List<Vector3Int>(tiles);
        isPendingMode = false;

        RefreshVisuals();
        CreateLabel();
    }

    /// <summary>
    /// 예약 모드에서 타일 목록만 업데이트합니다.
    /// 라벨은 표시하지 않습니다.
    /// </summary>
    /// <param name="newTiles">새 타일 좌표 목록</param>
    public void UpdateTiles(List<Vector3Int> newTiles)
    {
        isPendingMode = true;
        tilePositions = new List<Vector3Int>(newTiles);

        RefreshVisuals();

        if (labelInstance != null) Destroy(labelInstance);
    }

    #endregion

    #region 업데이트

    void Update()
    {
        if (isPendingMode) return;

        if (workOrder == null || workOrder.IsCompleted() || !workOrder.isActive)
        {
            Destroy(gameObject);
            return;
        }

        UpdateLabel();
        UpdateTileVisuals();
    }

    /// <summary>
    /// 완료된 타일을 시각적으로 제거하고 메시를 갱신합니다.
    /// </summary>
    private void UpdateTileVisuals()
    {
        if (workOrder == null || workOrder.taskQueue == null) return;

        var remainingTiles = new List<Vector3Int>();

        foreach (var tile in tilePositions)
        {
            bool isCompleted = false;

            foreach (var task in workOrder.taskQueue.CompletedTasks)
            {
                Vector3 taskPos = task.GetPosition();
                if (Mathf.FloorToInt(taskPos.x) == tile.x && Mathf.FloorToInt(taskPos.y) == tile.y)
                {
                    isCompleted = true;
                    break;
                }
            }

            if (!isCompleted)
            {
                remainingTiles.Add(tile);
            }
        }

        if (remainingTiles.Count != tilePositions.Count)
        {
            tilePositions = remainingTiles;
            RefreshVisuals();
        }
    }

    #endregion

    #region 시각 효과

    private void RefreshVisuals()
    {
        GenerateOutlineMesh();
        UpdateCollider();
        UpdateColor();
    }

    /// <summary>
    /// 타일 경계의 외곽선 메시를 생성합니다.
    /// 인접한 타일과의 경계만 외곽선을 그립니다.
    /// </summary>
    private void GenerateOutlineMesh()
    {
        if (tilePositions == null || tilePositions.Count == 0)
        {
            meshFilter.mesh = null;
            return;
        }

        Mesh mesh = new Mesh();
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        HashSet<Vector3Int> tileSet = new HashSet<Vector3Int>(tilePositions);

        Vector3Int[] directions = { Vector3Int.up, Vector3Int.right, Vector3Int.down, Vector3Int.left };

        foreach (var tile in tilePositions)
        {
            for (int i = 0; i < 4; i++)
            {
                if (!tileSet.Contains(tile + directions[i]))
                {
                    AddEdgeMesh(tile, i, vertices, triangles);
                }
            }
        }

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateBounds();

        meshFilter.mesh = mesh;

        meshRenderer.sortingLayerName = "Default";
        meshRenderer.sortingOrder = OUTLINE_SORTING_ORDER;
    }

    /// <summary>
    /// 타일의 한 변에 대한 외곽선 쿼드를 추가합니다.
    /// </summary>
    /// <param name="tilePos">타일 좌표</param>
    /// <param name="dirIndex">방향 인덱스 (0=위, 1=오른쪽, 2=아래, 3=왼쪽)</param>
    /// <param name="verts">정점 리스트</param>
    /// <param name="tris">삼각형 인덱스 리스트</param>
    private void AddEdgeMesh(Vector3Int tilePos, int dirIndex, List<Vector3> verts, List<int> tris)
    {
        Vector3 center = new Vector3(tilePos.x + TILE_HALF_SIZE, tilePos.y + TILE_HALF_SIZE, 0) - transform.position;

        float d = TILE_HALF_SIZE;
        float t = lineWidth;

        Vector3[] quad = new Vector3[4];

        switch (dirIndex)
        {
            case 0: // Up
                quad[0] = center + new Vector3(-d, d, 0);
                quad[1] = center + new Vector3(d, d, 0);
                quad[2] = center + new Vector3(-d, d - t, 0);
                quad[3] = center + new Vector3(d, d - t, 0);
                break;
            case 1: // Right
                quad[0] = center + new Vector3(d - t, d, 0);
                quad[1] = center + new Vector3(d, d, 0);
                quad[2] = center + new Vector3(d - t, -d, 0);
                quad[3] = center + new Vector3(d, -d, 0);
                break;
            case 2: // Down
                quad[0] = center + new Vector3(-d, -d + t, 0);
                quad[1] = center + new Vector3(d, -d + t, 0);
                quad[2] = center + new Vector3(-d, -d, 0);
                quad[3] = center + new Vector3(d, -d, 0);
                break;
            case 3: // Left
                quad[0] = center + new Vector3(-d, d, 0);
                quad[1] = center + new Vector3(-d + t, d, 0);
                quad[2] = center + new Vector3(-d, -d, 0);
                quad[3] = center + new Vector3(-d + t, -d, 0);
                break;
        }

        int startIndex = verts.Count;
        verts.AddRange(quad);

        tris.Add(startIndex + 0); tris.Add(startIndex + 1); tris.Add(startIndex + 2);
        tris.Add(startIndex + 2); tris.Add(startIndex + 1); tris.Add(startIndex + 3);
    }

    private void UpdateCollider()
    {
        if (tilePositions.Count == 0) return;

        int minX = tilePositions.Min(t => t.x);
        int maxX = tilePositions.Max(t => t.x);
        int minY = tilePositions.Min(t => t.y);
        int maxY = tilePositions.Max(t => t.y);

        Vector2 center = new Vector2((minX + maxX) / 2f + TILE_HALF_SIZE, (minY + maxY) / 2f + TILE_HALF_SIZE);
        Vector2 size = new Vector2(maxX - minX + 1, maxY - minY + 1);

        boxCollider.offset = center - (Vector2)transform.position;
        boxCollider.size = size;
    }

    private void UpdateColor()
    {
        Color c = isPendingMode ? pendingColor : (isSelected ? selectedColor : normalColor);
        if (meshRenderer != null) meshRenderer.material.color = c;
    }

    #endregion

    #region 라벨

    private void CreateLabel()
    {
        if (labelPrefab != null && !isPendingMode)
        {
            labelInstance = Instantiate(labelPrefab, transform);
            labelText = labelInstance.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            UpdateLabelPos();
        }
    }

    private void UpdateLabelPos()
    {
        if (labelInstance != null && tilePositions.Count > 0)
        {
            int maxY = tilePositions.Max(t => t.y);
            int minX = tilePositions.Min(t => t.x);
            int maxX = tilePositions.Max(t => t.x);

            Vector3 centerTop = new Vector3((minX + maxX) / 2f + TILE_HALF_SIZE, maxY + LABEL_Y_OFFSET, 0);
            labelInstance.transform.position = new Vector3(centerTop.x, centerTop.y, 0);
        }
    }

    private void UpdateLabel()
    {
        if (labelText != null && workOrder != null)
        {
            int pending = workOrder.taskQueue.PendingCount;
            int assigned = workOrder.taskQueue.AssignedCount;
            int completed = workOrder.taskQueue.CompletedCount;

            // 자동 픽업: 현재 작업 중인 직원 수만 표시 (인원 제한 없음)
            // 전용 할당: 등록 인원/최대 인원 표시
            string workerInfo = workOrder.IsAutoPickup
                ? $"작업중: {workOrder.assignedWorkers.Count}명"
                : $"({workOrder.assignedWorkers.Count}/{workOrder.maxAssignedWorkers})";

            labelText.text = $"{workOrder.orderName}\n" +
                            $"{workerInfo}\n" +
                            $"[{completed}/{pending + assigned + completed}]";
        }
    }

    #endregion

    #region 상호작용

    /// <summary>
    /// 클릭 시 호출됩니다 (InteractionManager에서 호출).
    /// 작업 배정 UI를 표시합니다.
    /// </summary>
    public void OnClicked()
    {
        if (isPendingMode) return;

        if (WorkSystemManager.instance != null)
        {
            isSelected = true;
            UpdateColor();
            WorkSystemManager.instance.ShowAssignmentUI(workOrder, this);
        }
    }

    /// <summary>
    /// 선택 해제 시 호출됩니다.
    /// </summary>
    public void Deselect()
    {
        isSelected = false;
        UpdateColor();
    }

    #endregion
}
