using UnityEngine;

/// <summary>
/// 작물 (쌀·감자·침식 쌀·침식 감자) — 1회성 부류.
///
/// 야생에서는 자연 생성기가, 밭·수경재배기에서는 재배 칸(<see cref="CropPlot"/>)이 만듭니다.
/// 같은 개체 정의·프리팹을 씁니다.
///   수확: 작물 + 씨앗(확률, yields에서 chance로 지정) → 사라짐 → 재배 칸이면 다시 파종
///   제초: 씨앗 (다 자랐으면 수확물도)
///
/// 단계별 스프라이트가 비어 있으면 성체 스프라이트를 성장도에 따라 키워서 보여 줍니다 (임시 그래픽용).
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class CropPlant : PlantBase
{
    [Header("작물 — 단계 스프라이트 (비어 있으면 성체 스프라이트를 크기로 표현)")]
    [SerializeField] private Sprite sproutSprite;
    [SerializeField] private Sprite youngSprite;
    [SerializeField] private Sprite matureSprite;

    [Tooltip("스프라이트 색 (임시 그래픽 구분용). 흰색이면 원본 그대로.")]
    [SerializeField] private Color tint = Color.white;

    [Tooltip("성장도 0일 때 배율 (단계 스프라이트가 없을 때만)")]
    [SerializeField, Range(0.1f, 1f)] private float minScale = 0.35f;

    private SpriteRenderer spriteRenderer;
    private Vector3 baseScale = Vector3.one;

    /// <summary>재배 칸이 작물 크기를 줄여 담을 때 쓰는 기준 배율</summary>
    public void SetBaseScale(float scale)
    {
        baseScale = Vector3.one * scale;
        OnGrowthChanged();
    }

    protected override void Awake()
    {
        // 작물은 항상 1회성 — 수확하면 사라지고 재배 칸이 다시 심는다
        lifecycle = PlantLifecycle.SingleUse;
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.color = tint;
        base.Awake();
    }

    protected override void OnGrowthChanged()
    {
        if (spriteRenderer == null) return;

        Sprite staged = IsMature ? matureSprite : Growth >= 0.5f ? youngSprite : sproutSprite;
        if (staged != null)
        {
            spriteRenderer.sprite = staged;
            transform.localScale = baseScale;
            return;
        }

        if (matureSprite != null) spriteRenderer.sprite = matureSprite;
        transform.localScale = baseScale * Mathf.Lerp(minScale, 1f, Growth);
    }

    public override void GetPreview(out Sprite sprite, out float scale)
    {
        base.GetPreview(out sprite, out scale);
        if (sproutSprite != null) { sprite = sproutSprite; return; }
        if (matureSprite != null) sprite = matureSprite;
        scale = minScale;
    }
}
