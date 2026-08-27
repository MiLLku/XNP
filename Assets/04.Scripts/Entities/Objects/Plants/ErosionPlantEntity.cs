using UnityEngine;

/// <summary>
/// 침식 식물 엔티티.
///
/// ToxicFern, CorruptedMushroom 등 자연 침식 식물 프리팹에 부착합니다.
/// TerrainErosionEmitter는 방 침식 등록/해제를 담당하고,
/// 이 컴포넌트는 저장/복원 식별과 <b>채광으로 부수는 경로</b>를 담당합니다.
///
/// <b>제거 = 채광</b>
/// IHarvestable을 WorkType.Mining으로 구현하므로 기존 작업 파이프라인
/// (클릭 → 오더 생성 → 직원 배정 → 완료)을 그대로 탑니다. 새 오더 타입이 필요 없습니다.
/// 부수면 발원지가 사라져 방 침식이 더 이상 오르지 않습니다.
/// 단, <b>이미 고인 침식은 남습니다</b> — 그건 세척 작업이나 환기로 지워야 합니다.
///
/// 캐낸 직원은 <see cref="IErosionHazardWork"/>를 통해 침식을 뒤집어씁니다.
/// 위험을 없애는 대가로 사람이 오염되는 것이 이 시스템의 기본 교환입니다.
///
/// entityId 규칙:
///   20 = ToxicFern (독성 고사리)
///   21 = CorruptedMushroom (부패한 버섯)
/// </summary>
[RequireComponent(typeof(TerrainErosionEmitter))]
public class ErosionPlantEntity : MonoBehaviour, IHarvestable, IErosionHazardWork
{
    #region 설정

    /// <summary>ResourceManager에서 프리팹을 조회할 때 사용하는 엔티티 ID</summary>
    [SerializeField] public int entityId;

    [Header("제거 작업")]
    [Tooltip("캐내는 데 걸리는 시간(초). 발원지가 위험할수록 길게 잡습니다.")]
    [SerializeField] private float removalTime = 8f;

    [Tooltip("캐낸 직원이 받는 침식량")]
    [SerializeField] private float workerErosionCost = 12f;

    [Tooltip("표시 이름")]
    [SerializeField] private string displayName = "침식 발원지";

    #endregion

    #region 상태

    private TerrainErosionEmitter emitter;
    private bool isBeingRemoved;

    #endregion

    #region 초기화

    private void Awake()
    {
        emitter = GetComponent<TerrainErosionEmitter>();

        // 클릭·드래그 선택(Physics2D)에 잡히도록 콜라이더를 보장한다 (ChoppableTree와 같은 처리)
        if (GetComponent<Collider2D>() == null)
            gameObject.AddComponent<BoxCollider2D>();
    }

    #endregion

    #region IHarvestable

    public bool CanHarvest() => !isBeingRemoved;

    public float GetHarvestTime() => removalTime;

    /// <summary>채광으로 분류합니다 — 곡괭이를 든 직원이 부숩니다.</summary>
    public WorkType GetHarvestType() => WorkType.Mining;

    public void Harvest()
    {
        if (isBeingRemoved) return;
        isBeingRemoved = true;

        // 등록 해제가 먼저 — 파괴 프레임에 한 틱 더 오염시키지 않도록
        if (emitter != null)
            TerrainErosionManager.instance?.UnregisterSource(emitter);

        Debug.Log($"[ErosionPlant] {displayName} 제거됨 @{transform.position}");

        Destroy(gameObject);
    }

    #endregion

    #region IErosionHazardWork

    public float WorkerErosionCost => workerErosionCost;

    public string HazardDisplayName => $"{displayName} 제거";

    #endregion
}
