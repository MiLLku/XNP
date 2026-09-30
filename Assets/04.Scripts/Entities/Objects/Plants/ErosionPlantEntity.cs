using UnityEngine;

/// <summary>
/// 침식 식물 — 1회성 부류.
///
/// ToxicFern, CorruptedMushroom 등 자연 침식 식물 프리팹에 부착합니다.
/// TerrainErosionEmitter는 방 침식 등록/해제를 담당하고,
/// 이 컴포넌트는 저장/복원 식별과 <b>제거 경로</b>를 담당합니다.
///
/// <b>제거 = 제초</b> (구: 채광)
/// 수확물(<see cref="PlantBase"/>의 yields)이 있으면 수확 명령으로, 없으면 제초 명령으로 제거합니다.
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
public class ErosionPlantEntity : PlantBase, IErosionHazardWork
{
    #region 설정

    /// <summary>ResourceManager에서 프리팹을 조회할 때 사용하는 엔티티 ID</summary>
    [SerializeField] public int entityId;

    [Tooltip("제거한 직원이 받는 침식량")]
    [SerializeField] private float workerErosionCost = 12f;

    [Tooltip("표시 이름")]
    [SerializeField] private string displayName = "침식 발원지";

    #endregion

    private TerrainErosionEmitter emitter;
    private bool removed;

    protected override void Awake()
    {
        // 침식 식물은 항상 1회성 — 수확하면 사라진다
        lifecycle = PlantLifecycle.SingleUse;
        emitter = GetComponent<TerrainErosionEmitter>();
        base.Awake();
    }

    /// <summary>단계별 비주얼이 없습니다.</summary>
    protected override void OnGrowthChanged() { }

    protected override void Remove()
    {
        if (removed) return;
        removed = true;

        // 등록 해제가 먼저 — 파괴 프레임에 한 틱 더 오염시키지 않도록
        if (emitter != null)
            TerrainErosionManager.instance?.UnregisterSource(emitter);

        Debug.Log($"[ErosionPlant] {displayName} 제거됨 @{transform.position}");
        base.Remove();
    }

    #region IErosionHazardWork

    public float WorkerErosionCost => workerErosionCost;

    public string HazardDisplayName => $"{displayName} 제거";

    #endregion
}
