using UnityEngine;

/// <summary>
/// 스프라이트 외곽선 강조 — 셰이더 없이 같은 스프라이트를 상하좌우로 1픽셀씩 밀어
/// 단색으로 뒤에 깔아 테두리처럼 보이게 합니다.
///
/// 처음 켤 때 자식 렌더러 4개를 만들고 이후 재사용합니다 (풀링된 DroppedItem에서도 안전).
/// 켤 때마다 현재 스프라이트·정렬을 다시 복사하므로 풀 재사용으로 스프라이트가 바뀌어도 맞습니다.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteOutline : MonoBehaviour
{
    private static readonly Vector2[] Offsets = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

    [SerializeField] private Color color = Color.white;

    private static Material silhouette;

    /// <summary>내장 GUI/Text Shader — 텍스처 알파 × 정점 색. 항상 포함되는 내장 셰이더라 빌드에서도 안전.</summary>
    private static Material SilhouetteMaterial
    {
        get
        {
            if (silhouette == null)
            {
                var shader = Shader.Find("GUI/Text Shader");
                if (shader != null) silhouette = new Material(shader);
            }
            return silhouette;
        }
    }

    private SpriteRenderer source;
    private SpriteRenderer[] parts;

    /// <summary>외곽선을 켜거나 끕니다.</summary>
    public void SetVisible(bool visible)
    {
        if (source == null) source = GetComponent<SpriteRenderer>();
        if (visible && parts == null) Build();
        if (parts == null) return;

        foreach (var p in parts)
        {
            p.enabled = visible;
            if (!visible) continue;
            p.sprite = source.sprite;
            p.flipX = source.flipX;
            p.flipY = source.flipY;
            p.sortingLayerID = source.sortingLayerID;
            p.sortingOrder = source.sortingOrder - 1;
        }
    }

    private void Build()
    {
        float px = source.sprite != null ? 1f / source.sprite.pixelsPerUnit : 1f / 16f;
        parts = new SpriteRenderer[Offsets.Length];

        for (int i = 0; i < Offsets.Length; i++)
        {
            var go = new GameObject("Outline");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = (Vector3)(Offsets[i] * px);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.color = color;
            // 틴트로는 흰색을 만들 수 없다 — 알파만 쓰고 정점 색으로 칠하는 내장 셰이더로 실루엣을 만든다
            sr.sharedMaterial = SilhouetteMaterial != null ? SilhouetteMaterial : source.sharedMaterial;
            parts[i] = sr;
        }
    }

    /// <summary>대상에 외곽선 컴포넌트를 붙이거나 가져옵니다.</summary>
    public static SpriteOutline For(Component target)
    {
        if (target == null || target.GetComponent<SpriteRenderer>() == null) return null;
        var o = target.GetComponent<SpriteOutline>();
        return o != null ? o : target.gameObject.AddComponent<SpriteOutline>();
    }
}
