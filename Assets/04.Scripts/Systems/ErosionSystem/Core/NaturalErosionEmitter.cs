using UnityEngine;

/// <summary>
/// 자연 침식 방출 컴포넌트 (INaturalErosionSource 범용 구현체).
///
/// 이 컴포넌트가 붙은 GameObject는 OnEnable/OnDisable 시 NaturalErosionManager에
/// 자동 등록/해제됩니다.
///
/// 전파 방식 (ignoresBarriers):
///   false (식물형) : BFS 경로 탐색, 벽/문에서 차단됨
///   true  (광물형) : 원형 거리 기반, 벽/문 관통
///
/// 인스펙터 설정:
///   maxIntensity   : 발원지에서의 최대 침식 수치
///   decayPerTile   : 타일 1칸당 침식 수치 감소량 (유효 반경 = maxIntensity / decayPerTile)
///   ignoresBarriers: 장벽 관통 여부
/// </summary>
public class NaturalErosionEmitter : MonoBehaviour, INaturalErosionSource
{
    #region 설정

    [Header("자연 침식 방출 설정")]
    [Tooltip("발원지에서의 최대 침식 수치 (거리 0 기준)")]
    [SerializeField] private float maxIntensity = 10f;

    [Tooltip("타일 1칸당 침식 수치 감소량 (예: 0.5 → 유효 반경 20칸)")]
    [SerializeField] private float decayPerTile = 0.5f;

    [Tooltip("true = 광물형: 원형 전파, 벽/문 관통\nfalse = 식물형: BFS 전파, 벽/문에서 차단")]
    [SerializeField] private bool ignoresBarriers = false;

    #endregion

    #region INaturalErosionSource

    /// <summary>발원지의 타일 좌표 (transform 기반 FloorToInt)</summary>
    public Vector2Int TilePosition => new Vector2Int(
        Mathf.FloorToInt(transform.position.x),
        Mathf.FloorToInt(transform.position.y)
    );

    public float MaxIntensity     => maxIntensity;
    public float DecayPerTile     => decayPerTile;
    public bool  IsActive         => isActiveAndEnabled;
    public bool  IgnoresBarriers  => ignoresBarriers;

    #endregion

    #region Unity 라이프사이클

    private void OnEnable()
    {
        NaturalErosionManager.instance?.RegisterSource(this);
    }

    private void OnDisable()
    {
        NaturalErosionManager.instance?.UnregisterSource(this);
    }

    #endregion
}
