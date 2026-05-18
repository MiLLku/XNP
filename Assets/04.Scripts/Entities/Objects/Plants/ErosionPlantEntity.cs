using UnityEngine;

/// <summary>
/// 침식 식물 엔티티 마커.
///
/// ToxicFern, CorruptedMushroom 등 자연 침식 식물 프리팹에 부착합니다.
/// NaturalErosionEmitter는 침식 등록/해제를 담당하고,
/// 이 컴포넌트는 MapGenerator의 저장/복원 시 엔티티를 식별하는 데 사용됩니다.
///
/// entityId 규칙:
///   20 = ToxicFern (독성 고사리)
///   21 = CorruptedMushroom (부패한 버섯)
/// </summary>
[RequireComponent(typeof(NaturalErosionEmitter))]
public class ErosionPlantEntity : MonoBehaviour
{
    /// <summary>ResourceManager에서 프리팹을 조회할 때 사용하는 엔티티 ID</summary>
    [SerializeField] public int entityId;
}
