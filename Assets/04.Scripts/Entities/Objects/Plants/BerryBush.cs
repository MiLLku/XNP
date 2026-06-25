using UnityEngine;

namespace Object.Plant
{
    /// <summary>
    /// 베리 덤불 — 시간이 지나면 자라고(empty→half→full), 수확하면 식량을 바닥에 떨어뜨린 뒤
    /// 다시 자라납니다.
    ///
    /// 공통 수확 인터페이스(IHarvestable·IWorkTarget)를 구현하므로, 나무 벌목·작물 수확 등과
    /// 동일한 작업 파이프라인(수확 드래그 지정 → 직원이 원예 작업으로 자동 수확 → 드롭 운반)에
    /// 그대로 편입됩니다. 향후 다른 수확물도 이 두 인터페이스만 구현하면 같은 흐름을 탑니다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class BerryBush : MonoBehaviour, IHarvestable, IWorkTarget
    {
        [Header("상태별 스프라이트")]
        [SerializeField] private Sprite emptySprite;
        [SerializeField] private Sprite halfSprite;
        [SerializeField] private Sprite fullSprite;

        [Header("생산물")]
        [Tooltip("수확 시 떨어뜨릴 식량 아이템")]
        [SerializeField] private ItemData itemToProduce;
        [Tooltip("한 번 수확 시 생산 개수")]
        [SerializeField] private int harvestAmount = 3;

        [Header("성장")]
        [Tooltip("empty에서 full까지 자라는 데 걸리는 시간(초)")]
        [SerializeField] private float growthTime = 40f;
        [Tooltip("수확 작업 자체에 걸리는 시간(초)")]
        [SerializeField] private float harvestTime = 2f;

        private SpriteRenderer spriteRenderer;

        /// <summary>현재 덤불 상태 (0=empty, 1=half, 2=full)</summary>
        private int _growthState = 0;

        /// <summary>누적 성장 시간</summary>
        private float _growth = 0f;

        /// <summary>현재 직원이 수확 중인지 (이중 처리 방지)</summary>
        private bool _isBeingHarvested = false;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();

            // 수확 드래그 선택(Physics2D.OverlapBox)에 잡히도록 Collider 보장.
            // 프리팹에 콜라이더가 없어도 런타임에 스프라이트 크기에 맞춰 자동 부착된다.
            if (GetComponent<Collider2D>() == null)
                gameObject.AddComponent<BoxCollider2D>();

            _growthState = 0;
            _growth = 0f;
            UpdateSprite();
        }

        private void Update()
        {
            // full이 아니고 수확 중이 아니면 시간에 따라 성장 (수확 후 자동 재성장 포함)
            if (_growthState < 2 && !_isBeingHarvested)
            {
                _growth += Time.deltaTime;
                int stage = _growth >= growthTime ? 2
                          : _growth >= growthTime * 0.5f ? 1 : 0;
                if (stage != _growthState)
                {
                    _growthState = stage;
                    UpdateSprite();
                }
            }
        }

        #region IHarvestable

        public bool CanHarvest() => _growthState == 2 && !_isBeingHarvested;

        public void Harvest()
        {
            if (_growthState < 2) return; // 자기방어 (이미 수확됐거나 미성숙)

            DropProduce();

            // 빈 상태로 되돌리고 재성장 시작
            _growthState = 0;
            _growth = 0f;
            _isBeingHarvested = false;
            UpdateSprite();
        }

        public float GetHarvestTime() => harvestTime;

        public WorkType GetHarvestType() => WorkType.Gardening;

        #endregion

        #region IWorkTarget

        public Vector3 GetWorkPosition() => transform.position + new Vector3(0f, -0.3f, 0f);

        public WorkType GetWorkType() => WorkType.Gardening;

        public float GetWorkTime() => harvestTime;

        public bool IsWorkAvailable() => CanHarvest();

        public void CompleteWork(Employee worker)
        {
            if (_isBeingHarvested) return; // 이중 호출 가드
            _isBeingHarvested = true;
            Harvest();
        }

        public void CancelWork(Employee worker) => _isBeingHarvested = false;

        #endregion

        #region 생산물 드롭

        /// <summary>
        /// 수확물을 바닥에 떨어뜨립니다(채광·벌목과 동일하게 DroppedItem → 직원이 창고로 운반).
        /// DroppedItemManager가 없으면 인벤토리로 직접 반납(폴백).
        /// </summary>
        private void DropProduce()
        {
            if (itemToProduce == null || harvestAmount <= 0) return;

            if (DroppedItemManager.instance == null)
            {
                InventoryManager.instance?.AddItem(itemToProduce, harvestAmount);
                return;
            }

            for (int i = 0; i < harvestAmount; i++)
            {
                Vector3 pos = transform.position + new Vector3(UnityEngine.Random.Range(-0.4f, 0.4f), 0.2f, 0f);
                DroppedItemManager.instance.SpawnItem(itemToProduce, 1, pos);
            }
        }

        #endregion

        #region 외부 제어 (세이브·맵 생성 등)

        /// <summary>성장 상태를 직접 설정합니다 (0=empty, 1=half, 2=full).</summary>
        public void SetGrowthState(int state)
        {
            _growthState = Mathf.Clamp(state, 0, 2);
            _growth = _growthState >= 2 ? growthTime
                    : _growthState == 1 ? growthTime * 0.5f : 0f;
            UpdateSprite();
        }

        /// <summary>현재 성장 상태를 반환합니다.</summary>
        public int GetGrowthState() => _growthState;

        #endregion

        private void UpdateSprite()
        {
            if (spriteRenderer == null) return;
            switch (_growthState)
            {
                case 0: if (emptySprite != null) spriteRenderer.sprite = emptySprite; break;
                case 1: if (halfSprite != null)  spriteRenderer.sprite = halfSprite;  break;
                case 2: if (fullSprite != null)  spriteRenderer.sprite = fullSprite;  break;
            }
        }
    }
}
