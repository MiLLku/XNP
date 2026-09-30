using UnityEngine;

namespace Object.Plant
{
    /// <summary>
    /// 베리 덤불 — 재성장 부류. 성장도에 따라 empty → half → full로 자라고,
    /// 수확하면 식량을 떨어뜨린 뒤 empty부터 다시 자랍니다. 제초하면 묘목이 나옵니다.
    ///
    /// 성장·수확·제초·드롭은 모두 <see cref="PlantBase"/>가 처리하고, 여기서는 단계별 스프라이트만 정합니다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class BerryBush : PlantBase
    {
        [Header("상태별 스프라이트")]
        [SerializeField] private Sprite emptySprite;
        [SerializeField] private Sprite halfSprite;
        [SerializeField] private Sprite fullSprite;

        private SpriteRenderer spriteRenderer;

        protected override void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            base.Awake();
        }

        /// <summary>파종 직후 모습은 빈 덤불</summary>
        public override void GetPreview(out Sprite sprite, out float scale)
        {
            base.GetPreview(out sprite, out scale);
            if (emptySprite != null) sprite = emptySprite;
        }

        protected override void OnGrowthChanged()
        {
            if (spriteRenderer == null) return;

            Sprite s = IsMature ? fullSprite
                     : Growth >= 0.5f ? halfSprite
                     : emptySprite;
            if (s != null) spriteRenderer.sprite = s;
        }
    }
}
