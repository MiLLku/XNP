using UnityEngine;

namespace Object.Plant
{
    /// <summary>
    /// 베리 덤불.
    /// 수확 시 아이템을 반환하고 빈 상태로 전환됩니다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class BerryBush : MonoBehaviour
    {
        [Header("상태별 스프라이트")]
        [SerializeField] private Sprite emptySprite;
        [SerializeField] private Sprite halfSprite;
        [SerializeField] private Sprite fullSprite;

        [Header("생산물")]
        [Tooltip("수확 시 인벤토리에 들어갈 아이템")]
        [SerializeField] private ItemData itemToProduce;

        private SpriteRenderer spriteRenderer;

        /// <summary>현재 덤불 상태 (0=empty, 1=half, 2=full)</summary>
        private int _growthState = 0;

        /// <summary>수확 가능 여부 (full 상태일 때만)</summary>
        public bool CanHarvest => _growthState == 2;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();

            if (emptySprite != null)
            {
                spriteRenderer.sprite = emptySprite;
            }
            else
            {
                Debug.LogError("BerryBush 프리팹에 'Empty Sprite'가 설정되지 않았습니다!");
            }

            _growthState = 0;
        }

        /// <summary>
        /// 성장 상태를 설정합니다 (0=empty, 1=half, 2=full).
        /// </summary>
        public void SetGrowthState(int state)
        {
            _growthState = Mathf.Clamp(state, 0, 2);
            UpdateSprite();
        }

        /// <summary>
        /// 현재 성장 상태를 반환합니다.
        /// </summary>
        public int GetGrowthState() => _growthState;

        /// <summary>
        /// 수확을 수행하고 아이템 데이터를 반환합니다.
        /// full 상태에서만 수확 가능하며, 수확 후 empty 상태로 전환됩니다.
        /// </summary>
        /// <returns>수확된 아이템 데이터 (수확 불가 시 null)</returns>
        public ItemData Harvest()
        {
            if (_growthState < 2)
            {
                Debug.LogWarning($"[BerryBush] {name}: 아직 수확할 수 없습니다. (상태: {_growthState}/2)");
                return null;
            }

            _growthState = 0;
            UpdateSprite();
            return itemToProduce;
        }

        /// <summary>
        /// 현재 성장 상태에 맞는 스프라이트를 설정합니다.
        /// </summary>
        private void UpdateSprite()
        {
            switch (_growthState)
            {
                case 0: spriteRenderer.sprite = emptySprite; break;
                case 1: spriteRenderer.sprite = halfSprite; break;
                case 2: spriteRenderer.sprite = fullSprite; break;
            }
        }
    }
}
