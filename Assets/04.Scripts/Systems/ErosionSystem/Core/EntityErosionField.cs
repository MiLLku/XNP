using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// <b>개체 발원지</b> 등록소이자 타일 시각화 레이어.
///
/// 지형 발원지(<see cref="ITerrainErosionSource"/>)와 성격이 다릅니다.
///   · 지형 발원지 : 방 침식(농도)을 <b>올린다</b>. 없애도 고인 것은 남는다.
///   · 개체 발원지 : 범위에 들어온 직원에게 <b>고정량을 붙이고</b>, 벗어나면 <b>돌려준다</b>.
///                   공간에는 아무 흔적도 남기지 않는다.
///
/// 침식을 실제로 붙이고 떼는 판정은 직원 쪽(EmployeeErosionController)이 이 등록소를 훑어서 합니다.
/// 여기서 그리는 타일 값은 <b>오버레이 표시와 위험 구역 조회를 위한 것</b>입니다.
///
/// 비용은 개체 수 × 반경²입니다. 반경 5면 개체당 약 80칸이라 20마리여도 무시할 수준입니다.
/// 겹치면 합이 아니라 가장 강한 것을 표시합니다.
/// </summary>
public class EntityErosionField : DestroySingleton<EntityErosionField>
{
    #region 인스펙터

    [Header("설정")]
    [Tooltip("타일 표시를 다시 찍는 주기(초)")]
    [SerializeField] private float tickInterval = 0.5f;

    [SerializeField] private bool showDebugLogs = false;

    #endregion

    #region 상태

    /// <summary>칸별 표시 강도 (해당 칸을 덮는 개체 발원지 중 가장 큰 고정량)</summary>
    private float[,] values;

    private readonly List<Vector2Int> writtenCells = new List<Vector2Int>();

    private readonly HashSet<IEntityErosionSource> sources = new HashSet<IEntityErosionSource>();

    private float tickTimer;

    #endregion

    #region 프로퍼티

    /// <summary>등록된 개체 발원지 수</summary>
    public int SourceCount => sources.Count;

    /// <summary>표시용으로 칠해진 칸 수</summary>
    public int PaintedCellCount => writtenCells.Count;

    /// <summary>등록된 개체 발원지 전체 (직원 판정이 순회합니다)</summary>
    public IReadOnlyCollection<IEntityErosionSource> Sources => sources;

    #endregion

    #region 초기화

    protected override void Awake()
    {
        base.Awake();
        values = new float[GameMap.MAP_WIDTH, GameMap.MAP_HEIGHT];
    }

    #endregion

    #region 등록

    /// <summary>개체 발원지를 등록합니다.</summary>
    public void RegisterSource(IEntityErosionSource source)
    {
        if (source == null) return;
        sources.Add(source);
    }

    /// <summary>
    /// 등록을 해제합니다.
    /// 이 발원지가 붙여둔 침식은 직원 쪽 판정이 다음 틱에 알아서 돌려줍니다.
    /// </summary>
    public void UnregisterSource(IEntityErosionSource source)
    {
        if (source == null) return;
        sources.Remove(source);
    }

    #endregion

    #region 표시 갱신

    private void Update()
    {
        tickTimer += Time.deltaTime;
        if (tickTimer < Mathf.Max(0.05f, tickInterval)) return;

        tickTimer = 0f;
        Repaint();
    }

    /// <summary>표시 레이어를 지우고 등록된 발원지를 다시 찍습니다.</summary>
    private void Repaint()
    {
        // 지난 틱에 쓴 칸만 지운다 — 전체 40,000칸을 훑지 않는다
        foreach (var cell in writtenCells)
            values[cell.x, cell.y] = 0f;
        writtenCells.Clear();

        foreach (var source in sources)
        {
            if (source == null || !source.IsEmitting) continue;
            Stamp(source);
        }

        if (showDebugLogs && sources.Count > 0)
            Debug.Log($"[EntityErosionField] 개체 발원지 {sources.Count}개 / 표시 칸 {writtenCells.Count}개");
    }

    private void Stamp(IEntityErosionSource source)
    {
        float radius = source.EmitRadius;
        float amount = source.FixedErosionAmount;
        if (radius <= 0f || amount <= 0f) return;

        Vector2 center = source.EmitPosition;
        int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
        int maxX = Mathf.Min(GameMap.MAP_WIDTH - 1, Mathf.CeilToInt(center.x + radius));

        // 가로 판정 개체(오염 구체)는 세로 제한이 없다 — 화면에 보이는 범위만 칠한다
        int minY = source.HorizontalOnly ? 0 : Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
        int maxY = source.HorizontalOnly
            ? GameMap.MAP_HEIGHT - 1
            : Mathf.Min(GameMap.MAP_HEIGHT - 1, Mathf.CeilToInt(center.y + radius));

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (!source.Covers(new Vector2(x, y))) continue;

                // 범위 안이면 어디든 같은 값이다 — 고정량 모델이라 거리 감쇠가 없다
                if (amount <= values[x, y]) continue;

                if (values[x, y] <= 0f) writtenCells.Add(new Vector2Int(x, y));
                values[x, y] = amount;
            }
        }
    }

    #endregion

    #region 조회

    /// <summary>해당 칸을 덮는 개체 발원지 중 가장 큰 고정량 (표시·조회용)</summary>
    public float GetValueAt(int x, int y)
    {
        if (x < 0 || x >= GameMap.MAP_WIDTH || y < 0 || y >= GameMap.MAP_HEIGHT) return 0f;
        return values[x, y];
    }

    /// <inheritdoc cref="GetValueAt(int,int)"/>
    public float GetValueAt(Vector2Int cell) => GetValueAt(cell.x, cell.y);

    #endregion
}
