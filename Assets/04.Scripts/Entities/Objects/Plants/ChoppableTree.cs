using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 나무 — 재성장 부류. 벌목하면 목재를 떨어뜨리고 뿌리가 남아 다시 자랍니다.
/// 제초하면 뿌리까지 뽑혀 묘목이 나옵니다.
///
/// <b>단계별 크기</b> — 성장도에 따라 <see cref="stageScales"/> 단계로 커집니다.
/// 스프라이트 피벗이 좌하단이라 크기는 발밑 칸에서 위·오른쪽으로 늘어납니다.
///
/// <b>공간 부족 시 정지</b> — 다음 단계 크기만큼 위쪽 칸이 비어 있지 않으면 그 단계에서 멈춥니다.
/// 심는 것 자체는 막지 않습니다 — 좁은 곳에 심은 결과는 플레이어 몫입니다.
/// </summary>
public class ChoppableTree : PlantBase
{
    [Header("나무 — 단계")]
    [Tooltip("단계별 배율 (다 자란 크기 대비). 마지막이 성체(1). 칸 수는 올림으로 계산합니다.")]
    [SerializeField] private float[] stageScales = { 0.34f, 0.67f, 1f };

    [Header("나무 — 단계 스프라이트 (비어 있으면 성체 스프라이트)")]
    [SerializeField] private Sprite seedlingSprite;
    [SerializeField] private Sprite youngSprite;
    [SerializeField] private Sprite matureSprite;

    private SpriteRenderer spriteRenderer;

    /// <summary>다 자란 나무의 칸 크기 (성체 스프라이트 크기)</summary>
    private Vector2 matureSize = new Vector2(2f, 3f);

    private int LastStage => stageScales.Length - 1;

    /// <summary>현재 단계 (0 ~ LastStage)</summary>
    public int Stage => LastStage <= 0 ? 0 : Mathf.Min(LastStage, Mathf.FloorToInt(Growth * LastStage + 0.0001f));

    protected override void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = gameObject.AddComponent<SpriteRenderer>();

        Sprite mature = matureSprite != null ? matureSprite : spriteRenderer.sprite;
        if (mature != null) matureSize = mature.bounds.size;

        base.Awake();
    }

    protected override void OnGrowthChanged()
    {
        if (spriteRenderer == null) return;

        int stage = Stage;
        Sprite s = stage == LastStage ? matureSprite
                 : stage == 0 ? seedlingSprite
                 : youngSprite;
        if (s == null) s = matureSprite;
        if (s != null) spriteRenderer.sprite = s;

        transform.localScale = Vector3.one * stageScales[stage];
    }

    /// <summary>다음 단계 크기가 들어갈 공간이 있어야 자랍니다.</summary>
    protected override bool CheckExtraGrowth(List<string> failures)
    {
        int next = Mathf.Min(Stage + 1, LastStage);
        Vector2Int size = FootprintAt(next);
        if (HasSpace(size)) return true;

        failures?.Add($"공간 부족 (높이 {size.y}칸 필요)");
        return false;
    }

    /// <summary>묘목 단계 모습 — 첫 단계 배율, 묘목 스프라이트가 없으면 성체 스프라이트.</summary>
    public override void GetPreview(out Sprite sprite, out float scale)
    {
        base.GetPreview(out sprite, out scale);
        if (seedlingSprite != null) sprite = seedlingSprite;
        else if (matureSprite != null) sprite = matureSprite;
        scale = stageScales.Length > 0 ? stageScales[0] : 1f;
    }

    /// <summary>다 자라려면 필요한 공간. 프리팹에서도 호출되므로 스프라이트에서 직접 크기를 읽는다.</summary>
    public override string DescribeSpaceNeed()
    {
        Sprite mature = matureSprite;
        if (mature == null && TryGetComponent(out SpriteRenderer sr)) mature = sr.sprite;
        Vector2 size = mature != null ? (Vector2)mature.bounds.size : matureSize;
        return $"위쪽 빈 공간 {Mathf.CeilToInt(size.x - 0.01f)}×{Mathf.CeilToInt(size.y - 0.01f)}칸 (좁으면 그 단계에서 멈춤)";
    }

    /// <summary>단계의 칸 크기 (올림).</summary>
    private Vector2Int FootprintAt(int stage)
    {
        float s = stageScales[stage];
        return new Vector2Int(
            Mathf.Max(1, Mathf.CeilToInt(matureSize.x * s - 0.01f)),
            Mathf.Max(1, Mathf.CeilToInt(matureSize.y * s - 0.01f)));
    }

    // ponytail: 지형 타일(공기)만 본다 — 건물이 자리를 막는 경우는 건물 점유 그리드가 필요해지면 추가
    private bool HasSpace(Vector2Int size)
    {
        var map = MapGenerator.instance != null ? MapGenerator.instance.GameMapInstance : null;
        if (map == null) return true;

        Vector2Int origin = Cell;
        for (int dx = 0; dx < size.x; dx++)
        for (int dy = 0; dy < size.y; dy++)
        {
            int x = origin.x + dx, y = origin.y + dy;
            if (x < 0 || x >= GameMap.MAP_WIDTH || y < 0 || y >= GameMap.MAP_HEIGHT) return false;
            if (map.TileGrid[x, y] != (int)TileType.Air) return false;
        }
        return true;
    }
}
